"""The shared industrial trim sheet, and the decal sheet that goes on top of it.

One 2048 sheet carries every piece of hard-surface detail the fleet uses: panel lines and
seams, bolt rows, diamond grip plate, vents, louvres, a hydraulic run, weld beads, tread
plate, track rubber, glass and the two plain fields. A generator maps a face into a named
cell (lib/materials.py:map_to_trim) and gets that detail without a texture of its own, so
a groomer, a chairlift terminal and a fence post all draw from the same material set. The
cell rectangles are the contract - they live in lib/materials.py:TRIM_CELLS and are
painted here, exactly, in the order that table declares them.

Every cell is built to tile in the direction it is used in: a bolt row repeats along its
length, grip plate repeats in both axes, the panel cells repeat as a wall of panelling.
Cell counts are chosen to divide the cell in pixels, and every blur and gradient wraps, so
there is no seam where a run of trim joins itself.

The decal sheet is separate because decals are alpha-composited over painted bodywork
rather than UV-mapped into it: warning triangles, direction arrows, hazard stripes, a
fleet number plate with a digit strip to compose from, grease points and an invented
maker's plate. Nothing on it imitates a real manufacturer - the names are ours.

Run standalone from tools/assetgen:  python3 -m textures.trimsheets [out_dir]
"""
import ast
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

import config
from lib import validate
from textures import pbr

SHEET = config.TRIM_SHEET_SIZE
DECAL_SHEET = config.TRIM_SHEET_SIZE


def trim_cells():
    """The cell table from lib/materials.py, which is where the contract keeps it.

    lib.materials imports bmesh for its UV helpers, and Blender is not needed to paint a
    texture; a texture-only build has to work without it. So the table is read from the
    same file either way rather than copied into a second place that could drift.
    """
    try:
        from lib.materials import TRIM_CELLS
        return TRIM_CELLS
    except ImportError:
        path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                            "lib", "materials.py")
        with open(path, "r", encoding="utf-8") as f:
            tree = ast.parse(f.read())
        for node in tree.body:
            if isinstance(node, ast.Assign) and getattr(node.targets[0], "id", "") == "TRIM_CELLS":
                return ast.literal_eval(node.value)
        raise


# Where each decal lives on the decal sheet, in the same UV convention as TRIM_CELLS.
# A generator maps a quad here to stick a decal on a machine; the digit strips hold five
# glyphs each, so a fleet number is composed by mapping a fifth of a strip per digit.
DECAL_CELLS = {
    "warn_general": (0.00, 0.75, 0.25, 1.00),
    "warn_crush": (0.25, 0.75, 0.50, 1.00),
    "warn_hot": (0.50, 0.75, 0.75, 1.00),
    "warn_electric": (0.75, 0.75, 1.00, 1.00),
    "arrow_single": (0.00, 0.50, 0.25, 0.75),
    "arrow_rotation": (0.25, 0.50, 0.50, 0.75),
    "hazard_stripes": (0.50, 0.50, 0.75, 0.75),
    "hazard_chevron": (0.75, 0.50, 1.00, 0.75),
    "plate_number": (0.00, 0.25, 0.25, 0.50),
    "digits_0_4": (0.25, 0.25, 0.50, 0.50),
    "digits_5_9": (0.50, 0.25, 0.75, 0.50),
    "grease_point": (0.75, 0.25, 1.00, 0.50),
    "makers_plate": (0.00, 0.00, 0.25, 0.25),
    "fuel_diesel": (0.25, 0.00, 0.50, 0.25),
    "tie_down": (0.50, 0.00, 0.75, 0.25),
    "inspection": (0.75, 0.00, 1.00, 0.25),
}

# Decal palette. Safety colours are the industry's, not a brand's.
SAFETY_YELLOW = (236, 190, 24, 255)
SAFETY_RED = (176, 44, 38, 255)
SAFETY_BLUE = (32, 86, 148, 255)
INK = (22, 22, 24, 255)
PAPER = (232, 232, 230, 255)
PLATE_METAL = (150, 152, 156, 255)

# The invented maker. No real manufacturer name or trade dress appears anywhere in this
# pipeline, so the plate carries one of ours.
MAKER_NAME = "FIRNWERK"
MAKER_LINE = "ALPINE PLANT"


# --------------------------------------------------------------------------- helpers
def _float_mask(shape, drawfn, supersample=3):
    """Rasterise vector shapes into a float mask, antialiased by supersampling.

    Hard-surface trim is drawn geometry - hexagons, slots, louvre blades - not noise, and
    a 1:1 rasterisation of it aliases into hash under a mip chain.
    """
    h, w = shape
    img = Image.new("F", (w * supersample, h * supersample), 0.0)
    draw = ImageDraw.Draw(img)
    drawfn(draw, supersample)
    return np.asarray(img.resize((w, h), Image.BOX), np.float32)


def _tile_offsets(shape):
    """The nine placements that make a shape crossing the cell edge come back on the
    other side, so a cell that is meant to tile does."""
    h, w = shape
    return [(ox, oy) for ox in (-w, 0, w) for oy in (-h, 0, h)]


def _axis(shape):
    """Normalised x and y ramps, shaped for broadcasting."""
    h, w = shape
    x = np.arange(w, dtype=np.float32)[None, :] / np.float32(w)
    y = np.arange(h, dtype=np.float32)[:, None] / np.float32(h)
    return x, y


def _line_dist(coord, centre, span):
    """Wrapped distance from a normalised coordinate to a line, in texels."""
    return np.abs(((coord - centre + 0.5) % 1.0) - 0.5) * np.float32(span)


def _groove(dist, half_width, lip=0.0):
    """A cut groove with the raised lip the press leaves on either side of it."""
    cut = np.clip(1.0 - dist / np.float32(half_width), 0.0, 1.0)
    if lip <= 0.0:
        return cut, np.zeros_like(cut)
    edge = np.clip(1.0 - np.abs(dist - half_width * 2.0) / np.float32(half_width * 1.5),
                   0.0, 1.0)
    return cut, edge * np.float32(lip)


def _grey(tone):
    """A neutral RGB stack from a single tone, with the faint warm cast paint has."""
    return np.stack((tone * 1.005, tone, tone * 0.985), axis=-1)


def _hex_points(cx, cy, r, rot=0.0):
    return [(cx + r * float(np.cos(rot + i * np.pi / 3.0)),
             cy + r * float(np.sin(rot + i * np.pi / 3.0))) for i in range(6)]


