"""The PBR material sets: albedo, normal, ORM and wear for every material family.

Six sets cover the fleet and the mountain - `machine` and `lift` at 2048, `prop` and
`concrete` at 1024, `glass` and `rubber` at 512. Each one is a single tiling square of a
material (painted steel, galvanised structure, weathered paint, glazing, track rubber,
cast concrete) shared by everything in its category, which is what keeps a machine at
three draw calls and makes a livery a tint rather than a texture of its own.

Everything here starts from a HEIGHT FIELD and is derived from it. The normal map is the
gradient of that height at the set's real texel size, as docs/ART_CONTRACT.md section 6
requires; ambient occlusion is its cavity; roughness follows where the surface is pitted
and where it is polished; and the wear masks read its curvature, which is the reason
edge wear lands on the edges that would really rub instead of on random noise.

The wear set is where the art meets the simulation. `ConditionVisuals` feeds a machine's
condition into `_WearAmount` and the four masks blend in at different thresholds - edge
polish first, then the salt film, then rust, then chipped paint - so a machine at 40 per
cent condition visibly looks it. The masks are built so that ordering is true: each one
occupies a different part of the surface rather than being four versions of one splatter.

Suffixes are contract, not taste: lib/unitymeta.py reads them to decide whether Unity
treats a file as sRGB colour, as a normal map or as linear data.

Run standalone from tools/assetgen:  python3 -m textures.pbr [out_dir]
"""
import os

import numpy as np
from PIL import Image, ImageDraw

import config
from lib import datasrc, validate

# Channel names per suffix, published in the manifest so a consumer never has to guess
# which channel holds what. ORM and WEAR come from config because the shader reads them.
CHANNELS = {
    "albedo": ("red", "green", "blue"),
    "normal": ("x", "y", "z"),
    "orm": config.ORM_CHANNELS,
    "wear": config.WEAR_CHANNELS,
}

# One record per set, in build order. `world_m` is how much real surface one tile covers,
# which is what makes the normal map's slope correct: a 6 mm panel stamping across 2 m of
# bodywork is a gentle rise, the same 6 mm across 0.5 m of track rubber is a hard lug.
# `cavity` is how hard the derived occlusion bites - a pitted casting shades itself far
# more than a painted panel does. `wear` weights the four masks for the material: zinc
# and paint rust and chip, glass and rubber do neither, and everything collects salt.
SETS = (
    {"id": "machine", "surface": "painted_steel", "size": config.TEX_SIZE_HERO,
     "world_m": 2.0, "relief_m": 0.007, "cavity": 6.0,
     "wear": (1.0, 1.0, 1.0, 1.0)},
    {"id": "lift", "surface": "galvanised", "size": config.TEX_SIZE_HERO,
     "world_m": 2.0, "relief_m": 0.005, "cavity": 5.0,
     "wear": (0.9, 1.2, 0.9, 0.45)},
    {"id": "prop", "surface": "weathered_paint", "size": config.TEX_SIZE_PROP,
     "world_m": 2.0, "relief_m": 0.008, "cavity": 4.5,
     "wear": (1.0, 0.8, 1.0, 1.25)},
    {"id": "glass", "surface": "glass", "size": config.TEX_SIZE_SMALL,
     "world_m": 1.0, "relief_m": 0.0008, "cavity": 2.5,
     "wear": (0.7, 0.12, 1.2, 0.1)},
    {"id": "rubber", "surface": "rubber", "size": config.TEX_SIZE_SMALL,
     "world_m": 0.5, "relief_m": 0.009, "cavity": 3.0,
     "wear": (0.8, 0.1, 0.9, 0.15)},
    {"id": "concrete", "surface": "concrete", "size": config.TEX_SIZE_PROP,
     "world_m": 2.0, "relief_m": 0.015, "cavity": 3.5,
     "wear": (0.6, 0.5, 1.1, 0.2)},
)


# --------------------------------------------------------------------------- noise
def rng_for(*parts):
    """A numpy generator seeded from the master seed and an identifying string.

    Nothing in the pipeline may touch an unseeded RNG: two runs have to produce
    byte-identical PNGs or the manifest stops meaning anything.
    """
    return np.random.default_rng(datasrc.seed_for(*parts))


