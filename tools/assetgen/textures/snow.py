"""Snow surface detail: corduroy, wind crust, drift, dirty snow, powder and sparkle.

The snow map and the surface map that `SnowSurface.shader` already samples are simulation
data - depth, density, roughness, groom age, surface type - at one texel per metre. They
say what the snow IS. Nothing in them says what it LOOKS like within that metre, and a
metre is far bigger than a corduroy rib, a wind plate or a snow crystal. These maps are
that missing scale: tiling detail the shader can sample at world scale on top of its data
maps, so groomed snow shows the comb, wind crust shows its broken plates, and thin snow
shows the grit underneath it.

Numbers come from the data, not from taste. The rib spacing and depth are
`CorduroyWavelengthM` and `CorduroyDepthM` out of render.json, which are the same two
numbers the shader displaces vertices with, so the normal map and the vertex displacement
describe the same ribs instead of fighting each other.

Two conventions hold across every map here, so a shader author only has to learn them
once:

  * every directional feature runs along +U. Corduroy ribs, wind streaks and drift tails
    all lie along the U axis and vary along V, so one rotation in the shader (the groom
    direction the surface map already carries, or the wind bearing) orients all of them.
  * every map is seamless in both axes, and the normals are the true gradient of a real
    height field at the tile's own texel size, as docs/ART_CONTRACT.md section 6 requires.

Run standalone from tools/assetgen:  python3 -m textures.snow [out_dir]
"""
import os

import numpy as np
from PIL import Image

import config
from lib import datasrc, validate
from textures.pbr import (blur, encode_normal, fbm, height_to_normal, ridged, rng_for,
                          scratch_field, speckle)
# Wind crust is a broken-plate pattern, which is the same jittered-lattice distance field
# terrain.py builds scree, gravel and granite jointing from. One implementation of it.
from textures.terrain import cellular

PREFIX = "snow"

# What each map's channels mean. The suffix decides the Unity importer settings
# (lib/unitymeta.py), and the channel list is published in the manifest so a shader author
# never has to open the generator to find out what is in the blue channel.
CHANNELS = {
    "corduroy_height": ("height",),
    "corduroy_normal": ("x", "y", "z"),
    "crust_height": ("height",),
    "crust_normal": ("x", "y", "z"),
    "drift_mask": ("deposition", "scour", "streak"),
    "dirty_albedo": ("red", "green", "blue"),
    "powder_normal": ("x", "y", "z"),
    "detail": ("x", "y", "z", "grain"),
}


# --------------------------------------------------------------------------- helpers
def norm01(a):
    lo = float(np.min(a))
    hi = float(np.max(a))
    if hi - lo < 1e-9:
        return np.zeros_like(a)
    return (a - lo) / np.float32(hi - lo)


def _rows(shape):
    return np.arange(shape[0], dtype=np.float32)[:, None] / np.float32(shape[0])


def _cols(shape):
    return np.arange(shape[1], dtype=np.float32)[None, :] / np.float32(shape[1])


def _rgb(colour):
    return np.asarray(colour, np.float32)[None, None, :]


def _toward(albedo, colour, amount):
    """Blend an albedo toward a colour by a 0..1 field. Contamination replaces snow, it
    does not add to it: grit added to white would come out whiter."""
    return albedo + (_rgb(colour) - albedo) * amount[..., None]


def smear(field, length, axis, decay):
    """Drag a field along one axis with exponential falloff.

    A drift does not sit where the snow was picked up, it tails away downwind, and the
    same is true of a meltwater stain running downslope. This is textures/pbr.py's
    downward streak with the axis opened up, because wind runs across the map while water
    runs down a model. Doubling the shift each pass reaches `length` texels in a handful
    of whole-array operations instead of one pass per texel.
    """
    out = field.astype(np.float32, copy=True)
    step = 1
    while step < length:
        out = np.maximum(out, np.roll(out, step, axis=axis) * np.float32(decay ** step))
        step *= 2
    return out


