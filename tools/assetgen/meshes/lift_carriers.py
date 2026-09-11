"""Lift carriers: the thing a rider actually sits in, on every line in lifts.json.

One generator covers the whole spread - a village double chair, a 32-passenger tricable
cabin, a 150-passenger tram car, a T-bar, a belt tile - because what separates them is
numbers, not kind. SeatsOrCabinCapacity says how many riders have to fit and therefore
every dimension of what they fit into. Family says whether they sit in the open, in a
cabin, in a car on rails, or hang off a bar. Grip says whether the carrier owns a
detachable grip body or a clamp bolted round the rope. RopeConfiguration says how many
ropes it hangs from, which is what turns a cabin into a funitel yoke or a tricable
bogie. ComfortScore pays for cushions, headrests and glass. LoadTimeS is a dwell, and a
long dwell is a car that has to fill and empty through several doors at once.

Two readings of `Options` are possible and only one of them survives contact with the
rest of the file. Each option carries a CapexPct and an OpexPct - a percentage *added*
to the type's cost - so `Options` is the upgrade menu, not the equipment list, and a
type that can still be sold a bubble does not have one yet. The one type that comes
with bubbles fitted shows it in the only place the data records it: its ComfortScore
sits exactly the bubble option's ComfortBonus above the otherwise identical chair that
does still offer the upgrade. So `carrier.fbx` is the chair as delivered, and every
chair that offers the upgrade also writes a `carrier_bubble.fbx` sibling for the state
after the player buys it - the same pattern lift_towers uses for its height classes.
"""
import math
import os

from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

# Carriers are addressed by lift record, not by a machine's Visual.Silhouette.
SILHOUETTES = frozenset()

# Parts that butt against each other are overlapped by this much instead. meshkit welds
# vertices closer than a hundredth of a millimetre, and two shells meeting on an exact
# plane would weld into edges with four faces on them, which is the non-manifold failure
# in validate.py.
LAP = 0.004

# Width one rider occupies on a chair, hip to hip with a coat on. Everything about a
# chair's width - frame, bar, bubble, footrest - is this times the capacity.
SEAT_PITCH_M = 0.52

