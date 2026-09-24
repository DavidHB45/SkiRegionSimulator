"""Ground and structure materials: what the mountain and everything bolted to it is made of.

Eight tiling sets - alpine granite, scree, packed dirt track, alpine grass, conifer bark,
cast concrete, asphalt and gravel - each an albedo, a normal and an ORM pack. They dress
the terrain mesh, the tree and rock props, lift footings, the resort roads and the car
park, so they are the one texture family a player looks at from two metres and from two
kilometres in the same frame. That is why every surface here is built from a low
frequency structure (bedding planes, fragment size, patch boundaries, rut lines) as well
as a high frequency one: a material that carries only fine noise turns to a flat average
the moment it mips, and the mountain goes to porridge at distance.

Everything starts from a height field, exactly as textures/pbr.py does, and the normal is
the real gradient of that height at the set's own texel size. `world_m` is how much
ground one tile covers, so a 15 mm gap between two scree fragments comes out as 15 mm of
slope and not as whatever looked good.

Names carry a `terrain_` prefix because `concrete` is already the machine-side cast
concrete set in textures/pbr.py, and these two are not the same material: that one is a
plinth seen from a metre away, this one is a road surface seen from a helicopter.

Run standalone from tools/assetgen:  python3 -m textures.terrain [out_dir]
"""
import os

import numpy as np
from PIL import Image

import config
from lib import validate
from textures.pbr import (blur, encode_normal, fbm, height_to_normal, ridged, rng_for,
                          scratch_field, speckle)

PREFIX = "terrain"

CHANNELS = {
    "albedo": ("red", "green", "blue"),
    "normal": ("x", "y", "z"),
    "orm": config.ORM_CHANNELS,
}

# One record per set, in build order. `relief_m` is the peak to trough range of the
# height field in metres: it is what turns a 0..1 field into a believable slope.
# `ao_gain` is how hard the height's own cavities shade it. It is per material because
# the same height range means different things: a scree gap is a hole that swallows light,
# a rolled asphalt surface barely shades itself at all.
SETS = (
    {"id": "rock", "surface": "rock", "size": config.TEX_SIZE_PROP,
     "world_m": 4.0, "relief_m": 0.22, "ao_gain": 5.0},
    {"id": "scree", "surface": "scree", "size": config.TEX_SIZE_PROP,
     "world_m": 3.0, "relief_m": 0.13, "ao_gain": 7.5},
    {"id": "dirt", "surface": "dirt", "size": config.TEX_SIZE_PROP,
     "world_m": 4.0, "relief_m": 0.055, "ao_gain": 4.5},
    {"id": "grass_alpine", "surface": "grass_alpine", "size": config.TEX_SIZE_PROP,
     "world_m": 2.0, "relief_m": 0.07, "ao_gain": 6.5},
    {"id": "bark", "surface": "bark", "size": config.TEX_SIZE_SMALL,
     "world_m": 1.2, "relief_m": 0.035, "ao_gain": 8.0},
    {"id": "concrete", "surface": "concrete", "size": config.TEX_SIZE_PROP,
     "world_m": 2.5, "relief_m": 0.016, "ao_gain": 3.5},
    {"id": "asphalt", "surface": "asphalt", "size": config.TEX_SIZE_PROP,
     "world_m": 4.0, "relief_m": 0.022, "ao_gain": 4.5},
    {"id": "gravel", "surface": "gravel", "size": config.TEX_SIZE_PROP,
     "world_m": 2.0, "relief_m": 0.045, "ao_gain": 6.0},
)


# --------------------------------------------------------------------------- helpers
def norm01(a):
    lo = float(np.min(a))
    hi = float(np.max(a))
    if hi - lo < 1e-9:
        return np.zeros_like(a)
    return (a - lo) / np.float32(hi - lo)


