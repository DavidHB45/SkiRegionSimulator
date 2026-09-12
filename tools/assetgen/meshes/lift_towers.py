"""Lift towers: every mast on every line in lifts.json comes out of this generator.

A tower is a consequence of numbers, not of a name. MaxSpanM says how far the rope has
to reach and therefore how much it sags between supports; MaxVerticalM says how broken
the ground under it is; SeatsOrCabinCapacity sizes the carrier the rope has to fly clear
of; LineSpeedMs says how hard that carrier swings; RopeConfiguration decides how many
rope planes the crossarm carries and whether there are track ropes at all;
CapexPerTower is the steel budget, so it sets the mast diameter; and
FoundationConcreteM3PerTower is a real volume of concrete, so it sets the plinth. A
tricable tower ends up visibly massive and a platter tower ends up a stick with two
sheaves because those records say so, not because this file knows their ids.

Four structural forms fall out of the same record:

    tube      a tapered steel tube, everything from a platter to a funitel
    lattice   four legs and a bay of bracing, for the aerial classes
    trestle   a ground frame carrying a conveyor belt, no mast at all
    plinth    a rail pier: a concrete block with track on top

Each lift writes three height classes - `tower.fbx` plus `tower_low.fbx` and
`tower_high.fbx` - because a real line puts a short tower on the flat below the terminal
and a tall one over the convex break, and one height for every support reads as a toy.
The parts above the mast are identical across the three; only the mast, the ladder and
the plinth grow.
"""
import math
import os

from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

# Suffix and height factor for each class. The nominal tower keeps the name the model
# registry looks for; the other two are siblings beside it.
HEIGHT_CLASSES = (("", 1.00), ("low", 0.60), ("high", 1.45))

# Rope sag between two supports as a fraction of the span. A haul rope carries its
# carriers and hangs slack enough to be comfortable; a track rope is pulled down a
# shaft by a counterweight and hangs far tighter, which is why a tram crossing three
# kilometres does not need a tower six times the height of a chairlift's.
SAG_RATIO = {
    "Surface": 0.014,
    "Mono": 0.0075,
    "Bi": 0.0070,
    "Funitel": 0.0070,
    "Reversible": 0.0050,
    "Tri": 0.0075,
    "Rack": 0.0,
}

# How much of the line's rope tension the tower's sheave train has to spread. A
# monocable carries everything on the haul rope, so its trains are long; a reversible
# tram parks the load on track-rope saddles and only needs a short haul train.
TRAIN_LOAD = {
    "Surface": 0.55,
    "Mono": 1.00,
    "Bi": 1.15,
    "Funitel": 1.15,
    "Reversible": 0.10,
    "Tri": 0.10,
    "Rack": 0.0,
}

# Parts that butt against each other are overlapped by this much instead. meshkit welds
# vertices closer than a hundredth of a millimetre, and two boxes meeting on an exact
# plane would weld into an edge with four faces on it, which is the non-manifold failure
# in validate.py.
LAP = 0.004


# --------------------------------------------------------------------------- numbers
def _num(value, default=0.0):
    """A JSON field as a float; a missing, null or nonsense value falls back."""
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _seg(radius):
    """Sides on a revolved part, from how big it reads on screen.

    A 0.75 m tram sheave earns more sides than a 0.16 m guide roller, and the small
    stuff is where a procedural pipeline throws its budget away.
    """
    return int(_clamp(6.0 + radius * 10.0, 8.0, 12.0))


def _strut(m, a, b, thickness, mat=METAL, parent=None):
    """A square brace between two points, left unchamfered.

    Lattice bracing is repeated up the mast with meshkit's array, and array copies the
    faces it is handed: a chamfered box hands back the faces the bevel operation then
    replaces, so anything that gets repeated is built plain.
    """
    d = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    length = math.sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2])
    if length < 1e-4:
        return []
    mid = (0.5 * (a[0] + b[0]), 0.5 * (a[1] + b[1]), 0.5 * (a[2] + b[2]))
    return m.box(mid, (thickness, thickness, length), mat=mat, parent=parent,
                 rot=mk.look_rotation(d), bevel=False)