# A standing car does not have one socket per rider without becoming a list of a
# hundred and fifty empties, so the grid stops here and the view crowds the rest.
SOCKET_LIMIT = 40


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

    A 0.35 m funicular wheel earns more sides than a 0.05 m bolt, and the small stuff is
    where a procedural pipeline throws its budget away.
    """
    return int(_clamp(6.0 + radius * 22.0, 8.0, 16.0))


def _options(record):
    return list(record.get("Options") or [])


def _option_bonus(option_id, field, default=0.0):
    """One field off the shared option table in lifts.json."""
    for opt in datasrc.lift_options():
        if opt.get("Id") == option_id:
            return _num(opt.get(field), default)
    return default


def _bubble_fitted(record):
    """Whether this chair comes with bubbles on it.

    `Options` lists what can still be bought, so a chair that offers the bubble has not
    got one. The chair that has is the one whose ComfortScore already carries the
    option's ComfortBonus over an otherwise identical chair that does still offer it -
    which is the data saying "fitted as standard" without anyone naming a lift id.
    """
    if str(record.get("Family")) != "Chair" or "bubble" in _options(record):
        return False
    bonus = _option_bonus("bubble", "ComfortBonus", 0.15)
    comfort = _num(record.get("ComfortScore"), 0.0)
    for peer in datasrc.lifts():
        if peer is record or str(peer.get("Family")) != "Chair":
            continue
        if "bubble" not in _options(peer):
            continue
        if peer.get("Grip") != record.get("Grip"):
            continue
        if peer.get("SeatsOrCabinCapacity") != record.get("SeatsOrCabinCapacity"):
            continue
        if comfort - _num(peer.get("ComfortScore"), 0.0) >= bonus - 0.005:
            return True
    return False


def _carrier_kind(record):
    """Which carrier this lift hangs on its rope, from what the record says it is.

    The surface tests are the only non-obvious ones. A conveyor has no span worth the
    name because it lies on the ground, so MaxSpanM separates a belt from a rope. Among
    the rope tows, capacity separates a T-bar from a single-rider carrier, and
    CapexPerCarrier separates the two of those: sixty dollars buys a handle on a strap,
    nine hundred buys a spring box and a disc.
    """
    family = str(record.get("Family") or "Chair")
    rope = str(record.get("RopeConfiguration") or "Mono")
    if family == "Rail":
        return "rail"
    if family == "Aerial":
        return "tram" if rope == "Reversible" else "cabin"
    if family == "Hybrid":
        return "hybrid"
    if family == "Gondola":
        return "cabin"
    if family == "Surface":
        if _num(record.get("MaxSpanM"), 100.0) <= 40.0:
            return "belt"
        if _num(record.get("SeatsOrCabinCapacity"), 1.0) >= 2.0:
            return "tbar"
        return "platter" if _num(record.get("CapexPerCarrier"), 0.0) >= 200.0 else "handle"
    return "chair"


def _common(record):
    """The handful of record fields every carrier is shaped by."""
    return {
        "id": str(record.get("Id") or "lift"),
        "name": str(record.get("DisplayName") or record.get("Id") or "lift"),
        "family": str(record.get("Family") or "Chair"),
        "rope": str(record.get("RopeConfiguration") or "Mono"),
        "grip": str(record.get("Grip") or "Fixed"),
        "detachable": str(record.get("Grip")) == "Detachable",
        "capacity": max(1, int(_num(record.get("SeatsOrCabinCapacity"), 1.0))),
        "tier": int(_num(record.get("Tier"), 1.0)),
        "comfort": _clamp(_num(record.get("ComfortScore"), 0.5), 0.0, 1.0),
        "load_s": max(1.0, _num(record.get("LoadTimeS"), 6.0)),
        "speed": max(0.3, _num(record.get("LineSpeedMs"), 2.5)),
        "grade": _num(record.get("MaxGradeDeg"), 25.0),
        "spacing": _num(record.get("CarrierSpacingM"), 0.0),
        "pph": max(100.0, _num(record.get("CapacityPph"), 1000.0)),
        "exposure": _num(record.get("WeatherExposure"), 1.0),
        "heated": "heated_seats" in _options(record),
    }


# --------------------------------------------------------------------------- profiles
def _ring(half_x, half_z, y, corner, per_corner):
    """Closed loop of Unity points round a rounded rectangle in the XZ plane.

    A cabin's cross section is this at a series of heights: a tier-1 shell samples two
    points per corner and comes out boxy and welded-looking, a tier-5 shell samples four
    and comes out as the wraparound moulding those actually are.
    """
    r = min(corner, half_x * 0.9, half_z * 0.9)
    n = max(2, int(per_corner))
    centres = ((half_x - r, half_z - r), (-(half_x - r), half_z - r),
               (-(half_x - r), -(half_z - r)), (half_x - r, -(half_z - r)))
    pts = []
    for k, (cx, cz) in enumerate(centres):
        a0 = math.radians(90.0 * k)
        for i in range(n):
            a = a0 + math.radians(90.0) * i / (n - 1)
            pts.append((cx + math.cos(a) * r, float(y), cz + math.sin(a) * r))
    return pts


def _scaled_ring(half_x, half_z, y, corner, per_corner, scale):
    return _ring(half_x * scale, half_z * scale, y, corner * scale, per_corner)


def _arc_shell(cu, cv, radius, thickness, a0_deg, a1_deg, samples):
    """Closed 2D loop for an arc shell: the outer sweep, then the inner sweep back.

    Both the bubble canopy over a chair and the gallery hoop over a conveyor belt are
    this shape in a different plane, so they share the profile and differ only in which
    pair of axes it is mapped onto.
    """
    n = max(3, int(samples))
    loop = []
    for i in range(n):
        a = math.radians(a0_deg + (a1_deg - a0_deg) * i / (n - 1))
        loop.append((cu + math.cos(a) * radius, cv + math.sin(a) * radius))
    inner = radius - thickness
    for i in range(n - 1, -1, -1):
        a = math.radians(a0_deg + (a1_deg - a0_deg) * i / (n - 1))
        loop.append((cu + math.cos(a) * inner, cv + math.sin(a) * inner))
    return loop


def _tilt(pitch_deg):
    return mk.unity_euler(pitch_deg, 0.0, 0.0)


def _on_tilt(origin, pitch_deg, local):
    """A point in a tilted body's own frame, as a model-space point.

    A funicular car is a staircase: each compartment's floor is level in the world while
    the chassis under it follows the rails. In the model - which is track space, +Z up
    the hill - that means the compartment is pitched nose-down by the design grade and
    its parts have to be assembled in its frame, not in the car's.
    """
    p = math.radians(pitch_deg)
    c, s = math.cos(p), math.sin(p)
    x, y, z = local
    return (origin[0] + x, origin[1] + y * c + z * s, origin[2] - y * s + z * c)


# --------------------------------------------------------------------------- grips
def _grip(m, s, node, x, y, parent=None):
    """The grip body, opening about X where it bites the rope.

    A detachable owns a real grip: a cast body, a jaw pair, the spring stack that holds
    the jaw shut with no power on it, and the cam roller the terminal rail presses to
    open it. A fixed grip is two plates and four bolts, which is all a chair that never
    lets go of the rope needs.
    """
    m.node(node, pivot=(x, y, 0.0), parent=parent)
    if s["detachable"]:
        m.box((x, y, 0.0), (0.22, 0.30, 0.40), mat=METAL, parent=node)
        for sx in (-1.0, 1.0):
            m.box((x + sx * 0.085, y + 0.16, 0.0), (0.07, 0.14, 0.44), mat=METAL,
                  parent=node)
        for i, sy in enumerate((-0.20, -0.34)):
            m.cylinder((x, y + sy, 0.0), 0.075 - i * 0.012, 0.15, axis=1,
                       segments=_seg(0.075), mat=METAL, parent=node)
        m.cylinder((x, y + 0.28, 0.0), 0.065, 0.11, axis=0, segments=_seg(0.065),
                   mat=METAL, parent=node)
        for sz in (-1.0, 1.0):
            m.cylinder((x, y + 0.05, sz * 0.24), 0.055, 0.12, axis=1, segments=8,
                       mat=METAL, parent=node)
        return
    for sx in (-1.0, 1.0):
        m.box((x + sx * 0.08, y + 0.02, 0.0), (0.06, 0.20, 0.30), mat=METAL, parent=node)
        for sz in (-0.10, 0.10):
            m.cylinder((x + sx * 0.08, y + 0.12, sz), 0.022, 0.20, axis=0, segments=6,
                       mat=METAL, parent=node)
    m.box((x, y - 0.12, 0.0), (0.20, 0.12, 0.24), mat=METAL, parent=node)


def _hanger_blade(m, s, node, y0, y1, z0, z1, width, parent=None):
    """The hanger arm itself: a tapered blade from the grip down to what it carries."""
    profile = [(y0, z0 - width * 0.9), (y0, z0 + width * 0.9),
               (y1, z1 + width * 0.55), (y1, z1 - width * 0.55)]
    m.prism(profile, (0.0, 0.0, 0.0), width, mat=BODY, parent=node, axis=0)


# --------------------------------------------------------------------------- chairs
def _chair_spec(record, bubble):
    s = _common(record)
    cap = s["capacity"]
    comfort = s["comfort"]
    s.update({
        "bubble": bool(bubble),
        "seat_w": cap * SEAT_PITCH_M,
        "frame_w": cap * SEAT_PITCH_M + 0.20,
        "pitch": SEAT_PITCH_M,
        "pan_y": 0.62,
        "cushion_y": 0.68 + 0.02 * comfort,
        "back_top": 1.20 + 0.14 * comfort,
        "hinge_y": 0.76,
        "foot_r": 0.055,
    })
    # How far the chair hangs below the rope: a bigger chair swings harder and needs a
    # longer arm to damp it, and a detachable adds the depth of its grip body on top.
    s["hanger_len"] = 1.70 + 0.06 * cap + (0.30 if s["detachable"] else 0.0)
    s["grip_y"] = s["back_top"] + 0.10 + s["hanger_len"]
    return s


def _chair_seats(m, s, parent):
    """One seat - cushion, back pad, headrest - arrayed across the capacity.

    The contour is in the shell behind it, which is one prism across the whole width;
    the seat itself is repeated, so it is built plain. meshkit's array copies the faces
    it is handed and a chamfer replaces those faces, so anything arrayed is unchamfered.
    """
    cap, pitch = s["capacity"], s["pitch"]
    x0 = -s["seat_w"] * 0.5 + pitch * 0.5
    faces = []
    faces += m.box((x0, s["cushion_y"], 0.06), (pitch - 0.05, 0.09, 0.40), mat=BODY,
                   parent=parent, bevel=False)
    back_h = s["back_top"] - s["cushion_y"] - 0.08
    faces += m.box((x0, s["cushion_y"] + 0.06 + back_h * 0.5, -0.14),
                   (pitch - 0.05, back_h, 0.08), mat=BODY, parent=parent,
                   rot=_tilt(12.0), bevel=False)
    if s["comfort"] >= 0.55:
        faces += m.box((x0, s["back_top"] + 0.04, -0.21), (pitch - 0.16, 0.16, 0.09),
                       mat=BODY, parent=parent, rot=_tilt(12.0), bevel=False)
    m.array(faces, cap, (pitch, 0.0, 0.0), parent=parent)

    # Dividers, one per seat boundary: the two outer ones are the arms.
    div = m.box((-s["seat_w"] * 0.5, 0.92, -0.02), (0.05, 0.34, 0.36), mat=METAL,
                parent=parent, bevel=False)
    m.array(div, cap + 1, (pitch, 0.0, 0.0), parent=parent)


def _build_chair(record, bubble, model_name):
    s = _chair_spec(record, bubble)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "chair"))
    half = s["frame_w"] * 0.5
    hang_y = s["grip_y"] - 0.16

    _grip(m, s, "grip_arm", 0.0, s["grip_y"])
    m.node("cabin_hanger", pivot=(0.0, hang_y, 0.0))
    _hanger_blade(m, s, "cabin_hanger", hang_y + 0.06, s["back_top"] - 0.06,
                  -0.02, -0.34, 0.16)

    # The seat shell: one prism across the full width carries the contour, so the seats
    # arrayed onto it can stay plain.
    shell = [(0.50, 0.26), (s["pan_y"] + 0.02, 0.28), (s["pan_y"] + 0.02, -0.10),
             (s["back_top"], -0.22), (s["back_top"], -0.38), (0.52, -0.34)]
    m.prism(shell, (0.0, 0.0, 0.0), s["seat_w"] + 0.06, mat=BODY,
            parent="cabin_hanger", axis=0)
    for sx in (-1.0, 1.0):
        m.box((sx * half, 0.86, -0.20), (0.06, 0.60, 0.30), mat=METAL,
              parent="cabin_hanger")
    m.box((0.0, 0.46, -0.02), (s["frame_w"], 0.10, 0.34), mat=METAL,
          parent="cabin_hanger")
    _chair_seats(m, s, "cabin_hanger")

    # The restraint bar. Every chair has one; what the auto option buys is the damper
    # that closes it, and on the modern chairs the footrest that comes down with it.
    m.node("bar", pivot=(0.0, s["hinge_y"], -0.06), parent="cabin_hanger")
    bar_y, bar_z = 0.88, 0.52
    for sx in (-1.0, 1.0):
        m.beam((sx * (half - 0.03), s["hinge_y"], -0.06),
               (sx * (half - 0.07), bar_y, bar_z), 0.055, mat=METAL, parent="bar",
               square=False, segments=8)
    m.cylinder((0.0, bar_y, bar_z), 0.030, s["frame_w"] - 0.14, axis=0,
               segments=8, mat=METAL, parent="bar")
    if "safety_bar_auto" in _options(record):
        for sx in (-1.0, 1.0):
            m.beam((sx * (half - 0.05), s["hinge_y"] + 0.02, 0.02),
                   (sx * (half - 0.06), bar_y - 0.10, bar_z * 0.55), 0.05, mat=METAL,
                   parent="bar", square=False, segments=6)

    foot_parent = "bar" if (s["tier"] >= 3 or s["detachable"]) else "cabin_hanger"
    m.cylinder((0.0, s["foot_r"], 0.64), s["foot_r"], s["frame_w"] * 0.88, axis=0,
               segments=8, mat=METAL, parent=foot_parent)
    for sx in (-1.0, 1.0):
        if foot_parent == "bar":
            m.beam((sx * s["seat_w"] * 0.30, bar_y - 0.03, bar_z),
                   (sx * s["seat_w"] * 0.30, s["foot_r"] + 0.02, 0.64), 0.04,
                   mat=METAL, parent="bar", square=False, segments=6)
        else:
            m.beam((sx * s["seat_w"] * 0.30, 0.50, 0.16),
                   (sx * s["seat_w"] * 0.30, s["foot_r"] + 0.02, 0.64), 0.045,
                   mat=METAL, parent="cabin_hanger", square=False, segments=6)

    if s["bubble"]:
        _chair_bubble(m, s)

    for i in range(s["capacity"]):
        x = -s["seat_w"] * 0.5 + s["pitch"] * (i + 0.5)
        m.socket("seat_%02d" % (i + 1), (x, s["cushion_y"] + 0.05, 0.06))

    colliders = [("seat_col", (0.0, 0.95, 0.10), (s["frame_w"], 1.05, 1.00))]
    return m, colliders, s


def _chair_bubble(m, s):
    """A polycarbonate canopy lofted over the seats, hinged behind the back.

    It is one arc section swept across the chair with the end sections drawn in, which
    is how the real ones are made: a constant extruded profile with a formed end cap.
    """
    radius = 0.80 + 0.14 * s["comfort"]
    cy = s["cushion_y"] + 0.24
    loop = _arc_shell(-0.02, cy, radius, 0.035, 158.0, -22.0, 7)
    half = s["seat_w"] * 0.5 + 0.10
    sections = []
    for x, scale in ((-half, 0.93), (-half * 0.96, 1.0),
                     (half * 0.96, 1.0), (half, 0.93)):
        sections.append([(x, cy + (v - cy) * scale, -0.02 + (u + 0.02) * scale)
                         for u, v in loop])
    m.node("bubble", pivot=(0.0, s["back_top"] + 0.06, -0.40), parent="cabin_hanger")
    m.loft(sections, mat=GLASS, parent="bubble")
    for sx in (-1.0, 1.0):
        m.beam((sx * half, s["back_top"] + 0.06, -0.40),
               (sx * half, cy + radius * 0.86, -0.02 - radius * 0.42), 0.05,
               mat=METAL, parent="bubble", square=False, segments=6)


# --------------------------------------------------------------------------- cabins
def _cabin_spec(record, capacity):
    s = _common(record)
    cap = max(4, int(capacity))
    comfort, tier = s["comfort"], s["tier"]
    # Floor area per rider falls as a cabin grows - an eight-seater gives each rider
    # about 0.40 m2 and a 32-passenger tricable about 0.29 - which is a cube-root law
    # in every dimension, and it is also why a big cabin is taller as well as wider.
    unit = float(cap) ** (1.0 / 3.0)
    s.update({
        "cap": cap,
        "half_x": 0.43 * unit,                  # across the line
        "half_z": 0.51 * unit,                  # along the line, where the doors are
        "height": 1.45 + 0.33 * unit,
        "corner": 0.10 + 0.05 * tier,
        "per_corner": 2 if tier <= 2 else (3 if tier <= 3 else 4),
        "sill": 0.86 + 0.05 * comfort,
        "head_drop": 0.46 - 0.14 * comfort,
        "bench": max(2, cap // 2),
        # A long dwell is a car that fills through more than one door at once, and a big
        # cabin needs the aperture whatever its dwell, so both numbers get a vote.
        "leaves": int(_clamp(max(round(s["load_s"] / 45.0), round(cap / 16.0)), 1, 3)),
    })
    s["head"] = s["height"] - s["head_drop"]
    s["hanger_len"] = 1.35 + 0.05 * cap + (0.35 if s["detachable"] else 0.0)
    s["grip_y"] = s["height"] + s["hanger_len"]
    return s


def _shell(m, s, parent, half_x, half_z, height, sill, head, corner, per_corner,
           floor_scale=0.88, roof_scale=0.84):
    """Body, window band and roof as three lofts sharing one rounded cross section.

    The band is a separate loft on the glass slot rather than a texture, and the body's
    closing rings become the sill shelf and the ceiling, which is what they are. The
    three overlap by a lap rather than meeting on a plane: meeting exactly would weld
    into non-manifold edges.
    """
    args = (corner, per_corner)
    body = [_scaled_ring(half_x, half_z, 0.0, *args, floor_scale),
            _scaled_ring(half_x, half_z, sill * 0.36, *args, 1.0),
            _ring(half_x, half_z, sill, *args)]
    m.loft(body, mat=BODY, parent=parent)
    m.loft([_ring(half_x, half_z, sill - LAP, *args),
            _scaled_ring(half_x, half_z, head, *args, 0.99)],
           mat=GLASS, parent=parent, closed_ends=False)
    m.loft([_scaled_ring(half_x, half_z, head - LAP, *args, 0.99),
            _scaled_ring(half_x, half_z, height - 0.12, *args, 0.97),
            _scaled_ring(half_x, half_z, height, *args, roof_scale)],
           mat=BODY, parent=parent)


def _doors(m, s, parent, half_x, half_z, sill, head, leaves, aperture):
    """Sliding leaves standing proud of the side walls, one node per side.

    They sit on the outside of the skin because that is where a plug door sits when it
    is shut, and it is the only way to read a door on a lofted shell without cutting it.
    """
    span = aperture * leaves
    for side, node in ((-1.0, "door_L"), (1.0, "door_R")):
        m.node(node, pivot=(side * half_x, sill * 0.5, 0.0), parent=parent)
        for i in range(leaves):
            z = -span * 0.5 + aperture * (i + 0.5)
            x = side * (half_x + 0.035)
            m.box((x, (head + 0.10) * 0.5 + 0.05, z),
                  (0.055, head + 0.05, aperture - 0.05), mat=BODY, parent=node)
            m.box((x + side * 0.02, (sill + head) * 0.5 + 0.14, z),
                  (0.03, head - sill - 0.06, aperture - 0.18), mat=GLASS, parent=node)
            m.box((x, 0.30, z), (0.035, 0.16, aperture - 0.22), mat=METAL, parent=node)


def _bench(m, s, parent, half_x, half_z, z_sign, count, seats, y0=0.0):
    """One bench seat arrayed across the cabin, backs to the end wall.

    The riders face each other along the line, which is the only arrangement that fits:
    the door has to have the long wall and the benches have to have the short ones.
    """
    if count <= 0:
        return
    pitch = (2.0 * half_x - 0.14) / count
    x0 = -half_x + 0.07 + pitch * 0.5
    faces = []
    faces += m.box((x0, y0 + 0.44, z_sign * (half_z - 0.32)),
                   (pitch - 0.04, 0.10, 0.42), mat=BODY, parent=parent, bevel=False)
    faces += m.box((x0, y0 + 0.72, z_sign * (half_z - 0.12)),
                   (pitch - 0.04, 0.46, 0.08), mat=BODY, parent=parent, bevel=False)
    faces += m.box((x0, y0 + 0.22, z_sign * (half_z - 0.30)),
                   (0.06, 0.34, 0.06), mat=METAL, parent=parent, bevel=False)
    m.array(faces, count, (pitch, 0.0, 0.0), parent=parent)
    for i in range(count):
        seats.append((x0 + pitch * i, y0 + 0.50, z_sign * (half_z - 0.30)))


def _roof_rack(m, s, parent, half_x, half_z, height, mat=METAL):
    """Ski rack rails and the roof ribs between them."""
    for sx in (-1.0, 1.0):
        m.box((sx * half_x * 0.62, height + 0.07, 0.0), (0.07, 0.10, half_z * 1.5),
              mat=mat, parent=parent)
    rib = m.box((0.0, height + 0.05, -half_z * 0.70), (half_x * 1.4, 0.05, 0.07),
                mat=mat, parent=parent, bevel=False)
    m.array(rib, 4, (0.0, 0.0, half_z * 0.46), parent=parent)


def _build_cabin(record, capacity, model_name):
    s = _cabin_spec(record, capacity)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "cabin"))
    hx, hz, h = s["half_x"], s["half_z"], s["height"]
    tri = s["rope"] == "Tri"
    funitel = s["rope"] == "Funitel"

    hang_top = s["grip_y"] - (1.10 if tri else 0.26)
    m.node("cabin_hanger", pivot=(0.0, hang_top, 0.0))
    _shell(m, s, "cabin_hanger", hx, hz, h, s["sill"], s["head"], s["corner"],
           s["per_corner"])
    _doors(m, s, "cabin_hanger", hx, hz, s["sill"], s["head"], s["leaves"],
           _clamp(0.42 + 0.022 * s["cap"], 0.62, hz * 1.5 / max(1, s["leaves"])))
    seats = []
    _bench(m, s, "cabin_hanger", hx, hz, -1.0, s["bench"], seats)
    _bench(m, s, "cabin_hanger", hx, hz, 1.0, s["cap"] - s["bench"], seats)
    _roof_rack(m, s, "cabin_hanger", hx, hz, h)
    m.box((0.0, h + 0.14, 0.0), (hx * 0.9, 0.12, hz * 0.6), mat=METAL,
          parent="cabin_hanger")

    if funitel:
        # A funitel hangs off two haul ropes, so the hanger is a yoke wide enough to
        # reach both of them and the cabin rides between them instead of under one.
        gap = hx * 2.6
        m.box((0.0, hang_top, 0.0), (gap + 0.30, 0.16, 0.26), mat=METAL,
              parent="cabin_hanger")
        for sx in (-1.0, 1.0):
            _hanger_blade(m, s, "cabin_hanger", hang_top + 0.02, h + 0.12,
                          0.0, 0.0, 0.15)
            m.beam((sx * gap * 0.5, hang_top + 0.06, 0.0), (0.0, h + 0.30, 0.0), 0.13,
                   mat=BODY, parent="cabin_hanger")
        for i, sx in enumerate((-1.0, 1.0)):
            _grip(m, s, "grip_arm" if i == 0 else "grip_arm_2", sx * gap * 0.5,
                  s["grip_y"])
    elif tri:
        _tricable_bogie(m, s, hang_top)
    else:
        _hanger_blade(m, s, "cabin_hanger", hang_top + 0.04, h + 0.10, 0.0, -0.04, 0.17)
        _grip(m, s, "grip_arm", 0.0, s["grip_y"])

    limit = min(len(seats), SOCKET_LIMIT)
    for i in range(limit):
        m.socket("seat_%02d" % (i + 1), seats[i])
    colliders = [("cabin_col", (0.0, h * 0.5, 0.0), (hx * 2.1, h, hz * 2.1))]
    return m, colliders, s


def _tricable_bogie(m, s, hang_top):
    """Two wheel carriages on the track ropes with the haul-rope grip between them.

    This is the flagship carrier and the bogie is what says so: a tricable does not
    hang off its haul rope at all, it rides two track ropes on rolling carriages and
    only holds the haul rope to be pulled along.
    """
    gauge = s["half_x"] * 2.0 + 0.9
    wheel_r = 0.20
    axle_y = s["grip_y"] + 0.34
    frame_y = axle_y - wheel_r - 0.16
    for sx in (-1.0, 1.0):
        x = sx * gauge * 0.5
        wheel = m.cylinder((x, axle_y, -0.72), wheel_r, 0.13, axis=0,
                           segments=_seg(wheel_r), mat=METAL)
        m.array(wheel, 4, (0.0, 0.0, 0.48))
        m.box((x, frame_y, 0.0), (0.20, 0.22, 1.90), mat=BODY)
        for sz in (-1.0, 1.0):
            m.beam((x, frame_y - 0.04, sz * 0.80), (x, frame_y - 0.30, sz * 0.30), 0.11,
                   mat=METAL)
        m.box((x, frame_y - 0.34, 0.0), (0.18, 0.14, 0.90), mat=METAL)
        m.beam((x, frame_y - 0.38, 0.0), (0.0, hang_top + 0.10, 0.0), 0.16, mat=BODY)
    m.box((0.0, hang_top + 0.04, 0.0), (gauge * 0.55, 0.16, 0.34), mat=METAL)
    _grip(m, s, "grip_arm", 0.0, s["grip_y"])


# --------------------------------------------------------------------------- tram car
def _build_tram(record, model_name):
    """A reversible tram car: a big box on a roof carriage that rides the track ropes."""
    s = _common(record)
    cap = s["capacity"]
    # A tram is standing room: about a quarter of a square metre a head, and a car body
    # that grows past four metres wide will not pass a tower.
    area = 0.25 * cap
    half_x = _clamp(1.20 + 0.006 * cap, 1.30, 2.00)
    half_z = _clamp(area / (4.0 * half_x), 2.4, 5.6)
    height = 2.55 + 0.0035 * cap
    sill, head = 1.02, height - 0.42
    leaves = int(_clamp(max(round(s["load_s"] / 45.0), round(cap / 40.0)), 2, 3))
    s.update({"cap": cap, "half_x": half_x, "half_z": half_z, "height": height})

    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "tram"))
    carriage_y = height + 2.35
    m.node("cabin_hanger", pivot=(0.0, height + 0.30, 0.0))
    _shell(m, s, "cabin_hanger", half_x, half_z, height, sill, head, 0.34, 4)
    _doors(m, s, "cabin_hanger", half_x, half_z, sill, head, leaves,
           _clamp(half_z * 0.9 / leaves, 0.7, 1.25))

    # Perimeter benches and a stanchion grid: a tram is a standing car with seats round
    # the wall, not a row of pairs.
    seats = []
    _bench(m, s, "cabin_hanger", half_x, half_z, -1.0, int(_clamp(cap / 14.0, 2, 6)),
           seats)
    _bench(m, s, "cabin_hanger", half_x, half_z, 1.0, int(_clamp(cap / 14.0, 2, 6)),
           seats)
    poles = int(_clamp(half_z, 2, 5))
    for sx in (-1.0, 1.0):
        pole = m.cylinder((sx * half_x * 0.45, height * 0.5, -half_z * 0.55), 0.035,
                          height - 0.20, axis=1, segments=6, mat=METAL,
                          parent="cabin_hanger")
        m.array(pole, poles, (0.0, 0.0, half_z * 1.1 / max(1, poles - 1)),
                parent="cabin_hanger")
    m.box((0.0, height + 0.10, 0.0), (half_x * 1.4, 0.14, half_z * 1.4), mat=METAL,
          parent="cabin_hanger")

    # The yoke, and above it the carriage: two rope trains of wheels on a rocker with
    # the haul and counter ropes socketed into the frame between them.
    for sx in (-1.0, 1.0):
        m.beam((sx * half_x * 0.72, height + 0.18, 0.0),
               (0.0, carriage_y - 0.55, 0.0), 0.20, mat=BODY, parent="cabin_hanger")
    m.cylinder((0.0, height + 0.34, 0.0), 0.14, half_x * 1.1, axis=0, segments=10,
               mat=METAL, parent="cabin_hanger")

    gauge = half_x * 1.15
    wheel_r = 0.26
    for sx in (-1.0, 1.0):
        x = sx * gauge
        wheel = m.cylinder((x, carriage_y, -1.35), wheel_r, 0.16, axis=0,
                           segments=_seg(wheel_r), mat=METAL)
        m.array(wheel, 4, (0.0, 0.0, 0.90))
        m.box((x, carriage_y - wheel_r - 0.20, 0.0), (0.26, 0.26, 3.30), mat=BODY)
        m.box((x, carriage_y - wheel_r - 0.46, 0.0), (0.22, 0.24, 2.10), mat=METAL)
    m.box((0.0, carriage_y - wheel_r - 0.52, 0.0), (gauge * 2.0, 0.22, 0.50),
          mat=BODY)
    m.box((0.0, carriage_y - wheel_r - 0.85, 0.0), (0.50, 0.44, 0.90), mat=METAL)
    _grip(m, s, "grip_arm", 0.0, carriage_y - wheel_r - 1.25)

    _car_sockets(m, cap, half_x, half_z, 0.0, 0.0)
    colliders = [("car_col", (0.0, height * 0.5, 0.0),
                  (half_x * 2.1, height, half_z * 2.1))]
    return m, colliders, s


def _car_sockets(m, cap, half_x, half_z, y, z0):
    """A standing grid of rider positions, capped so a tram is not 150 empties."""
    count = min(int(cap), SOCKET_LIMIT)
    cols = max(1, int(round(math.sqrt(count * half_x / max(0.4, half_z)))))
    rows = max(1, int(math.ceil(count / float(cols))))
    made = 0
    for r in range(rows):
        for c in range(cols):
            if made >= count:
                return
            x = -half_x * 0.72 + (2.0 * half_x * 0.72) * ((c + 0.5) / cols)
            z = z0 - half_z * 0.72 + (2.0 * half_z * 0.72) * ((r + 0.5) / rows)
            made += 1
            m.socket("seat_%02d" % made, (x, y + 0.05, z))


# --------------------------------------------------------------------------- rail cars
def _build_rail(record, model_name):
    """A funicular car, a rack railcar or an inclined-elevator cabin.

    The rack line is the odd one out and the record says so: its rope configuration is
    Rack, so it is self-propelled, its body sits square on the track and only the floor
    inside it steps. The rope-hauled cars are staircases - each compartment level in the
    world, pitched nose-down in track space, stepping up the hill.
    """
    s = _common(record)
    cap = s["capacity"]
    rack = s["rope"] == "Rack"
    # A car is built for a working grade rather than the ceiling its technology allows,
    # so the design angle is a fraction of the record's limit.
    grade = _clamp(s["grade"] * 0.62, 12.0, 32.0)
    half_x = _clamp(1.00 + 0.003 * cap, 1.05, 1.60)
    bays = int(_clamp(round(cap / 24.0), 2, 6))
    bay_len = _clamp(2.0 + 0.004 * cap, 2.0, 3.0)
    height = 2.25 + 0.0025 * cap
    sill, head = 0.95, height - 0.40
    s.update({"cap": cap, "half_x": half_x, "bays": bays, "grade": grade})

    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "rail"))
    pitch = 0.0 if rack else -grade
    step = bay_len / math.cos(math.radians(grade))
    length = step * bays
    deck = 0.62
    m.node("cabin_hanger", pivot=(0.0, deck, 0.0))

    seats = []
    per_bay = max(2, int(math.ceil(cap / float(bays * 2))))
    leaves = int(_clamp(max(round(s["load_s"] / 45.0), 1), 1, 2))
    for b in range(bays):
        z = -length * 0.5 + step * (b + 0.5)
        origin = (0.0, deck, z)
        _rail_bay(m, s, origin, pitch, half_x, bay_len * 0.5, height, sill, head,
                  leaves, seats, per_bay, rack, b)

    _rail_underframe(m, s, half_x, length, deck, rack)
    limit = min(len(seats), SOCKET_LIMIT)
    for i in range(limit):
        m.socket("seat_%02d" % (i + 1), seats[i])
    colliders = [("car_col", (0.0, deck + height * 0.5, 0.0),
                  (half_x * 2.1, height + 0.6, length))]
    return m, colliders, s


def _rail_bay(m, s, origin, pitch, half_x, half_z, height, sill, head, leaves, seats,
              per_bay, rack, index):
    """One compartment of a rail car, assembled in its own level frame."""
    corner, per_corner = 0.16, 3
    args = (corner, per_corner)

    def ring(y, scale=1.0):
        return [_on_tilt(origin, pitch, (p[0], p[1], p[2]))
                for p in _scaled_ring(half_x, half_z, y, *args, scale)]

    m.loft([ring(0.0, 0.90), ring(sill * 0.34), ring(sill)], mat=BODY,
           parent="cabin_hanger")
    m.loft([ring(sill - LAP), ring(head, 0.99)], mat=GLASS, parent="cabin_hanger",
           closed_ends=False)
    m.loft([ring(head - LAP, 0.99), ring(height - 0.10, 0.97), ring(height, 0.88)],
           mat=BODY, parent="cabin_hanger")

    rot = _tilt(pitch)
    aperture = _clamp(half_z * 1.1 / leaves, 0.62, 1.10)
    for side, node in ((-1.0, "door_L"), (1.0, "door_R")):
        name = "%s_%02d" % (node, index + 1) if index else node
        m.node(name, pivot=_on_tilt(origin, pitch, (side * half_x, sill * 0.5, 0.0)),
               parent="cabin_hanger")
        for i in range(leaves):
            z = -aperture * leaves * 0.5 + aperture * (i + 0.5)
            m.box(_on_tilt(origin, pitch, (side * (half_x + 0.04),
                                           (head + 0.10) * 0.5 + 0.05, z)),
                  (0.055, head + 0.05, aperture - 0.06), mat=BODY, parent=name, rot=rot)
            m.box(_on_tilt(origin, pitch, (side * (half_x + 0.06),
                                           (sill + head) * 0.5 + 0.12, z)),
                  (0.03, head - sill - 0.06, aperture - 0.20), mat=GLASS, parent=name,
                  rot=rot)

    # Seats: a rack car steps its rows up the aisle, a staircase car has a level bay and
    # seats it like a room.
    rise = 0.0 if not rack else _clamp(math.tan(math.radians(s["grade"])) * half_z, 0.0, 0.9)
    pitch_x = (2.0 * half_x - 0.20) / max(1, per_bay)
    for sz in (-1.0, 1.0):
        y = 0.10 + (rise if sz > 0 else 0.0)
        faces = m.box(_on_tilt(origin, pitch,
                               (-half_x + 0.10 + pitch_x * 0.5, y + 0.44,
                                sz * (half_z - 0.34))),
                      (pitch_x - 0.05, 0.10, 0.44), mat=BODY, parent="cabin_hanger",
                      rot=rot, bevel=False)
        faces += m.box(_on_tilt(origin, pitch,
                                (-half_x + 0.10 + pitch_x * 0.5, y + 0.72,
                                 sz * (half_z - 0.12))),
                       (pitch_x - 0.05, 0.48, 0.08), mat=BODY, parent="cabin_hanger",
                       rot=rot, bevel=False)
        m.array(faces, per_bay, (pitch_x, 0.0, 0.0), parent="cabin_hanger")
        for i in range(per_bay):
            seats.append(_on_tilt(origin, pitch,
                                  (-half_x + 0.10 + pitch_x * (i + 0.5), y + 0.52,
                                   sz * (half_z - 0.32))))


def _rail_underframe(m, s, half_x, length, deck, rack):
    """Chassis, bogies and what pulls the car: a rope shackle or a rack pinion."""
    gauge = half_x * 1.15
    wheel_r = 0.32
    m.box((0.0, deck - 0.18, 0.0), (half_x * 1.9, 0.26, length * 0.98), mat=METAL)
    for sz in (-1.0, 1.0):
        for sx in (-1.0, 1.0):
            x, z = sx * gauge * 0.5, sz * length * 0.32
            wheel = m.cylinder((x, wheel_r, z - 0.55), wheel_r, 0.12, axis=0,
                               segments=_seg(wheel_r), mat=METAL)
            m.array(wheel, 2, (0.0, 0.0, 1.10))
            m.cylinder((x, wheel_r, z - 0.55), wheel_r * 1.16, 0.04, axis=0,
                       segments=_seg(wheel_r), mat=METAL)
            m.box((x, wheel_r + 0.22, z), (0.16, 0.26, 1.50), mat=METAL)
        m.box((0.0, wheel_r + 0.30, sz * length * 0.32), (gauge, 0.20, 0.60),
              mat=METAL)
    if rack:
        # An electric rack car carries its own drive: a pinion down on the rack rail and
        # a pantograph up on the roof.
        m.cylinder((0.0, 0.30, 0.0), 0.30, 0.12, axis=0, segments=12, mat=METAL)
        tooth = m.box((0.0, 0.60, 0.0), (0.10, 0.10, 0.06), mat=METAL, bevel=False)
        m.array(tooth, 10, (0.0, 0.0, 0.0), parent=None)
        m.box((0.0, 0.20, 0.0), (0.46, 0.16, 0.70), mat=METAL)
    else:
        m.box((0.0, deck - 0.30, -length * 0.5 + 0.30), (0.34, 0.30, 0.50), mat=METAL)
        m.cylinder((0.0, deck - 0.30, -length * 0.5 + 0.04), 0.10, 0.36, axis=2,
                   segments=10, mat=METAL)


# --------------------------------------------------------------------------- surface
def _surface_spec(record):
    s = _common(record)
    # A surface hanger is long enough that the carrier reaches the snow from a rope the
    # towers have to hold well above a skier's head, and its spring box swallows the
    # jerk a faster line gives the rider.
    s["hanger_len"] = _clamp(2.6 + 0.25 * s["speed"], 2.8, 3.9)
    return s


def _surface_hanger(m, s, top_y, bottom_y, spring=True):
    """Grip, spring box and the telescoping tube down to whatever the rider holds."""
    _grip(m, s, "grip_arm", 0.0, top_y)
    m.node("cabin_hanger", pivot=(0.0, top_y - 0.20, 0.0))
    if spring:
        m.box((0.0, top_y - 0.42, 0.0), (0.26, 0.44, 0.30), mat=BODY,
              parent="cabin_hanger")
        m.cylinder((0.0, top_y - 0.70, 0.0), 0.075, 0.22, axis=1, segments=8,
                   mat=METAL, parent="cabin_hanger")
        for sx in (-1.0, 1.0):
            m.box((sx * 0.10, top_y - 0.42, 0.17), (0.05, 0.36, 0.05), mat=METAL,
                  parent="cabin_hanger", bevel=False)
        tube_top = top_y - 0.78
    else:
        tube_top = top_y - 0.22
    m.cylinder((0.0, (tube_top + bottom_y) * 0.5, 0.0), 0.032,
               tube_top - bottom_y, axis=1, segments=8, mat=METAL,
               parent="cabin_hanger")
    m.cylinder((0.0, tube_top - 0.30, 0.0), 0.048, 0.56, axis=1, segments=8,
               mat=METAL, parent="cabin_hanger")
    return tube_top


def _build_tbar(record, model_name):
    """Two riders leaning on a crossbar at the end of a spring-loaded hanger."""
    s = _surface_spec(record)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "tbar"))
    bar_y = 0.10
    top_y = bar_y + s["hanger_len"]
    _surface_hanger(m, s, top_y, bar_y + 0.06)
    width = 0.42 * s["capacity"] + 0.12
    m.cylinder((0.0, bar_y, 0.0), 0.035, width, axis=0, segments=8, mat=METAL,
               parent="cabin_hanger")
    for sx in (-1.0, 1.0):
        m.box((sx * width * 0.27, bar_y, 0.0), (width * 0.36, 0.09, 0.13), mat=BODY,
              parent="cabin_hanger")
        m.cylinder((sx * width * 0.5, bar_y, 0.0), 0.045, 0.05, axis=0, segments=8,
                   mat=METAL, parent="cabin_hanger")
    m.box((0.0, bar_y + 0.10, 0.0), (0.13, 0.16, 0.10), mat=METAL, parent="cabin_hanger")
    for i in range(s["capacity"]):
        x = width * (-0.27 if i == 0 else 0.27) if s["capacity"] == 2 else 0.0
        m.socket("seat_%02d" % (i + 1), (x, bar_y, 0.42))
    colliders = [("carrier_col", (0.0, bar_y + 0.05, 0.0), (width, 0.30, 0.40))]
    return m, colliders, s


def _build_platter(record, model_name):
    """One rider astride a pole with a disc under the seat."""
    s = _surface_spec(record)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "platter"))
    disc_r = 0.17
    disc_y = 0.05
    top_y = disc_y + s["hanger_len"]
    _surface_hanger(m, s, top_y, disc_y + 0.04)
    m.lathe([(0.0, disc_y + 0.055), (disc_r * 0.55, disc_y + 0.045),
             (disc_r, disc_y + 0.020), (disc_r, disc_y - 0.020),
             (disc_r * 0.55, disc_y - 0.035), (0.0, disc_y - 0.030)],
            (0.0, 0.0, 0.0), segments=_seg(disc_r) + 4, mat=BODY,
            parent="cabin_hanger")
    m.cylinder((0.0, disc_y + 0.14, 0.0), 0.055, 0.20, axis=1, segments=8, mat=METAL,
               parent="cabin_hanger")
    m.socket("seat_01", (0.0, disc_y + 0.02, 0.30))
    colliders = [("carrier_col", (0.0, disc_y, 0.0), (disc_r * 2.2, 0.20, disc_r * 2.2))]
    return m, colliders, s


def _build_handle(record, model_name):
    """A handle tow's carrier: a grip clamp, a short strap and a crossbar to hold."""
    s = _surface_spec(record)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "handle"))
    bar_y = 0.06
    top_y = bar_y + _clamp(0.55 + 0.10 * s["speed"], 0.6, 1.0)
    _grip(m, s, "grip_arm", 0.0, top_y)
    m.node("cabin_hanger", pivot=(0.0, top_y - 0.18, 0.0))
    m.box((0.0, top_y - 0.30, 0.0), (0.09, 0.22, 0.16), mat=METAL,
          parent="cabin_hanger")
    m.cylinder((0.0, (top_y - 0.36 + bar_y) * 0.5, 0.0), 0.022, top_y - 0.36 - bar_y,
               axis=1, segments=8, mat=METAL, parent="cabin_hanger")
    for sy in (0.0, 0.06, 0.12):
        m.cylinder((0.0, bar_y + 0.22 + sy, 0.0), 0.030, 0.03, axis=1, segments=8,
                   mat=METAL, parent="cabin_hanger")
    m.cylinder((0.0, bar_y, 0.0), 0.032, 0.34, axis=0, segments=10, mat=BODY,
               parent="cabin_hanger")
    for sx in (-1.0, 1.0):
        m.cylinder((sx * 0.17, bar_y, 0.0), 0.042, 0.04, axis=0, segments=8, mat=METAL,
                   parent="cabin_hanger")
        m.box((sx * 0.09, bar_y + 0.10, 0.0), (0.05, 0.14, 0.05), mat=METAL,
              parent="cabin_hanger", bevel=False)
    m.socket("seat_01", (0.0, bar_y, 0.55))
    colliders = [("carrier_col", (0.0, bar_y + 0.10, 0.0), (0.40, 0.40, 0.20))]
    return m, colliders, s