# --------------------------------------------------------------------------- cells
# Every painter returns (height 0..1, albedo RGB, roughness, metallic) for its cell.
# Normals, occlusion and the final packing are derived from the height afterwards, the
# same way as in textures/pbr.py - there is one gradient implementation in the pipeline.
def _panel_large(shape, rng):
    """Bodywork panelling: two seams that run off the edges, a screwed-down strip and
    the shallow stamping of a large pressed panel."""
    h, w = shape
    x, y = _axis(shape)
    height = 0.55 + pbr.fbm(shape, 16, 3, rng) * 0.07
    height += pbr.micro_relief(shape, rng, 0.05, freq_divisor=4, octaves=2)

    seam = np.zeros(shape, np.float32)
    lip = np.zeros(shape, np.float32)
    for cx in (0.0, 0.46):
        cut, ridge = _groove(_line_dist(x, cx, w), max(2.0, w / 340.0), lip=0.04)
        seam = np.maximum(seam, cut)
        lip = np.maximum(lip, ridge)
    for cy in (0.0, 0.54):
        cut, ridge = _groove(_line_dist(y, cy, h), max(2.0, h / 170.0), lip=0.04)
        seam = np.maximum(seam, cut)
        lip = np.maximum(lip, ridge)
    height -= seam * 0.30
    height += lip

    # A screwed strip beside the long seam, on a pitch that divides the cell so a wall of
    # panels keeps an even screw spacing across the join.
    screws = 8
    def _screws(draw, ss):
        r = 0.011 * w * ss
        for i in range(screws):
            cy = (i + 0.5) / screws * h * ss
            cx = 0.50 * w * ss
            for ox, oy in _tile_offsets(shape):
                a, b = cx + ox * ss, cy + oy * ss
                draw.ellipse([a - r, b - r, a + r, b + r], fill=1.0)
                draw.line([a - r * 0.6, b, a + r * 0.6, b], fill=0.25, width=int(ss))
    head = _float_mask(shape, _screws)
    height += head * 0.05

    dents = np.clip((pbr.fbm(shape, 5, 3, rng) - 0.62) * 4.0, 0.0, 1.0)
    height -= dents * 0.05
    height = np.clip(height, 0.0, 1.0)

    tone = 0.73 + 0.05 * (pbr.fbm(shape, 9, 3, rng) - 0.5)
    tone -= seam * 0.28
    tone -= dents * 0.05
    albedo = _grey(tone)
    albedo = np.where(head[..., None] > 0.3,
                      np.array((0.52, 0.53, 0.55), np.float32), albedo)

    rough = 0.44 + 0.10 * pbr.fbm(shape, 14, 2, rng) + seam * 0.10 - head * 0.08
    metal = np.full(shape, 0.02, np.float32) + head * 0.9
    return height, albedo, rough, metal


def _panel_small(shape, rng):
    """A bolted access hatch: what covers a hydraulic tank or a battery box."""
    h, w = shape
    x, y = _axis(shape)
    height = 0.55 + pbr.fbm(shape, 18, 3, rng) * 0.06
    height += pbr.micro_relief(shape, rng, 0.05, freq_divisor=4, octaves=2)

    inset = 0.12
    dx = np.maximum(inset - x, x - (1.0 - inset)) * w
    dy = np.maximum(inset - y, y - (1.0 - inset)) * h
    outside = np.maximum(dx, dy)
    hatch = np.clip(-outside / max(2.0, w / 280.0), 0.0, 1.0)   # 1 inside the hatch
    rim = np.clip(1.0 - np.abs(outside) / max(2.0, w / 200.0), 0.0, 1.0)
    height += hatch * 0.06
    height -= rim * 0.22

    def _fixings(draw, ss):
        r = 0.018 * w * ss
        pts = []
        for i in range(4):
            t = (i + 0.5) / 4.0
            pts += [(inset + (1 - 2 * inset) * t, inset * 0.55),
                    (inset + (1 - 2 * inset) * t, 1.0 - inset * 0.55),
                    (inset * 0.55, inset + (1 - 2 * inset) * t),
                    (1.0 - inset * 0.55, inset + (1 - 2 * inset) * t)]
        for u, v in pts:
            cx, cy = u * w * ss, v * h * ss
            draw.polygon(_hex_points(cx, cy, r, rot=0.26), fill=1.0)
            draw.ellipse([cx - r * 1.5, cy - r * 1.5, cx + r * 1.5, cy + r * 1.5],
                         outline=0.35, width=max(1, int(ss * 0.7)))
    bolts = _float_mask(shape, _fixings)
    height += bolts * 0.09
    height = np.clip(height, 0.0, 1.0)

    tone = 0.72 + 0.05 * (pbr.fbm(shape, 10, 3, rng) - 0.5) - rim * 0.30
    albedo = _grey(tone)
    albedo = np.where(bolts[..., None] > 0.3,
                      np.array((0.50, 0.51, 0.54), np.float32), albedo)
    rough = 0.45 + 0.09 * pbr.fbm(shape, 15, 2, rng) + rim * 0.10 - bolts * 0.10
    metal = np.full(shape, 0.02, np.float32) + bolts * 0.9
    return height, albedo, rough, metal


def _bolt_row(shape, rng):
    """A flanged joint: the raised strip and the hex heads that hold it down."""
    h, w = shape
    x, y = _axis(shape)
    height = 0.42 + pbr.fbm(shape, 20, 3, rng) * 0.05
    height += pbr.micro_relief(shape, rng, 0.07, freq_divisor=6)

    flange = np.clip(1.0 - np.abs(y - 0.5) / 0.30, 0.0, 1.0)
    flange = np.clip(flange * 6.0, 0.0, 1.0)
    height += flange * 0.16
    weld, _ = _groove(_line_dist(y, 0.5, h), max(2.0, h / 40.0))
    height -= weld * 0.04

    count = 8                                  # divides the cell, so the row repeats clean
    def _heads(draw, ss):
        r = 0.030 * w * ss
        for i in range(count):
            cx = (i + 0.5) / count * w * ss
            cy = 0.5 * h * ss
            for ox, oy in _tile_offsets(shape):
                a, b = cx + ox * ss, cy + oy * ss
                draw.ellipse([a - r * 1.45, b - r * 1.45, a + r * 1.45, b + r * 1.45],
                             fill=0.45)
                draw.polygon(_hex_points(a, b, r), fill=1.0)
    heads = _float_mask(shape, _heads)
    height += heads * 0.30
    height = np.clip(height, 0.0, 1.0)

    grime = pbr.fbm(shape, 8, 3, rng)
    tone = 0.44 + 0.10 * (grime - 0.5) + flange * 0.05
    albedo = _grey(tone)
    albedo = np.where(heads[..., None] > 0.25,
                      np.array((0.46, 0.47, 0.50), np.float32), albedo)
    rough = 0.48 + 0.18 * grime - heads * 0.14
    metal = np.full(shape, 0.85, np.float32)
    return height, albedo, rough, metal