# --------------------------------------------------------------------------- the spec
def _spec(record, factor):
    """Every dimension of one tower, derived from the lift record and a height factor."""
    family = str(record.get("Family") or "Chair")
    rope = str(record.get("RopeConfiguration") or "Mono")
    grip = str(record.get("Grip") or "Fixed")
    span = _num(record.get("MaxSpanM"), 200.0)
    vertical = _num(record.get("MaxVerticalM"), 300.0)
    speed = _num(record.get("LineSpeedMs"), 2.5)
    capacity = max(1.0, _num(record.get("SeatsOrCabinCapacity"), 4.0))
    capex = max(1000.0, _num(record.get("CapexPerTower"), 50000.0))
    concrete = max(0.2, _num(record.get("FoundationConcreteM3PerTower"), 8.0))
    exposure = _num(record.get("WeatherExposure"), 1.0)
    detachable = grip == "Detachable"

    # What hangs below the rope, and how much air has to be left under it. Skiers pass
    # under a chair; a groomer working the piste has to pass under a cabin.
    if family == "Rail":
        # A rail pier carries a track bed, not a rope in the air. Its height is what the
        # ground under it asks for, and the poured volume is the only honest measure of
        # that: the deeper the dip a pier bridges, the more concrete it takes.
        rope_y = _clamp(0.35 + 0.25 * concrete ** (1.0 / 3.0), 0.45, 1.8) * factor
    else:
        if family == "Surface":
            drop, clearance = 0.9, 2.1
        else:
            body = 1.5 + 0.55 * capacity ** 0.25
            hanger = 2.0 + (0.7 if detachable else 0.25)
            drop = body + hanger
            clearance = 3.2 if family in ("Gondola", "Aerial") else 2.5

        # Rope height at the tower: carrier clearance, plus the sag it has to be lifted
        # out of, plus what broken ground and a fast swinging carrier ask for on top.
        sag = SAG_RATIO.get(rope, 0.008) * span
        rope_y = drop + clearance + sag + 0.0018 * vertical + 0.22 * speed
        # The low tower is the one on the flat: it still owes the carrier its clearance.
        rope_y = max(drop + clearance, rope_y * factor) if factor < 1.0 else rope_y * factor

    # Structural form. A foundation under a cubic metre is a bearing pad, not a footing
    # that could hold a mast upright, so those lifts get a ground frame.
    if family == "Rail":
        kind = "plinth"
    elif concrete < 1.0:
        kind = "trestle"
    elif family == "Aerial":
        kind = "lattice"
    else:
        kind = "tube"

    # Rope planes. A monocable straddles the mast with the up line one side and the down
    # line the other, far enough apart that carriers pass clear; a funitel runs two
    # loops a fixed distance apart on each side; a tricable hangs its haul rope between
    # two track ropes.
    carrier_w = 0.9 + 0.55 * capacity ** 0.42
    line_half = 0.5 * (carrier_w + 2.6 + min(0.004 * span, 2.0))
    if rope == "Surface":
        line_half = max(1.1, line_half * 0.42)          # one short arm, ropes close in
    planes = [line_half]
    if rope in ("Bi", "Funitel"):
        planes = [line_half - 1.6, line_half + 1.6]
    saddles = []
    if rope == "Tri":
        saddles = [line_half - 1.25, line_half + 1.25]

    # Sheaves per rope plane: a longer span means more rope weight and more tension on
    # the tower, and the train grows in pairs to spread it. Two, four, six or eight.
    load = span / 200.0 * TRAIN_LOAD.get(rope, 1.0)
    per_plane = int(_clamp(2 * math.ceil(max(load, 0.5)), 2, 8))
    if len(planes) > 1:
        per_plane = max(2, per_plane // 2)               # the load is split over two loops

    # Sheave diameter follows the rope it carries, and rope diameter follows tension:
    # more span and heavier carriers, thicker rope, bigger sheave.
    sheave_r = 0.5 * _clamp(0.30 + 0.0004 * span + 0.004 * capacity, 0.32, 0.78)
    sheave_w = sheave_r * 0.55
    pitch = sheave_r * 2.9

    # Mast section. Height gives the lever arm and the capex gives the steel, so both
    # belong in the diameter; below the crossarm the tube tapers out toward the base.
    mast_r1 = _clamp(0.05 + 0.010 * rope_y + 0.40 * math.sqrt(capex / 1.0e6), 0.12, 0.70)
    mast_r0 = mast_r1 * 1.45
    lat_top = _clamp(0.085 * rope_y, 1.8, 4.6)
    lat_base = lat_top * 1.85

    arm_depth = _clamp(0.20 + 0.035 * rope_y, 0.30, 0.95)
    train_len = per_plane * pitch
    # The rope rests on top of the sheaves, the train hangs off the rockers and the
    # rockers hang off the arm, so the arm sits well below the rope line.
    rocker_levels = 1 + (1 if per_plane >= 4 else 0) + (1 if per_plane >= 8 else 0)
    arm_y = rope_y - sheave_r - 0.30 - 0.26 * rocker_levels - arm_depth * 0.5

    # Only the visible pedestal is modelled: a footing is mostly buried, and the cube
    # root of the poured volume is what sets how big that pedestal reads.
    plinth_side = _clamp(1.35 * concrete ** (1.0 / 3.0), 0.9, 10.0)
    plinth_h = rope_y if family == "Rail" else _clamp(0.30 + 0.055 * rope_y, 0.30, 1.60)
    # A carpet's belt runs a hand's breadth off the snow on adjustable legs, so its
    # height class is the leg setting rather than a mast length.
    deck = _clamp(0.12 * rope_y, 0.22, 0.75)

    return {
        "id": record.get("Id", "lift"),
        "kind": kind,
        "family": family,
        "rope": rope,
        "detachable": detachable,
        "span": span,
        "exposure": exposure,
        "capacity": capacity,
        "rope_y": rope_y,
        "arm_y": arm_y,
        "arm_depth": arm_depth,
        "mast_top": arm_y + arm_depth * 0.5,
        "mast_r0": mast_r0,
        "mast_r1": mast_r1,
        "lat_top": lat_top,
        "lat_base": lat_base,
        "planes": planes,
        "saddles": saddles,
        "per_plane": per_plane,
        "sheave_r": sheave_r,
        "sheave_w": sheave_w,
        "pitch": pitch,
        "train_len": train_len,
        "arm_half": (max(saddles) if saddles else max(planes)) + 0.55,
        "plinth_side": plinth_side,
        "plinth_h": plinth_h,
        "concrete": concrete,
        "deck": deck,
        "night": "night_lighting" in (record.get("Options") or []),
    }


# --------------------------------------------------------------------------- foundation
def _ring(half, y, cut):
    """Square ring with cut corners, as Unity points at height `y`."""
    a, b = half, half - cut
    return [(-b, y, -a), (b, y, -a), (a, y, -b), (a, y, b),
            (b, y, a), (-b, y, a), (-a, y, b), (-a, y, -b)]


def _foundation(m, s, side=None):
    """Concrete pedestal with a chamfered top, a base plate and its anchor bolts.

    The block is battered - wider at grade than at the top - the way a poured footing
    is, and the top edge is chamfered so water and ice shed off it instead of spalling
    the corner.
    """
    half = 0.5 * (side or s["plinth_side"])
    h = s["plinth_h"]
    cut = _clamp(half * 0.22, 0.05, 0.5)
    cham = _clamp(half * 0.10, 0.03, 0.18)
    top = half * 0.84
    m.loft([_ring(half, 0.0, cut),
            _ring(top, h - cham, cut * 0.84),
            _ring(top - cham, h, cut * 0.84)], mat=METAL)

    if s["kind"] in ("trestle", "plinth"):
        return
    if s["kind"] == "lattice":
        # A lattice tower stands on four separate leg anchors, not on one base plate.
        half = s["lat_base"] * 0.5
        for sx in (-1.0, 1.0):
            for sz in (-1.0, 1.0):
                m.box((sx * half, h + 0.05, sz * half), (0.55, 0.12, 0.55), mat=METAL)
                for bz in (-0.18, 0.18):
                    m.cylinder((sx * half + bz, h + 0.14, sz * half), 0.035, 0.14,
                               axis=1, segments=6, mat=METAL)
        return
    plate_r = s["mast_r0"] * 1.6
    m.cylinder((0.0, h + 0.03, 0.0), plate_r, 0.06 + LAP, axis=1, segments=_seg(plate_r),
               mat=METAL)
    bolts = int(_clamp(4.0 + plate_r * 6.0, 6.0, 12.0))
    for i in range(bolts):
        a = 2.0 * math.pi * i / bolts
        m.cylinder((math.cos(a) * plate_r * 0.84, h + 0.10, math.sin(a) * plate_r * 0.84),
                   0.035, 0.16, axis=1, segments=6, mat=METAL)


# --------------------------------------------------------------------------- masts
def _mast_tube(m, s):
    """Tapered tube mast with a base collar and a splice flange on the tall ones."""
    y0, y1 = s["plinth_h"], s["mast_top"]
    length = y1 - y0
    seg = _seg(s["mast_r0"])
    m.cylinder((0.0, (y0 + y1) * 0.5, 0.0), s["mast_r0"], length + LAP, axis=1,
               segments=seg, mat=BODY, radius_end=s["mast_r1"])
    m.cylinder((0.0, y0 + 0.12, 0.0), s["mast_r0"] * 1.18, 0.10, axis=1, segments=seg,
               mat=METAL)
    if length > 11.0:
        y = y0 + length * 0.5
        r = s["mast_r0"] + (s["mast_r1"] - s["mast_r0"]) * 0.5
        m.cylinder((0.0, y, 0.0), r * 1.16, 0.09, axis=1, segments=seg, mat=METAL)


def _mast_lattice(m, s):
    """Four legs and one arrayed bay of bracing, on a splayed base frame.

    The bay above the batter is prismatic so a single bay of legs, ties and diagonals
    can be repeated up the mast; the splay that makes the tower look planted is a
    separate frame at the bottom, which is also how these are built.
    """
    y0, y1 = s["plinth_h"], s["mast_top"]
    top, base = s["lat_top"] * 0.5, s["lat_base"] * 0.5
    leg = _clamp(s["lat_top"] * 0.09, 0.12, 0.30)
    batter = min(0.28 * (y1 - y0), 7.0)
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))

    for sx, sz in corners:
        _strut(m, (sx * base, y0, sz * base), (sx * top, y0 + batter, sz * top), leg,
               mat=BODY)
    for i in range(4):
        ax, az = corners[i]
        bx, bz = corners[(i + 1) % 4]
        _strut(m, (ax * base, y0 + batter * 0.45, az * base),
               (bx * base, y0 + batter * 0.45, bz * base), leg * 0.55)

    shaft = y1 - (y0 + batter)
    bays = max(2, int(round(shaft / (s["lat_top"] * 1.3))))
    bay_h = shaft / bays
    y = y0 + batter
    faces = []
    for i in range(4):
        ax, az = corners[i]
        bx, bz = corners[(i + 1) % 4]
        faces += m.box((ax * top, y + bay_h * 0.5, az * top), (leg, bay_h + LAP, leg),
                       mat=BODY, bevel=False)
        span_x = abs(ax - bx) * top or leg * 0.8
        span_z = abs(az - bz) * top or leg * 0.8
        faces += m.box((0.5 * (ax + bx) * top, y + bay_h - leg * 0.5,
                        0.5 * (az + bz) * top), (span_x, leg * 0.8, span_z),
                       mat=METAL, bevel=False)
        faces += _strut(m, (ax * top, y + 0.05, az * top),
                        (bx * top, y + bay_h - 0.05, bz * top), leg * 0.5)
    m.array(faces, bays, (0.0, bay_h, 0.0))

    cap = s["lat_top"] * 0.5 + leg
    m.box((0.0, y1 - 0.10, 0.0), (cap * 2.0, 0.20, cap * 2.0), mat=BODY)