def _build_belt(record, model_name):
    """A conveyor has no carrier, so what it gets is a tile of its own belt.

    The view scrolls the `carpet_belt` node along Z and repeats the tile up the line, so
    the tile is one carrier spacing long and carries its cleats, its bed and its side
    skirts. A covered conveyor puts its gallery on top of the same tile, which is the
    only thing separating the two records: WeatherExposure says one is sheltered.
    """
    s = _common(record)
    m = mk.Model(model_name, budget_key="lift_carrier",
                 seed=datasrc.seed_for(s["id"], "carrier", "belt"))
    length = _clamp(s["spacing"], 1.2, 4.0)
    width = _clamp(0.45 + s["pph"] / 6000.0, 0.55, 0.95)
    belt_y = 0.26

    m.node("carpet_belt", pivot=(0.0, belt_y, 0.0))
    m.box((0.0, belt_y, 0.0), (width, 0.03, length), mat=METAL, parent="carpet_belt",
          bevel=False)
    cleats = max(4, int(length / 0.12))
    cleat = m.box((0.0, belt_y + 0.025, -length * 0.5 + 0.06), (width - 0.02, 0.018, 0.05),
                  mat=METAL, parent="carpet_belt", bevel=False)
    m.array(cleat, cleats, (0.0, 0.0, length / cleats), parent="carpet_belt")

    m.box((0.0, belt_y - 0.06, 0.0), (width - 0.05, 0.07, length * 0.98), mat=METAL)
    for sx in (-1.0, 1.0):
        m.box((sx * (width * 0.5 + 0.05), belt_y + 0.02, 0.0), (0.09, 0.22, length),
              mat=BODY)
        m.box((sx * (width * 0.5 + 0.05), belt_y + 0.15, 0.0), (0.11, 0.04, length * 0.9),
              mat=METAL, bevel=False)
        m.box((sx * (width * 0.5 + 0.02), 0.06, 0.0), (0.07, 0.14, length * 0.5),
              mat=METAL)
    roller = m.cylinder((0.0, belt_y - 0.10, -length * 0.5 + 0.20), 0.075, width * 0.92,
                        axis=0, segments=8, mat=METAL)
    m.array(roller, max(2, int(length / 0.6)), (0.0, 0.0, 0.6))

    if s["exposure"] < 0.5:
        # A gallery is what the covered record pays for: hoops and a polycarbonate skin
        # over the same belt.
        arch_r = width * 0.72 + 0.45
        loop = _arc_shell(0.0, belt_y + 0.55, arch_r, 0.05, 172.0, 8.0, 7)
        for k in range(3):
            z = -length * 0.5 + length * (k + 0.5) / 3.0
            m.prism(loop, (0.0, 0.0, z), 0.07, mat=METAL, axis=2)
        skin = _arc_shell(0.0, belt_y + 0.55, arch_r - 0.045, 0.02, 172.0, 8.0, 7)
        m.loft([[(u, v, -length * 0.5) for u, v in skin],
                [(u, v, length * 0.5) for u, v in skin]], mat=GLASS, closed_ends=False)

    m.socket("seat_01", (0.0, belt_y + 0.04, 0.0))
    colliders = [("carrier_col", (0.0, belt_y * 0.5, 0.0), (width + 0.2, belt_y, length))]
    return m, colliders, s