def _grip_plate(shape, rng):
    """Diamond tread: the raised lozenges on a step, a walkway or a bonnet top."""
    x, y = _axis(shape)
    height = 0.35 + pbr.fbm(shape, 24, 2, rng) * 0.05
    height += pbr.micro_relief(shape, rng, 0.08, freq_divisor=5)

    # Lozenges in a staggered lattice, long axis across the plate, leaning the other way
    # on alternate rows - what a rolled tread plate actually comes out as. The counts are
    # even so the stagger stays in step where the cell wraps.
    px, py = 6.0, 4.0
    row = np.floor(y * py)
    lean = np.where((row % 2.0) < 0.5, 1.0, -1.0).astype(np.float32)
    ax = ((x * px + (row % 2.0) * 0.5) % 1.0) - 0.5
    ay = ((y * py) % 1.0) - 0.5
    d = np.abs(ax + ay * 0.40 * lean) + np.abs(ay) * 2.4
    diamond = np.clip((0.42 - d) * 5.0, 0.0, 1.0)
    height += diamond * 0.45
    height = np.clip(height, 0.0, 1.0)

    polish = diamond * 0.5                         # boots wear the tops bright
    wearnoise = pbr.fbm(shape, 7, 3, rng)
    tone = 0.38 + polish * 0.16 + 0.07 * (wearnoise - 0.5)
    albedo = _grey(tone)
    rough = 0.55 - polish * 0.22 + 0.15 * wearnoise
    metal = np.full(shape, 0.9, np.float32)
    return height, albedo, rough, metal


def _vent(shape, rng):
    """A perforated cooling grille in a recessed frame."""
    h, w = shape
    x, y = _axis(shape)
    height = np.full(shape, 0.62, np.float32) + pbr.fbm(shape, 22, 2, rng) * 0.04

    frame = np.clip(1.0 - np.minimum(np.minimum(x, 1.0 - x) * w,
                                     np.minimum(y, 1.0 - y) * h) / (0.055 * h), 0.0, 1.0)
    height -= (1.0 - frame) * 0.16                   # the grille sits below its frame

    pitch = max(6.0, h / 16.0)
    rows = np.floor((np.arange(h, dtype=np.float32)[:, None]) / pitch)
    stagger = (rows % 2.0) * (pitch * 0.5)
    cx = ((np.arange(w, dtype=np.float32)[None, :] + stagger) % pitch) - pitch * 0.5
    cy = ((np.arange(h, dtype=np.float32)[:, None]) % pitch) - pitch * 0.5
    dist = np.sqrt(cx * cx + cy * cy)
    hole = np.clip((pitch * 0.29 - dist), 0.0, 1.0) * (1.0 - frame)
    height -= hole * 0.45
    mesh = pbr.value_noise(shape, (int(h / 2), int(w / 2)), rng) * 0.03
    height += mesh * (1.0 - frame)
    height = np.clip(height, 0.0, 1.0)

    dirt = pbr.fbm(shape, 6, 3, rng)
    tone = 0.40 + 0.10 * (dirt - 0.5) + frame * 0.16 - hole * 0.30
    albedo = _grey(tone)
    rough = 0.52 + 0.2 * dirt + hole * 0.2
    metal = np.full(shape, 0.55, np.float32) + frame * 0.3
    return height, albedo, rough, metal


def _louvre(shape, rng):
    """Angled louvre blades: engine-bay air, rain kept out."""
    x, y = _axis(shape)
    blades = 8                                        # divides the cell height
    # Half a blade of phase, so the vertical back edge of a blade - the one place a
    # louvre's height field is genuinely discontinuous - lands inside the cell instead of
    # on the join, where it would read as a seam down a run of them.
    t = (y * blades + 0.5) % 1.0
    ramp = t                                          # each blade rises then steps back
    step = np.clip((t - 0.82) / 0.18, 0.0, 1.0)
    height = np.zeros(shape, np.float32) + 0.30 + ramp * 0.46 - step * 0.44
    height += pbr.micro_relief(shape, rng, 0.05, freq_divisor=6)

    frame = np.clip(1.0 - np.minimum(x, 1.0 - x) / 0.04, 0.0, 1.0)
    height = height * (1.0 - frame) + frame * 0.80    # side rails carry the blades
    height = np.clip(height, 0.0, 1.0)

    shade = np.clip(1.0 - t * 2.4, 0.0, 1.0)          # the shadow under each blade
    tone = 0.44 + ramp * 0.10 - shade * 0.24 + frame * 0.08
    tone += 0.05 * (pbr.fbm(shape, 10, 2, rng) - 0.5)
    albedo = _grey(tone)
    rough = 0.47 + 0.12 * pbr.fbm(shape, 13, 2, rng) + shade * 0.1
    metal = np.full(shape, 0.15, np.float32) + frame * 0.5
    return height, albedo, rough, metal