# --------------------------------------------------------------------------- crossarm
def _arm_ring(x, depth, width, y):
    """One cross-section of the fabricated arm: a box girder with a chamfered soffit."""
    hd, hw = depth * 0.5, width * 0.5
    c = min(depth, width) * 0.28
    return [(x, y + hd, -hw), (x, y + hd, hw), (x, y - hd + c, hw), (x, y - hd, hw - c),
            (x, y - hd, -hw + c), (x, y - hd + c, -hw)]


def _crossarm(m, s):
    """The arm as a fabricated beam: deep over the mast, shallow at the tips.

    A crossarm carries its whole load in bending at the mast and almost none at the tip,
    so it is built as a plate girder whose web tapers out to the ends. A box of constant
    depth would read as programmer art from the chair below it.
    """
    half = s["arm_half"]
    y = s["arm_y"]
    root = s["arm_depth"]
    tip = root * 0.52
    width = _clamp(root * 0.85, 0.24, 0.70)
    sections = []
    for t in (-1.0, -0.55, 0.0, 0.55, 1.0):
        depth = tip + (root - tip) * (1.0 - abs(t)) ** 0.7
        sections.append(_arm_ring(t * half, depth, width, y + (root - depth) * 0.5))
    m.loft(sections, mat=BODY, parent="crossarm")

    # Gussets tying the arm down to the mast head, and the head casting itself.
    head = s["mast_r1"] if s["kind"] == "tube" else s["lat_top"] * 0.45
    for sx in (-1.0, 1.0):
        m.beam((sx * head * 0.8, y - root * 0.45, 0.0),
               (sx * head * 2.6, y + root * 0.30, 0.0), width * 0.5, mat=METAL,
               parent="crossarm")
    m.box((0.0, y + root * 0.5 - 0.02, 0.0), (head * 2.4, root * 0.45, width * 1.5),
          mat=METAL, parent="crossarm")