# --------------------------------------------------------------------------- driver
def _emit(record, model, colliders, spec, path, variant, bubble=None):
    extra = {
        "family": "lift_carrier",
        "kind": "lift",
        "component": "carrier",
        "displayName": "%s carrier" % (record.get("DisplayName") or record["Id"]),
        "sourceId": record["Id"],
        "liftFamily": record.get("Family"),
        "carrierKind": variant,
        "capacity": spec.get("cap", spec.get("capacity")),
        "detachable": bool(spec.get("detachable")),
        # Heated seats change the material a cushion asks for, not its shape, so the
        # fact is published here for the runtime rather than modelled.
        "heatedSeats": bool(spec.get("heated")),
    }
    if bubble is not None:
        extra["bubbleFitted"] = bool(bubble)
    return export.emit(model, path, box_colliders=colliders, extra=extra)


def build(record, out_path):
    """Every carrier one lift flies. Returns one manifest record per file written.

    Most lifts write a single `carrier.fbx`. A chondola is two lifts on one rope and
    writes both. A chair that can be sold bubbles also writes the bubbled variant, so
    the model matches the lift after the player buys the upgrade.
    """
    folder = os.path.dirname(out_path)
    kind = _carrier_kind(record)
    lift_id = record["Id"]
    out = []

    def chair(stem, bubble):
        model, colliders, spec = _build_chair(record, bubble, "%s_%s" % (lift_id, stem))
        return _emit(record, model, colliders, spec,
                     os.path.join(folder, stem + ".fbx"), "chair", bubble)

    def cabin(stem, capacity):
        model, colliders, spec = _build_cabin(record, capacity,
                                              "%s_%s" % (lift_id, stem))
        return _emit(record, model, colliders, spec,
                     os.path.join(folder, stem + ".fbx"), "cabin")

    if kind in ("chair", "hybrid"):
        out.append(chair("carrier", _bubble_fitted(record)))
        if kind == "hybrid":
            out.append(cabin("carrier_cabin", record.get("SeatsOrCabinCapacity")))
        elif "bubble" in _options(record):
            out.append(chair("carrier_bubble", True))
        return out

    if kind == "cabin":
        return [cabin("carrier", record.get("SeatsOrCabinCapacity"))]

    builders = {
        "tram": _build_tram,
        "rail": _build_rail,
        "tbar": _build_tbar,
        "platter": _build_platter,
        "handle": _build_handle,
        "belt": _build_belt,
    }
    model, colliders, spec = builders[kind](record, "%s_carrier" % lift_id)
    return [_emit(record, model, colliders, spec,
                  os.path.join(folder, "carrier.fbx"), kind)]
