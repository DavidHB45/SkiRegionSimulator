"""Attachments: every implement in attachments.json, built from its own record.

An attachment is authored around its **mount point**, not its contact patch. The model
is parented to a machine's `mount_<Position>` socket at runtime, so local (0, 0, 0) is
where the implement bolts on and the work grows forward from there; a rear slot yaws the
model 180 degrees, which is why a rear tiller is still authored pointing +Z. That is the
one place the pivot-at-ground-contact rule in docs/ART_CONTRACT.md does not hold, and
lib/validate.py carries a guard for it.

Nothing here knows the name of a single implement. A mouldboard's height is whichever is
larger of the bite it takes (`Effects.CutDepthMm`) and the plate its `MassKg` says it is
made of; a bucket's depth is solved from `Effects.BucketM3` so the shell really holds
what the sim scoops with it; a winch drum's flanges are solved from the volume of
`Effects.RopeLengthM` of rope wound onto them; a spray bar is `Effects.SpreadWidthM`
across and a track setter's moulds stand `Effects.TrackGaugeM` apart. Change a number in
the JSON and the model changes on the next build.
"""
import math
import re

from config import BEVEL_WIDTH_M
from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

import bmesh          # noqa: E402  - meshkit imports bpy first, which puts bmesh on the path

# A mount socket sits about this far above the snow across the fleet: the chassis
# generators put theirs at roughly 0.6 of the track height and VehicleView at half of it.
# Anything that has to reach the ground - a cutting edge, a tiller rotor, a trailer wheel
# - is placed relative to this, so an implement meets the snow instead of hanging under
# the nose of the machine.
MOUNT_HEIGHT_M = 0.45
GROUND = -MOUNT_HEIGHT_M

# A mouldboard with its ribs, push frame and rams runs about this much steel per square
# metre of board. It is what turns a blade's MassKg into a board height, because
# CutDepthMm only describes how deep a bite the blade takes, not how tall the plate is.
BOARD_KG_M2 = 380.0

# Wound wire rope does not pack solid; the voids between turns cost about this much.
ROPE_PACKING = 1.15


# --------------------------------------------------------------------------- numbers
def _num(value, default=0.0):
    """A JSON field as a float. A missing, null or nonsense value falls back."""
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _lerp(a, b, t):
    return a + (b - a) * t


def _kind(record):
    return str(record.get("Kind") or "Blade")


def _eff(record, key, default=0.0):
    """One `Effects` coefficient. The sim reads the same block for its physics."""
    effects = record.get("Effects") or {}
    return _num(effects.get(key), default)


def _width(record, default=2.0):
    return _num(record.get("WorkingWidthM"), default) or default


def _mass(record, default=400.0):
    return _num(record.get("MassKg"), default) or default


def _segments(radius):
    """Sides on a revolved part, from how big it reads on screen.

    A 0.7 m reel flange earns twice the sides of a 0.06 m ram rod, and the small stuff is
    where a procedural pipeline throws its budget away if nobody says no.
    """
    return int(_clamp(9.0 + radius * 20.0, 8.0, 24.0))


def _chamferable(size):
    """Whether a part is thick enough for the standard chamfer to read as an edge.

    Below about three chamfer widths the bevel eats the face it is meant to catch a
    highlight on, and charges four times the triangles for it.
    """
    return size > BEVEL_WIDTH_M * 3.0


def _capacity_m3(record, fallback):
    """Published capacity from what the record calls itself, in cubic metres.

    A tank's litres and a hopper's cubic metres are part of the record's DisplayName -
    "Brine Tank 5,000 L" - and no Effects field carries a vessel volume, so the vessel is
    built to hold exactly what the fleet list says it holds.
    """
    text = str(record.get("DisplayName") or "").replace(",", "")
    found = re.search(r"(\d+(?:\.\d+)?)\s*(m3|m³|l)\b", text, re.IGNORECASE)
    if not found:
        return fallback
    value = _num(found.group(1), 0.0)
    if value <= 0.0:
        return fallback
    return value / 1000.0 if found.group(2).lower() == "l" else value


def _yaw_dir(yaw_deg):
    """Where Unity's +X axis points after a yaw about Y, as (dx, dz).

    Unity turns clockwise about +Y, so a positive yaw sweeps +X toward -Z - which is what
    a blade wing does when it is angled back.
    """
    a = math.radians(yaw_deg)
    return (math.cos(a), -math.sin(a))


# --------------------------------------------------------------------------- members
def _mid(a, b):
    return ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5)


def _bar(m, a, b, thickness, mat=METAL, parent=None, bevel=None, square=True):
    """A straight member between two points, chamfered only when it is thick enough."""
    d = tuple(b[i] - a[i] for i in range(3))
    length = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if length < 1e-4:
        return []
    if bevel is None:
        bevel = _chamferable(thickness)
    if not square:
        return m.cylinder(_mid(a, b), thickness * 0.5, length, axis=2,
                          segments=_segments(thickness), mat=mat, parent=parent,
                          rot=mk.look_rotation(d))
    return m.box(_mid(a, b), (thickness, thickness, length), mat=mat, parent=parent,
                 rot=mk.look_rotation(d), bevel=bevel)


def _ram(m, a, b, bore, parent=None):
    """A hydraulic ram: barrel over most of the stroke, bright rod the rest of it."""
    knee = tuple(_lerp(a[i], b[i], 0.58) for i in range(3))
    _bar(m, a, knee, bore, parent=parent, square=False)
    _bar(m, tuple(_lerp(a[i], b[i], 0.48) for i in range(3)), b, bore * 0.5,
         parent=parent, square=False)
    _pin(m, a, bore * 0.62, bore * 1.3, axis=0, parent=parent)
    _pin(m, b, bore * 0.42, bore * 0.9, axis=0, parent=parent)


def _pin(m, centre, radius, length, axis=0, parent=None, mat=METAL):
    """A hinge boss: what every joint on a real implement is built around."""
    m.cylinder(centre, radius, length, axis=axis, segments=_segments(radius), mat=mat,
               parent=parent)


def _bolts(m, centre, radius, count, size, axis=2, parent=None):
    """A bolt circle on a flange. Cheap, and it is what makes steel read as bolted."""
    for i in range(int(count)):
        a = 2.0 * math.pi * i / max(1, int(count))
        c, s = math.cos(a) * radius, math.sin(a) * radius
        if axis == 0:
            offset = (0.0, c, s)
        elif axis == 1:
            offset = (c, 0.0, s)
        else:
            offset = (c, s, 0.0)
        m.cylinder((centre[0] + offset[0], centre[1] + offset[1], centre[2] + offset[2]),
                   size, size * 1.1, axis=axis, segments=6, mat=METAL, parent=parent)


def _wheel(m, centre, radius, width, parent=None):
    """A road wheel as a lathe, so it has a tyre shoulder and a rim face."""
    hw = width * 0.5
    profile = [(radius * 0.22, -hw * 0.6), (radius * 0.55, -hw * 0.75),
               (radius * 0.88, -hw), (radius, -hw * 0.6), (radius, hw * 0.6),
               (radius * 0.88, hw), (radius * 0.55, hw * 0.75), (radius * 0.22, hw * 0.6)]
    m.lathe(profile, centre, segments=_segments(radius) + 2, mat=METAL, parent=parent,
            axis=0)
    _bolts(m, (centre[0] + hw * 0.55, centre[1], centre[2]), radius * 0.3, 5,
           radius * 0.07, axis=0, parent=parent)


def _mount_plate(m, width, height, thickness=0.05, parent=None):
    """The plate that actually bolts to the machine, at the model's origin.

    Every implement gets one: it is the part the mount socket is holding, and without it
    an attachment reads as floating a hand's width off the machine it is bolted to.
    """
    m.box((0.0, height * 0.04, thickness * 0.5), (width, height, thickness), mat=METAL,
          parent=parent)
    for side in (-1.0, 1.0):
        m.box((side * width * 0.42, height * 0.04, thickness * 1.6),
              (width * 0.1, height * 0.86, thickness * 2.0), mat=METAL, parent=parent)
    _bolts(m, (0.0, height * 0.04, thickness * 1.1), min(width, height) * 0.34, 6,
           min(width, height) * 0.055, axis=2, parent=parent)


# --------------------------------------------------------------------------- surfaces
def _board_profile(height, thickness, curl=1.0):
    """Side section of a real mouldboard as (y, z) points, cutting edge at y = 0.

    A plough board is not a plate. Its face is a circular arc concave toward the snow, so
    snow rides up it and rolls off the top instead of being shoved along as a wall; the
    bottom of the arc leans forward to the cutting edge and the top curls forward again.
    The section is that arc, offset back by the plate thickness and closed.
    """
    a0, a1 = math.radians(-38.0), math.radians(52.0 * curl)
    radius = height / max(0.4, math.sin(a1) - math.sin(a0))
    steps = 5
    face, back = [], []
    for i in range(steps + 1):
        a = _lerp(a0, a1, i / steps)
        face.append((radius * (math.sin(a) - math.sin(a0)), radius * (1.0 - math.cos(a))))
        back.append(((radius + thickness) * math.sin(a) - radius * math.sin(a0),
                     radius - (radius + thickness) * math.cos(a)))
    return face + list(reversed(back)), radius, (a0, a1)


def _board(m, node, anchor, yaw, width, height, thickness, ribs=True, edge=True):
    """One mouldboard: curved shell, bolted cutting edge, back ribs and a top rail.

    `anchor` is where the bottom of the board's belly sits and `yaw` how far it is angled;
    everything is authored about the board's own origin and carried onto the anchor at the
    end, so an angled wing's ribs and bolts land on the wing rather than beside it.
    """
    if width < 0.2 or height < 0.1:
        return
    profile, radius, (a0, a1) = _board_profile(height, thickness)
    rot = mk.unity_euler(0.0, yaw, 0.0) if abs(yaw) > 1e-6 else None
    m.prism(profile, anchor, width, mat=BODY, parent=node, rot=rot, axis=0)

    loose = []
    # The cutting edge is a replaceable wear bar bolted along the bottom of the face, so
    # it lies along the face tangent there rather than flat on the ground.
    edge_h = _clamp(height * 0.17, 0.08, 0.22)
    edge_t = _clamp(thickness * 0.55, 0.022, 0.05)
    if edge:
        ty, tz = math.cos(a0), math.sin(a0)
        cy = -ty * edge_h * 0.34 + tz * edge_t * 0.4
        cz = -tz * edge_h * 0.34 - ty * edge_t * 0.4
        loose += m.box((0.0, cy, cz), (width * 0.995, edge_h, edge_t), mat=METAL,
                       parent=node, rot=mk.unity_euler(-math.degrees(a0), 0.0, 0.0),
                       bevel=False)
        count = int(_clamp(width / 0.38, 4.0, 14.0))
        span = width * 0.86
        bolt = m.box((-span * 0.5, cy + edge_h * 0.16, cz - edge_t * 0.9),
                     (edge_t * 1.4, edge_t * 1.4, edge_t * 0.9), mat=METAL, parent=node,
                     bevel=False)
        loose += bolt + m.array(bolt, count, (span / max(1, count - 1), 0.0, 0.0),
                                parent=node)

    # Ribs stiffen the back of the shell and are the reason a mouldboard is not a plate.
    if ribs:
        count = int(_clamp(width / 0.75, 3.0, 8.0))
        span = width * 0.84
        rib_h = height * 0.86
        rib = m.box((-span * 0.5, rib_h * 0.5, -thickness - rib_h * 0.14),
                    (_clamp(thickness * 0.7, 0.03, 0.06), rib_h, rib_h * 0.4),
                    mat=BODY, parent=node, bevel=False)
        loose += rib + m.array(rib, count, (span / max(1, count - 1), 0.0, 0.0),
                               parent=node)

    # A top rail keeps snow off the frame and gives the silhouette its straight line.
    top_z = radius * (1.0 - math.cos(a1))
    loose += m.box((0.0, height + thickness * 0.2, top_z - thickness * 0.3),
                   (width * 0.99, thickness * 1.1, thickness * 2.0), mat=BODY,
                   parent=node, bevel=False)
    _place_faces(m, node, loose, anchor, rot)