def _rocker(m, s, x, za, zb, y_axle, y_top, thick):
    """A balance beam between two hang points.

    The rockers are what make a sheave train read as a sheave train: without them a row
    of wheels is just a row of wheels, and with them the eye sees the load being split
    pair by pair down the chain.
    """
    zc = 0.5 * (za + zb)
    lip = _clamp(abs(zb - za) * 0.16, 0.06, 0.22)
    profile = [(y_axle - 0.055, za), (y_axle + 0.055, za), (y_top, zc - lip),
               (y_top, zc + lip), (y_axle + 0.055, zb), (y_axle - 0.055, zb)]
    m.prism(profile, (x, 0.0, 0.0), thick, mat=METAL, parent="crossarm", axis=0)


def _train(m, s, x, sign, y_axle):
    """One rope plane: the rocker chain, its guards, and the sheave positions on it.

    Returns the axle points so the caller can name the sheaves uphill-first across the
    whole tower rather than per side.
    """
    n = s["per_plane"]
    pitch = s["pitch"]
    zs = [(i - (n - 1) * 0.5) * pitch for i in range(n)]
    plate = x + sign * (s["sheave_w"] * 0.5 + 0.06)
    thick = 0.055

    level = y_axle
    groups = [zs[i:i + 2] for i in range(0, n, 2)]
    for pair in groups:
        _rocker(m, s, plate, pair[0], pair[-1], level, level - 0.24, thick)
    while len(groups) > 1:
        level -= 0.26
        merged = []
        for i in range(0, len(groups), 2):
            block = groups[i:i + 2]
            za = 0.5 * (block[0][0] + block[0][-1])
            zb = 0.5 * (block[-1][0] + block[-1][-1])
            if za != zb:
                _rocker(m, s, plate, za, zb, level, level - 0.24, thick * 1.15)
            merged.append([za, zb])
        groups = merged
    # The king post: the whole train hangs off the arm on this one pin, so it has to
    # actually reach the arm's top plate rather than stop short above it.
    hang = 0.5 * (groups[0][0] + groups[0][-1])
    crown = level - 0.24
    arm_top = s["arm_y"] + s["arm_depth"] * 0.5 - LAP
    m.box((0.5 * (x + plate), 0.5 * (crown + arm_top), hang),
          (s["sheave_w"] + 0.16, max(0.16, crown - arm_top), 0.24), mat=METAL,
          parent="crossarm")

    # Anti-derail guards: the rope cannot leave the train sideways past these, and their
    # turned-up ends are what catch it if it ever tries.
    half = s["train_len"] * 0.5 + s["sheave_r"] * 0.4
    lift = s["sheave_r"] * 0.55
    for gsign in (-1.0, 1.0):
        gx = x + gsign * (s["sheave_w"] * 0.5 + 0.035)
        guard = [(y_axle + s["sheave_r"] * 0.55, -half),
                 (y_axle + s["sheave_r"] * 0.55 + lift, -half - 0.22),
                 (y_axle + s["sheave_r"] * 0.85 + lift, -half - 0.22),
                 (y_axle + s["sheave_r"] * 0.85, -half),
                 (y_axle + s["sheave_r"] * 0.85, half),
                 (y_axle + s["sheave_r"] * 0.85 + lift, half + 0.22),
                 (y_axle + s["sheave_r"] * 0.55 + lift, half + 0.22),
                 (y_axle + s["sheave_r"] * 0.55, half)]
        m.prism(guard, (gx, 0.0, 0.0), 0.05, mat=METAL, parent="crossarm", axis=0)
    return [(x, y_axle, z) for z in zs]