def cellular(shape, cells, rng, jitter=1.0, warp=0.0, warp_freq=6):
    """Tileable jittered-lattice distance field: F1, F2 and a per-cell random value.

    Loose ground is cells, not noise. A scree slope is angular fragments with a shadowed
    gap between them, gravel is rounded stones in dust, granite is blocks split along its
    joints and wind crust is broken plates, and all four are the distance to the nearest
    scattered site. Distances come out in cell units, so an anisotropic `cells` count
    stretches the cells along one axis: that is how a plate ends up longer across the
    wind than along it without a second code path.

    `warp` is what stops it looking like paving. A bare Voronoi has straight boundaries
    meeting at tidy vertices, and no natural material does: rock splits along a crooked
    line and a tussock has no edge at all. Displacing the sample position by a noise field
    before the distance is measured bends every boundary by that much, in cell units, and
    it stays seamless because the displacement field tiles too.

    Wrapping the lattice index but not the site position is what keeps it seamless: the
    site a pixel near the right edge measures against is the one that will arrive from
    the left when the map tiles.
    """
    h, w = shape
    cy, cx = cells if isinstance(cells, (tuple, list)) else (cells, cells)
    cy = max(2, int(cy))
    cx = max(2, int(cx))
    off_y = rng.random((cy, cx), dtype=np.float32)
    off_x = rng.random((cy, cx), dtype=np.float32)
    ident = rng.random((cy, cx), dtype=np.float32)

    v = (np.arange(h, dtype=np.float32) + 0.5) * (np.float32(cy) / np.float32(h))
    u = (np.arange(w, dtype=np.float32) + 0.5) * (np.float32(cx) / np.float32(w))
    v = v[:, None] + np.zeros((1, w), np.float32)
    u = u[None, :] + np.zeros((h, 1), np.float32)
    if warp:
        v = v + (fbm(shape, warp_freq, 3, rng) - 0.5) * np.float32(2.0 * warp)
        u = u + (fbm(shape, warp_freq, 3, rng) - 0.5) * np.float32(2.0 * warp)
    iv = np.floor(v).astype(np.int32)
    iu = np.floor(u).astype(np.int32)

    f1 = np.full(shape, 1.0e9, np.float32)
    f2 = np.full(shape, 1.0e9, np.float32)
    who = np.zeros(shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gy = (iv + dy) % cy
            gx = (iu + dx) % cx
            sy = (iv + dy) + 0.5 + (off_y[gy, gx] - 0.5) * jitter
            sx = (iu + dx) + 0.5 + (off_x[gy, gx] - 0.5) * jitter
            d = np.sqrt((v - sy) ** 2 + (u - sx) ** 2).astype(np.float32)
            f2 = np.minimum(f2, np.maximum(f1, d))
            who = np.where(d < f1, ident[gy, gx], who)
            f1 = np.minimum(f1, d)
    return f1, f2, who


def occlusion(height, gain, radii=(3, 9, 27)):
    """Cavity occlusion: how far below its own neighbourhood a texel sits.

    Three radii, so the pore between two gravel stones, the gap between two fragments and
    the shadow inside a rock joint all darken instead of only whichever scale happened to
    match the blur. It lives here rather than coming from textures/pbr.py because the
    ground sets need their own shading gain per material and pbr's is tuned for bodywork.
    """
    ao = np.ones_like(height)
    for i, r in enumerate(radii):
        cav = np.clip((blur(height, r) - height) * np.float32(gain), 0.0, 1.0)
        ao -= cav * np.float32(0.52 - 0.12 * i)
    return np.clip(ao, 0.0, 1.0)


def _rows(shape):
    return np.arange(shape[0], dtype=np.float32)[:, None] / np.float32(shape[0])


def _cols(shape):
    return np.arange(shape[1], dtype=np.float32)[None, :] / np.float32(shape[1])


def _rgb(colour):
    """One colour as a broadcastable (1, 1, 3) array."""
    return np.asarray(colour, np.float32)[None, None, :]


def _mix(a, b, t):
    """Lerp two RGB triples by a 0..1 field, returning an (h, w, 3) array."""
    return _rgb(a) + (_rgb(b) - _rgb(a)) * t[..., None]


def _toward(albedo, colour, amount):
    """Blend an existing albedo toward a colour by a 0..1 field.

    Contamination is a lerp, never an addition. Lichen on granite, dust in a scree gap and
    a bitumen patch all replace the colour underneath them; adding them instead would
    blow the surface out to white wherever two of them overlapped.
    """
    return albedo + (_rgb(colour) - albedo) * amount[..., None]


# --------------------------------------------------------------------------- surfaces
# Each surface returns a 0..1 height, a linear albedo and a roughness map. Nothing paints
# a normal or an occlusion map by hand: those are read back out of the height, so the
# lighting always agrees with the relief.
def _rock(shape, rng, spec):
    """Alpine granite: bedded, jointed, frost-shattered, and lichened where it is sheltered.

    Above the treeline the rock is not a noise field, it is a structure. It was laid down
    in beds, it is cut by joints, and every winter water freezes in those joints and levers
    a block off, so the faces are angular and the scars are fresh. The lichen matters more
    than it sounds: it is the only warm colour on a grey mountain and it is what stops
    granite reading as concrete.

    The joints are deliberately few and large. A face of a hundred small even blocks is
    paving, not rock; what a crag actually shows is three or four metre-scale blocks with
    a rough, heavily relieved face on each of them and a finer shatter only along the
    edges where the frost has been working.
    """
    # The bedding dips across the face. Three beds per tile keeps the count integer, which
    # is what lets the band wrap at the seam.
    dip = (fbm(shape, (2, 4), 4, rng) - 0.5) * 0.75
    band = (_rows(shape) + dip) * 3.0 % 1.0
    bedding = 1.0 - np.abs(band - 0.5) * 2.0
    height = np.power(np.clip(bedding, 0.0, 1.0), 1.8) * 0.52

    # A handful of metre-scale blocks, their boundaries bent well off the straight.
    f1, f2, ident = cellular(shape, (4, 3), rng, jitter=0.9, warp=0.55, warp_freq=5)
    joint = np.clip(1.0 - (f2 - f1) / 0.055, 0.0, 1.0)
    height += (ident - 0.5) * 0.40                       # every block sits at its own level
    height -= joint * 0.42                               # and the joint between them is a cut

    # Frost shatter: a finer angular break that only bites near the joints and the edges,
    # because that is where the water gets in.
    sf1, sf2, sident = cellular(shape, (13, 11), rng, jitter=1.0, warp=0.45, warp_freq=9)
    near_edge = np.clip(joint * 2.2 + np.clip(1.0 - (f2 - f1) / 0.35, 0.0, 1.0), 0.0, 1.0)
    shatter = np.clip(1.0 - (sf2 - sf1) / 0.10, 0.0, 1.0)
    height -= shatter * near_edge * 0.16
    height += (sident - 0.5) * near_edge * 0.14

    # The face itself. Most of the relief on rock is the rock, not its joints.
    height += fbm(shape, 5, 6, rng) * 0.46
    height += ridged(shape, (7, 18), 4, rng) * 0.16      # the grain of the flake planes
    height += ridged(shape, 45, 3, rng) * 0.09           # crystal edges
    spall = np.clip((fbm(shape, 13, 3, rng) - 0.64) * 5.0, 0.0, 1.0)
    height -= spall * 0.20                               # a frost-shattered scar
    height = norm01(height)

    cavity = np.clip(blur(height, 9) - height, 0.0, 1.0)
    grain = fbm(shape, 30, 4, rng)
    coarse = fbm(shape, 6, 4, rng)

    dark = (0.300, 0.292, 0.284)
    pale = (0.585, 0.570, 0.545)
    albedo = _mix(dark, pale, np.clip(0.10 + 0.85 * grain + 0.50 * (ident - 0.5)
                                      + 0.55 * (coarse - 0.5), 0.0, 1.0))
    albedo += spall[..., None] * np.asarray((0.11, 0.105, 0.095), np.float32)  # fresh break
    albedo *= 1.0 - cavity[..., None] * 0.65

    # Lichen colonises the sheltered, damp side of a block and the crack lines, never the
    # faces the wind scours, so it is keyed to cavity rather than sprinkled at random.
    patch = fbm(shape, 8, 4, rng)
    crustose = np.clip((patch - 0.44) * 3.4, 0.0, 1.0) * (0.45 + 0.9 * cavity)
    crustose = np.clip(crustose * speckle(shape, rng, shape[0] // 14, 0.55, softness=3.5)
                       * 2.0, 0.0, 1.0)
    foliose = np.clip((fbm(shape, 20, 3, rng) - 0.60) * 4.0, 0.0, 1.0) * crustose
    albedo = _toward(albedo, (0.34, 0.40, 0.20), crustose * 0.85)
    albedo = _toward(albedo, (0.62, 0.60, 0.44), foliose * 0.65)

    rough = 0.80 + 0.14 * grain - crustose * 0.10 + joint * 0.05
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.3, 1.0)

def _scree(shape, rng, spec):
    """Loose angular fragments: the apron below every crag on the mountain.

    Scree is graded by gravity, so the plates that came off the crag end up at the bottom
    of the run and the chips filter into the gaps between them. It is built as two
    fragment scales with the fine one packed into the coarse one's shadows, and each
    fragment gets its own size out of the lattice value, because rubble of one size is
    cobbles and rubble of many sizes is scree.
    """
    f1, f2, ident = cellular(shape, (10, 10), rng, jitter=1.0, warp=0.40, warp_freq=7)
    gap = np.clip(1.0 - (f2 - f1) / 0.20, 0.0, 1.0)
    # Fragment size varies by a factor of two across the slope, and a broken stone has flat
    # faces and a sharp lip, so the profile is a hard shoulder rather than a dome.
    radius = 0.40 + 0.55 * ident
    slab = np.clip(1.0 - f1 / radius, 0.0, 1.0)
    height = np.power(slab, 0.40) * (0.40 + 0.60 * ident) * 0.80
    # Each fragment lies at its own angle rather than flat on the slope.
    tilt = (fbm(shape, 30, 2, rng) - 0.5) * slab
    height += tilt * 0.22

    ff1, ff2, fident = cellular(shape, (27, 27), rng, jitter=1.0, warp=0.35, warp_freq=14)
    chips = np.power(np.clip(1.0 - ff1 / (0.45 + 0.5 * fident), 0.0, 1.0), 0.5)
    height += chips * 0.24 * (0.25 + 1.0 * gap)          # chips filter into the gaps
    height -= gap * 0.40                                 # and the gaps go deep
    height += fbm(shape, 4, 5, rng) * 0.26               # the whole slope is not level
    height += fbm(shape, 80, 3, rng) * 0.06
    height = norm01(height)

    cavity = np.clip(blur(height, 7) - height, 0.0, 1.0)
    tone = np.clip(0.20 + 0.70 * ident + 0.45 * (fbm(shape, 34, 4, rng) - 0.5)
                   + 0.35 * (fbm(shape, 6, 3, rng) - 0.5), 0.0, 1.0)
    albedo = _mix((0.215, 0.208, 0.200), (0.580, 0.562, 0.530), tone)
    # A freshly split face is lighter than the weathered top of the same stone.
    fresh = np.clip((ident - 0.68) * 3.2, 0.0, 1.0) * np.clip(1.0 - f1 / radius, 0.0, 1.0)
    albedo += fresh[..., None] * 0.10
    albedo *= 1.0 - cavity[..., None] * 0.80             # the gaps are deep and dark

    dust = np.clip((fbm(shape, 15, 4, rng) - 0.45) * 2.2, 0.0, 1.0) * (0.35 + 0.9 * gap)
    albedo = _toward(albedo, (0.46, 0.42, 0.36), dust * 0.55)

    rough = 0.84 + 0.12 * fbm(shape, 26, 3, rng) + dust * 0.06
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.3, 1.0)