def _hydraulic(shape, rng):
    """A hose run: two hoses, their crimped fittings and the P-clip that holds them."""
    x, y = _axis(shape)
    height = np.full(shape, 0.18, np.float32) + pbr.fbm(shape, 20, 2, rng) * 0.04
    hose_mask = np.zeros(shape, np.float32)
    hose_round = np.zeros(shape, np.float32)      # 1 along the crown, 0 at the tangent
    spiral = np.zeros(shape, np.float32)

    for centre, radius, wrap in ((0.34, 0.17, True), (0.66, 0.13, False)):
        d = np.abs(y - centre) / radius
        prof = np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
        height = np.maximum(height, 0.30 + prof * 0.55)
        hose_mask = np.maximum(hose_mask, np.clip(prof * 4.0, 0.0, 1.0))
        hose_round = np.maximum(hose_round, prof)
        if wrap:
            # The wire braid under the cover reads as a shallow spiral along the hose.
            phase = (x * 32.0 + (y - centre) * 6.0) % 1.0
            spiral = np.maximum(spiral, np.abs(phase - 0.5) * 2.0 * np.clip(prof * 3.0, 0, 1))
    height += spiral * 0.03

    fittings = 4                                       # divides the cell length
    band = ((x * fittings) % 1.0)
    ferrule = np.clip(1.0 - np.abs(band - 0.5) / 0.055, 0.0, 1.0) * hose_mask
    knurl = (np.abs(((x * fittings * 26.0) % 1.0) - 0.5) * 2.0) * ferrule
    height += ferrule * 0.12 + knurl * 0.02

    clip_band = np.clip(1.0 - np.abs(band - 0.02) / 0.02, 0.0, 1.0)
    height += clip_band * hose_mask * 0.06
    height = np.clip(height, 0.0, 1.0)

    dirt = pbr.fbm(shape, 9, 3, rng)
    tone = (0.10 + 0.05 * dirt + spiral * 0.02) * (0.55 + 0.45 * hose_round)
    tone = tone * (1.0 - ferrule) + (0.46 + 0.1 * dirt) * ferrule   # bright crimped steel
    tone = tone * hose_mask + (0.30 + 0.08 * dirt) * (1.0 - hose_mask)
    albedo = _grey(tone)
    rough = 0.72 - ferrule * 0.35 + 0.12 * dirt
    metal = ferrule * 0.9 + clip_band * hose_mask * 0.5
    return height, albedo, rough, metal


def _weld_bead(shape, rng):
    """A run of MIG weld, with the heat tint either side that says it was never painted."""
    w = shape[1]
    x, y = _axis(shape)
    height = 0.40 + pbr.fbm(shape, 26, 3, rng) * 0.06
    height += pbr.micro_relief(shape, rng, 0.08, freq_divisor=5)

    radius = 0.11                                            # a 10 mm bead, not a pipe
    d = np.abs(y - 0.5) / radius
    bead = np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
    # The ripples bow back towards the toes, the shape a moving weld puddle freezes in.
    phase = (x * 48.0 + np.minimum(np.abs(y - 0.5) / radius, 1.0) * 0.55) % 1.0
    ripple = 0.5 + 0.5 * np.cos(phase * 2.0 * np.pi)
    height = np.maximum(height, 0.44 + bead * (0.36 + ripple * 0.07))
    toe = np.clip(1.0 - np.abs(np.abs(y - 0.5) - radius * 1.25) / (radius * 0.5), 0.0, 1.0)
    height -= toe * 0.05                                     # the parent metal drawn in

    spatter = pbr.speckle(shape, rng, int(w / 4), 0.010, softness=14.0)
    spatter *= np.clip(1.0 - np.abs(y - 0.5) / 0.45, 0.0, 1.0)
    height += spatter * 0.10
    height = np.clip(height, 0.0, 1.0)

    heat = np.clip(1.0 - np.abs(y - 0.5) / 0.46, 0.0, 1.0)
    tone = 0.38 + bead * 0.14 + 0.06 * (pbr.fbm(shape, 12, 2, rng) - 0.5)
    r = tone * (1.0 + heat * 0.22 * (1.0 - bead))
    g = tone * (1.0 + heat * 0.06 * (1.0 - bead))
    b = tone * (1.0 - heat * 0.16 * (1.0 - bead) + bead * 0.05)
    albedo = np.stack((r, g, b), axis=-1)
    rough = 0.55 - bead * 0.12 + 0.18 * pbr.fbm(shape, 16, 2, rng) + spatter * 0.2
    metal = np.full(shape, 0.88, np.float32)
    return height, albedo, rough, metal


def _tread_plate(shape, rng):
    """Five-bar chequer plate: the floor of every walkway and machine deck."""
    x, y = _axis(shape)
    height = 0.30 + pbr.fbm(shape, 22, 2, rng) * 0.05
    height += pbr.micro_relief(shape, rng, 0.07, freq_divisor=5)

    block = 2.0                                        # two blocks each way
    bx = np.floor(x * block)
    by = np.floor(y * block)
    flip = np.where(((bx + by) % 2.0) < 0.5, 1.0, -1.0).astype(np.float32)
    t = ((x * 16.0 * flip + y * 16.0) % 1.0)
    bar = np.clip(1.0 - np.abs(t - 0.5) / 0.22, 0.0, 1.0)
    # Bars are short, not continuous: they end before the block does.
    along = np.abs(((x * block) % 1.0) - 0.5) * 2.0
    bar *= np.clip((0.88 - along) * 6.0, 0.0, 1.0)
    height += np.clip(bar * 2.0, 0.0, 1.0) * 0.30
    height = np.clip(height, 0.0, 1.0)

    grime = pbr.fbm(shape, 8, 3, rng)
    polish = np.clip(bar * 2.0, 0.0, 1.0)
    tone = 0.36 + polish * 0.14 + 0.09 * (grime - 0.5)
    albedo = _grey(tone)
    rough = 0.60 - polish * 0.22 + 0.18 * grime
    metal = np.full(shape, 0.9, np.float32)
    return height, albedo, rough, metal


def _rubber_track(shape, rng):
    """Track belt: grouser bars across the belt, guide lugs down the middle, and the
    pebbled rubber they are moulded from."""
    x, y = _axis(shape)
    pebble = pbr.fbm(shape, 40, 3, rng)
    height = 0.22 + pebble * 0.10
    height += pbr.micro_relief(shape, rng, 0.10, freq_divisor=5)

    grousers = 8                                       # a 250 mm pitch across a 2 m tile
    t = ((x * grousers) % 1.0)
    bar = np.clip((0.17 - np.abs(t - 0.5)) / 0.05, 0.0, 1.0)
    height += bar * 0.55

    lug = np.clip((0.10 - np.abs(y - 0.5)) / 0.04, 0.0, 1.0) * np.clip(
        (0.26 - np.abs(t - 0.5)) / 0.06, 0.0, 1.0)
    height += lug * 0.22                               # the guide lug the sprocket drives

    cord = np.abs(((y * 26.0) % 1.0) - 0.5) * 2.0      # reinforcing cord through the rubber
    height += cord * 0.02 * (1.0 - bar)
    height = np.clip(height, 0.0, 1.0)

    packed = pbr.fbm(shape, 6, 3, rng)                 # snow packed into the tread
    tone = 0.062 + 0.028 * pebble + bar * 0.03
    tone += np.clip(packed - 0.55, 0.0, 1.0) * 0.9 * (1.0 - bar)   # snow packed in behind
    albedo = np.stack((tone, tone * 1.0, tone * 1.02), axis=-1)
    rough = 0.86 + 0.10 * pebble - bar * 0.06
    metal = np.zeros(shape, np.float32)
    return height, albedo, rough, metal