def _sheave(m, s, name, axle):
    """One lathed sheave with a rope groove, in its own node, spinning about X.

    Geometry goes in at the axle's model-space position, not at the origin: meshkit
    rebases each node's mesh onto its own pivot at finalise, so a part authored where it
    actually sits is a part that ends up where it actually sits.
    """
    r, w = s["sheave_r"], s["sheave_w"]
    profile = [(r * 0.34, -w * 0.5), (r, -w * 0.30), (r * 0.82, 0.0),
               (r, w * 0.30), (r * 0.34, w * 0.5)]
    m.lathe(profile, axle, segments=_seg(r), mat=METAL, parent=name, axis=0)


def _saddle(m, s, x, y):
    """A track-rope shoe: a grooved arc the rope lies in rather than a wheel it rides.

    On a tricable the track ropes carry the cabin's weight and do not move, so the tower
    does not turn a wheel under them - it cradles them in a long shoe, and that shoe
    sits outboard of the haul-rope sheaves.
    """
    length = s["train_len"] * 0.9
    radius = length * 1.35
    groove = s["sheave_r"] * 0.62
    rings = []
    for i in range(4):
        t = (i / 3.0 - 0.5) * (length / radius)
        z = math.sin(t) * radius
        dy = (math.cos(t) - 1.0) * radius
        rings.append([(x - groove * 1.5, y + dy + groove * 0.9, z),
                      (x - groove * 1.5, y + dy + groove * 0.2, z),
                      (x - groove * 0.75, y + dy - groove * 0.5, z),
                      (x + groove * 0.75, y + dy - groove * 0.5, z),
                      (x + groove * 1.5, y + dy + groove * 0.2, z),
                      (x + groove * 1.5, y + dy + groove * 0.9, z),
                      (x + groove * 1.5, y + dy - groove * 1.9, z),
                      (x - groove * 1.5, y + dy - groove * 1.9, z)])
    m.loft(rings, mat=METAL, parent="crossarm")
    _strut(m, (x, y - groove * 1.9 + LAP, 0.0),
           (x, s["arm_y"] + s["arm_depth"] * 0.3, 0.0), groove * 1.4, parent="crossarm")