def _axis_weights(n, freq):
    """Lattice indices and a smoothstep blend for one axis, wrapping at the edge."""
    t = np.arange(n, dtype=np.float32) * (np.float32(freq) / np.float32(n))
    i0 = np.floor(t).astype(np.int32)
    f = (t - i0).astype(np.float32)
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0)
    return i0 % freq, (i0 + 1) % freq, f


def value_noise(shape, freq, rng):
    """Tileable value noise. `freq` is a lattice count, or (rows, cols) for stretched
    noise - brushed metal and rain streaks are the same field with an anisotropic
    lattice, so they stay one code path."""
    h, w = shape
    fy, fx = freq if isinstance(freq, (tuple, list)) else (freq, freq)
    fy = max(1, min(int(fy), h))
    fx = max(1, min(int(fx), w))
    lat = rng.random((fy, fx), dtype=np.float32)
    y0, y1, ty = _axis_weights(h, fy)
    x0, x1, tx = _axis_weights(w, fx)
    a = lat[np.ix_(y0, x0)]
    b = lat[np.ix_(y0, x1)]
    c = lat[np.ix_(y1, x0)]
    d = lat[np.ix_(y1, x1)]
    tx = tx[None, :]
    ty = ty[:, None]
    top = a + (b - a) * tx
    bot = c + (d - c) * tx
    return top + (bot - top) * ty


def fbm(shape, freq, octaves, rng, gain=0.5, lacunarity=2.0):
    """Summed octaves of value noise, normalised to 0..1 and still tileable."""
    fy, fx = freq if isinstance(freq, (tuple, list)) else (freq, freq)
    total = np.zeros(shape, np.float32)
    amp = 1.0
    norm = 0.0
    for _ in range(octaves):
        total += np.float32(amp) * value_noise(shape, (round(fy), round(fx)), rng)
        norm += amp
        amp *= gain
        fy *= lacunarity
        fx *= lacunarity
    return total / np.float32(norm)


def ridged(shape, freq, octaves, rng):
    """Creased noise: cracks, mould seams and mill scale all want a ridge, not a blob."""
    return 1.0 - np.abs(fbm(shape, freq, octaves, rng) * 2.0 - 1.0)