def _place_faces(m, node, faces, anchor, rot):
    """Carry freshly built faces onto a board's anchor, rotating them with it.

    meshkit places one primitive at a centre with an optional rotation, but a board is a
    group of parts that has to move as one - including the parts an array made - so they
    are built about the origin and moved together here.
    """
    bm = m.nodes[node or m.ROOT].bm
    verts = list({v for f in faces for v in f.verts})
    if not verts:
        return
    if rot is not None:
        bmesh.ops.transform(bm, matrix=rot, verts=verts)
    bmesh.ops.translate(bm, vec=mk.to_blender(anchor), verts=verts)


def _board_height(record, width):
    """How tall a mouldboard is, from the two things the record says about it."""
    bite = _eff(record, "CutDepthMm") / 1000.0
    plate = _mass(record) / max(0.5, width * BOARD_KG_M2)
    return _clamp(max(bite, plate), 0.32, 1.6)


def _helix(m, base, hub, radius, length, turns, axis=1, steps=26, thickness=0.018,
           mat=METAL, parent=None):
    """A helical flight as a stack of rotated sections skinned into one ribbon.

    One loft of rectangular sections gives a real auger flight - a surface that carries
    material along the shaft - where a stack of tilted discs gives a bottle brush.
    """
    sections = []
    for i in range(int(steps) + 1):
        t = i / float(steps)
        a = 2.0 * math.pi * turns * t
        off = length * (t - 0.5)
        c, s = math.cos(a), math.sin(a)
        ring = []
        for r, d in ((hub, -thickness), (radius, -thickness),
                     (radius, thickness), (hub, thickness)):
            if axis == 0:
                ring.append((base[0] + off + d, base[1] + c * r, base[2] + s * r))
            elif axis == 1:
                ring.append((base[0] + c * r, base[1] + off + d, base[2] + s * r))
            else:
                ring.append((base[0] + c * r, base[1] + s * r, base[2] + off + d))
        sections.append(ring)
    m.loft(sections, mat=mat, parent=parent, closed_ends=True)


def _rotor(m, node, centre, width, radius, rows=3, mat=METAL):
    """A working drum: shaft, end flanges and one tooth arrayed along each helical row.

    Two hundred modelled teeth cost two hundred times what one costs and read identically
    at 200 m, so a tooth is built once per row and repeated along the shaft.
    """
    m.cylinder(centre, radius * 0.42, width * 0.98, axis=0, segments=_segments(radius),
               mat=mat, parent=node)
    for side in (-1.0, 1.0):
        m.lathe([(radius * 0.42, 0.0), (radius * 0.78, 0.0), (radius * 0.78, 0.05),
                 (radius * 0.42, 0.05)],
                (centre[0] + side * width * 0.5, centre[1], centre[2]),
                segments=_segments(radius), mat=mat, parent=node, axis=0)
    spacing = _clamp(width / 17.0, 0.14, 0.30)
    per_row = int(_clamp(width / (spacing * rows), 3.0, 16.0))
    step = spacing * rows
    tooth_h = radius * 0.55
    tooth = _clamp(radius * 0.3, 0.03, 0.09)
    for row in range(int(rows)):
        a = 2.0 * math.pi * row / rows
        dy, dz = math.cos(a), math.sin(a)
        x0 = centre[0] - width * 0.46 + row * spacing
        seed = m.box((x0, centre[1] + dy * (radius * 0.62), centre[2] + dz * (radius * 0.62)),
                     (tooth, tooth_h, tooth * 1.3), mat=METAL, parent=node,
                     rot=mk.unity_euler(math.degrees(math.atan2(-dz, dy)), 0.0, 0.0),
                     bevel=False)
        m.array(seed, per_row, (step, 0.0, 0.0), parent=node)


def _hood(m, node, centre, width, radius, mat=BODY):
    """The shell over a rotor: a C section covering the front and top, open behind.

    A tiller hood stops at the top of its arc. What comes out from under the back of it is
    the corduroy the rotor and the comb just made, so closing the shell round the back
    would both hide the working parts and be wrong.
    """
    outer = radius * 1.28
    profile = []
    steps = 7
    start, end = math.radians(-28.0), math.radians(148.0)
    for i in range(steps + 1):
        a = _lerp(start, end, i / steps)
        profile.append((math.sin(a) * outer, math.cos(a) * outer))
    for i in range(steps + 1):
        a = _lerp(end, start, i / steps)
        inner = outer - _clamp(radius * 0.12, 0.03, 0.07)
        profile.append((math.sin(a) * inner, math.cos(a) * inner))
    m.prism(profile, centre, width, mat=mat, parent=node, axis=0)
    for side in (-1.0, 1.0):
        m.box((centre[0] + side * width * 0.5, centre[1] + outer * 0.1, centre[2]),
              (0.04, outer * 1.5, outer * 1.9), mat=METAL, parent=node)


def _comb(m, node, centre, width, depth, teeth_len):
    """The finisher: a cross bar, a rubber flap and one comb tooth arrayed across it."""
    m.box(centre, (width, depth * 0.55, depth * 0.5), mat=BODY, parent=node)
    m.box((centre[0], centre[1] - depth * 0.28, centre[2] + depth * 0.26),
          (width * 0.99, depth * 0.8, 0.02), mat=METAL, parent=node, bevel=False)
    count = int(_clamp(width / 0.075, 8.0, 44.0))
    span = width * 0.96
    tooth = m.box((-span * 0.5 + centre[0], centre[1] - depth * 0.5,
                   centre[2] - teeth_len * 0.5),
                  (0.016, 0.05, teeth_len), mat=METAL, parent=node, bevel=False)
    m.array(tooth, count, (span / max(1, count - 1), 0.0, 0.0), parent=node)


def _trailer(m, length, width, load_y, mass):
    """A towed frame: drawbar to the mount, two wheels on the snow, a jockey wheel.

    The wheels are what put a towed implement on the ground, so their radius comes off the
    axle load - a 2.4 t reel trailer rolls on something a 650 kg light plant does not.
    """
    radius = _clamp(0.17 + mass / 9000.0, 0.20, 0.42)
    tyre = _clamp(radius * 0.42, 0.10, 0.24)
    axle_z = length * 0.58
    half = width * 0.5
    for side in (-1.0, 1.0):
        _wheel(m, (side * (half + tyre * 0.6), GROUND + radius, axle_z), radius, tyre)
        _bar(m, (side * half * 0.9, load_y, axle_z), (side * half, GROUND + radius, axle_z),
             0.07)
    _bar(m, (-half - tyre * 0.4, GROUND + radius, axle_z),
         (half + tyre * 0.4, GROUND + radius, axle_z), 0.08)
    for side in (-1.0, 1.0):
        _bar(m, (side * half * 0.8, load_y, length * 0.12),
             (side * half * 0.8, load_y, length * 0.98), 0.09, mat=METAL)
    _bar(m, (-half * 0.8, load_y, length * 0.14), (half * 0.8, load_y, length * 0.14), 0.08)
    _bar(m, (-half * 0.8, load_y, length * 0.94), (half * 0.8, load_y, length * 0.94), 0.08)
    for side in (-1.0, 1.0):
        _bar(m, (side * half * 0.8, load_y, length * 0.22), (0.0, load_y, 0.06), 0.09)
    m.box((0.0, load_y + 0.02, 0.10), (0.22, 0.16, 0.30), mat=METAL)
    # Jockey wheel and a safety chain eye: the details that say "this gets parked".
    _bar(m, (0.0, load_y, length * 0.2), (0.0, GROUND + 0.16, length * 0.2), 0.06)
    _wheel(m, (0.0, GROUND + 0.10, length * 0.2), 0.10, 0.05)
    m.socket("hitch", (0.0, load_y + 0.02, 0.0))
    return radius


def _lamp(m, centre, size, parent=None, aim=0.0):
    """A work lamp: housing, lens and bracket. The lens is the only glass on an implement."""
    rot = mk.unity_euler(aim, 0.0, 0.0) if abs(aim) > 1e-6 else None
    m.box(centre, (size * 1.5, size, size * 0.5), mat=METAL, parent=parent, rot=rot)
    m.box((centre[0], centre[1] - math.sin(math.radians(aim)) * size * 0.3,
           centre[2] + size * 0.3), (size * 1.25, size * 0.8, 0.03), mat=GLASS,
          parent=parent, rot=rot)


# --------------------------------------------------------------------------- blades
# Groomer and dozer boards hang on a tilt circuit; a truck plough and a box pusher do not
# - they are hung off a lift frame and angled, and nothing rolls them about the direction
# of travel.
_TILT_KINDS = frozenset({"Blade", "Blade12Way", "UBlade", "ParkBlade"})

# How a kind splits its working width into a centre section and two wings, and how far
# those wings sweep: back for an angling blade, forward for a U-blade that has to hold a
# load in front of it.
_BLADE_SECTIONS = {
    "Blade12Way": (0.52, 26.0, True),
    "ParkBlade": (0.52, 24.0, True),
    "PlowWings": (0.62, 18.0, True),
    "VPlow": (0.0, 34.0, True),
    "UBlade": (0.58, -30.0, False),
}


def _build_blade(m, record):
    """Every mouldboard implement: blades, ploughs, U-blades, box pushers, park blades."""
    kind = _kind(record)
    width = max(0.8, _width(record, 3.0))
    height = _board_height(record, width)
    thick = _clamp(height * 0.085, 0.045, 0.11)
    reach = 0.38 + height * 0.72
    y0 = GROUND
    centre_frac, sweep, hinged = _BLADE_SECTIONS.get(kind, (1.0, 0.0, False))
    centre_w = width * centre_frac
    wing_w = (width - centre_w) * 0.5 if centre_frac < 0.999 else 0.0
    if kind == "VPlow":
        wing_w = width * 0.56       # each half of a V covers more than half the swath

    _mount_plate(m, _clamp(width * 0.22, 0.35, 0.8), _clamp(height * 0.8, 0.35, 0.9))
    m.node("blade_lift", pivot=(0.0, -0.03, 0.04))
    board_node = "blade_lift"
    if kind in _TILT_KINDS:
        m.node("blade_tilt", pivot=(0.0, y0 + height * 0.5, reach), parent="blade_lift")
        board_node = "blade_tilt"

    if centre_w > 0.2:
        _board(m, board_node, (0.0, y0, reach), 0.0, centre_w, height, thick)
        hinge_z = reach
    else:
        # A V-plough has no centre section: the two halves meet at a nose casting.
        hinge_z = reach + 0.12
        m.cylinder((0.0, y0 + height * 0.5, hinge_z), thick * 1.5, height,
                   axis=1, segments=10, mat=METAL, parent=board_node)

    # Where each board segment ends is where its skid shoe and its marker pole go, and a
    # swept wing does not end where a straight board would - so the ends are collected as
    # the segments are placed rather than assumed to sit at half the working width.
    ends = []
    if wing_w < 0.2:
        ends = [(-centre_w * 0.5, reach, board_node), (centre_w * 0.5, reach, board_node)]

    for side, node in ((-1.0, "blade_angle_L"), (1.0, "blade_angle_R")):
        if wing_w < 0.2:
            break
        hinge = (side * centre_w * 0.5, y0, hinge_z)
        yaw = side * sweep
        dx, dz = _yaw_dir(yaw)
        anchor = (hinge[0] + side * dx * wing_w * 0.5, y0,
                  hinge[2] + side * dz * wing_w * 0.5)
        tip_x = hinge[0] + side * dx * wing_w
        tip_z = hinge[2] + side * dz * wing_w
        if hinged:
            m.node(node, pivot=(hinge[0], y0 + height * 0.5, hinge[2]), parent=board_node)
            _board(m, node, anchor, yaw, wing_w, height, thick)
            _pin(m, (hinge[0], y0 + height * 0.5, hinge[2]), thick * 0.9, height * 0.96,
                 axis=1, parent=node)
            # The angle ram lives on the wing it swings: at the angles a blade works
            # through, a ram that follows its wing reads better than one that detaches.
            ram_tip = (anchor[0] + side * dx * wing_w * 0.3, y0 + height * 0.66,
                       anchor[2] + side * dz * wing_w * 0.3 - thick * 2.2)
            _ram(m, (side * max(centre_w * 0.18, thick * 2.0), y0 + height * 0.62,
                     hinge_z - thick * 4.0), ram_tip,
                 _clamp(height * 0.09, 0.05, 0.1), parent=node)
            ends.append((tip_x, tip_z, node))
        else:
            _board(m, board_node, anchor, yaw, wing_w, height, thick)
            ends.append((tip_x, tip_z, board_node))

    if kind == "BoxPusher":
        # End plates are what make a box pusher a box: they stop the windrow escaping.
        for side in (-1.0, 1.0):
            plate = [(0.0, -height * 0.75), (height * 0.95, -height * 0.8),
                     (height * 0.95, thick * 2.0), (height * 0.1, thick * 2.0)]
            m.prism(plate, (side * (width * 0.5 + 0.03), y0, reach), thick * 0.8,
                    mat=BODY, parent=board_node, axis=0)
            m.box((side * (width * 0.5 + 0.03), y0 + 0.05, reach - height * 0.4),
                  (thick * 1.4, 0.14, height * 0.8), mat=METAL, parent=board_node)

    marked = kind in ("PlowStraight", "VPlow", "PlowWings")
    for x, z, node in ends:
        # Skid shoes carry the board when the operator floats it; marker poles are how a
        # truck plough driver knows where its corners are in a whiteout.
        m.prism([(y0 - 0.02, z - thick * 3.0), (y0 + height * 0.2, z - thick * 3.4),
                 (y0 + height * 0.22, z - thick * 0.6), (y0 - 0.02, z - thick * 0.4)],
                (x - math.copysign(thick, x), 0.0, 0.0), thick * 0.9, mat=METAL,
                parent=node, axis=0)
        if marked:
            m.cylinder((x, y0 + height + 0.3, z - thick), 0.016, 0.62, axis=1, segments=6,
                       mat=METAL, parent=node)
            m.sphere((x, y0 + height + 0.62, z - thick), 0.04, segments=6, rings=4,
                     mat=BODY, parent=node)

    _blade_frame(m, board_node, width, height, thick, reach, y0)
    return [("att_col", (0.0, y0 + height * 0.5, reach * 0.9),
             (width, height * 1.2, reach * 1.4))]