# --------------------------------------------------------------------------- details
def _ladder(m, s, x, z, y0, y1):
    """Maintenance ladder: two stringers and one rung arrayed up the mast.

    Rungs go in at half a metre and stretch out beyond forty of them. A forty metre
    tower is never read from close enough to count rungs - what carries at that range is
    the pair of stringers against the mast - and forty rungs is already a tenth of the
    tower's whole triangle budget.
    """
    climb = y1 - y0
    if climb < 1.0:
        return
    width = 0.40
    for sx in (-1.0, 1.0):
        m.box((x + sx * width * 0.5, (y0 + y1) * 0.5, z), (0.045, climb, 0.05),
              mat=METAL, bevel=False)
    count = max(2, min(40, int((climb - 0.2) / 0.50)))
    spacing = (climb - 0.3) / count
    rung = m.box((x, y0 + 0.2, z), (width, 0.035, 0.035), mat=METAL, bevel=False)
    m.array(rung, count, (0.0, spacing, 0.0))


def _platform(m, s, y, half, depth):
    """Top platform: a grating deck at the arm with a rail a fitter can clip onto."""
    m.box((0.0, y - 0.03, 0.0), (half * 2.0, 0.05, depth), mat=METAL)
    rail = y + 1.05
    for sz in (-1.0, 1.0):
        for sx in (-1.0, 1.0):
            _strut(m, (sx * half * 0.92, y, sz * depth * 0.45),
                   (sx * half * 0.92, rail, sz * depth * 0.45), 0.05)
        _strut(m, (-half * 0.92, rail, sz * depth * 0.45),
               (half * 0.92, rail, sz * depth * 0.45), 0.045)
        _strut(m, (-half * 0.92, y + 0.5, sz * depth * 0.45),
               (half * 0.92, y + 0.5, sz * depth * 0.45), 0.035)


def _number_plate(m, s, face_z, y):
    """The tower number board, on the downhill face where a patroller reads it."""
    m.box((0.0, y, face_z - 0.03), (0.42, 0.52, 0.05), mat=BODY)
    m.box((0.0, y, face_z - 0.06), (0.30, 0.38, 0.02), mat=METAL, bevel=False)


def _catch_net(m, s):
    """Catch-net outriggers: aerial lines hang a net under the rope over anything below.

    The net itself is cloth and belongs to the line, not the tower; what the tower
    carries is the bracket, and that bracket is a big enough triangle to read.
    """
    y = s["arm_y"] - s["arm_depth"] * 0.4
    half = s["arm_half"]
    root = s["lat_top"] * 0.5 if s["kind"] == "lattice" else s["mast_r1"]
    for sx in (-1.0, 1.0):
        tip = (sx * half * 1.05, y - 1.9, 0.0)
        _strut(m, (sx * root, y, 0.0), tip, 0.10)
        _strut(m, tip, (sx * half * 1.05, y + 0.25, 0.0), 0.08)
        _strut(m, (sx * half * 1.05, y + 0.25, 0.0), (sx * root * 1.2, y + 0.30, 0.0),
               0.07)


# --------------------------------------------------------------------------- forms
def _build_mast_tower(m, s):
    """Tube or lattice: plinth, mast, crossarm, sheave trains and the fittings."""
    _foundation(m, s)
    if s["kind"] == "lattice":
        _mast_lattice(m, s)
        face = -s["lat_base"] * 0.5
        ladder_x, ladder_z = 0.0, -s["lat_top"] * 0.5 + 0.28
    else:
        _mast_tube(m, s)
        face = -s["mast_r0"]
        ladder_x, ladder_z = 0.0, -s["mast_r1"] - 0.26

    m.node("crossarm", pivot=(0.0, s["arm_y"], 0.0))
    _crossarm(m, s)

    y_axle = s["rope_y"] - s["sheave_r"]
    axles = []
    for sign in (-1.0, 1.0):
        for plane in s["planes"]:
            axles += _train(m, s, sign * plane, sign, y_axle)
    # Uphill first, then left to right, so sheave_01 is always the one the rope meets.
    axles.sort(key=lambda p: (-p[2], p[0]))
    for i, axle in enumerate(axles):
        name = "sheave_%02d" % (i + 1)
        m.node(name, pivot=axle, parent="crossarm")
        _sheave(m, s, name, axle)

    for sign in (-1.0, 1.0):
        for offset in s["saddles"]:
            _saddle(m, s, sign * offset, s["rope_y"] + s["sheave_r"] * 0.4)

    _ladder(m, s, ladder_x, ladder_z, s["plinth_h"], s["arm_y"] - 0.4)
    _platform(m, s, s["arm_y"] - s["arm_depth"] * 0.5 - 0.06,
              _clamp(s["arm_half"] * 0.5, 0.7, 2.2),
              _clamp(s["train_len"] * 0.5, 0.9, 2.4))
    _number_plate(m, s, face, min(2.2, s["arm_y"] * 0.5))
    if s["family"] == "Aerial":
        _catch_net(m, s)
    if s["night"]:
        for sx in (-1.0, 1.0):
            m.socket("light_%s" % ("L" if sx < 0 else "R"),
                     (sx * s["arm_half"] * 0.6, s["arm_y"] - 0.5, 0.25))

    width = max(s["plinth_side"], s["lat_base"] if s["kind"] == "lattice" else 0.0)
    return [("mast_col", (0.0, s["mast_top"] * 0.5, 0.0),
             (width * 0.7, s["mast_top"], width * 0.7))]