def _dirt(shape, rng, spec):
    """The summer track: a service road that has been driven on wet and then baked dry.

    The structure that reads is the pair of ruts, so they are built first and everything
    else is hung off them. A rut is lower, smoother and darker than the crown between the
    wheels, it holds the stones that got pressed into it, and it is where the tread print
    survives. Fourteen tread repeats across the tile keeps the print periodic at the seam,
    and the chevron apexes sit inside the tile rather than on it.
    """
    rows = _rows(shape)
    cols = _cols(shape)
    # Wheel tracks run along +U, at a plausible 1.8 m track width on a 4 m tile. A rut has
    # a shoulder where the tyre pushed the material sideways, not a soft airbrushed edge.
    rut = np.zeros(shape, np.float32)
    shoulder = np.zeros(shape, np.float32)
    for centre in (0.275, 0.725):
        d = np.abs(((rows - centre + 0.5) % 1.0) - 0.5) / 0.075
        rut += np.clip(1.0 - d * d * d, 0.0, 1.0)
        shoulder += np.clip(1.0 - np.abs(d - 1.15) * 7.0, 0.0, 1.0)
    wander = (fbm(shape, (3, 9), 3, rng) - 0.5) * 0.35
    rut = np.clip(rut + wander, 0.0, 1.0)
    shoulder = np.clip(shoulder + wander * 0.5, 0.0, 1.0)

    height = 0.50 + fbm(shape, 7, 5, rng) * 0.30
    height -= rut * 0.34                                  # the wheels pressed it down
    height += shoulder * 0.13                             # and pushed the spoil out sideways
    crown = np.clip(1.0 - rut * 1.8, 0.0, 1.0)
    height += crown * fbm(shape, 26, 4, rng) * 0.20       # loose material on the crown

    chevron = np.abs(((rows + 0.25) % 1.0) - 0.5)
    tread = 0.5 + 0.5 * np.cos((cols * 14.0 + chevron * 3.0) * 2.0 * np.pi)
    height -= np.power(tread, 3.0) * np.clip(rut * 1.4 - 0.25, 0.0, 1.0) * 0.22

    # Graded aggregate all through it, standing proud where the surface has dried out.
    stones = speckle(shape, rng, shape[0] // 7, 0.075, softness=9.0)
    grit = speckle(shape, rng, shape[0] // 3, 0.14, softness=14.0)
    height += stones * 0.22 * (0.35 + 0.8 * rut)          # gravel pressed into the rut
    height += grit * 0.07

    # Mud that was churned wet and dried cracks into plates; it only happens off the crown.
    cf1, cf2, cident = cellular(shape, (16, 14), rng, jitter=1.0, warp=0.40, warp_freq=11)
    crack = np.clip(1.0 - (cf2 - cf1) / 0.10, 0.0, 1.0)
    crack *= np.clip((fbm(shape, 6, 3, rng) - 0.36) * 3.0, 0.0, 1.0)
    height -= crack * 0.24
    height += fbm(shape, 110, 3, rng) * 0.07
    height = norm01(height)

    cavity = np.clip(blur(height, 8) - height, 0.0, 1.0)
    damp = np.clip(rut * 0.85 + (fbm(shape, 9, 4, rng) - 0.5) * 0.8, 0.0, 1.0)
    albedo = _mix((0.420, 0.355, 0.280), (0.175, 0.135, 0.100), damp)   # wet earth is dark
    albedo += (stones * 0.55 + grit * 0.35)[..., None] * np.asarray((0.20, 0.19, 0.175),
                                                                    np.float32)
    dust = crown * np.clip(fbm(shape, 12, 4, rng) * 1.4 - 0.2, 0.0, 1.0)
    albedo = _toward(albedo, (0.475, 0.415, 0.330), dust * 0.55)
    albedo *= 1.0 - cavity[..., None] * 0.62

    rough = 0.88 + 0.10 * fbm(shape, 30, 3, rng) - damp * 0.12
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.3, 1.0)

def _grass_alpine(shape, rng, spec):
    """Short alpine tussock: clumped, cropped, half dead and full of holes.

    Nothing above 2000 m grows as a lawn. It grows in tussocks with bare soil and stone
    between them, half of last year's growth is still standing and brown, and the whole
    thing is short because the wind crops it. The clumps come out of noise rather than out
    of a lattice, because a tussock has no boundary: it thins out into the next one, and a
    cell edge between them would read as turf laid in squares.
    """
    clump = np.clip((fbm(shape, 13, 5, rng) - 0.42) * 2.4, 0.0, 1.0)
    clump = np.clip(clump * (0.55 + 0.75 * fbm(shape, 34, 3, rng)), 0.0, 1.0)
    core = np.clip((fbm(shape, 30, 3, rng) - 0.55) * 3.6, 0.0, 1.0) * clump
    height = clump * 0.34 + core * 0.34

    # The blades are what the surface actually is; the clumps only say where they are tall.
    blades = scratch_field(shape, rng, 5200, shape[1] * 0.020, width=1,
                           angle_deg=90.0, spread=75.0)
    blades = np.clip(blades * 1.6, 0.0, 1.0)
    height += blades * 0.40 * (0.30 + 0.9 * clump)
    fine = scratch_field(shape, rng, 3200, shape[1] * 0.010, width=1,
                         angle_deg=90.0, spread=120.0)
    height += np.clip(fine * 1.4, 0.0, 1.0) * 0.16
    height += fbm(shape, 7, 4, rng) * 0.24                # the ground under it rolls
    bare = np.clip((0.34 - clump) * 3.0, 0.0, 1.0)
    stones = speckle(shape, rng, shape[0] // 9, 0.035, softness=10.0) * bare
    height += stones * 0.20
    height = norm01(height)

    cavity = np.clip(blur(height, 6) - height, 0.0, 1.0)
    dead = np.clip(0.28 + 0.85 * (fbm(shape, 9, 4, rng) - 0.42)
                   + 0.55 * (fbm(shape, 24, 3, rng) - 0.5), 0.0, 1.0)
    green = _mix((0.135, 0.215, 0.095), (0.300, 0.350, 0.150), fbm(shape, 28, 4, rng))
    straw = _mix((0.320, 0.280, 0.155), (0.475, 0.410, 0.240), fbm(shape, 21, 3, rng))
    albedo = green + (straw - green) * dead[..., None]
    soil = _mix((0.240, 0.200, 0.158), (0.345, 0.300, 0.240), fbm(shape, 30, 3, rng))
    albedo = albedo + (soil - albedo) * np.clip(bare * 0.95, 0.0, 1.0)[..., None]
    albedo += stones[..., None] * 0.13
    albedo *= 1.0 - cavity[..., None] * 0.70
    albedo += (blades * clump)[..., None] * np.asarray((0.06, 0.09, 0.035), np.float32)
    albedo *= (0.72 + 0.62 * np.clip(0.5 + (fbm(shape, 10, 3, rng) - 0.5) * 1.7
                                     + (core - 0.3) * 0.8, 0.0, 1.0))[..., None]

    rough = 0.78 + 0.15 * dead + 0.08 * bare
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.3, 1.0)

def _bark(shape, rng, spec):
    """Conifer bark for the tree props: deep vertical fissures between scaly plates.

    A spruce or a larch splits its bark along the trunk as the trunk swells, so the
    fissures run with V and the plates between them are long and narrow. It is the only
    set here that wraps around something, so the fissures are built from a V-elongated
    field that tiles in U at the trunk's circumference.
    """
    # Few lattice rows and many columns means the field changes slowly down the trunk and
    # quickly around it, which is exactly a fissure running with the grain.
    fissure = ridged(shape, (3, 26), 4, rng)
    plate = np.power(np.clip((fissure - 0.30) * 1.7, 0.0, 1.0), 0.8)
    height = plate * 0.70

    f1, f2, ident = cellular(shape, (26, 9), rng, jitter=1.0)
    scale = np.clip(1.0 - f1 * 1.2, 0.0, 1.0)
    height += scale * plate * 0.25 * (0.4 + 0.9 * ident)   # each plate is a stack of scales
    height -= np.clip(1.0 - (f2 - f1) / 0.14, 0.0, 1.0) * plate * 0.14
    height += fbm(shape, (4, 60), 3, rng) * 0.12           # fine grain along the fissures
    height += fbm(shape, 6, 4, rng) * 0.10                 # the trunk is not a cylinder
    height = norm01(height)

    cavity = np.clip(blur(height, 5) - height, 0.0, 1.0)
    tone = np.clip(0.30 + 0.6 * plate + 0.35 * (ident - 0.5), 0.0, 1.0)
    albedo = _mix((0.085, 0.062, 0.045), (0.370, 0.295, 0.215), tone)
    albedo *= 1.0 - cavity[..., None] * 0.85               # the fissures are nearly black
    # Old bark greys off and takes a dusting of lichen on the weather side.
    grey = np.clip((fbm(shape, 9, 3, rng) - 0.52) * 3.0, 0.0, 1.0) * plate
    albedo = _toward(albedo, (0.30, 0.30, 0.26), grey * 0.8)
    resin = speckle(shape, rng, shape[0] // 10, 0.006, softness=14.0)
    albedo += resin[..., None] * np.asarray((0.22, 0.16, 0.07), np.float32)

    rough = 0.90 + 0.08 * fbm(shape, 25, 3, rng) - resin * 0.45
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.25, 1.0)


def _concrete(shape, rng, spec):
    """Lift foundations, pump house walls, tower plinths: poured against forms and left.

    What identifies site concrete is not the aggregate, it is the joinery of the pour:
    the horizontal line where one lift of concrete met the next, the grid of holes the
    form ties left when the shutters came off, and the blow holes the vibrator missed.
    Four pour lines and a six by four tie grid per tile keep all of it periodic.
    """
    rows = _rows(shape)
    cols = _cols(shape)
    body = fbm(shape, 6, 5, rng)
    aggregate = fbm(shape, 44, 3, rng)
    height = body * 0.42 + aggregate * 0.20

    pour = np.abs(((rows * 4.0) % 1.0) - 0.5) * 2.0
    lip = np.clip(1.0 - pour * 22.0, 0.0, 1.0)
    height -= lip * 0.20                                   # the join between two lifts
    height += np.clip(1.0 - pour * 13.0, 0.0, 1.0) * 0.06  # grout that ran under the form

    # Form-tie holes: a regular grid, later filled, always slightly proud or slightly sunk.
    ties_y = np.abs(((rows * 4.0 + 0.5) % 1.0) - 0.5) * 2.0
    ties_x = np.abs(((cols * 6.0 + 0.5) % 1.0) - 0.5) * 2.0
    tie = np.clip(1.0 - np.sqrt(ties_y ** 2 + (ties_x * 0.67) ** 2) * 11.0, 0.0, 1.0)
    height -= tie * 0.34

    voids = speckle(shape, rng, shape[0] // 8, 0.022, softness=8.0)
    height -= voids * 0.40                                 # blow holes against the form
    spall = np.clip((fbm(shape, 10, 3, rng) - 0.74) * 6.0, 0.0, 1.0)
    height -= spall * 0.22
    height += fbm(shape, 130, 2, rng) * 0.05
    height = norm01(height)

    cavity = np.clip(blur(height, 12) - height, 0.0, 1.0)
    tone = np.clip(0.48 + 0.22 * (body - 0.5) + 0.14 * (aggregate - 0.5), 0.0, 1.0)
    albedo = _mix((0.310, 0.305, 0.295), (0.640, 0.630, 0.605), tone)
    albedo += spall[..., None] * np.asarray((0.07, 0.065, 0.06), np.float32)
    albedo *= 1.0 - cavity[..., None] * 0.60
    # Rain runs off the pour lip and stains the face under it, which is most of what makes
    # concrete outdoors look like it has been outdoors.
    below = (rows * 4.0 - 0.5) % 1.0
    stain = np.exp(-below * 7.0) * np.clip(fbm(shape, (5, 22), 3, rng) * 1.5 - 0.30,
                                           0.0, 1.0)
    albedo *= 1.0 - stain[..., None] * 0.26

    rough = 0.76 + 0.16 * aggregate + voids * 0.10 + stain * 0.05
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.25, 1.0)


def _asphalt(shape, rng, spec):
    """Resort road and car park: bitumen that has been through too many freeze cycles.

    Asphalt at altitude fails in a specific order. Water gets into the surface, freezes,
    and the binder lets go of the aggregate, so the surface ravels and the stones stand
    proud. Then it cracks, and the crack network is a cell boundary, not a scratch. Then
    somebody trenches it for a snowmaking main and patches it, and the patch never
    matches. All three are in here, and they are the low frequency that survives mipping.
    """
    aggregate = fbm(shape, 70, 3, rng)
    height = 0.55 + aggregate * 0.20 + fbm(shape, 9, 4, rng) * 0.18

    ravel = np.clip((fbm(shape, 13, 4, rng) - 0.48) * 2.6, 0.0, 1.0)
    stones = speckle(shape, rng, shape[0] // 6, 0.09, softness=11.0)
    height += stones * ravel * 0.28                       # stones out of the binder
    height -= ravel * 0.06

    cf1, cf2, cident = cellular(shape, (7, 7), rng, jitter=1.0)
    crack = np.clip(1.0 - (cf2 - cf1) / 0.045, 0.0, 1.0)
    crack = np.clip(crack * (0.45 + 1.1 * fbm(shape, 30, 3, rng)), 0.0, 1.0)
    height -= crack * 0.42
    branch = np.clip(ridged(shape, 18, 4, rng) - 0.80, 0.0, 1.0) * 5.0
    height -= np.clip(branch, 0.0, 1.0) * 0.18             # the fine cracking off the mains

    patch = np.clip((fbm(shape, 4, 3, rng) - 0.50) * 4.0, 0.0, 1.0)
    patch = np.clip(patch * 1.6, 0.0, 1.0)
    height += patch * 0.05                                 # a patch sits proud of the road
    height += fbm(shape, 150, 2, rng) * 0.04
    height = norm01(height)

    cavity = np.clip(blur(height, 7) - height, 0.0, 1.0)
    # A wheel path is polished dark; the middle of the lane keeps its grey aggregate.
    polish = np.zeros(shape, np.float32)
    for centre in (0.30, 0.70):
        d = np.abs(((_rows(shape) - centre + 0.5) % 1.0) - 0.5) / 0.10
        polish += np.clip(1.0 - d * d, 0.0, 1.0)
    polish = np.clip(polish * (0.6 + 0.6 * fbm(shape, (3, 9), 3, rng)), 0.0, 1.0)

    albedo = _mix((0.062, 0.060, 0.062), (0.185, 0.182, 0.180),
                  np.clip(0.30 + 0.7 * aggregate, 0.0, 1.0))
    albedo += (stones * ravel)[..., None] * np.asarray((0.16, 0.155, 0.145), np.float32)
    albedo *= 1.0 - polish[..., None] * 0.22
    albedo = _toward(albedo, (0.055, 0.052, 0.056), patch * 0.85)
    albedo *= 1.0 - cavity[..., None] * 0.75               # a crack is a black line

    rough = 0.74 + 0.18 * ravel + 0.10 * aggregate - polish * 0.22
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.2, 1.0)


def _gravel(shape, rng, spec):
    """Access road: crushed stone rolled into a bound surface, not loose rubble.

    The difference between this and scree is that gravel has been compacted. The stones
    are graded, they interlock, and the fines have been rolled into the gaps, so the
    surface is much flatter than its stone size suggests and the gaps read as a tone
    change rather than as a shadow.
    """
    f1, f2, ident = cellular(shape, (23, 23), rng, jitter=1.0)
    stone = np.power(np.clip(1.0 - f1 * 1.25, 0.0, 1.0), 0.65) * (0.45 + 0.55 * ident)
    height = stone * 0.60
    gap = np.clip(1.0 - (f2 - f1) / 0.15, 0.0, 1.0)

    ff1, ff2, fident = cellular(shape, (57, 57), rng, jitter=1.0)
    fines = np.power(np.clip(1.0 - ff1 * 1.2, 0.0, 1.0), 0.7)
    height += fines * 0.16 * (0.4 + 0.9 * gap)             # rolled into the voids
    height -= gap * 0.14
    height += fbm(shape, 6, 4, rng) * 0.18                 # the camber and the potholes
    pot = np.clip((fbm(shape, 11, 3, rng) - 0.70) * 5.0, 0.0, 1.0)
    height -= pot * 0.22
    height += fbm(shape, 110, 2, rng) * 0.04
    height = norm01(height)

    cavity = np.clip(blur(height, 6) - height, 0.0, 1.0)
    tone = np.clip(0.28 + 0.6 * ident + 0.3 * (fbm(shape, 45, 3, rng) - 0.5), 0.0, 1.0)
    albedo = _mix((0.245, 0.235, 0.220), (0.545, 0.530, 0.495), tone)
    dust = np.clip(0.35 + 0.8 * gap * fbm(shape, 20, 3, rng) * 2.0, 0.0, 1.0)
    albedo = _toward(albedo, (0.42, 0.40, 0.35), dust * 0.45)
    albedo *= 1.0 - cavity[..., None] * 0.55
    albedo *= 1.0 - pot[..., None] * 0.20                  # a pothole holds water and mud

    rough = 0.86 + 0.10 * dust + 0.06 * fbm(shape, 33, 3, rng)
    return np.clip(height, 0, 1), np.clip(albedo, 0, 1), np.clip(rough, 0.3, 1.0)


SURFACES = {
    "rock": _rock,
    "scree": _scree,
    "dirt": _dirt,
    "grass_alpine": _grass_alpine,
    "bark": _bark,
    "concrete": _concrete,
    "asphalt": _asphalt,
    "gravel": _gravel,
}


# --------------------------------------------------------------------------- writing
def write_map(array, out_dir, set_id, suffix, size, world_m):
    """Write one map, validate it against the contract and return its manifest record."""
    data = np.clip(array * 255.0 + 0.5, 0.0, 255.0).astype(np.uint8)
    image = Image.fromarray(data)
    name = "%s_%s_%s" % (PREFIX, set_id, suffix)
    validate.check_texture(name, image, expect_size=size,
                           expect_channels=len(CHANNELS[suffix]))
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".png")
    image.save(path, format="PNG", optimize=True)
    return {
        "id": name,
        "path": config.rel_to_root(path),
        "resource": config.resource_path(path),
        "kind": "texture",
        "set": "%s_%s" % (PREFIX, set_id),
        "map": suffix,
        "size": size,
        "tileMeters": world_m,
        "channels": list(CHANNELS[suffix]),
    }