def _blade_frame(m, board_node, width, height, thick, reach, y0):
    """Push frame, lift rams and trip springs: how the board hangs off the machine."""
    frame_x = _clamp(width * 0.13, 0.22, 0.55)
    frame_y = y0 + height * 0.34
    bore = _clamp(height * 0.13, 0.06, 0.14)
    for side in (-1.0, 1.0):
        _bar(m, (side * frame_x * 0.6, -0.04, 0.05), (side * frame_x, frame_y, reach - thick),
             _clamp(height * 0.17, 0.09, 0.18), parent="blade_lift")
        _ram(m, (side * frame_x * 1.15, 0.30, -0.02),
             (side * frame_x * 1.3, y0 + height * 0.82, reach - thick * 2.2), bore,
             parent="blade_lift")
        # Trip springs let the board fold back over a buried rock instead of stopping
        # the machine dead, and they are half the character of a plough's silhouette.
        m.cylinder((side * frame_x * 0.75, y0 + height * 0.95, reach - thick * 3.0),
                   bore * 0.75, height * 0.5, axis=1, segments=8, mat=METAL,
                   parent=board_node)
    _bar(m, (-frame_x, frame_y, reach - thick * 1.5), (frame_x, frame_y, reach - thick * 1.5),
         _clamp(height * 0.15, 0.08, 0.16), parent="blade_lift")
    m.box((0.0, frame_y + height * 0.1, reach * 0.45),
          (frame_x * 1.4, height * 0.2, reach * 0.5), mat=METAL, parent="blade_lift")
    # The hose pair that actually feeds the rams: half of what a blade looks like from
    # the side is its plumbing.
    for side in (-1.0, 1.0):
        _bar(m, (side * frame_x * 0.5, 0.16, 0.02),
             (side * frame_x * 1.0, frame_y + height * 0.22, reach * 0.55), 0.035,
             parent="blade_lift", square=False)
        _bar(m, (side * frame_x * 1.0, frame_y + height * 0.22, reach * 0.55),
             (side * frame_x * 1.2, y0 + height * 0.7, reach - thick * 3.0), 0.035,
             parent="blade_lift", square=False)


# --------------------------------------------------------------------------- tillers
def _build_tiller(m, record):
    """Tillers, park tillers, track setters and the pipe cutter: a rotor in a hood.

    Everything that matters is in the record: WorkingWidthM sets the rotor, DepthRatingMm
    how far it can bite and therefore how big a drum it needs, FinishQuality how fine the
    trailing comb is, and TrackGaugeM where a nordic setter's two moulds sit.
    """
    kind = _kind(record)
    width = max(0.8, _width(record, 3.0))
    depth = _eff(record, "DepthRatingMm", 250.0) / 1000.0
    radius = _clamp(depth * 0.55 + 0.12, 0.16, 0.42)
    hood_z = 0.95 + radius * 1.4
    rotor_y = GROUND + radius * 0.72
    finish = _eff(record, "FinishQuality", 0.9)

    _mount_plate(m, _clamp(width * 0.16, 0.4, 0.8), 0.55)
    m.node("tiller_arm", pivot=(0.0, 0.0, 0.06))
    m.node("tiller_rotor", pivot=(0.0, rotor_y, hood_z), parent="tiller_arm")

    _hood(m, "tiller_arm", (0.0, rotor_y, hood_z), width, radius)
    _rotor(m, "tiller_rotor", (0.0, rotor_y, hood_z), width, radius,
           rows=3 if width < 5.0 else 4)

    # Lift arms and their rams: the geometry that says this thing is carried, not pushed.
    arm_x = _clamp(width * 0.16, 0.3, 0.7)
    for side in (-1.0, 1.0):
        _bar(m, (side * arm_x * 0.7, 0.0, 0.06),
             (side * arm_x, rotor_y + radius * 1.3, hood_z - radius * 0.9), 0.11,
             parent="tiller_arm")
        _ram(m, (side * arm_x * 1.25, 0.34, 0.0),
             (side * arm_x * 1.1, rotor_y + radius * 1.45, hood_z - radius * 0.3), 0.075,
             parent="tiller_arm")
        # Depth skids ride the snow and set how deep the rotor cuts.
        m.prism([(0.0, -radius * 0.9), (0.0, radius * 1.1), (radius * 0.3, radius * 1.1),
                 (radius * 0.45, -radius * 0.9)],
                (side * width * 0.5, GROUND + 0.01, hood_z), 0.05, mat=METAL,
                parent="tiller_arm", axis=0)
    _bar(m, (-arm_x, rotor_y + radius * 1.5, hood_z - radius * 1.1),
         (arm_x, rotor_y + radius * 1.5, hood_z - radius * 1.1), 0.12, parent="tiller_arm")
    # Hydraulic motor on one end of the rotor, hoses running back to the frame.
    m.cylinder((width * 0.5 + 0.12, rotor_y, hood_z), radius * 0.36, 0.24, axis=0,
               segments=12, mat=METAL, parent="tiller_arm")
    _bar(m, (width * 0.5 + 0.2, rotor_y + radius * 0.6, hood_z),
         (arm_x, rotor_y + radius * 1.4, hood_z - radius), 0.045, parent="tiller_arm",
         square=False)

    comb_z = hood_z + radius * 1.5
    m.node("finisher", pivot=(0.0, rotor_y + radius * 0.6, hood_z + radius * 1.1),
           parent="tiller_arm")
    _comb(m, "finisher", (0.0, GROUND + 0.14, comb_z), width * 0.99,
          _clamp(0.1 + finish * 0.12, 0.1, 0.24),
          _clamp(0.10 + finish * 0.14, 0.10, 0.26))
    for side in (-1.0, 1.0):
        _bar(m, (side * arm_x, rotor_y + radius * 0.7, hood_z + radius * 0.4),
             (side * arm_x, GROUND + 0.2, comb_z), 0.05, parent="finisher")

    if kind == "TrackSetter":
        _track_moulds(m, record, comb_z, radius)
    if kind == "PipeCutter":
        _pipe_shell(m, record, width, hood_z, radius)

    return [("att_col", (0.0, rotor_y + radius * 0.2, hood_z),
             (width + 0.3, radius * 3.0, radius * 4.0))]


def _track_moulds(m, record, comb_z, radius):
    """Two classic-track moulds at the gauge the record sets, trailing the finisher."""
    gauge = _eff(record, "TrackGaugeM", 0.72)
    groove = _clamp(gauge * 0.25, 0.10, 0.24)
    depth = _clamp(_eff(record, "CompactionKpa", 25.0) / 220.0, 0.06, 0.16)
    length = 0.62
    for side in (-1.0, 1.0):
        x = side * gauge * 0.5
        # An open-bottomed box that presses the groove: two cheeks and a crown.
        profile = [(0.0, -length * 0.5), (depth * 2.4, -length * 0.5),
                   (depth * 2.4, length * 0.5), (0.0, length * 0.5)]
        for off in (-groove * 0.5, groove * 0.5):
            m.prism(profile, (x + off, GROUND + 0.02, comb_z + length * 0.75), 0.035,
                    mat=METAL, parent="finisher", axis=0)
        m.box((x, GROUND + depth * 2.5, comb_z + length * 0.75),
              (groove + 0.07, 0.05, length), mat=BODY, parent="finisher")
        _bar(m, (x, GROUND + depth * 2.6, comb_z + length * 0.3),
             (x, GROUND + 0.34, comb_z - 0.1), 0.05, parent="finisher")
        m.box((x, GROUND + 0.36, comb_z - 0.05), (0.12, 0.1, 0.2), mat=METAL,
              parent="finisher")


def _pipe_shell(m, record, width, hood_z, radius):
    """The half-pipe former: a transverse arc that leaves a wall behind the rotor.

    Its curvature is the pipe the record shapes - ShapeDepthMm is the wall the machine
    is cutting toward - so a deeper pipe gets a tighter, taller shell.
    """
    wall = _clamp(_eff(record, "ShapeDepthMm", 6700.0) / 1000.0, 2.0, 8.0)
    arc_r = _clamp(wall * 0.55, width * 0.5, width * 1.4)
    half = width * 0.5
    thick = 0.06
    face, back = [], []
    steps = 8
    span = math.asin(_clamp(half / arc_r, 0.1, 0.95))
    for i in range(steps + 1):
        a = _lerp(-span, span, i / steps)
        # The back of the shell is the face pushed straight out along its own radius;
        # offsetting it any other way collapses the two curves onto each other at the
        # crown, which is a hole in the mesh rather than a plate.
        face.append((math.sin(a) * arc_r, arc_r - math.cos(a) * arc_r))
        back.append((math.sin(a) * (arc_r + thick),
                     arc_r - math.cos(a) * (arc_r + thick)))
    profile = face + list(reversed(back))
    shell_z = hood_z + radius * 2.4
    m.prism(profile, (0.0, GROUND + thick + 0.02, shell_z), 0.5, mat=BODY,
            parent="finisher", axis=2)
    for side in (-1.0, 1.0):
        rise = arc_r - math.cos(span) * arc_r
        _bar(m, (side * half, GROUND + rise + 0.1, shell_z),
             (side * half * 0.5, GROUND + 0.9, hood_z + radius), 0.07, parent="finisher")