def _glass_clean(shape, rng):
    """Glazing as it leaves the wash: flat, dark, and only as rough as float glass is."""
    height = 0.5 + (pbr.fbm(shape, 4, 2, rng) - 0.5) * 0.25
    height += pbr.micro_relief(shape, rng, 0.03, freq_divisor=6)
    height = np.clip(height, 0.0, 1.0)

    haze = pbr.fbm(shape, 6, 3, rng)
    tone = 0.055 + 0.012 * haze
    albedo = np.stack((tone * 0.92, tone, tone * 1.08), axis=-1)
    rough = np.full(shape, 0.04, np.float32) + np.clip(haze - 0.7, 0, 1) * 0.3
    metal = np.zeros(shape, np.float32)
    return height, albedo, rough, metal


def _steel_plain(shape, rng):
    """The plain unpainted field: rolled steel, brushed one way, with mill scale left."""
    w = shape[1]
    height = 0.45 + pbr.fbm(shape, (6, 90), 4, rng) * 0.28
    height += pbr.fbm(shape, 30, 3, rng) * 0.10
    height += pbr.micro_relief(shape, rng, 0.08, freq_divisor=4, octaves=2)
    brush = pbr.scratch_field(shape, rng, 220, w * 0.4, width=1, angle_deg=0.0, spread=6.0)
    brush = np.clip(pbr.blur(brush, 1) * 1.8, 0.0, 1.0)
    height += brush * 0.05
    height = np.clip(height, 0.0, 1.0)

    scale = pbr.fbm(shape, 9, 4, rng)
    tone = 0.45 + 0.10 * (scale - 0.5) + brush * 0.06
    albedo = np.stack((tone * 1.0, tone * 0.99, tone * 0.98), axis=-1)
    rough = 0.42 + 0.22 * scale - brush * 0.12
    metal = np.clip(0.95 - np.clip(scale - 0.72, 0, 1) * 1.6, 0.0, 1.0)
    return height, albedo, np.clip(rough, 0.05, 0.95), metal


def _paint_plain(shape, rng):
    """The plain painted field, and the one cell that matters most for livery.

    It stays a light neutral on purpose: LiveryTint multiplies _LiveryColor through this
    texture, so a machine's colour comes from its record in vehicles.json and this cell
    supplies only the orange peel, the polish and the dirt.
    """
    w = shape[1]
    height = 0.52 + pbr.fbm(shape, 40, 3, rng) * 0.16         # orange peel
    height += pbr.fbm(shape, 5, 3, rng) * 0.10                # sheet not dead flat
    height += pbr.micro_relief(shape, rng, 0.06, freq_divisor=4, octaves=2)
    sanding = pbr.scratch_field(shape, rng, 120, w * 0.12, width=1, angle_deg=35.0,
                                spread=30.0)
    sanding = np.clip(pbr.blur(sanding, 1) * 1.6, 0.0, 1.0)
    height -= sanding * 0.03
    height = np.clip(height, 0.0, 1.0)

    dirt = pbr.fbm(shape, 7, 4, rng)
    tone = 0.76 + 0.05 * (pbr.fbm(shape, 12, 3, rng) - 0.5) - np.clip(dirt - 0.58, 0, 1) * 0.35
    albedo = _grey(tone)
    rough = 0.40 + 0.12 * dirt + sanding * 0.1
    metal = np.full(shape, 0.02, np.float32)
    return height, albedo, np.clip(rough, 0.05, 0.95), metal


# Art parameters per cell: how much real surface the cell covers and how deep its relief
# is, which together set the slope the normal map comes out with. The cell rectangles
# themselves are not here - they are the contract, and come from lib/materials.py.
CELL_SPECS = {
    "panel_large": {"painter": _panel_large, "world_m": 4.0, "relief_m": 0.020, "cavity": 5.0},
    "panel_small": {"painter": _panel_small, "world_m": 2.0, "relief_m": 0.018, "cavity": 5.0},
    "bolt_row": {"painter": _bolt_row, "world_m": 2.0, "relief_m": 0.030, "cavity": 4.0},
    "grip_plate": {"painter": _grip_plate, "world_m": 1.0, "relief_m": 0.005, "cavity": 4.0},
    "vent": {"painter": _vent, "world_m": 0.8, "relief_m": 0.020, "cavity": 4.0},
    "louvre": {"painter": _louvre, "world_m": 0.8, "relief_m": 0.045, "cavity": 3.5},
    "hydraulic": {"painter": _hydraulic, "world_m": 1.2, "relief_m": 0.040, "cavity": 3.0},
    "weld_bead": {"painter": _weld_bead, "world_m": 0.6, "relief_m": 0.016, "cavity": 4.0},
    "tread_plate": {"painter": _tread_plate, "world_m": 1.2, "relief_m": 0.010, "cavity": 4.0},
    "rubber_track": {"painter": _rubber_track, "world_m": 2.0, "relief_m": 0.045, "cavity": 3.0},
    "glass_clean": {"painter": _glass_clean, "world_m": 1.5, "relief_m": 0.0008, "cavity": 2.0},
    "steel_plain": {"painter": _steel_plain, "world_m": 1.5, "relief_m": 0.005, "cavity": 4.0},
    "paint_plain": {"painter": _paint_plain, "world_m": 1.5, "relief_m": 0.004, "cavity": 4.5},
}


# --------------------------------------------------------------------------- sheet
def _cell_rect(uv, size):
    """Pixel rectangle for a UV cell. V runs up in UV and down in image rows, so the
    cell's top row is the one at v1."""
    u0, v0, u1, v1 = uv
    x0 = int(round(u0 * size))
    x1 = int(round(u1 * size))
    y0 = int(round((1.0 - v1) * size))
    y1 = int(round((1.0 - v0) * size))
    return x0, y0, x1, y1