def build_set(spec, out_dir):
    """Build one ground material: height field first, everything else derived from it."""
    size = int(spec["size"])
    shape = (size, size)
    rng = rng_for("terrain", spec["id"], spec["surface"])

    height, albedo, rough = SURFACES[spec["surface"]](shape, rng, spec)
    texel_m = spec["world_m"] / size
    normal = encode_normal(height_to_normal(height, spec["relief_m"], texel_m))

    ao = occlusion(height, spec["ao_gain"])
    albedo = np.clip(albedo * (0.55 + 0.45 * ao[..., None]), 0.0, 1.0)
    # Nothing on the ground is a metal, so the blue channel is flat and the pack is
    # really an AO and roughness pair. It stays a three-channel ORM because that is what
    # the contract says the shader samples.
    metal = np.zeros(shape, np.float32)
    orm = np.stack((ao, np.clip(rough, 0.0, 1.0), metal), axis=-1)

    return [
        write_map(albedo, out_dir, spec["id"], "albedo", size, spec["world_m"]),
        write_map(normal, out_dir, spec["id"], "normal", size, spec["world_m"]),
        write_map(orm, out_dir, spec["id"], "orm", size, spec["world_m"]),
    ]


def build_all(out_dir):
    """Every ground and structure set, in file order. One manifest record per PNG."""
    records = []
    for spec in SETS:
        records += build_set(spec, out_dir)
    return records