# --------------------------------------------------------------------------- blower
def _build_blower(m, record):
    """A blower head: auger housing, ribbon augers, an impeller and a chute on its ring."""
    width = max(1.0, _width(record, 2.2))
    rate = _eff(record, "RemoveRateKgs", 100.0)
    throw = _eff(record, "ThrowDistanceM", 20.0)
    house_h = _clamp(0.55 + rate / 260.0, 0.7, 1.25)
    auger_r = _clamp(house_h * 0.36, 0.2, 0.42)
    house_z = 0.42 + auger_r * 1.6
    axis_y = GROUND + auger_r + 0.1

    _mount_plate(m, _clamp(width * 0.3, 0.5, 0.9), 0.7)
    # The housing is a C section open at the front: that mouth is where the snow goes in,
    # and closing it would bury the augers inside a box nobody can see into.
    front, back, wall = auger_r * 1.5, auger_r * 1.4, 0.055
    top = GROUND + house_h
    m.prism([(GROUND, front), (GROUND, -back), (top, -back), (top, front * 0.45),
             (top - wall, front * 0.45), (top - wall, -back + wall),
             (GROUND + wall, -back + wall), (GROUND + wall, front)],
            (0.0, 0.0, house_z), width, mat=BODY, axis=0)
    m.box((0.0, GROUND + 0.04, house_z + front * 0.97), (width * 0.99, 0.13, 0.05),
          mat=METAL)
    for side in (-1.0, 1.0):
        m.box((side * width * 0.5, GROUND + house_h * 0.5, house_z),
              (0.05, house_h, auger_r * 3.0), mat=BODY)
        _helix(m, (side * width * 0.25, axis_y, house_z), auger_r * 0.3, auger_r,
               width * 0.42, 1.6 * side, axis=0, steps=22)
        m.cylinder((side * width * 0.25, axis_y, house_z), auger_r * 0.28, width * 0.46,
                   axis=0, segments=10, mat=METAL)

    imp_r = _clamp(0.3 + throw / 90.0, 0.34, 0.62)
    imp_z = house_z - back - imp_r * 0.4          # the volute sits behind the auger wall
    m.node("blower_impeller", pivot=(0.0, axis_y + imp_r * 0.15, imp_z))
    m.tube((0.0, axis_y + imp_r * 0.15, imp_z), imp_r * 1.14, imp_r * 1.0, 0.3, axis=2,
           segments=_segments(imp_r) + 4, mat=BODY)
    m.cylinder((0.0, axis_y + imp_r * 0.15, imp_z), imp_r * 0.24, 0.3, axis=2,
               segments=10, mat=METAL, parent="blower_impeller")
    blades = int(_clamp(3.0 + rate / 60.0, 4.0, 7.0))
    for i in range(blades):
        a = 2.0 * math.pi * i / blades
        c, s = math.cos(a), math.sin(a)
        m.box((c * imp_r * 0.6, axis_y + imp_r * 0.15 + s * imp_r * 0.6, imp_z),
              (0.04, imp_r * 0.95, 0.26), mat=METAL, parent="blower_impeller",
              rot=mk.unity_euler(0.0, 0.0, math.degrees(math.atan2(s, c)) - 90.0),
              bevel=False)

    ring_y = axis_y + imp_r * 1.3
    m.node("blower_chute", pivot=(0.0, ring_y, imp_z))
    m.tube((0.0, ring_y, imp_z), imp_r * 0.62, imp_r * 0.5, 0.1, axis=1, segments=14,
           mat=METAL)
    chute_h = _clamp(0.5 + throw / 34.0, 0.6, 1.3)
    m.cylinder((0.0, ring_y + chute_h * 0.5, imp_z), imp_r * 0.55, chute_h, axis=1,
               segments=14, mat=BODY, parent="blower_chute", radius_end=imp_r * 0.42,
               caps=False)
    # The deflector at the top is what sets how far the snow actually goes.
    m.box((0.0, ring_y + chute_h + imp_r * 0.18, imp_z + imp_r * 0.4),
          (imp_r * 1.0, imp_r * 0.9, 0.05), mat=BODY, parent="blower_chute",
          rot=mk.unity_euler(_clamp(70.0 - throw, 25.0, 60.0), 0.0, 0.0))
    _bolts(m, (0.0, ring_y - 0.04, imp_z), imp_r * 0.58, 8, 0.022, axis=1)
    return [("att_col", (0.0, GROUND + house_h * 0.5, house_z),
             (width + 0.1, house_h + 0.2, auger_r * 3.2))]


# --------------------------------------------------------------------------- buckets
# The inside face of a bucket as (y, z) on a unit square: cutting edge at the front
# bottom, a curved floor sweeping back to the heel, then up the back plate. The straight
# line that closes it from the top of the back plate to the lip is the open mouth, so the
# polygon it encloses is the struck capacity.
_BUCKET_UNIT = ((0.0, 1.0), (0.02, 0.70), (0.09, 0.40), (0.22, 0.17), (0.42, 0.04),
                (0.66, 0.0), (0.88, 0.05), (1.0, 0.14))


def _bucket_profile(area, wall, aspect=0.78):
    """A bucket shell whose mouth encloses exactly `area` square metres, as (y, z) points.

    The bucket is the attachment whose art has to agree with a simulation number:
    Effects.BucketM3 is what the sim scoops with, so the inside face is built on a unit
    shape and scaled until width times enclosed area is that volume, then offset outward
    by the plate thickness to make the shell. Change the m3 in the JSON and it gets deeper.
    """
    inner = [(y * aspect, z) for y, z in _BUCKET_UNIT]
    a = 0.0
    for i in range(len(inner)):
        y0, z0 = inner[i]
        y1, z1 = inner[(i + 1) % len(inner)]
        a += y0 * z1 - y1 * z0
    scale = math.sqrt(area / max(1e-4, abs(a) * 0.5))
    inner = [(y * scale, z * scale) for y, z in inner]
    cy = sum(y for y, _z in inner) / len(inner)
    cz = sum(z for _y, z in inner) / len(inner)
    outer = []
    for y, z in inner:
        dy, dz = y - cy, z - cz
        d = math.hypot(dy, dz) or 1.0
        outer.append((y + dy / d * wall, z + dz / d * wall))
    return inner + list(reversed(outer)), inner


def _build_bucket(m, record):
    """A snow or light-material bucket, sized so the shell holds Effects.BucketM3."""
    width = max(0.8, _width(record, 2.4))
    volume = _eff(record, "BucketM3", 1.5)
    lift = _eff(record, "LiftCapacityKg", 2000.0)
    profile, inner = _bucket_profile(volume / width, _clamp(volume * 0.02, 0.03, 0.06))
    depth = max(z for _y, z in inner)
    height = max(y for y, _z in inner)

    _mount_plate(m, _clamp(width * 0.3, 0.5, 1.0), _clamp(height * 0.7, 0.4, 0.9))
    m.node("bucket", pivot=(0.0, GROUND + height * 0.2, 0.12))
    m.prism(profile, (0.0, GROUND, 0.16), width, mat=BODY, parent="bucket", axis=0)
    # Side plates stand a little proud of the shell: they are the wear part on a bucket.
    for side in (-1.0, 1.0):
        m.prism(profile, (side * (width * 0.5 + 0.012), GROUND, 0.16), 0.03, mat=METAL,
                parent="bucket", axis=0)
    # Cutting edge and its bolt row along the lip.
    m.box((0.0, GROUND + 0.03, 0.16 + depth * 0.97), (width * 0.99, 0.1, 0.05),
          mat=METAL, parent="bucket")
    count = int(_clamp(width / 0.34, 4.0, 12.0))
    span = width * 0.88
    bolt = m.box((-span * 0.5, GROUND + 0.055, 0.16 + depth * 0.94), (0.04, 0.04, 0.03),
                 mat=METAL, parent="bucket", bevel=False)
    m.array(bolt, count, (span / max(1, count - 1), 0.0, 0.0), parent="bucket")
    # Back frame: two ribs and a cross tube carrying the load back to the mount.
    rib_x = _clamp(width * 0.2, 0.3, 0.7)
    for side in (-1.0, 1.0):
        m.box((side * rib_x, GROUND + height * 0.45, 0.1),
              (_clamp(lift / 60000.0, 0.04, 0.09), height * 0.8, 0.12), mat=BODY,
              parent="bucket")
        _bar(m, (side * rib_x, GROUND + height * 0.86, 0.14),
             (side * rib_x * 0.7, 0.02, 0.06), 0.08, parent="bucket")
    _bar(m, (-rib_x, GROUND + height * 0.8, 0.12), (rib_x, GROUND + height * 0.8, 0.12),
         0.09, parent="bucket")
    # Spill guard: it sits on the rim at the top of the back plate, which is where the
    # snow comes over when the bucket is full.
    m.box((0.0, GROUND + height + 0.03, 0.16 + depth * 0.16),
          (width * 0.99, 0.06, depth * 0.26), mat=BODY, parent="bucket",
          rot=mk.unity_euler(-18.0, 0.0, 0.0))
    # A heel wear bar and corner protectors: the three places a bucket wears out, and the
    # three bolt-on parts a yard keeps on the shelf for it.
    m.box((0.0, GROUND + height * 0.06, 0.15), (width * 0.99, 0.09, 0.06), mat=METAL,
          parent="bucket")
    for side in (-1.0, 1.0):
        m.prism([(GROUND + 0.02, 0.16 + depth * 0.72),
                 (GROUND + height * 0.3, 0.16 + depth * 0.86),
                 (GROUND + height * 0.32, 0.16 + depth * 0.99),
                 (GROUND + 0.02, 0.16 + depth * 0.99)],
                (side * (width * 0.5 + 0.03), 0.0, 0.0), 0.04, mat=METAL, parent="bucket",
                axis=0)
        m.tube((side * rib_x * 0.55, GROUND + height * 0.92, 0.13), 0.07, 0.035, 0.03,
               axis=2, segments=10, mat=METAL, parent="bucket")
    ribs = int(_clamp(width / 0.6, 3.0, 7.0))
    span = width * 0.8
    plate = m.box((-span * 0.5, GROUND + height * 0.55, 0.1),
                  (0.05, height * 0.5, 0.05), mat=BODY, parent="bucket", bevel=False)
    m.array(plate, ribs, (span / max(1, ribs - 1), 0.0, 0.0), parent="bucket")
    return [("att_col", (0.0, GROUND + height * 0.5, 0.16 + depth * 0.5),
             (width, height, depth))]


def _build_forks(m, record):
    """Pallet forks: a carriage and two tines that slide along it."""
    reach = _clamp(_eff(record, "ReachM", 1.2), 0.7, 2.0)
    lift = _eff(record, "LiftCapacityKg", 2000.0)
    spread = max(0.6, _width(record, 1.2))
    thick = _clamp(0.03 + lift / 90000.0, 0.035, 0.075)
    height = _clamp(0.9 + lift / 9000.0, 0.9, 1.5)
    rail_y = (GROUND + 0.12, GROUND + height * 0.85)

    _mount_plate(m, spread * 0.8, height * 0.7)
    for y in rail_y:
        _bar(m, (-spread * 0.75, y, 0.09), (spread * 0.75, y, 0.09), 0.075, mat=METAL)
    for side in (-1.0, 1.0):
        _bar(m, (side * spread * 0.72, rail_y[0], 0.09),
             (side * spread * 0.72, rail_y[1], 0.09), 0.07, mat=METAL)
        m.box((side * spread * 0.3, GROUND + height * 0.5, 0.05),
              (0.06, height * 0.7, 0.06), mat=METAL)

    for side, node in ((-1.0, "fork_L"), (1.0, "fork_R")):
        x = side * spread * 0.5
        m.node(node, pivot=(x, rail_y[1], 0.09))
        # The tine is one L section: shank up the carriage, blade forward along the snow,
        # tapering to the tip the way a fork that has to slide under a pallet does.
        z0, shank = 0.10, GROUND + height * 0.88
        m.prism([(GROUND + 0.01, z0), (GROUND + 0.01, z0 + reach),
                 (GROUND + 0.01 + thick * 0.45, z0 + reach),
                 (GROUND + 0.01 + thick, z0 + thick * 2.0),
                 (shank, z0 + thick), (shank, z0)],
                (x, 0.0, 0.0), _clamp(0.09 + lift / 40000.0, 0.1, 0.2), mat=METAL,
                parent=node, axis=0)
        for y in rail_y:
            m.box((x, y, 0.09), (_clamp(0.16 + lift / 30000.0, 0.18, 0.3), 0.14, 0.16),
                  mat=METAL, parent=node)
    # Load backrest: the grid that stops a pallet coming through the cab window.
    back_h = height * 0.55
    bars = int(_clamp(spread / 0.24, 4.0, 9.0))
    span = spread * 1.3
    upright = m.box((-span * 0.5, GROUND + height * 0.9 + back_h * 0.5, 0.05),
                    (0.05, back_h, 0.05), mat=METAL, bevel=False)
    m.array(upright, bars, (span / max(1, bars - 1), 0.0, 0.0))
    for t in (0.15, 0.85):
        _bar(m, (-span * 0.55, GROUND + height * 0.9 + back_h * t, 0.05),
             (span * 0.55, GROUND + height * 0.9 + back_h * t, 0.05), 0.05)
    for side in (-1.0, 1.0):
        _bar(m, (side * span * 0.5, GROUND + height * 0.9 + back_h, 0.05),
             (side * spread * 0.6, rail_y[1], 0.09), 0.055)
    return [("att_col", (0.0, GROUND + height * 0.4, reach * 0.5),
             (spread * 1.5, height, reach + 0.2))]


