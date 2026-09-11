"""Contract enforcement. A model that breaks a rule fails the build, it does not warn.

The rules exist because each of them is a bug that is invisible in Blender and obvious
in the game: a machine at 1/100 scale, a blade that cannot be raised because its
transform is missing, a mesh whose inside faces out, a 90 000-triangle groomer that
tanks the frame rate when six of them are grooming at once.
"""
import math

from config import MIN_FEATURE_M, TRI_BUDGETS

# Articulation transforms every generator of that family must publish. Gameplay binds
# these by name through ArticulationBinder and throws if one is missing, so the build
# has to guarantee them.
REQUIRED_NODES = {
    "tracked": ("track_L", "track_R"),
    "wheeled": (),
    "artic": ("pivot_center",),
    "lift_terminal": ("bullwheel",),
    "lift_barn": (),           # a cabin garage is a shed on a storage rail, not a station
    "lift_tower": (),
    "lift_carrier": (),
    "attachment": (),          # an implement's transforms depend on its Kind, not the family
    "station": (),             # fixed plant: a gun's yaw and pitch depend on what kind it is
    "prop": (),                # scenery: a gate or a wind sock moves, nothing binds to it
}


class ContractError(Exception):
    """A generated asset violates docs/ART_CONTRACT.md."""


_issues = []


def reset():
    _issues.clear()


def issues():
    return list(_issues)


def note(kind, asset, message):
    _issues.append({"kind": kind, "asset": asset, "message": message})


def _fail(asset, message):
    raise ContractError("%s: %s" % (asset, message))


# --------------------------------------------------------------------------- models
def check_model(model, record):
    """Run every model rule. Raises ContractError on anything that would ship broken."""
    name = record.get("id", model.name)
    budget = record.get("budget", "prop")
    tris = record.get("triangles", [0])

    lo, hi = TRI_BUDGETS.get(budget, TRI_BUDGETS["prop"])
    if tris[0] > hi:
        _fail(name, "LOD0 is %d triangles, budget '%s' allows %d" % (tris[0], budget, hi))
    if tris[0] < lo:
        note("budget", name,
             "LOD0 is %d triangles, under the %d floor for '%s' - the generator may have "
             "degenerated" % (tris[0], lo, budget))

    for level in range(1, len(tris)):
        if tris[level] and tris[level] > tris[level - 1]:
            _fail(name, "LOD%d (%d tris) is heavier than LOD%d (%d tris)"
                  % (level, tris[level], level - 1, tris[level - 1]))
        if tris[level] == 0 and tris[0] > 0:
            note("lod", name, "LOD%d is empty" % level)

    _check_scale(name, record)
    _check_pivot(name, record)
    _check_geometry(model, name)
    _check_required_nodes(name, record)

    if record.get("hullFaces", 0) > 250:
        _fail(name, "convex collider has %d faces; Unity's limit is 255"
              % record["hullFaces"])


def _check_scale(name, record):
    lo = record.get("boundsMin") or [0, 0, 0]
    hi = record.get("boundsMax") or [0, 0, 0]
    size = [hi[i] - lo[i] for i in range(3)]
    longest = max(size) if size else 0.0
    if longest < 0.05:
        _fail(name, "is %.3f m across - that is not metres, check the generator's units"
              % longest)
    if longest > 400.0:
        _fail(name, "is %.1f m across; nothing in the fleet or on a lift line is that big"
              % longest)
    if min(size) < MIN_FEATURE_M and longest > 1.0:
        note("scale", name, "one axis is only %.3f m - the model may be flat" % min(size))


def _check_pivot(name, record):
    """The pivot sits at ground contact centre: y_min ~= 0 and x centred.

    An attachment is the exception: its origin is the mount point it bolts to, not a
    contact patch, so a blade's cutting edge legitimately hangs below y = 0 and a light
    tower's mast legitimately stands well above it.
    """
    lo = record.get("boundsMin") or [0, 0, 0]
    hi = record.get("boundsMax") or [0, 0, 0]
    if record.get("kind") == "attachment":
        # The ground rule does not apply, but something still has to: an implement whose
        # origin sits away from its own geometry would bolt to the socket and then float
        # alongside the machine. Check the mount point is on the implement instead.
        for axis, label in ((0, "X"), (1, "Y"), (2, "Z")):
            slack = max(0.5, hi[axis] - lo[axis])
            if lo[axis] - slack > 0.0 or hi[axis] + slack < 0.0:
                _fail(name, "mount point is outside the implement in %s (it spans %.2f to "
                            "%.2f m); the origin must sit where it bolts on"
                      % (label, lo[axis], hi[axis]))
        return
    if abs(lo[1]) > 0.25:
        _fail(name, "pivot is %.2f m off the ground; it must sit at the contact patch"
              % lo[1])
    half = (hi[0] - lo[0]) * 0.5
    centre = (hi[0] + lo[0]) * 0.5
    if half > 0.2 and abs(centre) > max(0.25, half * 0.25):
        note("pivot", name, "is %.2f m off the centreline in X" % centre)