def _build_trestle(m, s, record):
    """Conveyor support: a ground frame under the belt, with a gallery when it has one.

    There is no mast here because the record says so - half a cubic metre of concrete
    under a support is a bearing pad, not a footing that could hold a mast upright. The
    belt runs a hand's breadth off the snow on a bed of idlers, and a carpet whose
    weather exposure is a fraction of an open one is under a gallery, so it gets the
    polycarbonate arch that keeps the drift off it.
    """
    belt_w = _clamp(0.62 + 0.00008 * _num(record.get("CapacityPph"), 1200.0), 0.6, 0.95)
    length = 2.2
    deck = s["deck"]
    frame = belt_w * 0.5 + 0.10
    foot = length * 0.5 - 0.25
    pad = _clamp(math.sqrt(s["concrete"] * 0.25 / 0.35), 0.30, 1.10)

    for sx in (-1.0, 1.0):
        m.box((sx * frame, deck - 0.09, 0.0), (0.09, 0.18, length), mat=METAL)
        for sz in (-1.0, 1.0):
            m.box((sx * frame, deck * 0.5, sz * foot), (0.08, deck, 0.08), mat=METAL,
                  bevel=False)
            m.box((sx * frame, 0.06, sz * foot), (pad, 0.12, pad), mat=METAL)
            m.cylinder((sx * frame, 0.14, sz * foot), 0.03, 0.09, axis=1, segments=6,
                       mat=METAL)
    for sz in (-1.0, 1.0):
        m.box((0.0, deck - 0.14, sz * foot), (frame * 2.0, 0.07, 0.07), mat=METAL,
              bevel=False)
    m.box((0.0, deck - 0.02, 0.0), (belt_w + 0.06, 0.04, length), mat=METAL)
    for sx in (-1.0, 1.0):
        m.box((sx * (belt_w * 0.5 + 0.03), deck + 0.10, 0.0), (0.05, 0.22, length),
              mat=BODY)

    # A bed of idlers under the belt, and the belt's own cleats on top of it: both are
    # one part arrayed, which is the only sane way to model a hundred of anything.
    idler = m.cylinder((0.0, deck - 0.06, -length * 0.5 + 0.22), 0.055, belt_w, axis=0,
                       segments=6, mat=METAL)
    m.array(idler, max(2, int(length / 0.22)), (0.0, 0.0, 0.22))
    cleat = m.box((0.0, deck + 0.02, -length * 0.5 + 0.12), (belt_w, 0.03, 0.05),
                  mat=METAL, bevel=False)
    m.array(cleat, max(2, int(length / 0.18)), (0.0, 0.0, 0.18))

    m.node("crossarm", pivot=(0.0, deck, 0.0))
    for i, sz in enumerate((1.0, -1.0)):
        axle = (0.0, deck - 0.04, sz * (length * 0.5 - 0.10))
        name = "sheave_%02d" % (i + 1)
        m.node(name, pivot=axle, parent="crossarm")
        m.cylinder(axle, 0.085, belt_w + 0.04, axis=0, segments=10, mat=METAL,
                   parent=name)

    if s["exposure"] < 0.6:
        rise = 1.6
        outer, inner = [], []
        for i in range(9):
            a = math.pi * i / 8.0
            ox, oy = math.cos(a) * (frame + 0.12), math.sin(a) * rise
            outer.append((ox, deck + 0.20 + oy))
            inner.append((ox * 0.955, deck + 0.20 + oy * 0.95))
        m.prism(outer + list(reversed(inner)), (0.0, 0.0, 0.0), length * 0.94,
                mat=GLASS, axis=2)
        for sz in (-1.0, 1.0):
            for i in (0, 4, 8):
                a = math.pi * i / 8.0
                m.box((math.cos(a) * (frame + 0.13), deck + 0.20 + math.sin(a) * rise,
                       sz * length * 0.47), (0.07, 0.07, 0.07), mat=BODY, bevel=False)
    if s["night"]:
        for sx in (-1.0, 1.0):
            m.socket("light_%s" % ("L" if sx < 0 else "R"),
                     (sx * frame, deck + (1.6 if s["exposure"] < 0.6 else 0.9), 0.0))
    return [("belt_col", (0.0, deck * 0.5, 0.0), (belt_w + 0.5, deck + 0.2, length))]