def _build_grapple(m, record):
    """A grapple: a tined lower jaw and a clamp that closes onto it."""
    width = max(0.8, _width(record, 1.6))
    reach = _clamp(_eff(record, "ReachM", 1.4), 0.8, 2.2)
    lift = _eff(record, "LiftCapacityKg", 2000.0)
    thick = _clamp(0.04 + lift / 60000.0, 0.05, 0.1)

    _mount_plate(m, width * 0.5, 0.7)
    m.node("bucket", pivot=(0.0, GROUND + 0.24, 0.12))
    jaw = [(0.0, 0.12), (0.0, 0.12 + reach), (0.1, 0.12 + reach),
           (0.34, 0.5), (0.62, 0.16), (0.62, 0.12)]
    tines = int(_clamp(width / 0.42, 3.0, 6.0))
    for i in range(tines):
        x = -width * 0.38 + width * 0.76 * i / max(1, tines - 1)
        m.prism(jaw, (x, GROUND + 0.02, 0.0), thick * 1.6, mat=METAL, parent="bucket",
                axis=0)
    m.prism([(0.28, 0.1), (0.62, 0.1), (0.62, 0.02), (0.34, 0.02)],
            (0.0, GROUND, 0.0), width * 0.9, mat=BODY, parent="bucket", axis=0)
    _bar(m, (-width * 0.42, GROUND + 0.1, 0.12 + reach * 0.75),
         (width * 0.42, GROUND + 0.1, 0.12 + reach * 0.75), 0.06, parent="bucket")
    _bar(m, (-width * 0.42, GROUND + 0.6, 0.16), (width * 0.42, GROUND + 0.6, 0.16), 0.08,
         parent="bucket")

    # The clamp swings down onto the load; its pivot is the top of the back frame.
    m.node("grapple_arm", pivot=(0.0, GROUND + 0.72, 0.2), parent="bucket")
    claw = [(0.0, 0.0), (0.0, reach * 0.72), (0.12, reach * 0.78), (0.3, reach * 0.3),
            (0.34, 0.0)]
    for i in range(max(2, tines - 1)):
        x = -width * 0.3 + width * 0.6 * i / max(1, max(2, tines - 1) - 1)
        m.prism(claw, (x, GROUND + 0.72, 0.2), thick * 1.4, mat=BODY,
                parent="grapple_arm", axis=0)
    _bar(m, (-width * 0.34, GROUND + 0.74, 0.24), (width * 0.34, GROUND + 0.74, 0.24),
         0.07, parent="grapple_arm")
    _pin(m, (0.0, GROUND + 0.72, 0.2), thick * 1.2, width * 0.78, axis=0,
         parent="grapple_arm")
    for side in (-1.0, 1.0):
        _ram(m, (side * width * 0.22, GROUND + 0.95, 0.02),
             (side * width * 0.28, GROUND + 0.88, 0.34), thick * 1.1, parent="grapple_arm")
    return [("att_col", (0.0, GROUND + 0.5, 0.12 + reach * 0.5),
             (width + 0.2, 1.4, reach + 0.3))]


# --------------------------------------------------------------------------- rotating
def _build_broom(m, record):
    """An angle broom: a bristle drum on a frame, with a hood and a motor."""
    width = max(0.8, _width(record, 2.2))
    radius = _clamp(0.26 + _eff(record, "CutDepthMm", 40.0) / 400.0, 0.28, 0.45)
    axis_y = GROUND + radius * 0.94
    drum_z = 0.5 + radius * 1.2
    yaw = 14.0                    # a broom is hung angled, which is the whole point of it
    dx, dz = _yaw_dir(yaw)

    _mount_plate(m, _clamp(width * 0.28, 0.45, 0.8), 0.6)
    m.node("tiller_rotor", pivot=(0.0, axis_y, drum_z))
    core = [(radius * 0.22, -width * 0.5), (radius * 0.34, -width * 0.46),
            (radius * 0.34, width * 0.46), (radius * 0.22, width * 0.5)]
    m.lathe(core, (0.0, axis_y, drum_z), segments=12, mat=METAL, parent="tiller_rotor",
            axis=0, rot=mk.unity_euler(0.0, yaw, 0.0))
    # The bristle mass is one lathe; the rows of bristles are what break its silhouette.
    m.lathe([(radius * 0.4, -width * 0.46), (radius, -width * 0.42),
             (radius, width * 0.42), (radius * 0.4, width * 0.46)],
            (0.0, axis_y, drum_z), segments=_segments(radius) + 4, mat=METAL,
            parent="tiller_rotor", axis=0, rot=mk.unity_euler(0.0, yaw, 0.0))
    rows = 12
    for i in range(rows):
        a = 2.0 * math.pi * i / rows
        dy, dzr = math.cos(a), math.sin(a)
        m.box((0.0, axis_y + dy * radius * 0.72, drum_z + dzr * radius * 0.72),
              (width * 0.9, radius * 0.62, 0.03), mat=METAL, parent="tiller_rotor",
              rot=mk.unity_euler(math.degrees(math.atan2(-dzr, dy)), yaw, 0.0),
              bevel=False)

    # Hood over the top half, sitting on the frame rather than on the drum.
    hood = []
    for i in range(9):
        a = _lerp(math.radians(-10.0), math.radians(190.0), i / 8.0)
        hood.append((math.sin(a) * radius * 1.22, math.cos(a) * radius * 1.22))
    for i in range(9):
        a = _lerp(math.radians(190.0), math.radians(-10.0), i / 8.0)
        hood.append((math.sin(a) * radius * 1.14, math.cos(a) * radius * 1.14))
    m.prism(hood, (0.0, axis_y, drum_z), width * 0.98, mat=BODY,
            rot=mk.unity_euler(0.0, yaw, 0.0), axis=0)
    for side in (-1.0, 1.0):
        end = (side * dx * width * 0.5, axis_y, drum_z + side * dz * width * 0.5)
        m.box((end[0], end[1] + radius * 0.1, end[2]), (0.06, radius * 2.2, radius * 2.2),
              mat=BODY, rot=mk.unity_euler(0.0, yaw, 0.0))
        _bar(m, (side * 0.22, 0.02, 0.06),
             (end[0] * 0.92, axis_y + radius * 1.2, end[2] * 0.94), 0.09)
        m.cylinder((end[0] * 1.06, axis_y, end[2] * 1.06), radius * 0.3, 0.2, axis=0,
                   segments=10, mat=METAL, rot=mk.unity_euler(0.0, yaw, 0.0))
    _bar(m, (-0.28, 0.02, 0.06), (0.28, 0.02, 0.06), 0.1)
    # Castor wheels set the brush height: run a broom on its bristles and it lasts a week.
    for side in (-1.0, 1.0):
        x = side * dx * width * 0.42
        z = drum_z + side * dz * width * 0.42 - radius * 1.45
        _bar(m, (x, axis_y + radius * 0.5, z + 0.1), (x, GROUND + 0.16, z), 0.06)
        _wheel(m, (x, GROUND + 0.12, z), 0.12, 0.07)
    return [("att_col", (0.0, axis_y, drum_z), (width, radius * 2.4, radius * 2.6))]


def _build_auger(m, record):
    """A post-hole auger: a helical flight on a shaft under a drive head."""
    bore = max(0.15, _width(record, 0.6))
    length = _clamp(0.9 + bore * 1.6, 1.1, 2.4)
    head_y = 0.55

    _mount_plate(m, 0.5, 0.6)
    # The boom that holds the auger out in front of the machine.
    _bar(m, (0.0, 0.05, 0.06), (0.0, head_y, 0.62), 0.14, mat=BODY)
    _bar(m, (-0.22, 0.02, 0.06), (0.0, head_y * 0.55, 0.42), 0.08)
    _bar(m, (0.22, 0.02, 0.06), (0.0, head_y * 0.55, 0.42), 0.08)
    m.box((0.0, head_y + 0.12, 0.62), (0.34, 0.3, 0.34), mat=BODY)
    m.cylinder((0.0, head_y + 0.04, 0.62), 0.13, 0.2, axis=1, segments=12, mat=METAL)
    _bolts(m, (0.0, head_y + 0.14, 0.62), 0.16, 6, 0.025, axis=1)

    m.node("auger_bit", pivot=(0.0, head_y - 0.06, 0.62))
    top = head_y - 0.1
    m.cylinder((0.0, top - length * 0.5, 0.62), bore * 0.17, length, axis=1, segments=10,
               mat=METAL, parent="auger_bit")
    _helix(m, (0.0, top - length * 0.52, 0.62), bore * 0.17, bore * 0.5, length * 0.88,
           2.4, axis=1, steps=30, parent="auger_bit")
    # The point and its two carbide teeth: the end of an auger reads as a drill or not.
    m.cylinder((0.0, top - length - 0.09, 0.62), bore * 0.1, 0.2, axis=1, segments=8,
               mat=METAL, parent="auger_bit", radius_end=0.012)
    for side in (-1.0, 1.0):
        m.box((side * bore * 0.34, top - length + 0.02, 0.62), (0.05, 0.07, 0.05),
              mat=METAL, parent="auger_bit", bevel=False)
    return [("att_col", (0.0, top - length * 0.4, 0.62),
             (bore + 0.1, length + 0.4, bore + 0.1))]