# --------------------------------------------------------------------------- self-test
def seam_stats(a):
    """How visible the wrap is, in the map's own units.

    A seamless map is one whose first and last row are as close to each other as any two
    neighbouring rows inside it, because when it tiles they become neighbours. The number
    that matters is therefore not the seam difference on its own but its ratio to the
    local row-to-row difference: at 1.0 the seam is invisible, at 5 it is a line.
    """
    local_v = float(np.mean(np.abs(a[1:] - a[:-1])))
    local_h = float(np.mean(np.abs(a[:, 1:] - a[:, :-1])))
    seam_v = float(np.mean(np.abs(a[0] - a[-1])))
    seam_h = float(np.mean(np.abs(a[:, 0] - a[:, -1])))
    return seam_v, local_v, seam_h, local_h


def report(records):
    """Read back what was written and print the structure that is actually in it.

    A flat fill and a real material both pass validation; only the per-channel spread and
    the seam ratio tell them apart, so the build prints both.
    """
    print("%-30s %5s %-26s %-28s %s"
          % ("map", "size", "mean/std per channel", "seam row|col (abs)", "seam/local"))
    for r in records:
        path = os.path.join(config.ROOT, r["path"])
        a = np.asarray(Image.open(path), np.float32) / 255.0
        if a.ndim == 2:
            a = a[..., None]
        stats = " ".join("%.3f/%.3f" % (a[..., i].mean(), a[..., i].std())
                         for i in range(a.shape[2]))
        sv, lv, sh, lh = seam_stats(a)
        print("%-30s %5d %-26s %-28s %.2f|%.2f"
              % (r["id"], r["size"], stats[:26], "%.4f|%.4f" % (sv, sh),
                 sv / max(lv, 1e-6), sh / max(lh, 1e-6)))


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.TEXTURES_DIR
    validate.reset()
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