# --------------------------------------------------------------------------- corduroy
def corduroy(shape, rng, wavelength_m, tile_m):
    """Groomed corduroy: the ribbing a tiller's finisher comb leaves behind it.

    The comb is a row of fingers dragged through the tilled snow, so what it leaves is a
    narrow groove and a broad rounded ridge, not a sine wave. It is also not perfect: the
    comb flexes and the machine wanders, so the rib lines bow slightly along their length
    and the whole set of them breathes.

    The tile is one tiller pass wide, so it carries one pass joint: the slightly deeper
    groove and small windrow of snow the outer finger pushes sideways where one pass
    overlaps the last. It sits in the middle of the tile rather than on the wrap, because
    the sharpest feature in a map is the last thing that should land on a seam.
    """
    ribs = max(1, int(round(tile_m / wavelength_m)))
    rows = _rows(shape)

    # The wander has to be tileable and gentle: a whole comb flexes together, so it moves
    # slowly across the ribs and only a little faster along them.
    wander = (fbm(shape, (2, 8), 3, rng) - 0.5) * 0.12
    phase = (rows * np.float32(ribs) + wander) * np.float32(2.0 * np.pi)
    rib = 0.5 - 0.5 * np.cos(phase)
    height = np.power(rib, 1.25) * 0.80          # narrow groove, broad crest

    joint = np.abs(rows - 0.5)
    height -= np.clip(1.0 - joint / 0.012, 0.0, 1.0) * 0.22   # the pass joint runs deeper
    height += np.clip(1.0 - np.abs(joint - 0.020) / 0.012, 0.0, 1.0) * 0.10
    # and throws a small windrow of snow sideways out of the overlap

    # Tilled snow is granular: the comb leaves the grain, it does not smooth it away.
    height += fbm(shape, 150, 3, rng) * 0.07
    height += fbm(shape, (4, 40), 3, rng) * 0.05          # chatter along the rib
    height += fbm(shape, 3, 4, rng) * 0.10                # the piste itself is not flat
    clods = speckle(shape, rng, shape[0] // 10, 0.02, softness=10.0)
    height += clods * 0.06                                # clods the tiller did not break
    return np.clip(norm01(height), 0.0, 1.0)


# --------------------------------------------------------------------------- wind crust
def crust(shape, rng):
    """Wind crust: a slab frozen hard on top, then broken into plates.

    Wind packs the surface into a slab and the sun glazes it. It is stiff, it is not
    bonded to the powder under it, and the first thing that crosses it breaks it into
    plates that tilt and ride over each other. The plates are longer across the wind than
    along it, so the lattice is stretched rather than square, and their edges are ragged
    because a slab fractures along its own grain and not along a Voronoi boundary.
    """
    f1, f2, ident = cellular(shape, (19, 10), rng, jitter=0.9)

    # A ragged fracture: modulating the edge width with noise breaks the straight cell
    # boundary into something that snapped rather than something that was drawn.
    width = 0.09 + 0.11 * fbm(shape, 36, 3, rng)
    edge = np.clip(1.0 - (f2 - f1) / width, 0.0, 1.0)

    height = 0.5 + (ident - 0.5) * 0.42                   # each plate rides at its own level
    height += np.clip(1.0 - f1 * 1.5, 0.0, 1.0) * 0.10    # and is domed by the wind
    height -= edge * 0.45                                 # the crack between them
    lip = np.clip((edge - 0.35) * 2.2, 0.0, 1.0) * np.clip((0.85 - edge) * 2.0, 0.0, 1.0)
    height += lip * 0.16                                  # the broken plate lifts at its edge

    # Sastrugi: the wind carves ridges along its own direction across the whole surface.
    height += ridged(shape, (26, 5), 3, rng) * 0.16
    height += fbm(shape, (40, 9), 3, rng) * 0.08
    height += fbm(shape, 4, 4, rng) * 0.12                # the drift underneath
    pits = speckle(shape, rng, shape[0] // 6, 0.03, softness=12.0)
    height -= pits * 0.10                                 # where the glaze has blown out
    return np.clip(norm01(height), 0.0, 1.0)


# --------------------------------------------------------------------------- drift
def drift_mask(shape, rng):
    """Where the wind put the snow, where it took it from, and the streaks it left.

    R deposition  the lee-side drift: snow that was picked up upwind and dropped here.
    G scour       the windward side of the same obstruction, blown down to old surface.
    B streak      the fine surface flow lines, which is what actually reads as wind.

    Deposition and scour are deliberately not each other's inverse. They are offset along
    the wind, because that is the physical relationship: a shelter scours immediately
    upwind of itself and deposits a tail downwind, and a shader that lerps snow depth with
    a single symmetric mask gets drifts on the wrong side of everything.
    """
    shelter = fbm(shape, (7, 4), 4, rng)
    shelter = np.clip((shelter - 0.46) * 2.8, 0.0, 1.0)

    tail = smear(shelter, max(8, shape[1] // 3), axis=1,
                 decay=1.0 - 5.0 / np.float32(shape[1]))
    gust = fbm(shape, (30, 5), 4, rng)
    deposition = np.clip(tail * (0.45 + 0.85 * gust) * 1.25, 0.0, 1.0)

    # Scour sits where the shelter is rising downwind, which is its windward face.
    lead = max(4, shape[1] // 24)
    scour = np.clip((np.roll(shelter, -lead, axis=1) - shelter) * 2.0, 0.0, 1.0)
    scour = np.clip(blur(scour, max(3, shape[0] // 160)) * 1.9 * (0.45 + 0.8 * gust),
                    0.0, 1.0)
    # Open ground between the drifts is scoured too, just evenly rather than in a line.
    scour = np.maximum(scour, np.clip((0.35 - deposition) * 1.1, 0.0, 1.0)
                       * np.clip((gust - 0.42) * 1.6, 0.0, 1.0))

    streak = ridged(shape, (70, 7), 3, rng)
    streak = np.clip((streak - 0.62) * 2.6, 0.0, 1.0)
    streak = np.maximum(streak, smear(speckle(shape, rng, shape[0] // 5, 0.015,
                                              softness=10.0),
                                      max(8, shape[1] // 12), axis=1,
                                      decay=1.0 - 26.0 / np.float32(shape[1])) * 0.7)
    return np.stack((deposition, scour, np.clip(streak, 0.0, 1.0)), axis=-1)


# --------------------------------------------------------------------------- dirty snow
def dirty_albedo(shape, rng):
    """What snow looks like when there is not much of it left.

    This is the variation map a shader lerps toward as depth falls: the same surface, but
    carrying everything a thin pack collects. Grit thrown off the access road, needles and
    twigs from the trees at the piste edge, the grey of refrozen melt, and the brown where
    the ground is coming through. It is authored as a full albedo rather than a mask so
    the lerp is one texture read and the contamination keeps its own colour.
    """
    base = np.full(shape + (3,), 0.0, np.float32)
    clean = fbm(shape, 18, 4, rng)
    base += _rgb((0.865, 0.885, 0.925)) + _rgb((0.055, 0.050, 0.045)) * (clean - 0.5)[..., None]

    # Refrozen, dirt-laden melt: grey, and pooled in the low ground rather than sprinkled.
    grime = np.clip((fbm(shape, 6, 5, rng) - 0.42) * 2.2, 0.0, 1.0)
    base = _toward(base, (0.505, 0.505, 0.510), grime * 0.70)

    # Road grit: fine, dark, dense near the contaminated end and thinning away from it.
    grit = speckle(shape, rng, shape[0] // 3, 0.10, softness=16.0)
    grit_field = np.clip(fbm(shape, 5, 3, rng) * 1.6 - 0.35, 0.0, 1.0)
    base = _toward(base, (0.135, 0.125, 0.115), np.clip(grit * grit_field * 1.4, 0.0, 0.9))

    # Pine litter: needles fall as short straight lines, so they are drawn as short
    # straight lines rather than approximated with a blob of brown noise.
    needles = scratch_field(shape, rng, 520, shape[1] * 0.016, width=1, spread=180.0)
    needles = np.clip(needles * 1.5, 0.0, 1.0) * np.clip(fbm(shape, 4, 3, rng) * 1.8 - 0.4,
                                                         0.0, 1.0)
    base = _toward(base, (0.240, 0.165, 0.085), np.clip(needles, 0.0, 0.95))
    bark_bits = speckle(shape, rng, shape[0] // 12, 0.010, softness=10.0)
    base = _toward(base, (0.185, 0.135, 0.090), bark_bits * 0.8)

    # Edge contamination: the ground itself showing through where the pack has worn out.
    through = np.clip((fbm(shape, 3, 4, rng) - 0.58) * 3.4, 0.0, 1.0)
    through = np.clip(through * (0.5 + 0.8 * fbm(shape, 14, 3, rng)), 0.0, 1.0)
    base = _toward(base, (0.285, 0.238, 0.182), through * 0.92)

    # Ski and board scrape: thin snow gets scraped to a harder, greyer surface in lines.
    scrape = scratch_field(shape, rng, 90, shape[1] * 0.30, width=2, angle_deg=0.0,
                           spread=26.0)
    scrape = np.clip(blur(scrape, 2) * 1.6, 0.0, 1.0)
    base = _toward(base, (0.640, 0.660, 0.700), scrape * 0.45)
    return np.clip(base, 0.0, 1.0)


# --------------------------------------------------------------------------- powder
def powder(shape, rng):
    """Fresh powder micro-relief: soft, shallow and almost shapeless, on purpose.

    New snow has no structure to speak of. What it has is the clumping of flakes that fell
    together and the faint dimpling where they landed, and the whole thing is soft because
    nothing has consolidated it yet. The temptation is to give it more relief than it has;
    a powder normal with hard edges reads as polystyrene.
    """
    height = fbm(shape, 22, 4, rng) * 0.62
    height += fbm(shape, 60, 3, rng) * 0.24               # clumps of flakes
    dimples = speckle(shape, rng, shape[0] // 4, 0.06, softness=7.0)
    height -= dimples * 0.18                              # where a flake cluster landed
    height += fbm(shape, 7, 3, rng) * 0.22                # the drift it settled into
    height = blur(height, 2, passes=1)                    # nothing in powder is sharp
    return np.clip(norm01(height), 0.0, 1.0)


# --------------------------------------------------------------------------- detail
def detail_grain(shape, rng):
    """Crystal-scale grain, packed as a detail normal plus a breakup channel.

    RGB is a tangent-space normal of the individual grains and A is their brightness
    variation. Sparkle is not a texture of bright dots: it is thousands of flat crystal
    facets each catching the sun at its own angle, so it has to live in the normal. The
    facets are built as cells with per-crystal steepness, which is why a few of them fire
    at any one viewing angle instead of the whole surface glittering at once.
    """
    f1, f2, ident = cellular(shape, (110, 110), rng, jitter=1.0)
    # A low ident is a rounded old grain, a high one a sharp new crystal. Raising the cone
    # to a per-cell power is what gives the set a mixture rather than one grain repeated.
    sharpness = 0.35 + 1.6 * ident
    facet = np.power(np.clip(1.0 - f1 * 1.25, 0.0, 1.0), sharpness)
    height = facet * (0.45 + 0.55 * ident)

    ff1, ff2, fident = cellular(shape, (260, 260), rng, jitter=1.0)
    height += np.power(np.clip(1.0 - ff1 * 1.2, 0.0, 1.0), 0.8) * 0.25 * fident
    height -= np.clip(1.0 - (f2 - f1) / 0.12, 0.0, 1.0) * 0.20   # the void between grains
    height += fbm(shape, 40, 3, rng) * 0.12
    height = np.clip(norm01(height), 0.0, 1.0)

    grain = np.clip(0.5 + (ident - 0.5) * 0.9 + (fbm(shape, 60, 3, rng) - 0.5) * 0.5,
                    0.0, 1.0)
    return height, grain


# --------------------------------------------------------------------------- writing
def write_map(array, out_dir, name, size, world_m):
    """Write one map, validate it against the contract and return its manifest record."""
    suffix = name[len(PREFIX) + 1:]
    data = np.clip(array * 255.0 + 0.5, 0.0, 255.0).astype(np.uint8)
    image = Image.fromarray(data)
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
        "set": PREFIX,
        "map": suffix,
        "size": size,
        "tileMeters": round(world_m, 4),
        "channels": list(CHANNELS[suffix]),
    }


def build_all(out_dir):
    """Every snow surface map, in file order. One manifest record per PNG.

    The two grooming numbers are read from render.json rather than chosen here, so a
    change to `CorduroyWavelengthM` moves the ribs in this map and in the shader's vertex
    displacement in the same build.
    """
    render = datasrc.render()
    wavelength = float(render["CorduroyWavelengthM"])
    depth = float(render["CorduroyDepthM"])

    size = config.TEX_SIZE_PROP
    shape = (size, size)
    records = []

    # One tiller pass across the tile, rounded to a whole number of ribs so the rib phase
    # is continuous at the seam. A 3.8 m finisher at a 0.12 m rib spacing is 32 ribs.
    ribs = max(1, int(round(3.8 / wavelength)))
    cord_tile = ribs * wavelength
    field = corduroy(shape, rng_for("snow", "corduroy"), wavelength, cord_tile)
    # The shader displaces vertices by sin() * CorduroyDepthM, so the full range of this
    # height field is two of those: matching it is what keeps the normal map and the
    # displacement describing the same rib.
    records.append(write_map(field, out_dir, "snow_corduroy_height", size, cord_tile))
    records.append(write_map(
        encode_normal(height_to_normal(field, depth * 2.0, cord_tile / size)),
        out_dir, "snow_corduroy_normal", size, cord_tile))

    crust_tile = 4.0
    field = crust(shape, rng_for("snow", "crust"))
    records.append(write_map(field, out_dir, "snow_crust_height", size, crust_tile))
    records.append(write_map(
        encode_normal(height_to_normal(field, 0.06, crust_tile / size)),
        out_dir, "snow_crust_normal", size, crust_tile))

    # Drift is a landscape-scale feature, so its tile covers a lot more ground than the
    # surface maps do and it is meant to be sampled at a much lower UV frequency.
    records.append(write_map(drift_mask(shape, rng_for("snow", "drift")),
                             out_dir, "snow_drift_mask", size, 24.0))

    records.append(write_map(dirty_albedo(shape, rng_for("snow", "dirty")),
                             out_dir, "snow_dirty_albedo", size, 8.0))

    powder_tile = 2.0
    records.append(write_map(
        encode_normal(height_to_normal(powder(shape, rng_for("snow", "powder")),
                                       0.032, powder_tile / size)),
        out_dir, "snow_powder_normal", size, powder_tile))

    # The detail map is tiled far more often than anything else here, so it is the small
    # size: at half a metre a tile, 512 texels is a third of a millimetre each.
    detail_size = config.TEX_SIZE_SMALL
    detail_tile = 0.5
    height, grain = detail_grain((detail_size, detail_size), rng_for("snow", "detail"))
    normal = encode_normal(height_to_normal(height, 0.0055, detail_tile / detail_size))
    records.append(write_map(np.concatenate((normal, grain[..., None]), axis=-1),
                             out_dir, "snow_detail", detail_size, detail_tile))
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
    """Read back what was written and print the structure that is actually in it."""
    print("%-26s %5s %-30s %-22s %s"
          % ("map", "size", "mean/std per channel", "seam row|col", "seam/local"))
    for r in records:
        path = os.path.join(config.ROOT, r["path"])
        a = np.asarray(Image.open(path), np.float32) / 255.0
        if a.ndim == 2:
            a = a[..., None]
        stats = " ".join("%.3f/%.3f" % (a[..., i].mean(), a[..., i].std())
                         for i in range(a.shape[2]))
        sv, lv, sh, lh = seam_stats(a)
        print("%-26s %5d %-30s %-22s %.2f|%.2f"
              % (r["id"], r["size"], stats, "%.4f|%.4f" % (sv, sh),
                 sv / max(lv, 1e-6), sh / max(lh, 1e-6)))


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.TEXTURES_DIR
    validate.reset()
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