def _blur_axis(a, radius, axis):
    n = a.shape[axis]
    r = min(int(radius), (n - 1) // 2)
    if r < 1:
        return a
    pre = np.take(a, np.arange(n - r, n), axis=axis)
    post = np.take(a, np.arange(0, r), axis=axis)
    p = np.concatenate((pre, a, post), axis=axis)
    cs = np.cumsum(p, axis=axis, dtype=np.float32)
    pad = list(p.shape)
    pad[axis] = 1
    cs = np.concatenate((np.zeros(pad, np.float32), cs), axis=axis)
    k = 2 * r + 1
    hi = np.take(cs, np.arange(k, k + n), axis=axis)
    lo = np.take(cs, np.arange(0, n), axis=axis)
    return (hi - lo) / np.float32(k)


def blur(a, radius, passes=2):
    """Wrapping box blur, repeated until it is close enough to a gaussian.

    It has to wrap: a blur that clamps at the border would leave a seam in every map
    derived from it, and every map here is meant to tile.
    """
    out = a
    for _ in range(passes):
        out = _blur_axis(_blur_axis(out, radius, 0), radius, 1)
    return out


def curvature(height, radius):
    """Convex-positive, concave-negative curvature at one scale.

    A height minus its own blur is a scale-space Laplacian: it is positive on ridges and
    stamped edges, negative in grooves and pits, and zero on flat panel. That signal is
    what edge wear and rust are keyed off, which is why wear lands where a machine is
    actually handled instead of wherever the noise happened to be bright.
    """
    lap = height - blur(height, radius)
    scale = float(np.std(lap)) * 2.5
    if scale < 1e-6:
        return np.zeros_like(lap)
    return np.clip(lap / np.float32(scale), -1.0, 1.0)


def streak_down(seed, length, decay=0.94):
    """Drag a field downward with exponential falloff.

    Rust does not sit where the water started, it runs. Row index grows downward in an
    image while V grows upward, so a positive roll on axis 0 is down the model. Doubling
    the shift each pass reaches `length` rows in a handful of whole-array operations
    instead of one pass per row.
    """
    out = seed.astype(np.float32, copy=True)
    step = 1
    while step < length:
        out = np.maximum(out, np.roll(out, step, axis=0) * np.float32(decay ** step))
        step *= 2
    return out


def scratch_field(shape, rng, count, length, width=1, brightness=1.0,
                  angle_deg=None, spread=180.0):
    """Fine linear damage. Every line is drawn nine times, offset by a tile, so a
    scratch that runs off one edge arrives on the other and the map still tiles."""
    h, w = shape
    img = Image.new("F", (w, h), 0.0)
    draw = ImageDraw.Draw(img)
    base = 0.0 if angle_deg is None else float(angle_deg)
    for _ in range(int(count)):
        x = float(rng.random()) * w
        y = float(rng.random()) * h
        ang = np.deg2rad(base + (float(rng.random()) - 0.5) * spread)
        ln = length * (0.35 + 0.65 * float(rng.random()))
        dx = float(np.cos(ang)) * ln
        dy = float(np.sin(ang)) * ln
        val = brightness * (0.35 + 0.65 * float(rng.random()))
        for ox in (-w, 0, w):
            for oy in (-h, 0, h):
                draw.line([x + ox, y + oy, x + dx + ox, y + dy + oy],
                          fill=val, width=int(width))
    return np.asarray(img, np.float32)


def speckle(shape, rng, freq, coverage, softness=24.0):
    """Sparse high-contrast flecks: the top `coverage` fraction of a fine noise field."""
    n = value_noise(shape, freq, rng)
    threshold = float(np.quantile(n, 1.0 - coverage))
    return np.clip((n - threshold) * softness, 0.0, 1.0)


def _norm01(a):
    lo = float(np.min(a))
    hi = float(np.max(a))
    if hi - lo < 1e-9:
        return np.zeros_like(a)
    return (a - lo) / (hi - lo)


# --------------------------------------------------------------------------- derivation
def height_to_normal(height, relief_m, texel_m):
    """Tangent-space normal from the real slope of the height field.

    `height` is 0..1, `relief_m` is what full scale means in metres and `texel_m` is how
    much ground one texel covers, so the gradient comes out as a true slope and a 6 mm
    stamping reads as 6 mm rather than as whatever looked good. Green is +Y (Unity's
    convention, and unitymeta.py leaves flipGreenChannel off), and because the row index
    grows downward while V grows upward the row derivative is used as it stands.
    """
    scale = np.float32(0.5 * relief_m / max(texel_m, 1e-9))
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * scale
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * scale
    nx = -dx
    ny = dy
    nz = np.ones_like(height)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack((nx * inv, ny * inv, nz * inv), axis=-1)


def ambient_occlusion(height, gain, radii=(3, 9, 27)):
    """Cavity occlusion: how far below its own neighbourhood a texel sits, measured at
    three radii so a blow hole, a weld groove and a panel recess all darken.

    `gain` is per material because the same height range means different things: a pitted
    casting shades itself, a sheet of painted steel barely does.
    """
    ao = np.ones_like(height)
    for i, r in enumerate(radii):
        cav = np.clip((blur(height, r) - height) * np.float32(gain), 0.0, 1.0)
        ao -= cav * np.float32(0.55 - 0.13 * i)
    return np.clip(ao, 0.0, 1.0)


def encode_normal(normal):
    return np.clip(normal * 0.5 + 0.5, 0.0, 1.0)


# --------------------------------------------------------------------------- writing
def _to_u8(a):
    return np.clip(a * 255.0 + 0.5, 0.0, 255.0).astype(np.uint8)


def write_map(array, out_dir, set_id, suffix, size, channels=None):
    """Write one map, validate it against the contract and return its manifest record.

    `channels` overrides the packing published for the suffix. The only user of it is the
    decal sheet, which is an _albedo file carrying an alpha coverage channel because a
    decal has to be composited over bodywork rather than mapped into it.
    """
    names = tuple(channels or CHANNELS[suffix])
    image = Image.fromarray(_to_u8(array))
    name = "%s_%s" % (set_id, suffix)
    validate.check_texture(name, image, expect_size=size, expect_channels=len(names))
    path = os.path.join(out_dir, name + ".png")
    os.makedirs(out_dir, exist_ok=True)
    # No optimize pass: it costs ten seconds on a noisy 2048 RGBA and saves 3 per cent.
    image.save(path, format="PNG")
    return {
        "id": name,
        "path": config.rel_to_root(path),
        "resource": config.resource_path(path),
        "kind": "texture",
        "set": set_id,
        "map": suffix,
        "size": size,
        "channels": list(names),
    }


# --------------------------------------------------------------------------- surfaces
# Each surface returns the four fields every later stage is derived from: a 0..1 height,
# a linear albedo, a roughness map and a metallic map. Nothing below paints a normal or
# an occlusion map by hand - those are read back out of the height.
def _painted_steel(shape, rng, spec):
    """Machine bodywork: stamped sheet under a thick industrial paint.

    The paint is deliberately a light neutral. Livery colour arrives as a tint on this
    shared set (_LiveryColor), so anything saturated here would fight every machine in
    the fleet; what the albedo carries is the dirt, the polish and the stamping shadow.
    """
    h = fbm(shape, 3, 4, rng) * 0.55                        # sheet not quite flat
    h += fbm(shape, 48, 3, rng) * 0.12                      # orange peel in the paint
    h += fbm(shape, (7, 200), 2, rng) * 0.05                # roller drag along the panel

    # Two shallow stiffening ribs, the kind pressed into a bonnet so it does not drum.
    rows = np.arange(shape[0], dtype=np.float32)[:, None] / shape[0]
    rib = np.zeros(shape, np.float32)
    for centre, wide in ((0.27, 0.035), (0.73, 0.05)):
        d = np.abs(((rows - centre + 0.5) % 1.0) - 0.5) / wide
        rib += np.clip(1.0 - d * d, 0.0, 1.0)
    h += rib * 0.22
    h += micro_relief(shape, rng, 0.26, freq_divisor=6)      # grain in the topcoat
    h = _norm01(h)

    scuff = scratch_field(shape, rng, 90, shape[1] * 0.10, width=1, angle_deg=8.0,
                          spread=40.0)
    scuff = np.clip(blur(scuff, 1) * 2.2, 0.0, 1.0)
    h -= scuff * 0.03

    grime = fbm(shape, 6, 4, rng)
    tone = 0.72 + 0.13 * (fbm(shape, 11, 3, rng) - 0.5)
    tone -= np.clip(grime - 0.50, 0, 1) * 0.34              # road film down the flanks
    tone -= np.clip(blur(h, 12) - h, 0.0, 1.0) * 1.6        # stamping shadow
    tone += scuff * 0.12
    albedo = np.stack((tone, tone * 0.995, tone * 0.985), axis=-1)

    rough = 0.34 + 0.30 * fbm(shape, 20, 3, rng) + 0.26 * grime
    rough -= scuff * 0.16                                   # a scuff polishes the paint
    metal = np.full(shape, 0.02, np.float32)
    return h, albedo, np.clip(rough, 0.05, 0.95), metal


def _galvanised(shape, rng, spec):
    """Lift steel: hot-dip galvanising over rolled section, then years of weather.

    The spangle is the giveaway of galvanising - crystal facets frozen into the zinc -
    so it is built as banded facets rather than as another blob of noise.
    """
    base = fbm(shape, 5, 4, rng)
    facet_seed = fbm(shape, 26, 2, rng)
    facets = np.abs(((facet_seed * 7.0) % 1.0) - 0.5) * 2.0   # crystal boundaries
    h = base * 0.45 + facets * 0.22
    h += fbm(shape, (9, 120), 3, rng) * 0.16                  # rolling direction
    h += ridged(shape, 60, 2, rng) * 0.08                     # mill scale
    h += micro_relief(shape, rng, 0.18, freq_divisor=6)
    h = _norm01(h)

    brush = scratch_field(shape, rng, 160, shape[1] * 0.25, width=1, angle_deg=0.0,
                          spread=8.0)
    brush = np.clip(blur(brush, 1) * 1.8, 0.0, 1.0)
    h += brush * 0.02

    mottle = fbm(shape, 8, 4, rng)
    tone = 0.52 + 0.13 * (facets - 0.5) + 0.09 * (mottle - 0.5)
    tone -= np.clip(blur(h, 10) - h, 0.0, 1.0) * 0.30
    albedo = np.stack((tone * 0.97, tone * 0.99, tone * 1.03), axis=-1)   # zinc runs cool

    rough = 0.34 + 0.22 * mottle + 0.12 * facets - brush * 0.10
    metal = np.clip(0.92 - 0.25 * np.clip(mottle - 0.6, 0, 1) * 2.5, 0.0, 1.0)
    return h, albedo, np.clip(rough, 0.05, 0.95), metal


def _weathered_paint(shape, rng, spec):
    """Props: signage, fencing, shack cladding. Painted timber and thin sheet, outside
    all winter, so the grain comes through the paint and the finish has gone chalky."""
    grain = fbm(shape, (4, 90), 4, rng)
    h = grain * 0.40 + fbm(shape, 5, 4, rng) * 0.35
    h += ridged(shape, (6, 40), 3, rng) * 0.18                # raised grain
    boards = np.abs(((np.arange(shape[0], dtype=np.float32)[:, None] / shape[0] * 6.0)
                     % 1.0) - 0.5) * 2.0
    h -= np.clip(1.0 - boards * 14.0, 0.0, 1.0) * 0.30        # gaps between boards
    h += micro_relief(shape, rng, 0.24, freq_divisor=6)       # chalked, open surface
    h = _norm01(h)

    nicks = speckle(shape, rng, shape[0] // 6, 0.012, softness=12.0)
    h -= nicks * 0.25

    tone = 0.66 + 0.10 * (grain - 0.5) + 0.07 * (fbm(shape, 9, 3, rng) - 0.5)
    tone -= np.clip(blur(h, 14) - h, 0.0, 1.0) * 0.45
    tone -= nicks * 0.22
    albedo = np.stack((tone * 1.01, tone, tone * 0.96), axis=-1)

    rough = 0.46 + 0.34 * fbm(shape, 14, 3, rng) + nicks * 0.22 + grain * 0.10
    metal = np.full(shape, 0.0, np.float32)
    return h, albedo, np.clip(rough, 0.05, 0.98), metal


def _glass(shape, rng, spec):
    """Cab glazing. Almost flat by definition - what sells glass is the near-zero
    roughness and the few things on it: wiper arcs, a salt haze, fine sand scoring."""
    h = fbm(shape, 3, 2, rng) * 0.7                            # float-glass waviness
    h += fbm(shape, 90, 2, rng) * 0.05
    wipe = scratch_field(shape, rng, 40, shape[1] * 0.55, width=2, angle_deg=6.0,
                         spread=6.0)
    wipe = np.clip(blur(wipe, 2) * 1.6, 0.0, 1.0)
    h += wipe * 0.10
    h += micro_relief(shape, rng, 0.04, freq_divisor=6)        # sand scoring, barely there
    h = _norm01(h)

    haze = fbm(shape, 7, 3, rng)
    tone = 0.055 + 0.02 * haze + wipe * 0.03
    albedo = np.stack((tone * 0.92, tone, tone * 1.06), axis=-1)

    rough = 0.04 + 0.10 * np.clip(haze - 0.6, 0, 1) * 2.5 + wipe * 0.08
    metal = np.full(shape, 0.0, np.float32)
    return h, albedo, np.clip(rough, 0.02, 0.6), metal


def _rubber(shape, rng, spec):
    """Track pads and tyres: moulded rubber, pebbled from the mould and full of pores,
    with the flash line the mould halves leave."""
    pebble = fbm(shape, 34, 3, rng)
    h = pebble * 0.6 + fbm(shape, 110, 2, rng) * 0.2
    pores = speckle(shape, rng, shape[0] // 4, 0.05, softness=10.0)
    h -= pores * 0.35
    cols = np.arange(shape[1], dtype=np.float32)[None, :] / shape[1]
    seam = np.clip(1.0 - np.abs(((cols - 0.5 + 0.5) % 1.0) - 0.5) / 0.006, 0.0, 1.0)
    h += seam * 0.30                                            # mould flash
    h += micro_relief(shape, rng, 0.14, freq_divisor=5)
    h = _norm01(h)

    bloom = fbm(shape, 12, 3, rng)                              # antiozonant bloom
    tone = 0.040 + 0.022 * bloom + 0.02 * pebble
    tone -= np.clip(blur(h, 6) - h, 0.0, 1.0) * 0.02
    albedo = np.stack((tone, tone * 0.99, tone * 0.97), axis=-1)

    rough = 0.74 + 0.26 * pebble - 0.14 * np.clip(bloom - 0.7, 0, 1) * 3.0
    metal = np.full(shape, 0.0, np.float32)
    return h, albedo, np.clip(rough, 0.3, 1.0), metal


def _concrete(shape, rng, spec):
    """Footings, pads and terminal plinths: board-formed concrete with the aggregate
    showing where the surface has spalled and the air voids the vibrator missed."""
    body = fbm(shape, 6, 5, rng)
    aggregate = fbm(shape, 40, 3, rng)
    h = body * 0.5 + aggregate * 0.25
    rows = np.arange(shape[0], dtype=np.float32)[:, None] / shape[0]
    board = np.abs(((rows * 8.0) % 1.0) - 0.5) * 2.0
    h -= np.clip(1.0 - board * 10.0, 0.0, 1.0) * 0.22           # form-board joint
    voids = speckle(shape, rng, shape[0] // 8, 0.02, softness=8.0)
    h -= voids * 0.45                                           # blow holes
    spall = np.clip((fbm(shape, 9, 3, rng) - 0.72) * 6.0, 0.0, 1.0)
    h -= spall * 0.20
    h += micro_relief(shape, rng, 0.34, freq_divisor=5)         # sand in the mix
    h = _norm01(h)

    tone = 0.50 + 0.11 * (body - 0.5) + 0.07 * (aggregate - 0.5)
    tone -= np.clip(blur(h, 16) - h, 0.0, 1.0) * 0.5
    tone += spall * 0.06                                        # fresh break is lighter
    albedo = np.stack((tone * 1.02, tone, tone * 0.95), axis=-1)

    rough = 0.62 + 0.30 * aggregate + voids * 0.12 + 0.12 * (body - 0.5)
    metal = np.full(shape, 0.0, np.float32)
    return h, albedo, np.clip(rough, 0.2, 1.0), metal


def micro_relief(shape, rng, amount, freq_divisor=8, octaves=3):
    """Detail near texel scale, which is the only thing a normal map can actually show.

    A height field built only from metre-scale noise differentiates to nothing: the slope
    between two adjacent texels is tiny however deep the shape is. Real surfaces carry
    grain a few texels wide, and this is it.
    """
    freq = max(4, min(shape[0], shape[0] // freq_divisor))
    return (fbm(shape, freq, octaves, rng) - 0.5) * np.float32(amount)


SURFACES = {
    "painted_steel": _painted_steel,
    "galvanised": _galvanised,
    "weathered_paint": _weathered_paint,
    "glass": _glass,
    "rubber": _rubber,
    "concrete": _concrete,
}


# --------------------------------------------------------------------------- wear
def wear_masks(height, spec, rng):
    """The four sim-driven masks, all keyed off the height field's own curvature.

    R edge wear   convex curvature: the rubbed corners, rib crests and door edges.
    G rust        starts in the concave curvature where water sits, then runs downward.
    B salt/dirt   the road film: heaviest at the bottom of the tile, thinning upward.
    A chipping    sparse, hard-edged flecks, thrown at the same edges as R but rarer.

    They are deliberately disjoint in where they live. ConditionVisuals brings them in at
    different thresholds, and four masks that all covered the same texels would just make
    one dirtier mask instead of a machine that ages in stages.
    """
    shape = height.shape
    convex = np.clip(curvature(height, 2), 0.0, 1.0)
    convex = np.maximum(convex, np.clip(curvature(height, 6), 0.0, 1.0) * 0.8)
    concave = np.clip(-curvature(height, 4), 0.0, 1.0)
    concave = np.maximum(concave, np.clip(-curvature(height, 12), 0.0, 1.0) * 0.9)

    # Where on the part the wear is: a machine is not worn evenly, it is worn where it is
    # climbed on, loaded and brushed past.
    zones = fbm(shape, 4, 3, rng)
    lower = 1.0 - np.arange(shape[0], dtype=np.float32)[:, None] / np.float32(shape[0])
    lower = np.clip((0.62 - lower) / 0.62, 0.0, 1.0)            # 1 at the bottom edge

    edge = convex * (0.45 + 0.85 * zones)
    edge = np.clip(edge * 1.45, 0.0, 1.0) ** 1.6                # keep it on the crests
    edge = np.maximum(edge, blur(edge, 2) * 0.6)

    seeds = concave * np.clip((fbm(shape, 13, 3, rng) - 0.42) * 3.2, 0.0, 1.0)
    seeds = np.maximum(seeds, speckle(shape, rng, shape[0] // 16, 0.006, softness=6.0)
                       * concave * 1.5)
    run = streak_down(seeds, max(8, shape[0] // 4), decay=1.0 - 12.0 / shape[0])
    rust = np.clip(seeds * 1.2 + run * 0.75 * (0.4 + 0.6 * fbm(shape, (5, 40), 3, rng)),
                   0.0, 1.0)
    rust *= 0.35 + 0.65 * zones

    salt = lower * (0.45 + 0.55 * fbm(shape, 9, 4, rng))
    salt = np.clip(salt * (1.0 + concave * 0.8) * 1.2, 0.0, 1.0)
    salt = np.maximum(salt, streak_down(salt * 0.5, max(6, shape[0] // 12),
                                        decay=1.0 - 20.0 / shape[0]))

    chip = speckle(shape, rng, shape[0] // 5, 0.030, softness=30.0)
    chip *= 0.15 + 1.6 * edge                                   # paint lets go at edges
    chip = np.clip(chip * (0.3 + 1.2 * zones), 0.0, 1.0)

    gain = spec.get("wear", (1.0, 1.0, 1.0, 1.0))
    return np.clip(np.stack((edge * gain[0], rust * gain[1],
                             salt * gain[2], chip * gain[3]), axis=-1), 0.0, 1.0)


# --------------------------------------------------------------------------- build
def build_set(spec, out_dir):
    """Build one material set: height field first, everything else derived from it."""
    size = int(spec["size"])
    shape = (size, size)
    rng = rng_for("pbr", spec["id"], spec["surface"])

    height, albedo, rough, metal = SURFACES[spec["surface"]](shape, rng, spec)
    height = np.clip(height, 0.0, 1.0)

    texel_m = spec["world_m"] / size
    normal = encode_normal(height_to_normal(height, spec["relief_m"], texel_m))

    ao = ambient_occlusion(height, spec["cavity"])
    albedo = np.clip(albedo * (0.45 + 0.55 * ao[..., None]), 0.0, 1.0)
    orm = np.stack((ao, np.clip(rough, 0.0, 1.0), np.clip(metal, 0.0, 1.0)), axis=-1)

    wear = wear_masks(height, spec, rng_for("pbr", spec["id"], "wear"))

    return [
        write_map(albedo, out_dir, spec["id"], "albedo", size),
        write_map(normal, out_dir, spec["id"], "normal", size),
        write_map(orm, out_dir, spec["id"], "orm", size),
        write_map(wear, out_dir, spec["id"], "wear", size),
    ]


def build_all(out_dir):
    """Every material set, in file order. Returns one manifest record per PNG."""
    records = []
    for spec in SETS:
        records += build_set(spec, out_dir)
    return records


# --------------------------------------------------------------------------- self-test
def report(records):
    """Read back what was written and print the structure that is actually in it.

    A flat fill and a painted surface both pass validation; only the per-channel spread
    tells them apart, so the build prints it.
    """
    print("%-18s %6s %-28s %s" % ("map", "size", "channels", "mean/std per channel"))
    for r in records:
        path = os.path.join(config.ROOT, r["path"])
        a = np.asarray(Image.open(path), np.float32) / 255.0
        stats = "  ".join("%s %.3f/%.3f" % (n[:4], a[..., i].mean(), a[..., i].std())
                          for i, n in enumerate(r["channels"]))
        print("%-18s %6d %-28s %s"
              % (r["id"], r["size"], ",".join(r["channels"]), stats))


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.TEXTURES_DIR
    validate.reset()
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