def _check_geometry(model, name):
    """Non-manifold and zero-area geometry, counted straight off the bmeshes."""
    bad_edges = 0
    zero_faces = 0
    loose = 0
    for node_name in model.order:
        bm = model.nodes[node_name].bm
        for e in bm.edges:
            if len(e.link_faces) > 2:
                bad_edges += 1
        for f in bm.faces:
            if f.calc_area() < 1e-9:
                zero_faces += 1
        for v in bm.verts:
            if not v.link_edges:
                loose += 1
    if bad_edges:
        _fail(name, "%d non-manifold edges (more than two faces meet)" % bad_edges)
    if zero_faces:
        _fail(name, "%d zero-area faces" % zero_faces)
    if loose:
        note("geometry", name, "%d loose vertices" % loose)


def _check_required_nodes(name, record):
    family = record.get("family")
    required = REQUIRED_NODES.get(family, ())
    present = set(record.get("nodes", [])) | set(record.get("sockets", []))
    missing = [n for n in required if n not in present]
    if missing:
        _fail(name, "missing articulation transform(s): %s. docs/ART_CONTRACT.md lists "
                    "what a '%s' model must publish." % (", ".join(missing), family))


# --------------------------------------------------------------------------- textures
def check_texture(name, image, expect_size=None, expect_channels=None):
    w, h = image.size
    if w != h:
        note("texture", name, "is %dx%d; square textures pack and mip better" % (w, h))
    if w & (w - 1):
        _fail(name, "is %d px wide - texture sizes must be powers of two" % w)
    if expect_size and w != expect_size:
        _fail(name, "is %d px, expected %d" % (w, expect_size))
    if expect_channels and len(image.getbands()) != expect_channels:
        _fail(name, "has %d channels, expected %d"
              % (len(image.getbands()), expect_channels))


def check_uv_overlap(model, name, samples=4096):
    """Cheap overlap probe: bucket face UV centroids and flag heavy collisions.

    A full UV overlap test is a quadratic problem; what actually goes wrong in a
    procedural pipeline is a whole part projected onto the same square as another, and
    that shows up immediately as a centroid pile-up.
    """
    buckets = {}
    total = 0
    for node_name in model.order:
        bm = model.nodes[node_name].bm
        uv_layer = bm.loops.layers.uv.active
        if uv_layer is None:
            continue
        for f in bm.faces:
            total += 1
            if total > samples:
                break
            us = [l[uv_layer].uv for l in f.loops]
            cu = sum(u.x for u in us) / len(us)
            cv = sum(u.y for u in us) / len(us)
            key = (round(cu, 2), round(cv, 2))
            buckets[key] = buckets.get(key, 0) + 1
    if not total:
        return
    worst = max(buckets.values()) if buckets else 0
    if worst > max(24, total * 0.25):
        note("uv", name, "%d of %d faces share one UV cell - the unwrap has collapsed"
             % (worst, total))


# --------------------------------------------------------------------------- audio
def check_audio(name, samples, sample_rate, loop=True):
    import numpy as np

    if samples.ndim != 1:
        _fail(name, "must be mono")
    if not len(samples):
        _fail(name, "is empty")
    peak = float(np.max(np.abs(samples)))
    if peak > 1.0 + 1e-6:
        _fail(name, "clips (peak %.3f)" % peak)
    if peak < 0.05:
        note("audio", name, "peaks at only %.3f - it will be inaudible" % peak)
    if loop:
        edge = abs(float(samples[0]) - float(samples[-1]))
        if edge > 0.06:
            note("audio", name, "loop seam discontinuity %.3f - it will click" % edge)
    if not math.isfinite(float(np.sum(samples))):
        _fail(name, "contains NaN or infinity")