def _build_plinth(m, s, record):
    """Rail pier: a concrete block carrying the track, with no mast on it at all.

    A funicular or a rack line runs on a bed, not on a rope in the air, so its supports
    are piers every 25 to 35 metres. The rack rail down the centre is one tooth arrayed
    along the pier; a rope-hauled line gets the rollers that carry its haul rope clear
    of the sleepers instead.
    """
    length = 3.4
    gauge = 1.44
    h = s["plinth_h"]
    side = _clamp(s["plinth_side"] * 1.15, 1.8, 4.0)
    m.loft([_ring(side * 0.5, 0.0, side * 0.12),
            _ring(side * 0.45, h - 0.10, side * 0.10),
            _ring(side * 0.45 - 0.06, h, side * 0.10)], mat=METAL)

    for sz in (-1.0, 0.0, 1.0):
        m.box((0.0, h + 0.06, sz * length * 0.30), (gauge + 0.5, 0.12, 0.26), mat=METAL)
        for sx in (-1.0, 1.0):
            m.cylinder((sx * (gauge * 0.5 + 0.19), h + 0.15, sz * length * 0.30), 0.028,
                       0.10, axis=1, segments=6, mat=METAL)
    for sx in (-1.0, 1.0):
        rail = [(h + 0.12, -length * 0.5), (h + 0.30, -length * 0.5),
                (h + 0.30, length * 0.5), (h + 0.12, length * 0.5)]
        m.prism(rail, (sx * gauge * 0.5, 0.0, 0.0), 0.09, mat=METAL, axis=0)
        m.box((sx * gauge * 0.5, h + 0.15, 0.0), (0.16, 0.06, length * 0.98), mat=METAL,
              bevel=False)
        for sz in (-1.0, 0.0, 1.0):
            m.box((sx * gauge * 0.5, h + 0.14, sz * length * 0.30), (0.30, 0.09, 0.30),
                  mat=METAL, bevel=False)
            m.box((sx * (gauge * 0.5 + 0.10), h + 0.20, sz * length * 0.30),
                  (0.09, 0.07, 0.16), mat=METAL, bevel=False)

    if s["rope"] == "Rack":
        m.box((0.0, h + 0.16, 0.0), (0.13, 0.14, length * 0.98), mat=METAL)
        tooth = m.box((0.0, h + 0.27, -length * 0.5 + 0.08), (0.13, 0.09, 0.055),
                      mat=METAL, bevel=False)
        m.array(tooth, int(length * 0.96 / 0.12), (0.0, 0.0, 0.12))
    else:
        m.box((0.0, h + 0.10, 0.0), (0.44, 0.10, length * 0.9), mat=METAL)

    # Signal and power cable runs live in a ducted trough bolted to the pier's flank.
    m.box((side * 0.42, h - 0.18, 0.0), (0.24, 0.22, length * 0.8), mat=METAL)
    m.box((side * 0.42, h - 0.06, 0.0), (0.26, 0.04, length * 0.78), mat=BODY,
          bevel=False)

    # A pier's sheaves are the rollers that hold the haul rope clear of the sleepers;
    # on a rack line the same casting carries the hold-down rollers over the rack.
    m.node("crossarm", pivot=(0.0, h, 0.0))
    off = 0.40 if s["rope"] == "Rack" else 0.0
    for i, sz in enumerate((1.0, -1.0)):
        x = off * (1.0 if i == 0 else -1.0)
        axle = (x, h + 0.22, sz * length * 0.26)
        name = "sheave_%02d" % (i + 1)
        m.node(name, pivot=axle, parent="crossarm")
        m.cylinder(axle, 0.13, 0.16, axis=0, segments=10, mat=METAL, parent=name)
        m.box((x, h + 0.12, sz * length * 0.26), (0.30, 0.20, 0.06), mat=METAL,
              parent="crossarm")
    return [("pier_col", (0.0, h * 0.5, 0.0), (side, h, length))]


# --------------------------------------------------------------------------- build
def build(record, out_path):
    """Build every height class of one lift's tower. Returns one record per file."""
    folder = os.path.dirname(out_path)
    stem = os.path.splitext(os.path.basename(out_path))[0]
    records = []

    for suffix, factor in HEIGHT_CLASSES:
        name = stem if not suffix else "%s_%s" % (stem, suffix)
        s = _spec(record, factor)
        model = mk.Model("%s_%s" % (record["Id"], name), budget_key="lift_tower",
                         seed=datasrc.seed_for(record["Id"], name))
        if s["kind"] == "trestle":
            colliders = _build_trestle(model, s, record)
        elif s["kind"] == "plinth":
            colliders = _build_plinth(model, s, record)
        else:
            colliders = _build_mast_tower(model, s)

        records.append(export.emit(
            model, os.path.join(folder, name + ".fbx"), box_colliders=colliders,
            extra={"family": "lift_tower", "kind": "lift", "component": "tower",
                   "displayName": record.get("DisplayName", record["Id"]),
                   "sourceId": record["Id"],
                   "heightClass": suffix or "mid",
                   "structure": s["kind"],
                   "ropeHeightM": round(s["rope_y"], 2),
                   "sheaves": len(s["planes"]) * s["per_plane"] * 2}))
    return records