def _build_mulcher(m, record):
    """A forestry mulcher: a heavy housing, a tooth drum and push skids."""
    width = max(0.8, _width(record, 2.0))
    radius = _clamp(0.26 + _eff(record, "CutDepthMm", 100.0) / 700.0, 0.3, 0.5)
    axis_y = GROUND + radius * 1.05
    drum_z = 0.55 + radius * 1.3
    mass = _mass(record, 1500.0)

    _mount_plate(m, _clamp(width * 0.3, 0.5, 0.9), 0.8)
    m.node("tiller_rotor", pivot=(0.0, axis_y, drum_z))
    _rotor(m, "tiller_rotor", (0.0, axis_y, drum_z), width * 0.92, radius, rows=4)

    # The housing is a thick box open at the front bottom, with a hinged pusher bar.
    house_h = radius * 2.1
    profile = [(GROUND + 0.02, -radius * 1.5), (GROUND + 0.02, radius * 1.35),
               (GROUND + house_h * 0.45, radius * 1.6), (GROUND + house_h, radius * 1.3),
               (GROUND + house_h, -radius * 1.5)]
    m.prism(profile, (0.0, 0.0, drum_z), width, mat=BODY, axis=0)
    m.box((0.0, GROUND + house_h * 0.62, drum_z + radius * 1.5),
          (width * 0.98, radius * 0.5, 0.07), mat=METAL,
          rot=mk.unity_euler(18.0, 0.0, 0.0))
    for side in (-1.0, 1.0):
        m.box((side * width * 0.5, GROUND + house_h * 0.5, drum_z),
              (0.06, house_h, radius * 3.0), mat=BODY)
        # Skid shoes carry the housing on the ground and set the cutting height.
        m.prism([(0.0, -radius * 1.4), (0.0, radius * 1.4), (0.1, radius * 1.55),
                 (0.14, -radius * 1.5)],
                (side * width * 0.5, GROUND, drum_z), 0.07, mat=METAL, axis=0)
        _bar(m, (side * 0.3, 0.02, 0.06),
             (side * width * 0.36, GROUND + house_h * 0.9, drum_z - radius * 1.2), 0.12,
             mat=BODY)
        _ram(m, (side * 0.42, 0.4, 0.0),
             (side * width * 0.34, GROUND + house_h * 0.95, drum_z - radius * 0.6),
             _clamp(mass / 26000.0, 0.06, 0.11))
    # Drive motor and belt guard on one end, which is where the power actually arrives.
    m.cylinder((width * 0.5 + 0.13, axis_y, drum_z), radius * 0.42, 0.26, axis=0,
               segments=12, mat=METAL)
    m.box((width * 0.5 + 0.16, axis_y + radius * 0.4, drum_z),
          (0.1, radius * 1.6, radius * 2.2), mat=BODY)
    return [("att_col", (0.0, GROUND + house_h * 0.5, drum_z),
             (width + 0.2, house_h, radius * 3.2))]


# --------------------------------------------------------------------------- spreading
def _build_spreader(m, record):
    """A hopper spreader: hopper, conveyor slot and the disc that throws the grit."""
    volume = _capacity_m3(record, _mass(record, 900.0) / 230.0)
    width = max(0.8, _width(record, 2.0))
    spread = _clamp(_eff(record, "SpreadWidthM", width), 1.5, 8.0)
    # A hopper is a V: the section that holds `volume` over the length the body allows.
    body_w = _clamp(width * 0.62, 0.9, 2.0)
    length = _clamp(volume / max(0.35, body_w * 0.62), 1.0, 3.2)
    height = _clamp(volume / max(0.4, body_w * length * 0.62), 0.7, 1.6)
    floor_y = GROUND + 0.5
    mid_z = 0.42 + length * 0.5

    _mount_plate(m, _clamp(body_w * 0.7, 0.5, 1.0), 0.8)
    # Hopper shell: a wide mouth narrowing to the conveyor slot along the bottom.
    top = height + floor_y
    sections = []
    for z in (mid_z - length * 0.5, mid_z + length * 0.5):
        sections.append([(-body_w * 0.5, top, z), (body_w * 0.5, top, z),
                         (body_w * 0.16, floor_y, z), (-body_w * 0.16, floor_y, z)])
    m.loft(sections, mat=BODY)
    m.box((0.0, floor_y - 0.04, mid_z), (body_w * 0.34, 0.08, length * 0.98), mat=METAL)
    # A grate over the mouth and a frame under the belly: both are structure, both read.
    bars = int(_clamp(length / 0.28, 3.0, 9.0))
    grate = m.box((0.0, top + 0.02, mid_z - length * 0.45), (body_w * 0.96, 0.04, 0.05),
                  mat=METAL, bevel=False)
    m.array(grate, bars, (0.0, 0.0, length * 0.9 / max(1, bars - 1)))
    for side in (-1.0, 1.0):
        _bar(m, (side * body_w * 0.42, floor_y - 0.06, mid_z - length * 0.46),
             (side * body_w * 0.42, floor_y - 0.06, mid_z + length * 0.46), 0.09)
        _bar(m, (side * body_w * 0.42, floor_y - 0.06, mid_z - length * 0.4),
             (side * body_w * 0.3, 0.0, 0.08), 0.08)
    m.box((body_w * 0.5, floor_y + height * 0.35, mid_z + length * 0.5),
          (0.22, 0.3, 0.28), mat=METAL)

    disc_y = GROUND + 0.22
    disc_z = mid_z + length * 0.5 + 0.18
    m.node("spreader_disc", pivot=(0.0, disc_y, disc_z))
    m.lathe([(0.02, 0.0), (_clamp(spread * 0.06, 0.18, 0.34), 0.03),
             (_clamp(spread * 0.06, 0.18, 0.34), 0.06), (0.05, 0.08)],
            (0.0, disc_y, disc_z), segments=16, mat=METAL, parent="spreader_disc")
    fins = 4
    for i in range(fins):
        a = 2.0 * math.pi * i / fins
        r = _clamp(spread * 0.045, 0.12, 0.24)
        m.box((math.cos(a) * r, disc_y + 0.07, disc_z + math.sin(a) * r),
              (_clamp(spread * 0.05, 0.14, 0.28), 0.06, 0.03), mat=METAL,
              parent="spreader_disc", rot=mk.unity_euler(0.0, -math.degrees(a), 0.0),
              bevel=False)
    _bar(m, (0.0, floor_y - 0.02, disc_z - 0.1), (0.0, disc_y + 0.12, disc_z), 0.07)
    m.box((0.0, disc_y + 0.26, disc_z), (0.24, 0.26, 0.2), mat=BODY)
    # A shroud round the back of the disc keeps grit off the machine, and the chute drops
    # the feed onto it; without both, a spreader reads as a box with a plate under it.
    shroud = _clamp(spread * 0.08, 0.26, 0.45)
    for i in range(7):
        a = _lerp(math.radians(120.0), math.radians(300.0), i / 6.0)
        m.box((math.cos(a) * shroud, disc_y + 0.14, disc_z + math.sin(a) * shroud),
              (0.06, 0.22, 0.06), mat=BODY, rot=mk.unity_euler(0.0, -math.degrees(a), 0.0),
              bevel=False)
    m.prism([(-0.22, floor_y - 0.02), (0.22, floor_y - 0.02), (0.16, disc_y + 0.14),
             (-0.16, disc_y + 0.14)],
            (0.0, 0.0, disc_z - 0.04), 0.3, mat=BODY, axis=2)
    # Access ladder up the side of the hopper, because someone has to get the tarp off.
    for side in (-1.0, 1.0):
        rungs = 3
        rung = m.box((side * body_w * 0.52, floor_y + 0.12, mid_z + length * 0.3),
                     (0.18, 0.04, 0.04), mat=METAL, bevel=False)
        m.array(rung, rungs, (0.0, height * 0.3, 0.0))
        _bar(m, (side * body_w * 0.56, floor_y + 0.06, mid_z + length * 0.3),
             (side * body_w * 0.56, floor_y + height * 0.8, mid_z + length * 0.3), 0.04)
    return [("att_col", (0.0, floor_y + height * 0.4, mid_z),
             (body_w, height + 0.6, length + 0.4))]


def _build_tank(m, record):
    """A brine tank and spray bar, built to the litres the record publishes."""
    volume = _capacity_m3(record, _mass(record, 700.0) / 140.0)
    spread = _clamp(_eff(record, "SpreadWidthM", 3.0), 1.2, 8.0)
    width = max(0.8, _width(record, 2.0))
    body_w = _clamp(width * 0.6, 0.9, 2.0)
    radius = _clamp(body_w * 0.5, 0.4, 0.9)
    length = _clamp(volume / max(0.2, math.pi * radius * radius), 0.9, 3.6)
    axis_y = GROUND + radius + 0.45
    mid_z = 0.45 + length * 0.5

    _mount_plate(m, _clamp(body_w * 0.7, 0.5, 1.0), 0.8)
    # A real tank is a lathe: dished ends, not a cut-off cylinder.
    m.lathe([(0.0, -length * 0.5 - radius * 0.3),
             (radius * 0.7, -length * 0.5 - radius * 0.12),
             (radius, -length * 0.5), (radius, length * 0.5),
             (radius * 0.7, length * 0.5 + radius * 0.12),
             (0.0, length * 0.5 + radius * 0.3)],
            (0.0, axis_y, mid_z), segments=_segments(radius) + 6, mat=BODY, axis=2)
    # Baffle rings, the filler and a sight tube.
    rings = int(_clamp(length / 0.8, 2.0, 5.0))
    for i in range(rings):
        z = mid_z - length * 0.4 + length * 0.8 * i / max(1, rings - 1)
        m.tube((0.0, axis_y, z), radius * 1.04, radius * 0.99, 0.04, axis=2,
               segments=_segments(radius) + 6, mat=METAL)
    m.cylinder((0.0, axis_y + radius * 0.95, mid_z - length * 0.2), radius * 0.2, 0.14,
               axis=1, segments=10, mat=METAL)
    _bolts(m, (0.0, axis_y + radius + 0.05, mid_z - length * 0.2), radius * 0.26, 6, 0.02,
           axis=1)
    # Cradle: a tank does not sit on a frame, it sits in one, so the bolsters are cut to
    # the barrel and the tank drops into them.
    for z in (mid_z - length * 0.32, mid_z + length * 0.32):
        bolster = [(-radius * 1.1, GROUND + 0.3), (radius * 1.1, GROUND + 0.3)]
        for i in range(9):
            a = _lerp(math.radians(-20.0), math.radians(200.0), i / 8.0)
            bolster.append((math.cos(a) * radius * 1.02,
                            axis_y - math.sin(a) * radius * 1.02))
        m.prism(bolster, (0.0, 0.0, z), 0.07, mat=METAL, axis=2)
    for side in (-1.0, 1.0):
        _bar(m, (side * radius * 0.8, GROUND + 0.35, mid_z - length * 0.45),
             (side * radius * 0.8, GROUND + 0.35, mid_z + length * 0.45), 0.09)
        _bar(m, (side * radius * 0.8, GROUND + 0.35, mid_z - length * 0.4),
             (side * radius * 0.6, 0.0, 0.08), 0.08)

    # Pump and manifold at the back, and the spray bar across the width it treats.
    pump_z = mid_z + length * 0.5 + 0.2
    m.cylinder((0.0, GROUND + 0.55, pump_z), 0.16, 0.26, axis=2, segments=12, mat=METAL)
    m.lathe([(0.05, 0.0), (0.19, 0.04), (0.19, 0.12), (0.05, 0.16)],
            (0.0, GROUND + 0.55, pump_z + 0.18), segments=14, mat=METAL, axis=2)
    m.box((0.0, GROUND + 0.3, pump_z), (0.4, 0.24, 0.22), mat=BODY)
    bar_y = GROUND + 0.16
    _bar(m, (-spread * 0.5, bar_y, pump_z + 0.1), (spread * 0.5, bar_y, pump_z + 0.1), 0.06,
         mat=METAL)
    nozzles = int(_clamp(spread / 0.45, 4.0, 14.0))
    span = spread * 0.92
    nozzle = m.box((-span * 0.5, bar_y - 0.07, pump_z + 0.1), (0.035, 0.09, 0.035),
                   mat=METAL, bevel=False)
    m.array(nozzle, nozzles, (span / max(1, nozzles - 1), 0.0, 0.0))
    for side in (-1.0, 1.0):
        _bar(m, (side * span * 0.45, bar_y, pump_z + 0.1),
             (side * radius * 0.7, GROUND + 0.4, pump_z - 0.1), 0.04)
    return [("att_col", (0.0, axis_y, mid_z), (radius * 2.2, radius * 2.4, length + 0.5))]