def build_industrial(out_dir, size=SHEET):
    """Paint every contract cell into one sheet and derive its normal and ORM maps."""
    cells = trim_cells()
    albedo = np.zeros((size, size, 3), np.float32)
    normal = np.zeros((size, size, 3), np.float32)
    orm = np.zeros((size, size, 3), np.float32)

    for name, uv in cells.items():
        spec = CELL_SPECS.get(name)
        if spec is None:
            raise KeyError("trim cell '%s' is declared in lib/materials.py but nothing "
                           "here paints it" % name)
        x0, y0, x1, y1 = _cell_rect(uv, size)
        shape = (y1 - y0, x1 - x0)
        rng = pbr.rng_for("trim", name)
        height, cell_albedo, rough, metal = spec["painter"](shape, rng)
        height = np.clip(height, 0.0, 1.0)

        texel_m = spec["world_m"] / shape[1]
        cell_normal = pbr.encode_normal(
            pbr.height_to_normal(height, spec["relief_m"], texel_m))
        ao = pbr.ambient_occlusion(height, spec["cavity"],
                                   radii=(2, 6, max(8, shape[0] // 16)))
        cell_albedo = pbr.apply_occlusion(cell_albedo, ao)

        albedo[y0:y1, x0:x1] = cell_albedo
        normal[y0:y1, x0:x1] = cell_normal
        orm[y0:y1, x0:x1] = np.stack((ao, np.clip(rough, 0.0, 1.0),
                                      np.clip(metal, 0.0, 1.0)), axis=-1)

    return [
        pbr.write_map(albedo, out_dir, "trim_industrial", "albedo", size),
        pbr.write_map(normal, out_dir, "trim_industrial", "normal", size),
        pbr.write_map(orm, out_dir, "trim_industrial", "orm", size),
    ]


# --------------------------------------------------------------------------- decals
def _font(px):
    """Pillow's bundled face, sized. It ships with the pinned Pillow, so the sheet is the
    same on every machine that builds it."""
    try:
        return ImageFont.load_default(size=px)
    except TypeError:
        return ImageFont.load_default()


def _text(draw, xy, text, px, fill, anchor="mm", spacing=0):
    if spacing:
        # Letter-spaced text has to be laid out by hand; Pillow has no tracking.
        font = _font(px)
        widths = [draw.textlength(c, font=font) + spacing for c in text]
        total = sum(widths) - spacing
        x = xy[0] - total * 0.5
        for c, wdt in zip(text, widths):
            draw.text((x, xy[1]), c, font=font, fill=fill, anchor="lm")
            x += wdt
        return
    draw.text(xy, text, font=_font(px), fill=fill, anchor=anchor)


def _triangle(draw, size, pictogram):
    """A warning triangle with its black border, and whatever goes inside it."""
    m = size * 0.07
    apex = (size * 0.5, m)
    left = (m * 0.7, size - m * 1.4)
    right = (size - m * 0.7, size - m * 1.4)
    draw.polygon([apex, left, right], fill=INK)
    inset = size * 0.055
    draw.polygon([(apex[0], apex[1] + inset * 1.9),
                  (left[0] + inset * 1.6, left[1] - inset),
                  (right[0] - inset * 1.6, right[1] - inset)], fill=SAFETY_YELLOW)
    pictogram(draw, size)


def _pic_exclamation(draw, size):
    cx = size * 0.5
    draw.polygon([(cx - size * 0.045, size * 0.38), (cx + size * 0.045, size * 0.38),
                  (cx + size * 0.030, size * 0.64), (cx - size * 0.030, size * 0.64)],
                 fill=INK)
    r = size * 0.042
    draw.ellipse([cx - r, size * 0.70 - r, cx + r, size * 0.70 + r], fill=INK)


def _pic_crush(draw, size):
    """Fingers between a roller and a belt: the tiller and the bullwheel hazard."""
    cx = size * 0.5
    draw.ellipse([cx - size * 0.16, size * 0.40, cx + size * 0.16, size * 0.60], fill=INK)
    draw.rectangle([cx - size * 0.20, size * 0.62, cx + size * 0.20, size * 0.66], fill=INK)
    for i in range(3):
        x = cx - size * 0.08 + i * size * 0.08
        draw.rectangle([x - size * 0.014, size * 0.64, x + size * 0.014, size * 0.76],
                       fill=INK)


def _pic_hot(draw, size):
    """A hot surface: the exhaust and the hydraulic cooler."""
    cx = size * 0.5
    draw.arc([cx - size * 0.22, size * 0.56, cx + size * 0.22, size * 0.78],
             start=180, end=360, fill=INK, width=int(size * 0.035))
    for i in (-1, 0, 1):
        x = cx + i * size * 0.10
        draw.line([(x, size * 0.52), (x - size * 0.03, size * 0.45),
                   (x + size * 0.03, size * 0.40), (x, size * 0.34)],
                  fill=INK, width=int(size * 0.025), joint="curve")


def _pic_electric(draw, size):
    cx = size * 0.5
    draw.polygon([(cx + size * 0.07, size * 0.36), (cx - size * 0.09, size * 0.60),
                  (cx - size * 0.005, size * 0.60), (cx - size * 0.06, size * 0.78),
                  (cx + size * 0.11, size * 0.52), (cx + size * 0.015, size * 0.52)],
                 fill=INK)


def _decal_warn(kind):
    def paint(draw, size):
        draw.rounded_rectangle([0, 0, size - 1, size - 1], radius=size * 0.04,
                               fill=(0, 0, 0, 0))
        _triangle(draw, size, {"warn_general": _pic_exclamation,
                               "warn_crush": _pic_crush,
                               "warn_hot": _pic_hot,
                               "warn_electric": _pic_electric}[kind])
    return paint


def _decal_arrow_single(draw, size):
    cx = size * 0.5
    draw.polygon([(cx, size * 0.12), (size * 0.86, size * 0.50), (size * 0.66, size * 0.50),
                  (size * 0.66, size * 0.88), (size * 0.34, size * 0.88),
                  (size * 0.34, size * 0.50), (size * 0.14, size * 0.50)], fill=PAPER)
    draw.polygon([(cx, size * 0.18), (size * 0.79, size * 0.50), (size * 0.62, size * 0.50),
                  (size * 0.62, size * 0.82), (size * 0.38, size * 0.82),
                  (size * 0.38, size * 0.50), (size * 0.21, size * 0.50)], fill=SAFETY_RED)


def _decal_arrow_rotation(draw, size):
    """Direction of rotation: it goes on a bullwheel cover and a rotor guard."""
    box = [size * 0.16, size * 0.16, size * 0.84, size * 0.84]
    draw.arc(box, start=200, end=110, fill=PAPER, width=int(size * 0.13))
    draw.polygon([(size * 0.80, size * 0.16), (size * 0.90, size * 0.46),
                  (size * 0.60, size * 0.38)], fill=PAPER)
    box2 = [size * 0.22, size * 0.22, size * 0.78, size * 0.78]
    draw.arc(box2, start=204, end=104, fill=SAFETY_BLUE, width=int(size * 0.07))
    draw.polygon([(size * 0.785, size * 0.235), (size * 0.855, size * 0.44),
                  (size * 0.645, size * 0.385)], fill=SAFETY_BLUE)


def _decal_hazard_stripes(draw, size):
    """Diagonal hazard stripes, at a pitch that divides the cell so a run of them tiles."""
    draw.rectangle([0, 0, size, size], fill=SAFETY_YELLOW)
    bands = 8
    pitch = size / bands
    for i in range(-bands, bands * 2):
        x = i * pitch
        draw.polygon([(x, 0), (x + pitch * 0.5, 0),
                      (x + pitch * 0.5 + size, size), (x + size, size)], fill=INK)


def _decal_hazard_chevron(draw, size):
    """Chevrons pointing the way off a machine: the rear of a blade, a barrier end."""
    draw.rectangle([0, 0, size, size], fill=PAPER)
    rows = 4
    pitch = size / rows
    for i in range(rows + 1):
        y = i * pitch
        draw.polygon([(0, y), (size * 0.5, y + pitch * 0.55), (size, y),
                      (size, y + pitch * 0.45), (size * 0.5, y + pitch), (0, y + pitch * 0.45)],
                     fill=SAFETY_RED)


def _plate_body(draw, size, fill, border):
    draw.rounded_rectangle([size * 0.06, size * 0.20, size * 0.94, size * 0.80],
                           radius=size * 0.05, fill=fill, outline=border,
                           width=max(2, int(size * 0.014)))
    r = size * 0.018
    for x, y in ((0.13, 0.27), (0.87, 0.27), (0.13, 0.73), (0.87, 0.73)):
        draw.ellipse([size * x - r, size * y - r, size * x + r, size * y + r],
                     fill=(90, 92, 96, 255))


def _decal_plate_number(draw, size):
    """A blank fleet-number plate. The number itself is composed from the digit strips,
    which is what lets one decal sheet number a whole fleet."""
    _plate_body(draw, size, PAPER, INK)
    draw.rectangle([size * 0.12, size * 0.255, size * 0.88, size * 0.35], fill=INK)
    _text(draw, (size * 0.5, size * 0.303), "FLEET No.", int(size * 0.062), PAPER,
          spacing=size * 0.012)


_SEGMENTS = {
    "0": "abcdef", "1": "bc", "2": "abdeg", "3": "abcdg", "4": "bcfg",
    "5": "acdfg", "6": "acdefg", "7": "abc", "8": "abcdefg", "9": "abcdfg",
}


def _seven_seg(draw, x, y, w, h, digit, fill):
    """A seven-segment digit, drawn as stencil bars.

    A stencil is what a real plant number looks like, and drawing the bars is one
    deterministic code path rather than a font whose metrics could move under us.
    """
    t = min(w, h) * 0.20
    segs = {
        "a": [(x + t * 0.6, y), (x + w - t * 0.6, y), (x + w - t, y + t), (x + t, y + t)],
        "b": [(x + w, y + t * 0.6), (x + w, y + h * 0.5 - t * 0.2),
              (x + w - t, y + h * 0.5 - t * 0.6), (x + w - t, y + t)],
        "c": [(x + w, y + h * 0.5 + t * 0.2), (x + w, y + h - t * 0.6),
              (x + w - t, y + h - t), (x + w - t, y + h * 0.5 + t * 0.6)],
        "d": [(x + t, y + h - t), (x + w - t, y + h - t), (x + w - t * 0.6, y + h),
              (x + t * 0.6, y + h)],
        "e": [(x, y + h * 0.5 + t * 0.2), (x + t, y + h * 0.5 + t * 0.6),
              (x + t, y + h - t), (x, y + h - t * 0.6)],
        "f": [(x, y + t * 0.6), (x + t, y + t), (x + t, y + h * 0.5 - t * 0.6),
              (x, y + h * 0.5 - t * 0.2)],
        "g": [(x + t, y + h * 0.5 - t * 0.4), (x + w - t, y + h * 0.5 - t * 0.4),
              (x + w - t, y + h * 0.5 + t * 0.4), (x + t, y + h * 0.5 + t * 0.4)],
    }
    for key in _SEGMENTS[digit]:
        draw.polygon(segs[key], fill=fill)


def _decal_digits(first):
    def paint(draw, size):
        draw.rectangle([0, 0, size, size], fill=(0, 0, 0, 0))
        cell = size / 5.0
        for i in range(5):
            digit = str(first + i)
            _seven_seg(draw, i * cell + cell * 0.22, size * 0.24,
                       cell * 0.56, size * 0.52, digit, INK)
    return paint


def _decal_grease_point(draw, size):
    """A lubrication point: what an operator looks for before a shift."""
    cx = cy = size * 0.5
    r = size * 0.36
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=SAFETY_YELLOW, outline=INK,
                 width=max(2, int(size * 0.02)))
    draw.ellipse([cx - size * 0.11, cy - size * 0.20, cx + size * 0.11, cy + size * 0.02],
                 fill=INK)
    draw.rectangle([cx - size * 0.045, cy - size * 0.02, cx + size * 0.045, cy + size * 0.16],
                   fill=INK)
    draw.rectangle([cx - size * 0.14, cy + size * 0.16, cx + size * 0.14, cy + size * 0.21],
                   fill=INK)
    _text(draw, (cx, cy + size * 0.29), "LUBE", int(size * 0.085), INK)


def _decal_makers_plate(draw, size):
    """The maker's plate. The name is invented; every field on it is the kind of thing a
    real plate carries, and none of it copies anybody's."""
    _plate_body(draw, size, PLATE_METAL, (70, 72, 76, 255))
    _text(draw, (size * 0.5, size * 0.315), MAKER_NAME, int(size * 0.105), INK,
          spacing=size * 0.018)
    _text(draw, (size * 0.5, size * 0.405), MAKER_LINE, int(size * 0.050), (58, 60, 64, 255),
          spacing=size * 0.010)
    draw.line([(size * 0.14, size * 0.455), (size * 0.86, size * 0.455)],
              fill=(96, 98, 102, 255), width=max(1, int(size * 0.006)))
    rows = ("TYPE          . . . . . .",
            "SERIAL No.    . . . . . .",
            "YEAR          . . . . . .",
            "MASS kg       . . . . . .")
    for i, row in enumerate(rows):
        draw.text((size * 0.15, size * 0.50 + i * size * 0.066), row,
                  font=_font(int(size * 0.045)), fill=(46, 48, 52, 255), anchor="lm")


def _decal_fuel_diesel(draw, size):
    _plate_body(draw, size, INK, (12, 12, 14, 255))
    _text(draw, (size * 0.5, size * 0.40), "DIESEL", int(size * 0.130), PAPER,
          spacing=size * 0.014)
    _text(draw, (size * 0.5, size * 0.58), "ONLY", int(size * 0.085), SAFETY_YELLOW,
          spacing=size * 0.012)
    draw.line([(size * 0.20, size * 0.68), (size * 0.80, size * 0.68)], fill=SAFETY_YELLOW,
              width=max(2, int(size * 0.012)))


def _decal_tie_down(draw, size):
    """A lashing point: where the low-loader chains go when a machine moves resort."""
    cx = size * 0.5
    draw.ellipse([cx - size * 0.34, size * 0.16, cx + size * 0.34, size * 0.84],
                 outline=SAFETY_YELLOW, width=max(3, int(size * 0.035)))
    draw.polygon([(cx - size * 0.20, size * 0.62), (cx, size * 0.30),
                  (cx + size * 0.20, size * 0.62), (cx + size * 0.10, size * 0.62),
                  (cx, size * 0.44), (cx - size * 0.10, size * 0.62)], fill=SAFETY_YELLOW)
    draw.rectangle([cx - size * 0.24, size * 0.66, cx + size * 0.24, size * 0.72],
                   fill=SAFETY_YELLOW)


def _decal_inspection(draw, size):
    """The annual inspection sticker every lift and machine carries."""
    cx = cy = size * 0.5
    r = size * 0.38
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=PAPER, outline=SAFETY_BLUE,
                 width=max(3, int(size * 0.030)))
    draw.ellipse([cx - r * 0.80, cy - r * 0.80, cx + r * 0.80, cy + r * 0.80],
                 outline=SAFETY_BLUE, width=max(1, int(size * 0.010)))
    _text(draw, (cx, cy - size * 0.16), "INSPECTED", int(size * 0.072), SAFETY_BLUE,
          spacing=size * 0.008)
    draw.line([(cx - r * 0.55, cy - size * 0.075), (cx + r * 0.55, cy - size * 0.075)],
              fill=SAFETY_BLUE, width=max(1, int(size * 0.008)))
    for i in range(12):
        ang = i * np.pi / 6.0
        x = cx + float(np.sin(ang)) * r * 0.62
        y = cy + size * 0.10 - float(np.cos(ang)) * r * 0.22
        draw.ellipse([x - size * 0.012, y - size * 0.012, x + size * 0.012, y + size * 0.012],
                     fill=SAFETY_BLUE)
    _text(draw, (cx, cy + size * 0.24), "NEXT DUE", int(size * 0.052), INK,
          spacing=size * 0.006)


DECAL_PAINTERS = {
    "warn_general": _decal_warn("warn_general"),
    "warn_crush": _decal_warn("warn_crush"),
    "warn_hot": _decal_warn("warn_hot"),
    "warn_electric": _decal_warn("warn_electric"),
    "arrow_single": _decal_arrow_single,
    "arrow_rotation": _decal_arrow_rotation,
    "hazard_stripes": _decal_hazard_stripes,
    "hazard_chevron": _decal_hazard_chevron,
    "plate_number": _decal_plate_number,
    "digits_0_4": _decal_digits(0),
    "digits_5_9": _decal_digits(5),
    "grease_point": _decal_grease_point,
    "makers_plate": _decal_makers_plate,
    "fuel_diesel": _decal_fuel_diesel,
    "tie_down": _decal_tie_down,
    "inspection": _decal_inspection,
}


def build_decals(out_dir, size=DECAL_SHEET, supersample=2):
    """The decal sheet: RGB over an alpha coverage mask, drawn as vectors.

    Alpha is the point of this sheet - a decal is composited onto painted bodywork, so
    everything outside a decal has to be transparent rather than a colour. That is the
    one place where an _albedo file here carries four channels instead of three.
    """
    cell_px = size // 4
    sheet = Image.new("RGBA", (size * supersample, size * supersample), (0, 0, 0, 0))
    for name, uv in DECAL_CELLS.items():
        painter = DECAL_PAINTERS[name]
        x0, y0, x1, y1 = _cell_rect(uv, size)
        tile = Image.new("RGBA", ((x1 - x0) * supersample, (y1 - y0) * supersample),
                         (0, 0, 0, 0))
        painter(ImageDraw.Draw(tile), (x1 - x0) * supersample)
        sheet.paste(tile, (x0 * supersample, y0 * supersample))
    sheet = sheet.resize((size, size), Image.LANCZOS)

    data = np.asarray(sheet, np.float32) / 255.0
    record = pbr.write_map(data, out_dir, "trim_decals", "albedo", size,
                           channels=("red", "green", "blue", "alpha"))
    record["cells"] = {k: list(v) for k, v in DECAL_CELLS.items()}
    record["cellPixels"] = cell_px
    return [record]


# --------------------------------------------------------------------------- build
def build_all(out_dir):
    """The industrial sheet and the decal sheet. Returns one record per PNG."""
    records = build_industrial(out_dir)
    cells = {k: list(v) for k, v in trim_cells().items()}
    for record in records:
        record["cells"] = cells      # what UVs where, published for whoever maps to it
    records += build_decals(out_dir)
    return records


def seam_report(out_dir, size=SHEET):
    """Per-cell wrap error for the painted sheet.

    The sheet as a whole is not a tiling texture - its cells butt against each other - so
    the only seam measurement that means anything is taken inside a cell. A ratio near or
    below 1 says the cell's own repeat is invisible; a large one says a run of that trim
    will show a line where it joins.
    """
    path = os.path.join(out_dir, "trim_industrial_albedo.png")
    sheet = np.asarray(Image.open(path).convert("L"), np.float32) / 255.0
    print("%-16s %-12s %s" % ("cell", "pixels", "seam u/v (ratio to internal variation)"))
    for name, uv in trim_cells().items():
        x0, y0, x1, y1 = _cell_rect(uv, size)
        u, v = pbr.seam_error(sheet[y0:y1, x0:x1])
        print("%-16s %-12s %.2f / %.2f" % (name, "%dx%d" % (x1 - x0, y1 - y0), u, v))


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.TEXTURES_DIR
    validate.reset()
    pbr.report(build_all(target), seams=False)
    print()
    seam_report(target)
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