def _build_pump(m, record):
    """A towed pump skid: engine, pump volute and manifolds on a trailer frame."""
    width = max(0.9, _width(record, 2.0))
    mass = _mass(record, 1800.0)
    length = _clamp(1.1 + mass / 1400.0, 1.6, 3.2)
    deck_y = GROUND + 0.55
    body_w = _clamp(width * 0.8, 0.9, 1.9)

    _mount_plate(m, 0.45, 0.5)
    _trailer(m, length, body_w, deck_y, mass)
    m.box((0.0, deck_y + 0.05, length * 0.55), (body_w * 0.96, 0.1, length * 0.8),
          mat=METAL)
    # Engine block under a canopy, pump on the same skid, coupled across a guard.
    eng_z = length * 0.75
    m.box((0.0, deck_y + 0.34, eng_z), (body_w * 0.72, 0.52, length * 0.34), mat=BODY)
    m.box((0.0, deck_y + 0.66, eng_z), (body_w * 0.78, 0.14, length * 0.38), mat=BODY)
    louvre = m.box((body_w * 0.37, deck_y + 0.34, eng_z - length * 0.12),
                   (0.03, 0.36, 0.04), mat=METAL, bevel=False)
    m.array(louvre, 5, (0.0, 0.0, length * 0.05))
    m.cylinder((body_w * 0.25, deck_y + 0.78, eng_z + length * 0.1), 0.05, 0.34, axis=1,
               segments=8, mat=METAL)

    pump_z = length * 0.32
    m.lathe([(0.06, -0.12), (0.28, -0.1), (0.32, 0.0), (0.28, 0.1), (0.06, 0.12)],
            (0.0, deck_y + 0.3, pump_z), segments=16, mat=METAL, axis=2)
    m.cylinder((0.0, deck_y + 0.3, pump_z + 0.2), 0.1, 0.22, axis=2, segments=10, mat=METAL)
    # Suction and discharge: flanged pipes are what makes a pump skid legible at all.
    for side, radius in ((-1.0, 0.13), (1.0, 0.09)):
        x = side * body_w * 0.33
        m.cylinder((x, deck_y + 0.3, pump_z), radius, body_w * 0.5, axis=0, segments=12,
                   mat=METAL)
        m.cylinder((x, deck_y + 0.52, pump_z), radius, 0.5, axis=1, segments=12, mat=METAL)
        m.lathe([(radius, 0.0), (radius * 1.7, 0.0), (radius * 1.7, 0.04), (radius, 0.04)],
                (x, deck_y + 0.76, pump_z), segments=12, mat=METAL, axis=1)
        _bolts(m, (x, deck_y + 0.79, pump_z), radius * 1.4, 6, 0.022, axis=1)
    m.box((body_w * 0.3, deck_y + 0.5, length * 0.1), (0.26, 0.3, 0.18), mat=BODY)
    return [("att_col", (0.0, deck_y + 0.4, length * 0.55), (body_w, 1.2, length))]


# --------------------------------------------------------------------------- rope
def _rope_wound(rope_m, pull_kn, drum_len, hub_r):
    """How far out the rope winds on a drum, and the flange that has to contain it.

    Rope diameter follows the pull the record rates it for, the wound volume follows the
    length, and the radius is solved from both - so a 1,200 m winch drum really is bigger
    than a 1,000 m one, and a 2,500 m haul rope reel is the size it has to be.
    """
    rope_d = _clamp(0.0018 * math.sqrt(max(1.0, pull_kn)) * 4.0, 0.010, 0.032)
    volume = rope_m * math.pi * (rope_d * 0.5) ** 2 * ROPE_PACKING
    wound = math.sqrt(volume / max(0.1, math.pi * drum_len) + hub_r * hub_r)
    wound = _clamp(wound, hub_r * 1.15, hub_r * 4.5)
    return wound, wound * 1.22


def _rope_drum(m, node, centre, wound, flange, drum_len, hub_r, mat=METAL):
    """The drum itself: hub, the wound rope on it and a flange each end."""
    m.cylinder(centre, hub_r, drum_len, axis=0, segments=_segments(hub_r) + 2, mat=mat,
               parent=node)
    m.cylinder(centre, wound, drum_len * 0.92, axis=0, segments=_segments(wound) + 4,
               mat=METAL, parent=node)
    for side in (-1.0, 1.0):
        m.lathe([(hub_r * 0.5, 0.0), (flange, 0.0), (flange, 0.05), (hub_r * 0.5, 0.06)],
                (centre[0] + side * drum_len * 0.5, centre[1], centre[2]),
                segments=_segments(flange) + 4, mat=mat, parent=node, axis=0)
        _bolts(m, (centre[0] + side * (drum_len * 0.5 + 0.04), centre[1], centre[2]),
               flange * 0.55, 6, 0.022, axis=0, parent=node)


def _build_winch(m, record):
    """A roof winch: a drum in its housing, a boom that yaws and a fairlead."""
    rope = _eff(record, "RopeLengthM", 1000.0)
    pull = _eff(record, "PullKn", 45.0)
    mass = _mass(record, 1400.0)
    scale = _clamp(mass / 1400.0, 0.7, 1.5)
    drum_len = _clamp(0.5 + pull / 200.0, 0.5, 0.95)
    hub_r = _clamp(0.09 + pull / 700.0, 0.1, 0.2)
    base_y = 0.0

    _mount_plate(m, 0.8 * scale, 0.5)
    m.box((0.0, base_y + 0.12, 0.0), (1.0 * scale, 0.26, 0.9 * scale), mat=BODY)
    for side in (-1.0, 1.0):
        m.box((side * 0.42 * scale, base_y + 0.1, side * 0.36 * scale),
              (0.1, 0.3, 0.24), mat=METAL)

    m.node("winch_boom", pivot=(0.0, base_y + 0.26, 0.0))
    # The drum has to stand clear of the cab roof for the rope to run to the anchor, so
    # the post grows with the pull it is rated for rather than with the drum.
    tower = _clamp(1.0 + pull / 120.0, 1.0, 1.7) * scale
    m.lathe([(0.3 * scale, 0.0), (0.28 * scale, 0.08), (0.22 * scale, 0.1)],
            (0.0, base_y + 0.26, 0.0), segments=16, mat=METAL, parent="winch_boom")
    head = (0.0, base_y + 0.26 + tower, 0.0)
    for side in (-1.0, 1.0):
        _bar(m, (side * 0.22 * scale, base_y + 0.3, -0.06),
             (side * 0.14 * scale, head[1], 0.0), 0.1 * scale, mat=BODY, parent="winch_boom")
        _bar(m, (side * 0.2 * scale, base_y + 0.34, 0.16),
             (side * 0.13 * scale, head[1] - 0.1, 0.02), 0.07, parent="winch_boom")

    m.node("winch_drum", pivot=head, parent="winch_boom")
    wound, flange = _rope_wound(rope, pull, drum_len, hub_r)
    _rope_drum(m, "winch_drum", head, wound, flange, drum_len, hub_r)
    # The housing is a cover over the top of the drum between two bearing plates cut to
    # the drum: a closed box would hide the one part of a winch anybody looks at.
    for side in (-1.0, 1.0):
        m.lathe([(0.0, 0.0), (flange * 1.16, 0.0), (flange * 1.16, 0.05), (0.0, 0.05)],
                (head[0] + side * (drum_len * 0.5 + 0.06), head[1], head[2]),
                segments=10, mat=BODY, parent="winch_boom", axis=0)
        _bolts(m, (head[0] + side * (drum_len * 0.5 + 0.1), head[1], head[2]),
               flange * 0.95, 6, 0.024, axis=0, parent="winch_boom")
    m.prism([(flange * 0.9, -flange * 1.3), (flange * 1.25, -flange * 0.5),
             (flange * 1.25, flange * 0.9), (flange * 1.1, flange * 1.15),
             (flange * 1.0, flange * 0.85), (flange * 1.12, -flange * 0.45),
             (flange * 0.78, -flange * 1.25)],
            (head[0], head[1], head[2]), drum_len * 1.1, mat=BODY, parent="winch_boom",
            axis=0)
    fair_z = flange * 1.5
    for side in (-1.0, 1.0):
        m.cylinder((head[0] + side * flange * 0.55, head[1] - flange * 0.2, fair_z),
                   flange * 0.16, flange * 0.5, axis=1, segments=10, mat=METAL,
                   parent="winch_boom")
    m.cylinder((head[0], head[1] - flange * 0.42, fair_z), flange * 0.14, flange * 1.1,
               axis=0, segments=10, mat=METAL, parent="winch_boom")
    m.box((head[0], head[1] - flange * 0.05, fair_z), (flange * 1.5, flange * 0.16, 0.08),
          mat=METAL, parent="winch_boom")
    # A rope guard and the hydraulic motor on the drum end.
    m.cylinder((head[0] + drum_len * 0.72, head[1], head[2]), flange * 0.34, 0.22, axis=0,
               segments=12, mat=METAL, parent="winch_boom")
    return [("att_col", (0.0, base_y + 0.26 + tower, 0.0),
             (drum_len * 1.5, flange * 2.6, flange * 3.2))]


def _build_reel(m, record):
    """A rope reel trailer: a flanged reel on a towed frame with a brake and a guide."""
    rope = _eff(record, "RopeLengthM", 2000.0)
    mass = _mass(record, 2000.0)
    width = max(1.0, _width(record, 2.4))
    body_w = _clamp(width * 0.7, 1.0, 2.0)
    length = _clamp(1.4 + mass / 1600.0, 1.8, 3.4)
    deck_y = GROUND + 0.55
    drum_len = _clamp(body_w * 0.62, 0.6, 1.4)
    hub_r = _clamp(0.18 + rope / 26000.0, 0.2, 0.36)

    # The reel is rated in rope, not in steel, so its flanges follow the wound volume and
    # the axle then has to sit high enough that a full reel clears the frame it hangs in.
    wound, flange = _rope_wound(rope, 90.0, drum_len, hub_r)
    axle_y = deck_y + flange * 0.78
    axle_z = length * 0.58

    _mount_plate(m, 0.45, 0.5)
    _trailer(m, length, body_w, deck_y, mass)
    m.node("winch_drum", pivot=(0.0, axle_y, axle_z))
    _rope_drum(m, "winch_drum", (0.0, axle_y, axle_z), wound, flange, drum_len, hub_r,
               mat=BODY)
    spokes = 6
    for side in (-1.0, 1.0):
        x = side * drum_len * 0.5
        for i in range(spokes):
            a = 2.0 * math.pi * i / spokes
            m.box((x, axle_y + math.cos(a) * flange * 0.6,
                   axle_z + math.sin(a) * flange * 0.6),
                  (0.05, flange * 0.9, 0.05), mat=BODY,
                  rot=mk.unity_euler(math.degrees(a), 0.0, 0.0), bevel=False)
        # Stands, bearing blocks and the brake band on one side.
        _bar(m, (x * 1.35, deck_y + 0.04, axle_z), (x * 1.2, axle_y, axle_z), 0.12, mat=BODY)
        m.box((x * 1.2, axle_y, axle_z), (0.14, 0.22, 0.22), mat=METAL)
    m.cylinder((0.0, axle_y, axle_z), hub_r * 0.45, drum_len * 1.5, axis=0, segments=10,
               mat=METAL)
    m.tube((drum_len * 0.62, axle_y, axle_z), flange * 0.62, flange * 0.5, 0.06, axis=0,
           segments=14, mat=METAL)
    # Rope guide across the front of the reel: a roller pair on a cross bar.
    guide_z = axle_z - flange - 0.2
    _bar(m, (-body_w * 0.45, deck_y + 0.3, guide_z), (body_w * 0.45, deck_y + 0.3, guide_z),
         0.07)
    for side in (-1.0, 1.0):
        m.cylinder((side * 0.16, deck_y + 0.42, guide_z), 0.07, 0.26, axis=1, segments=10,
                   mat=METAL)
    m.cylinder((0.0, deck_y + 0.56, guide_z), 0.06, 0.44, axis=0, segments=10, mat=METAL)
    return [("att_col", (0.0, axle_y, axle_z), (drum_len + 0.4, flange * 2.2, flange * 2.2))]


# --------------------------------------------------------------------------- hitches
def _build_hitch(m, record):
    """A drawbar with a ball or a pintle, depending on what the record says it tows."""
    capacity = _eff(record, "LiftCapacityKg", 1000.0)
    width = max(0.25, _width(record, 0.5))
    heavy = capacity >= 1000.0
    bar = _clamp(0.05 + capacity / 90000.0, 0.05, 0.12)
    length = _clamp(width * 1.1, 0.3, 0.9)
    drop = _clamp(0.1 + capacity / 40000.0, 0.1, 0.26)

    _mount_plate(m, _clamp(width * 1.2, 0.3, 0.7), _clamp(width * 1.1, 0.3, 0.6))
    # Drawbar: out from the plate, then down to the coupling height.
    m.box((0.0, 0.0, length * 0.55), (bar * 1.6, bar * 1.6, length), mat=METAL)
    m.box((0.0, -drop * 0.5, length * 1.02), (bar * 1.5, drop + bar, bar * 1.6), mat=METAL)
    for side in (-1.0, 1.0):
        _bar(m, (side * width * 0.42, 0.02, 0.06), (side * bar * 0.6, 0.0, length * 0.8),
             bar * 0.9)
        # Safety chain eyes with a few links hanging off them, on every real hitch there
        # has ever been. The links alternate their axis, which is what a chain is.
        m.tube((side * bar * 1.3, -drop * 0.2, length * 0.72), bar * 0.62, bar * 0.34,
               bar * 0.5, axis=0, segments=10, mat=METAL)
        for i in range(3):
            m.tube((side * bar * 1.3, -drop * 0.2 - bar * (0.7 + i * 0.62),
                    length * 0.72), bar * 0.4, bar * 0.22, bar * 0.22,
                   axis=0 if i % 2 else 2, segments=8, mat=METAL)

    head = (0.0, -drop, length * 1.02)
    if heavy:
        # A pintle: jaw, hook and the latch over it.
        m.prism([(0.0, -bar * 1.4), (bar * 2.2, -bar * 1.6), (bar * 2.6, 0.0),
                 (bar * 2.0, bar * 1.8), (bar * 0.6, bar * 1.4), (0.0, bar * 0.6)],
                (head[0], head[1], head[2]), bar * 2.2, mat=METAL, axis=0)
        m.box((head[0], head[1] + bar * 2.4, head[2] + bar * 0.4),
              (bar * 2.4, bar * 0.8, bar * 2.6), mat=METAL,
              rot=mk.unity_euler(18.0, 0.0, 0.0))
        _pin(m, (head[0], head[1] + bar * 2.4, head[2] - bar * 1.1), bar * 0.5, bar * 2.8,
             axis=0)
        m.socket("hitch", (head[0], head[1] + bar * 0.6, head[2] + bar * 1.2))
    else:
        # A ball on its shank, with the lynch pin through the shank below.
        m.cylinder((head[0], head[1] + bar * 0.5, head[2]), bar * 0.9, bar * 1.2, axis=1,
                   segments=12, mat=METAL)
        m.sphere((head[0], head[1] + bar * 1.7, head[2]), bar * 1.25, segments=12, rings=8,
                 mat=METAL)
        m.cylinder((head[0], head[1] - bar * 0.4, head[2]), bar * 0.28, bar * 3.2, axis=0,
                   segments=8, mat=METAL)
        m.socket("hitch", (head[0], head[1] + bar * 1.7, head[2]))
    return [("att_col", (0.0, -drop * 0.5, length * 0.6),
             (width, drop + bar * 4.0, length * 1.3))]


# --------------------------------------------------------------------------- structures
def _build_jib(m, record):
    """A lattice jib: one bay arrayed out to Effects.ReachM, with a head and a hook."""
    reach = _clamp(_eff(record, "ReachM", 6.0), 2.0, 12.0)
    capacity = _eff(record, "LiftCapacityKg", 2000.0)
    face = _clamp(0.18 + capacity / 30000.0, 0.2, 0.42)
    chord = _clamp(0.035 + capacity / 90000.0, 0.04, 0.075)
    pitch = 22.0
    bays = int(_clamp(reach / _clamp(face * 2.6, 0.5, 1.1), 3.0, 10.0))
    bay = reach / bays
    dy, dz = math.sin(math.radians(pitch)), math.cos(math.radians(pitch))
    root_y, root_z = 0.35, 0.22

    _mount_plate(m, 0.7, 0.8)
    m.box((0.0, 0.14, 0.16), (0.6, 0.5, 0.22), mat=BODY)
    m.node("boom_01", pivot=(0.0, root_y, root_z))

    def _point(t, sx, sy):
        return (sx * face * 0.5, root_y + dy * t + sy * face * 0.5, root_z + dz * t)

    corners = ((-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0))
    for sx, sy in corners:
        _bar(m, _point(0.0, sx, sy), _point(reach, sx, sy), chord, mat=BODY,
             parent="boom_01")
    # One bay of lacing, arrayed along the jib: a lattice is a repeat, so build the repeat.
    seed = []
    for (sx0, sy0), (sx1, sy1) in ((corners[0], corners[1]), (corners[2], corners[3]),
                                   (corners[0], corners[3]), (corners[1], corners[2])):
        seed += _bar(m, _point(0.0, sx0, sy0), _point(0.0, sx1, sy1), chord * 0.7,
                     parent="boom_01", bevel=False)
        seed += _bar(m, _point(0.0, sx0, sy0), _point(bay, sx1, sy1), chord * 0.55,
                     parent="boom_01", bevel=False)
    m.array(seed, bays + 1, (0.0, dy * bay, dz * bay), parent="boom_01")

    # Head: a sheave between cheek plates, and the hook block hanging under it.
    head = _point(reach, 0.0, 0.0)
    for sx in (-1.0, 1.0):
        m.box((head[0] + sx * face * 0.4, head[1], head[2]), (0.04, face * 1.1, face * 1.2),
              mat=BODY, parent="boom_01")
    m.lathe([(face * 0.16, -0.04), (face * 0.42, -0.05), (face * 0.36, 0.0),
             (face * 0.42, 0.05), (face * 0.16, 0.04)],
            (head[0], head[1], head[2] + face * 0.2), segments=14, mat=METAL,
            parent="boom_01", axis=0)
    hook_y = head[1] - _clamp(reach * 0.12, 0.4, 1.0)
    _bar(m, (head[0], head[1], head[2] + face * 0.2), (head[0], hook_y, head[2] + face * 0.2),
         0.018, parent="boom_01", bevel=False)
    m.box((head[0], hook_y - 0.1, head[2] + face * 0.2), (0.14, 0.22, 0.1), mat=METAL,
          parent="boom_01")
    m.cylinder((head[0], hook_y - 0.26, head[2] + face * 0.2), 0.05, 0.2, axis=1,
               segments=8, mat=METAL, parent="boom_01", radius_end=0.02)
    return [("att_col", (0.0, root_y + dy * reach * 0.5, root_z + dz * reach * 0.5),
             (face * 1.4, dy * reach + face, dz * reach + face))]


def _build_light_tower(m, record):
    """A light tower: nested mast sections and a head of lamps, on its own trailer."""
    lumens = _eff(record, "LumensBonus", 40000.0)
    mass = _mass(record, 650.0)
    width = max(0.8, _width(record, 1.5))
    lamps = int(_clamp(lumens / 15000.0, 2.0, 6.0))
    height = _clamp(3.0 + lumens / 18000.0, 3.4, 7.5)
    body_w = _clamp(width * 0.8, 0.8, 1.6)
    length = _clamp(1.0 + mass / 700.0, 1.3, 2.6)
    deck_y = GROUND + 0.5

    _mount_plate(m, 0.45, 0.5)
    _trailer(m, length, body_w, deck_y, mass)
    # Generator cabinet on the deck: a light tower is a power plant with a mast on it.
    m.box((0.0, deck_y + 0.3, length * 0.72), (body_w * 0.9, 0.55, length * 0.5), mat=BODY)
    m.box((0.0, deck_y + 0.6, length * 0.72), (body_w * 0.94, 0.08, length * 0.54), mat=BODY)
    louvre = m.box((body_w * 0.45, deck_y + 0.3, length * 0.6), (0.03, 0.34, 0.05),
                   mat=METAL, bevel=False)
    m.array(louvre, 5, (0.0, 0.0, length * 0.05))
    for side in (-1.0, 1.0):
        # Outriggers: the reason the thing does not fall over with the mast up.
        _bar(m, (side * body_w * 0.4, deck_y - 0.05, length * 0.3),
             (side * body_w * 0.85, deck_y - 0.05, length * 0.3), 0.08)
        m.cylinder((side * body_w * 0.85, GROUND + 0.12, length * 0.3), 0.06, 0.3, axis=1,
                   segments=8, mat=METAL)
        m.cylinder((side * body_w * 0.85, GROUND + 0.02, length * 0.3), 0.13, 0.05, axis=1,
                   segments=10, mat=METAL)

    mast_z = length * 0.32
    m.node("mast", pivot=(0.0, deck_y + 0.1, mast_z))
    sections = 3
    for i in range(sections):
        size = _clamp(0.24 - i * 0.045, 0.12, 0.24)
        seg_h = height / sections
        m.box((0.0, deck_y + 0.1 + seg_h * (i + 0.5), mast_z), (size, seg_h * 1.02, size),
              mat=BODY if i == 0 else METAL, parent="mast")
    # The head has to read from the other side of a car park at night, so the lamps are
    # sized to be seen rather than to scale off the mast.
    top = deck_y + 0.1 + height
    span = _clamp(lamps * 0.42, 0.9, 2.0)
    _bar(m, (-span * 0.5, top, mast_z), (span * 0.5, top, mast_z), 0.09, parent="mast")
    for i in range(lamps):
        x = -span * 0.42 + span * 0.84 * (i / max(1, lamps - 1))
        _lamp(m, (x, top + 0.26, mast_z), 0.34, parent="mast", aim=20.0)
        _bar(m, (x, top + 0.04, mast_z), (x, top + 0.22, mast_z), 0.05, parent="mast")
        m.socket("light_work_%02d" % (i + 1), (x, top + 0.26, mast_z + 0.2))
    _bar(m, (0.0, deck_y + 0.6, mast_z), (0.0, deck_y + 0.2, mast_z + 0.4), 0.05)
    return [("att_col", (0.0, deck_y + height * 0.5, mast_z), (0.4, height, 0.4))]


# --------------------------------------------------------------------------- dispatch
_BUILDERS = {
    "Blade": _build_blade,
    "Blade12Way": _build_blade,
    "UBlade": _build_blade,
    "ParkBlade": _build_blade,
    "PlowStraight": _build_blade,
    "PlowWings": _build_blade,
    "VPlow": _build_blade,
    "BoxPusher": _build_blade,
    "Tiller": _build_tiller,
    "ParkTiller": _build_tiller,
    "TrackSetter": _build_tiller,
    "PipeCutter": _build_tiller,
    "BlowerHead": _build_blower,
    "SnowBucket": _build_bucket,
    "LightBucket": _build_bucket,
    "Forks": _build_forks,
    "Grapple": _build_grapple,
    "Broom": _build_broom,
    "Auger": _build_auger,
    "Mulcher": _build_mulcher,
    "Spreader": _build_spreader,
    "BrineTank": _build_tank,
    "PumpSkid": _build_pump,
    "Winch": _build_winch,
    "CableReel": _build_reel,
    "Hitch": _build_hitch,
    "SledHitch": _build_hitch,
    "TowerJib": _build_jib,
    "LightTower": _build_light_tower,
}

KINDS = frozenset(_BUILDERS)


def build(record, out_path):
    """Build one attachment from its attachments.json record."""
    kind = _kind(record)
    builder = _BUILDERS.get(kind, _build_blade)
    model = mk.Model(record["Id"], budget_key="attachment",
                     seed=datasrc.seed_for(record["Id"]))
    colliders = builder(model, record) or []
    vis = datasrc.visual(record)
    return export.emit(model, out_path, box_colliders=colliders,
                       extra={"family": "attachment", "kind": "attachment",
                              "displayName": record.get("DisplayName", record["Id"]),
                              "sourceId": record["Id"],
                              "attachmentKind": kind,
                              # The mesh carries three material slots and no more, so the
                              # livery colours travel in the manifest for LiveryTint to
                              # apply rather than as vertex colour or a fourth material.
                              "colorHex": vis["ColorHex"],
                              "accentHex": vis["AccentHex"]})
