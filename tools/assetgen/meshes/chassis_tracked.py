"""Tracked chassis: one generator for every machine in the fleet that runs on belts.

Groomers, the crawler dozer, the excavators, the lattice crane, the tracked UTV, the
sleds and the walk-behind blower all come out of the code below. What separates a 15 t
winch cat from a patrol sled is the record and nothing else: MassKg decides how many
road wheels carry the belt, TrackWidthM and Visual.BodyW set the gauge, EnginePowerKw
sizes the engine deck, Tier decides whether the cab is an upright box or a wraparound
shell, LightingLumens decides how many work lights sit on the roof bar, and
AttachmentSlots says where an implement bolts on. No machine is named anywhere here.

The running gear is the part worth reading: a belt is a prism whose profile is the real
tangent outline of the idler and the sprocket, so it rests flat on the ground and sweeps
up over the nose the way a rubber track does, and one grouser bar is arrayed along each
run rather than sixty bars being modelled.
"""
import math

from config import BEVEL_WIDTH_M
from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

SILHOUETTES = frozenset({"groomer", "snowmobile", "excavator", "crane", "utv",
                         "walkbehind"})

# Counterweights and ballast are cast steel; cargo in a UTV bed is sand, bolts and rope.
_STEEL_KG_M3 = 7850.0
_CARGO_KG_M3 = 420.0


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


def _chamferable(size):
    """Whether a part is big enough for the standard chamfer to read as an edge.

    Below about three chamfer widths the bevel eats the face it is supposed to catch a
    highlight on, and costs four times the triangles to do it.
    """
    return size > BEVEL_WIDTH_M * 3.0


def _segments(radius):
    """Segment count for a revolved part, from how big it reads on screen.

    A 1.2 m bullwheel-sized sprocket earns twice the sides of a 0.15 m roller, and the
    small stuff is where a procedural pipeline wastes its budget if nobody says no.
    """
    return int(_clamp(10.0 + radius * 18.0, 12.0, 22.0))


def _slots(record):
    """AttachmentSlots in file order, as (position, default attachment id) pairs."""
    out = []
    for slot in record.get("AttachmentSlots") or []:
        if not isinstance(slot, dict):
            continue
        out.append((str(slot.get("Position") or "Front"),
                    str(slot.get("DefaultAttachmentId") or "")))
    return out


def _has_winch(record):
    """True when this machine carries a roof winch.

    Read from the data rather than from the id: the slot default names an attachment
    record, and that record's Kind is what says the thing is a winch.
    """
    kinds = {}
    try:
        for att in datasrc.attachments():
            kinds[att.get("Id")] = att.get("Kind")
    except (FileNotFoundError, KeyError, TypeError):
        kinds = {}
    for position, default in _slots(record):
        if position == "Roof" and kinds.get(default) == "Winch":
            return True
    return False


# --------------------------------------------------------------------------- belts
def _belt_loop(length, height, grouser_h, drive_at_rear):
    """Outline of one flattened belt loop as a (y, z) profile, plus where its parts sit.

    The loop is the convex outline of two circles resting on the snow: the idler and the
    sprocket. Both circles are tangent to y = 0, so the bottom run is flat however the
    two radii differ, and the top run follows the real external tangent - which is what
    gives a groomer belt its upswept nose and an excavator belt its raised drive end.
    """
    span = max(height - grouser_h, grouser_h)
    big, small = span * 0.5, span * 0.5 * 0.80
    r_front, r_rear = (small, big) if drive_at_rear else (big, small)
    front = (length * 0.5 - r_front, grouser_h + r_front, r_front)
    rear = (-length * 0.5 + r_rear, grouser_h + r_rear, r_rear)

    dz, dy = front[0] - rear[0], front[1] - rear[1]
    dist = math.hypot(dz, dy)
    beta = math.atan2(dy, dz)
    arc = math.acos(_clamp((r_rear - r_front) / dist, -1.0, 1.0))
    a, b = beta + arc, beta - arc
    top, bottom = (a, b) if math.sin(a) > math.sin(b) else (b, a)
    while top <= 0.0:
        top += 2.0 * math.pi
    while bottom >= 0.0:
        bottom -= 2.0 * math.pi

    steps = 6
    profile = []
    for circle, t0, t1 in ((front, bottom, top), (rear, top, bottom + 2.0 * math.pi)):
        cz, cy, r = circle
        for i in range(steps + 1):
            t = _lerp(t0, t1, i / steps)
            profile.append((cy + math.sin(t) * r, cz + math.cos(t) * r))

    def _point(circle, t):
        return (circle[0] + math.cos(t) * circle[2], circle[1] + math.sin(t) * circle[2])

    return {
        "profile": profile,
        "front": front,
        "rear": rear,
        "bottom": (_point(rear, bottom), _point(front, bottom)),
        "top": (_point(front, top), _point(rear, top)),
        "normal_top": (math.cos(top), math.sin(top)),
        "normal_bottom": (math.cos(bottom), math.sin(bottom)),
    }


def _scaled_profile(profile, centre, factor):
    cy, cz = centre
    return [(cy + (y - cy) * factor, cz + (z - cz) * factor) for y, z in profile]


def _grouser_run(m, node, cx, width, start, end, normal, grouser_h, thickness, spacing):
    """One grouser bar arrayed along a straight run of the belt.

    Sixty modelled pads cost sixty times what one costs and read identically at 200 m,
    so the bar is built once, sunk a tenth of its height into the belt so the two never
    share a vertex, and repeated along the run.
    """
    (z0, y0), (z1, y1) = start, end
    run = math.hypot(z1 - z0, y1 - y0)
    if run < spacing * 2.0:
        return
    count = int(_clamp(run / spacing, 3.0, 34.0))
    step = run / count
    uz, uy = (z1 - z0) / run, (y1 - y0) / run
    nz, ny = normal
    pitch = math.degrees(math.atan2(-nz, ny))
    cz = z0 + uz * step * 0.5 + nz * grouser_h * 0.45
    cy = y0 + uy * step * 0.5 + ny * grouser_h * 0.45
    bar = m.box((cx, cy, cz), (width * 0.97, grouser_h * 1.1, thickness), mat=METAL,
                parent=node, rot=mk.unity_euler(pitch, 0.0, 0.0), bevel=False)
    m.array(bar, count, (0.0, uy * step, uz * step), parent=node)


def _road_wheel(m, cx, cy, cz, radius, width, mat=METAL):
    """A road wheel as a lathe, so it has a rim and a hub face rather than a flat disc."""
    hw = width * 0.5
    profile = [(radius * 0.30, -hw), (radius * 0.92, -hw * 0.82), (radius, -hw * 0.55),
               (radius, hw * 0.55), (radius * 0.92, hw * 0.82), (radius * 0.30, hw)]
    m.lathe(profile, (cx, cy, cz), segments=_segments(radius), mat=mat, axis=0)


def _sprocket(m, cx, cy, cz, radius, width, teeth):
    """Drive sprocket: a lathe body with drive lugs set round it."""
    _road_wheel(m, cx, cy, cz, radius * 0.86, width * 0.9)
    if teeth < 3:
        return
    for i in range(teeth):
        t = 2.0 * math.pi * i / teeth
        dz, dy = math.cos(t), math.sin(t)
        m.box((cx, cy + dy * radius * 0.86, cz + dz * radius * 0.86),
              (width * 0.34, radius * 0.34, radius * 0.30), mat=METAL,
              rot=mk.unity_euler(math.degrees(math.atan2(-dz, dy)), 0.0, 0.0),
              bevel=_chamferable(radius * 0.30))


def _track(m, node, cx, cz, length, width, height, mass, drive_at_rear=False):
    """One complete belt: loop and grousers into `node`, running gear into the body.

    Only the belt scrolls, so only the belt lives under the articulation transform; the
    frame, the bogies and the sprocket are chassis and stay in `body`.
    """
    grouser_h = _clamp(height * 0.085, 0.035, 0.12)
    loop = _belt_loop(length, height, grouser_h, drive_at_rear)
    m.prism(loop["profile"], (cx, 0.0, cz), width, mat=METAL, parent=node, axis=0)

    spacing = _clamp(height * 0.14, 0.08, 0.24)
    thickness = _clamp(grouser_h * 0.62, 0.03, 0.09)
    for key in ("bottom", "top"):
        (za, ya), (zb, yb) = loop[key]
        _grouser_run(m, node, cx, width, (za + cz, ya), (zb + cz, yb),
                     loop["normal_" + key], grouser_h, thickness, spacing)

    frame = _scaled_profile(loop["profile"], (height * 0.5, 0.0), 0.72)
    m.prism(frame, (cx, 0.0, cz), width * 0.52, mat=METAL, axis=0)

    fz, fy, fr = loop["front"]
    rz, ry, rr = loop["rear"]
    _road_wheel(m, cx, fy, cz + fz, fr * 0.88, width * 0.72)
    _sprocket(m, cx, ry, cz + rz, rr, width * 0.72,
              int(_clamp(6.0 + rr * 14.0, 6.0, 14.0)))
    # A long belt carries a final drive on the inboard face of its sprocket and a
    # tensioner behind the idler; a bogie kit on the corner of a UTV has neither.
    long_belt = fz - rz > 1.4
    if long_belt:
        inboard = -1.0 if cx > 0.0 else 1.0
        m.lathe([(rr * 0.30, 0.0), (rr * 0.62, rr * 0.10), (rr * 0.62, rr * 0.34),
                 (rr * 0.40, rr * 0.42)],
                (cx + inboard * width * 0.5, ry, cz + rz), segments=_segments(rr * 0.6),
                mat=METAL, axis=0)
        m.box((cx + inboard * width * 0.34, fy, cz + fz - fr * 0.5),
              (width * 0.3, fr * 0.5, fr * 0.9), mat=METAL)

    # Bogies: a heavier machine spreads its mass over more wheels, which is both the
    # real reason and the reason the belt reads as carrying something.
    r_road = min(fr, rr) * 0.54
    count = int(round(_clamp(3.0 + mass / 3500.0, 3.0, 8.0)))
    for i in range(count):
        t = (i + 0.5) / count
        z = _lerp(rz + rr * 0.9, fz - fr * 0.9, t)
        _road_wheel(m, cx, grouser_h + r_road, cz + z, r_road, width * 0.62)
        _bar(m, (cx, grouser_h + r_road, cz + z), (cx, height * 0.62, cz + z * 0.55),
                r_road * 0.42)
    # Carrier rollers hold up the top run where it is long enough to sag.
    if long_belt:
        for t in (0.34, 0.68):
            z = _lerp(rz + rr, fz - fr, t)
            _road_wheel(m, cx, height - r_road * 0.7, cz + z, r_road * 0.62, width * 0.5)


# --------------------------------------------------------------------------- cab
def _cab_profile(tier, half_len, height):
    """Side outline of a cab shell, and the windscreen segment inside it.

    An older machine is an upright box with a flat screen; a current one rakes the
    screen back and curves the roof over into the rear pillar. Both are the same list of
    (z, y) points, so one loft builds either.
    """
    if tier <= 2:
        rake = height * 0.13
        corner = height * 0.14
        pts = [(-half_len, 0.0), (half_len, 0.0), (half_len, height * 0.52),
               (half_len - rake, height - corner), (half_len - rake - corner, height),
               (-half_len + corner, height), (-half_len, height - corner)]
        screen = ((half_len, height * 0.52), (half_len - rake, height - corner))
    else:
        rake = height * 0.34
        pts = [(-half_len, 0.0), (half_len, 0.0), (half_len, height * 0.22)]
        sill = (half_len, height * 0.22)
        head = (half_len - rake, height * 0.90)
        for i in (1, 2, 3):
            t = i / 3.0
            bow = math.sin(math.pi * t) * height * 0.035
            pts.append((_lerp(sill[0], head[0], t) + bow, _lerp(sill[1], head[1], t)))
        for i in (1, 2, 3):
            t = i / 3.0
            pts.append((_lerp(head[0], -half_len + height * 0.10, t),
                        height - math.cos(math.pi * 0.5 * (1.0 - t)) * height * 0.10))
        pts.append((-half_len, height * 0.72))
        screen = (sill, head)
    return pts, screen


def _glass_panel(m, centre, size, pitch=0.0, parent=None):
    m.box(centre, size, mat=GLASS, parent=parent,
          rot=mk.unity_euler(pitch, 0.0, 0.0) if abs(pitch) > 1e-6 else None)


def _cab(m, base_y, z_centre, width, length, height, tier, x_centre=0.0,
         rear_window=True):
    """Lofted cab shell with inset glazing. Returns the roof height.

    The shell is four sections across the width - two at the corners pulled in - so the
    roof edge is rounded instead of a razor line, and the glass is a separate inset
    shell a centimetre proud of it, which leaves the shell showing as the window frame.
    """
    m.node("cab", pivot=(x_centre, base_y, z_centre))
    profile, screen = _cab_profile(tier, length * 0.5, height)
    corner = min(width, length, height) * 0.16
    hw = width * 0.5
    ky, kz = 1.0 - corner / height, 1.0 - 2.0 * corner / length

    sections = []
    for x, pull in ((-hw, True), (-hw + corner, False), (hw - corner, False), (hw, True)):
        ring = []
        for z, y in profile:
            sy, sz = (y * ky, z * kz) if pull else (y, z)
            # The floor sinks into whatever the cab stands on: two surfaces that meet
            # exactly in the same plane fight for the pixel.
            ring.append((x_centre + x, base_y + sy - (0.03 if y < 1e-6 else 0.0),
                         z_centre + sz))
        sections.append(ring)
    m.loft(sections, mat=BODY, parent="cab")

    frame = min(length, height) * 0.10
    (z0, y0), (z1, y1) = screen
    span = math.hypot(z1 - z0, y1 - y0)
    pitch = math.degrees(math.atan2(-(z1 - z0), y1 - y0))
    _glass_panel(m, (x_centre, base_y + (y0 + y1) * 0.5 + 0.01,
                     z_centre + (z0 + z1) * 0.5 + 0.02),
                 (width * 0.80, span - frame, 0.05), pitch, parent="cab")
    for side in (-1.0, 1.0):
        m.box((x_centre + side * (hw - 0.015), base_y + height * 0.60, z_centre),
              (0.05, height * 0.40, length - frame * 2.2), mat=GLASS, parent="cab")
    if rear_window:
        m.box((x_centre, base_y + height * 0.62, z_centre - length * 0.5 + 0.03),
              (width * 0.66, height * 0.34, 0.05), mat=GLASS, parent="cab")

    for side in (-1.0, 1.0):
        _bar(m, (x_centre + side * hw * 0.92, base_y, z_centre + length * 0.46),
                (x_centre + side * hw * 0.86, base_y + height * 0.96,
                z_centre + length * 0.46 - (length * 0.5 - screen[1][0])),
                frame * 0.55, parent="cab")
        # Door: an outline on the side glass, and the handle an operator reaches for.
        x = x_centre + side * (hw + 0.012)
        dz0, dz1 = z_centre - length * 0.34, z_centre + length * 0.30
        dy0, dy1 = base_y + height * 0.08, base_y + height * 0.86
        for a, b in (((x, dy0, dz0), (x, dy1, dz0)), ((x, dy0, dz1), (x, dy1, dz1)),
                     ((x, dy0, dz0), (x, dy0, dz1))):
            _bar(m, a, b, frame * 0.30, parent="cab")
        m.box((x, base_y + height * 0.46, dz1 - length * 0.10),
              (frame * 0.5, frame * 0.35, length * 0.16), mat=METAL, parent="cab")
    wiper = z_centre + (screen[0][0] + screen[1][0]) * 0.5
    _bar(m, (x_centre - width * 0.22, base_y + (screen[0][1] + screen[1][1]) * 0.5,
            wiper + 0.05),
            (x_centre + width * 0.20, base_y + screen[0][1] + frame * 0.4, wiper + 0.05),
            frame * 0.22, parent="cab")
    return base_y + height


def _light_bar(m, y, z, width, lumens, parent=None, prefix="light_work"):
    """Work lights on the cab roof. How many is a data question: LightingLumens."""
    count = int(_clamp(2.0 + lumens / 7000.0, 2.0, 8.0))
    span = width * 0.82
    size = _clamp(span / (count * 1.9), 0.10, 0.26)
    m.box((0.0, y + size * 0.5, z), (span + size, size * 0.55, size * 0.7), mat=METAL,
          parent=parent)
    for i in range(count):
        x = -span * 0.5 + span * (i / max(1, count - 1))
        m.box((x, y + size * 0.9, z + 0.02), (size, size, size * 0.55), mat=METAL,
              parent=parent)
        m.box((x, y + size * 0.9, z + size * 0.32), (size * 0.74, size * 0.74, 0.04),
              mat=GLASS, parent=parent)
        m.socket("%s_%02d" % (prefix, i + 1), (x, y + size * 0.9, z + size * 0.34))


def _headlights(m, y, z, half_gauge, size, parent=None):
    """Headlight pods. The socket is a root-level anchor, as every socket is."""
    for side, name in ((-1.0, "light_L"), (1.0, "light_R")):
        x = side * half_gauge
        m.box((x, y, z - size * 0.2), (size * 1.25, size * 1.0, size * 0.5), mat=METAL,
              parent=parent)
        m.box((x, y, z + size * 0.05), (size * 0.95, size * 0.7, 0.04), mat=GLASS,
              parent=parent)
        m.socket(name, (x, y, z + size * 0.1))


def _handrail(m, a, b, thickness, posts=2, drop=0.0, parent=None):
    """A rail with its stanchions: the detail that makes a machine read as climbable."""
    _bar(m, a, b, thickness, parent=parent)
    if drop <= 0.0 or posts < 1:
        return
    for i in range(posts):
        t = (i + 0.5) / posts
        p = (_lerp(a[0], b[0], t), _lerp(a[1], b[1], t), _lerp(a[2], b[2], t))
        _bar(m, p, (p[0], p[1] - drop, p[2]), thickness * 0.85, parent=parent)


def _ram(m, a, b, bore, parent=None):
    """A hydraulic ram: barrel for most of the stroke, rod the rest."""
    knee = tuple(_lerp(a[i], b[i], 0.58) for i in range(3))
    m.beam(a, knee, bore, parent=parent, square=False, segments=10)
    m.beam(tuple(_lerp(a[i], b[i], 0.50) for i in range(3)), b, bore * 0.52,
           parent=parent, square=False, segments=8)


def _exhaust(m, x, y, z, radius, height, electric, parent=None):
    """Smoke anchor. An electric machine gets a cooling cowl and no stack.

    The node is published either way: gameplay binds it by name and an emitter that
    never emits costs nothing, where a missing transform is a startup exception.
    """
    m.node("exhaust", pivot=(x, y + (height if not electric else radius * 1.6), z),
           parent=parent)
    if electric:
        m.cylinder((x, y + radius * 0.8, z), radius * 1.5, radius * 1.6, axis=1,
                   segments=_segments(radius * 1.5), mat=METAL, parent="exhaust")
        m.tube((x, y + radius * 1.64, z), radius * 1.68, radius * 1.1, radius * 0.24,
               axis=1, segments=_segments(radius * 1.5), parent="exhaust")
        return
    m.cylinder((x, y + height * 0.5, z), radius, height, axis=1,
               segments=_segments(radius), mat=METAL, parent="exhaust")
    m.tube((x, y + height * 0.94, z), radius * 1.22, radius * 0.86, height * 0.12,
           axis=1, segments=_segments(radius), parent="exhaust")


def _grille(m, centre, size, slats):
    """Radiator grille: a recessed frame with louvres arrayed down it."""
    m.box(centre, size, mat=METAL)
    gap = size[1] / (slats + 1.0)
    first = m.box((centre[0], centre[1] - size[1] * 0.5 + gap, centre[2] + size[2] * 0.55),
                  (size[0] * 0.88, gap * 0.45, size[2] * 0.5), mat=METAL, bevel=False)
    m.array(first, slats, (0.0, gap, 0.0))


def _mount_sockets(m, record, anchors):
    """One socket per AttachmentSlots entry, where that implement actually bolts on."""
    for position, _default in _slots(record):
        point = anchors.get(position)
        if point is not None:
            m.socket("mount_" + position, point)
    if "Tow" in dict(_slots(record)) and "Tow" in anchors:
        m.socket("hitch", anchors["Tow"])


def _winch(m, x, y, z, mass):
    """Roof winch: a boom that yaws and a drum that spins, per the contract's names."""
    reach = _clamp(mass / 11000.0, 0.9, 1.5)
    head = (x, y + reach * 1.45, z - reach * 0.30)
    m.node("winch_boom", pivot=(x, y, z))
    m.cylinder((x, y + reach * 0.16, z), reach * 0.34, reach * 0.32, axis=1,
               segments=12, mat=METAL, parent="winch_boom")
    m.cylinder((x, y + reach * 0.80, z - reach * 0.16), reach * 0.15,
               reach * 1.32, axis=1, segments=10, mat=METAL, parent="winch_boom",
               rot=mk.unity_euler(13.0, 0.0, 0.0), radius_end=reach * 0.11)
    for side in (-1.0, 1.0):
        _bar(m, (x, y + reach * 0.34, z),
             (x + side * reach * 0.26, y + reach * 1.1, z - reach * 0.24),
             reach * 0.07, parent="winch_boom")
    m.node("winch_drum", pivot=head, parent="winch_boom")
    m.cylinder(head, reach * 0.24, reach * 0.46, axis=0, segments=14, mat=METAL,
               parent="winch_drum")
    for side in (-1.0, 1.0):
        m.cylinder((head[0] + side * reach * 0.25, head[1], head[2]), reach * 0.33,
                   reach * 0.05, axis=0, segments=14, mat=METAL, parent="winch_drum")


# --------------------------------------------------------------------------- groomer
def _build_groomer(m, record, vis):
    """Groomer, carrier and crawler dozer: belts, a deck, a cab forward, engine behind."""
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    track_h = vis["TrackH"]
    mass = _num(record.get("MassKg"), 8000.0)
    power = _num(record.get("EnginePowerKw"), 150.0)
    tier = datasrc.tier_of(record)
    seats = int(_num(record.get("Seats"), 2.0))
    width = _clamp(_num(record.get("TrackWidthM"), 0.0) or body_w * 0.26,
                   0.25, body_w * 0.45)
    gauge = (body_w - width) * 0.5
    belt_l = body_l * 0.94

    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * gauge, track_h * 0.5, 0.0))
        _track(m, node, side * gauge, 0.0, belt_l, width, track_h, mass)

    floor = track_h * 0.96
    deck_top = floor + body_h * 0.22
    engine_top = deck_top + body_h * 0.74
    inner = max(0.6, (gauge - width * 0.5) * 1.9)

    # The tub hangs between the belts; the deck plate bridges over them.
    m.prism([(track_h * 0.24, -body_l * 0.40), (floor, -body_l * 0.40),
             (floor, body_l * 0.40), (track_h * 0.30, body_l * 0.34),
             (track_h * 0.24, body_l * 0.30)],
            (0.0, 0.0, 0.0), inner, mat=BODY, axis=0)
    deck = []
    for z, w, drop in ((-0.50, 0.80, 0.55), (-0.34, 0.96, 0.90), (0.16, 1.00, 1.00),
                       (0.40, 0.94, 0.92), (0.50, 0.74, 0.66)):
        hw = body_w * 0.5 * w
        y0, y1 = floor - body_h * 0.06, floor + (deck_top - floor) * drop
        c = min(hw, y1 - y0) * 0.35
        deck.append([(-hw, y0, body_l * z), (hw, y0, body_l * z),
                     (hw, y1 - c, body_l * z), (hw - c, y1, body_l * z),
                     (-hw + c, y1, body_l * z), (-hw, y1 - c, body_l * z)])
    m.loft(deck, mat=BODY)

    cab_l = max(0.9, vis["CabL"])
    cab_w = max(0.9, vis["CabW"] * (0.88 + 0.06 * _clamp(seats, 1.0, 4.0)))
    cab_h = max(0.9, vis["CabH"])
    cab_z = _clamp(vis["CabOffset"], -body_l * 0.25, body_l * 0.5 - cab_l * 0.55)
    roof = _cab(m, deck_top, cab_z, cab_w, cab_l, cab_h, tier)

    # Engine deck: length and height follow the block that has to fit inside it.
    hood_l = _clamp(1.0 + power / 240.0, 1.1, body_l * 0.46)
    hood_z = cab_z - cab_l * 0.5 - hood_l * 0.5 - 0.06
    hood_w = body_w * 0.56
    hood_h = engine_top - deck_top
    m.box((0.0, deck_top + hood_h * 0.5 - 0.03, hood_z), (hood_w, hood_h, hood_l),
          mat=BODY)
    m.wedge((0.0, deck_top + hood_h * 0.5, hood_z - hood_l * 0.5 - hood_h * 0.22),
            (hood_w * 0.96, hood_h, hood_h * 0.44), mat=BODY,
            rot=mk.unity_euler(0.0, 180.0, 0.0), taper=0.55)
    _grille(m, (0.0, deck_top + hood_h * 0.52, hood_z - hood_l * 0.5 - hood_h * 0.42),
            (hood_w * 0.7, hood_h * 0.62, 0.10), int(_clamp(power / 60.0, 4.0, 9.0)))

    electric = str(record.get("FuelType", "")).lower() == "electric"
    _exhaust(m, hood_w * 0.36, deck_top + hood_h, hood_z + hood_l * 0.28,
             _clamp(0.03 + power / 4200.0, 0.04, 0.13), hood_h * 0.9, electric)

    # Fuel or battery, split either side of the tub, sized from what the record carries.
    store = _num(record.get("FuelCapacityL"), 0.0) / 1000.0
    store += _num(record.get("BatteryKwh"), 0.0) / 190.0
    if store > 0.02:
        room = max(0.2, floor - track_h * 0.30)
        tank_l = _clamp(body_l * 0.30, 0.5, body_l * 0.4)
        tank_h = _clamp(store / (2.0 * tank_l * 0.45), 0.18, room)
        for side in (-1.0, 1.0):
            m.box((side * (inner * 0.5 - 0.24), floor - tank_h * 0.5 - 0.03,
                   -body_l * 0.10),
                  (0.42, tank_h, tank_l), mat=BODY)

    fender_h = _clamp(body_h * 0.12, 0.08, 0.22)
    for side in (-1.0, 1.0):
        m.box((side * gauge, floor - fender_h * 0.5, 0.0),
              (width * 1.12, fender_h, belt_l * 0.86), mat=BODY)
        _handrail(m, (side * body_w * 0.46, deck_top + 0.42, cab_z - cab_l * 0.5),
                  (side * body_w * 0.46, deck_top + 0.42, body_l * 0.44),
                  0.055, posts=2, drop=0.40)
        m.box((side * body_w * 0.42, floor - fender_h - 0.16, cab_z - cab_l * 0.2),
              (0.34, 0.05, 0.44), mat=METAL)
        _bar(m, (side * cab_w * 0.52, deck_top + cab_h * 0.80, cab_z + cab_l * 0.35),
             (side * (cab_w * 0.52 + 0.34), deck_top + cab_h * 0.86,
              cab_z + cab_l * 0.42), 0.045)
        m.box((side * (cab_w * 0.52 + 0.36), deck_top + cab_h * 0.74,
               cab_z + cab_l * 0.44), (0.09, 0.30, 0.16), mat=METAL)
        louvre = m.box((side * hood_w * 0.5, deck_top + hood_h * 0.62,
                        hood_z - hood_l * 0.34),
                       (0.05, hood_h * 0.42, hood_l * 0.08), mat=METAL, bevel=False)
        m.array(louvre, 5, (0.0, 0.0, hood_l * 0.15))

    _light_bar(m, roof, cab_z + cab_l * 0.30, cab_w,
               _num(record.get("LightingLumens"), 6000.0), parent="cab")
    _headlights(m, floor + 0.06, body_l * 0.5 - 0.02, body_w * 0.30,
                _clamp(body_w * 0.09, 0.12, 0.26))

    if _has_winch(record):
        _winch(m, 0.0, roof, cab_z - cab_l * 0.18, mass)

    nose_y = _clamp(track_h * 0.62, 0.3, body_h)
    _mount_sockets(m, record, {
        "Front": (0.0, nose_y, body_l * 0.5 + 0.12),
        "Rear": (0.0, nose_y, -body_l * 0.5 - 0.12),
        "Mid": (0.0, track_h * 0.28, 0.0),
        "Roof": (0.0, roof + 0.04, cab_z - cab_l * 0.18),
        "Tow": (0.0, track_h * 0.42, -body_l * 0.5 - 0.06),
    })
    # The frames an implement actually bolts to: a push frame on the nose, lift arms at
    # the tail, each worked by its own ram.
    for side in (-1.0, 1.0):
        heel = (side * inner * 0.34, track_h * 0.42, body_l * 0.26)
        tip = (side * inner * 0.16, nose_y, body_l * 0.5 + 0.10)
        _bar(m, heel, tip, 0.15)
        _ram(m, (side * inner * 0.30, floor + 0.12, body_l * 0.04), _mid(heel, tip), 0.10)
        tail = (side * inner * 0.34, track_h * 0.55, -body_l * 0.26)
        tail_tip = (side * inner * 0.20, track_h * 0.46, -body_l * 0.5 - 0.12)
        _bar(m, tail, tail_tip, 0.14)
        _ram(m, (side * inner * 0.30, floor + 0.14, -body_l * 0.08),
             _mid(tail, tail_tip), 0.10)
    _bar(m, (-inner * 0.16, nose_y, body_l * 0.5 + 0.08),
            (inner * 0.16, nose_y, body_l * 0.5 + 0.08), 0.12)
    _bar(m, (-inner * 0.20, track_h * 0.46, -body_l * 0.5 - 0.10),
            (inner * 0.20, track_h * 0.46, -body_l * 0.5 - 0.10), 0.11)

    return [("cab_col", (0.0, deck_top + cab_h * 0.5, cab_z), (cab_w, cab_h, cab_l))]


# --------------------------------------------------------------------------- excavator
def _boom_section(m, node, a, b, root_thick, tip_thick, bow=0.0, mat=BODY):
    """A boom member as a tapered box-section weldment rather than a stick.

    Three rings lofted along the member give the deep root, the waist and the narrow
    tip a real boom has, and `bow` bends the waist the way a backhoe boom curves.
    """
    direction = tuple(b[i] - a[i] for i in range(3))
    length = math.sqrt(sum(c * c for c in direction))
    if length < 1e-4:
        return
    direction = tuple(c / length for c in direction)
    right, up = _cross_axes(direction)
    rings = []
    for t, thick in ((0.0, root_thick), (0.45, (root_thick + tip_thick) * 0.56),
                     (1.0, tip_thick)):
        centre = tuple(_lerp(a[i], b[i], t) + up[i] * bow * math.sin(math.pi * t)
                       for i in range(3))
        h = thick * 0.5
        rings.append([tuple(centre[i] + right[i] * sr * h + up[i] * su * h
                            for i in range(3))
                      for sr, su in ((1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0), (1.0, -1.0))])
    m.loft(rings, mat=mat, parent=node)
    for sr in (-1.0, 1.0):
        m.box(tuple(a[i] + right[i] * sr * root_thick * 0.62 for i in range(3)),
              (root_thick * 0.16, root_thick * 1.15, root_thick * 1.15), mat=METAL,
              parent=node)


def _bucket(m, node, tip, length, width, teeth):
    """Bucket: a shell swept around the pin, with a cutting edge and its teeth.

    Two arcs about the stick pin - an outer and an inner - give a shell of real
    thickness that opens back toward the machine, which is what makes a bucket read as
    a bucket and not as a lump on the end of the stick.
    """
    outer, inner = length * 1.05, length * 0.86
    a0, a1 = math.radians(-168.0), math.radians(-52.0)
    arc = []
    for i in range(5):
        a = _lerp(a0, a1, i / 4.0)
        arc.append((math.sin(a), math.cos(a)))
    pts = [(tip[1] + sy * outer, tip[2] + sz * outer) for sy, sz in arc]
    pts += [(tip[1] + sy * inner, tip[2] + sz * inner) for sy, sz in reversed(arc)]
    m.prism(pts, (tip[0], 0.0, 0.0), width, mat=METAL, parent=node, axis=0)
    # Side cutters: the wear plates that stand proud of the shell on a real bucket.
    for side in (-1.0, 1.0):
        m.prism(pts, (tip[0] + side * width * 0.5, 0.0, 0.0), width * 0.09, mat=METAL,
                parent=node, axis=0)
    if teeth < 1:
        return
    edge = (tip[1] + arc[-1][0] * (outer + inner) * 0.5,
            tip[2] + arc[-1][1] * (outer + inner) * 0.5)
    span = width * 0.84
    step = span / teeth
    first = m.box((tip[0] - span * 0.5 + step * 0.5, edge[0], edge[1]),
                  (step * 0.52, length * 0.2, length * 0.30), mat=METAL, parent=node,
                  rot=mk.unity_euler(math.degrees(a1) + 90.0, 0.0, 0.0), bevel=False)
    m.array(first, teeth, (step, 0.0, 0.0), parent=node)


def _build_excavator(m, record, vis):
    """Crawler excavator: track frame, slewing house, boom, stick, bucket."""
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    track_h = vis["TrackH"]
    mass = _num(record.get("MassKg"), 8000.0)
    power = _num(record.get("EnginePowerKw"), 80.0)
    tier = datasrc.tier_of(record)
    width = _clamp(_num(record.get("TrackWidthM"), 0.0) or body_w * 0.22,
                   0.18, body_w * 0.42)
    gauge = (body_w - width) * 0.5
    belt_l = body_l * 0.78

    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * gauge, track_h * 0.5, 0.0))
        _track(m, node, side * gauge, 0.0, belt_l, width, track_h, mass,
               drive_at_rear=True)

    frame_y = track_h * 0.52
    m.box((0.0, frame_y, 0.0), (gauge * 1.9, track_h * 0.34, belt_l * 0.34), mat=BODY)
    m.box((0.0, frame_y, 0.0), (gauge * 0.7, track_h * 0.40, belt_l * 0.9), mat=BODY)

    deck = track_h * 0.98
    house_l = body_l * 0.62
    house_w = body_w * 0.86
    house_h = body_h * 0.62
    cab_w = max(0.8, vis["CabW"] * 0.62)
    cab_l = max(0.9, vis["CabL"])
    cab_h = max(0.9, vis["CabH"])
    body_z = -body_l * 0.06
    m.node("turret", pivot=(0.0, deck, body_z))
    m.cylinder((0.0, deck + 0.06, body_z), house_w * 0.30, 0.14, axis=1, segments=16,
               mat=METAL, parent="turret")

    # The platform carries everything that slews; the engine house occupies the right
    # of it and the cab stands clear on the left, which is the layout of the real thing.
    inset = house_w * 0.08
    deck_plan = [(-house_w * 0.5, house_l * 0.36), (house_w * 0.5, house_l * 0.36),
                 (house_w * 0.5, -house_l * 0.40), (-house_w * 0.5, -house_l * 0.40)]
    x0 = -house_w * 0.5 + min(cab_w * 0.96, house_w * 0.42)
    x1 = house_w * 0.5
    plan = [(x0, house_l * 0.34), (x0 + inset, house_l * 0.5),
            (x1 - inset, house_l * 0.5), (x1, house_l * 0.34),
            (x1, -house_l * 0.34), (x1 - inset, -house_l * 0.52),
            (x0 + inset, -house_l * 0.52), (x0, -house_l * 0.34)]
    m.prism([(x, z + body_z) for x, z in plan], (0.0, deck + house_h * 0.5, 0.0),
            house_h, mat=BODY, parent="turret", axis=1)
    house_x = (x0 + x1) * 0.5

    # Counterweight: a quarter of the machine's mass, as a cast slab that size.
    cw = _clamp(mass * 0.22 / _STEEL_KG_M3, 0.05, 4.0)
    cw_w, cw_h = house_w * 0.88, house_h * 0.92
    cw_l = _clamp(cw / (cw_w * cw_h), 0.22, house_l * 0.5)
    m.box((0.0, deck + cw_h * 0.5, body_z - house_l * 0.5 - cw_l * 0.42),
          (cw_w, cw_h, cw_l), mat=BODY, parent="turret")

    cab_x = -house_w * 0.5 + cab_w * 0.54
    cab_z = body_z + house_l * 0.16
    cab_base = deck + 0.09
    roof = _cab(m, cab_base, cab_z, cab_w, cab_l, cab_h, tier, x_centre=cab_x,
                rear_window=False)
    m.nodes["cab"].parent = "turret"
    _light_bar(m, roof, cab_z + cab_l * 0.28, cab_w,
               _num(record.get("LightingLumens"), 6000.0), parent="cab")

    hood_h = house_h * 0.75
    _grille(m, (house_x, deck + hood_h * 0.55, body_z - house_l * 0.5 + 0.02),
            (house_w * 0.34, hood_h * 0.6, 0.10), int(_clamp(power / 40.0, 4.0, 8.0)))
    electric = str(record.get("FuelType", "")).lower() == "electric"
    _exhaust(m, house_w * 0.24, deck + house_h, body_z + house_l * 0.18,
             _clamp(0.03 + power / 3600.0, 0.035, 0.10), house_h * 0.5, electric,
             parent="turret")
    _handrail(m, (house_w * 0.46, deck + house_h + 0.34, body_z - house_l * 0.44),
              (house_w * 0.46, deck + house_h + 0.34, body_z + house_l * 0.30),
              0.05, posts=2, drop=0.34, parent="turret")

    # Catwalk, toolbox and a step: an excavator is walked around on, not just driven.
    m.prism([(x * 1.03, z * 1.03 + body_z) for x, z in deck_plan],
            (0.0, deck + 0.05, 0.0), 0.08, mat=METAL, parent="turret", axis=1)
    m.box((x0 + house_w * 0.13, deck + house_h * 0.26, body_z - house_l * 0.30),
          (house_w * 0.22, house_h * 0.42, house_l * 0.28), mat=BODY, parent="turret")
    m.box((cab_x, deck - track_h * 0.16, cab_z + cab_l * 0.34),
          (cab_w * 0.5, 0.05, 0.26), mat=METAL, parent="turret")
    for side in (-1.0, 1.0):
        _bar(m, (cab_x + side * cab_w * 0.5, deck + 0.1, cab_z + cab_l * 0.5),
                (cab_x + side * cab_w * 0.5, deck - track_h * 0.14, cab_z + cab_l * 0.42),
                0.05, parent="turret")

    # Boom and stick at rest: boom up, stick tucked, bucket curled toward the machine.
    reach = max(1.6, vis["BoomLengthM"] or body_l * 0.9)
    foot = (house_w * 0.10, deck + house_h * 0.42, body_z + house_l * 0.46)
    l1, l2 = reach * 0.56, reach * 0.40
    ang1, ang2 = math.radians(46.0), math.radians(-34.0)
    elbow = (foot[0], foot[1] + math.sin(ang1) * l1, foot[2] + math.cos(ang1) * l1)
    tip = (foot[0], elbow[1] + math.sin(ang2) * l2, elbow[2] + math.cos(ang2) * l2)

    thick = _clamp(reach * 0.10, 0.14, 0.65)
    m.node("boom_01", pivot=foot, parent="turret")
    _boom_section(m, "boom_01", foot, elbow, thick, thick * 0.80, bow=thick * 0.55)
    m.node("boom_02", pivot=elbow, parent="boom_01")
    _boom_section(m, "boom_02", elbow, tip, thick * 0.80, thick * 0.46)
    m.node("bucket", pivot=tip, parent="boom_02")
    _bucket(m, "bucket", tip, _clamp(reach * 0.16, 0.22, 1.1),
            _clamp(body_w * 0.34, 0.28, 1.4), int(_clamp(reach * 0.8, 3.0, 6.0)))

    # Rams, and the linkage that curls the bucket: the parts an operator watches.
    _ram(m, (foot[0], foot[1] - thick * 0.7, foot[2] + thick * 0.4),
         (_lerp(foot[0], elbow[0], 0.55), _lerp(foot[1], elbow[1], 0.55) - thick * 0.55,
          _lerp(foot[2], elbow[2], 0.55)), thick * 0.5, parent="boom_01")
    _ram(m, (elbow[0], elbow[1] + thick * 0.72, elbow[2] - thick * 0.5),
         (_lerp(elbow[0], tip[0], 0.62), _lerp(elbow[1], tip[1], 0.62) + thick * 0.30,
          _lerp(elbow[2], tip[2], 0.62)), thick * 0.42, parent="boom_02")
    link = (_lerp(elbow[0], tip[0], 0.80), _lerp(elbow[1], tip[1], 0.80) + thick * 0.55,
            _lerp(elbow[2], tip[2], 0.80))
    _ram(m, (elbow[0], elbow[1] + thick * 0.5, elbow[2] - thick * 0.2), link,
         thick * 0.34, parent="boom_02")
    for sr in (-1.0, 1.0):
        _bar(m, (link[0] + sr * thick * 0.3, link[1], link[2]),
                (tip[0] + sr * thick * 0.3, tip[1] + thick * 0.28, tip[2]),
                thick * 0.16, parent="bucket")
        m.beam((foot[0] + sr * thick * 0.34, foot[1] + thick * 0.4, foot[2]),
               (_lerp(foot[0], elbow[0], 0.94) + sr * thick * 0.34,
                _lerp(foot[1], elbow[1], 0.94) + thick * 0.34,
                _lerp(foot[2], elbow[2], 0.94)), thick * 0.14, parent="boom_01",
               square=False, segments=8)
    m.box((foot[0], foot[1] + thick * 0.9, foot[2] - thick * 0.1),
          (thick * 0.5, thick * 0.42, thick * 0.3), mat=METAL, parent="boom_01")

    _headlights(m, roof - cab_h * 0.10, cab_z + cab_l * 0.5, cab_w * 0.32,
                _clamp(cab_w * 0.16, 0.10, 0.22), parent="cab")
    _mount_sockets(m, record, {
        "Front": (0.0, frame_y, body_l * 0.5 + 0.10),
        "Rear": (0.0, frame_y, -body_l * 0.5 - 0.10),
        "Mid": (0.0, track_h * 0.28, 0.0),
        "Roof": (cab_x, roof + 0.04, cab_z),
        "Tow": (0.0, frame_y, -belt_l * 0.5 - 0.08),
    })
    return [("cab_col", (cab_x, cab_base + cab_h * 0.5, cab_z),
             (cab_w, cab_h, cab_l))]


# --------------------------------------------------------------------------- crane
def _cross_axes(direction):
    """The two axes across a member: one horizontal, one in the member's own plane."""
    dx, dy, dz = direction
    right = (1.0, 0.0, 0.0)
    up = (dy * right[2] - dz * right[1], dz * right[0] - dx * right[2],
          dx * right[1] - dy * right[0])
    norm = math.sqrt(up[0] ** 2 + up[1] ** 2 + up[2] ** 2)
    if norm < 1e-6:
        return (0.0, 0.0, 1.0), (0.0, 1.0, 0.0)
    return right, tuple(c / norm for c in up)


def _lattice(m, node, base, direction, bays, bay_len, face, chord):
    """A lattice boom section: one bay of chords and lacing, arrayed up the boom.

    Four chords, four diagonals and a lateral make a bay, and the boom is that bay
    repeated - which is how the real thing is pinned together and shipped. Every member
    runs a little past its joint so that two of them overlap rather than share a vertex:
    welded steel looks like that, and a weld in the mesh would be a non-manifold edge.
    """
    step = tuple(c * bay_len for c in direction)
    right, up = _cross_axes(direction)
    half = face * 0.5

    def corner(sign_r, sign_u, along):
        return tuple(base[i] + right[i] * sign_r * half + up[i] * sign_u * half
                     + direction[i] * along for i in range(3))

    # A bay is arrayed, so every member of it is unchamfered: meshkit's bevel adds
    # faces of its own and m.array only repeats the ones the primitive returned.
    faces = []
    for sr in (-1.0, 1.0):
        for su in (-1.0, 1.0):
            faces += _bar(m, corner(sr, su, 0.0), corner(sr, su, bay_len), chord,
                          parent=node, grow=1.06, bevel=False)
    lace = chord * 0.62
    for sr in (-1.0, 1.0):
        faces += _bar(m, corner(sr, -1.0, 0.0), corner(sr, 1.0, bay_len), lace,
                      parent=node, grow=1.06, bevel=False)
        faces += _bar(m, corner(sr, -1.0, bay_len), corner(sr, 1.0, 0.0), lace,
                      parent=node, grow=1.06, bevel=False)
    for su in (-1.0, 1.0):
        faces += _bar(m, corner(-1.0, su, 0.0), corner(1.0, su, bay_len), lace,
                      parent=node, grow=1.06, bevel=False)
    faces += _bar(m, corner(-1.0, -1.0, 0.0), corner(1.0, -1.0, 0.0), lace,
                  parent=node, grow=1.06, bevel=False)
    if bays > 1:
        m.array(faces, bays, step, parent=node)
    return tuple(base[i] + step[i] * bays for i in range(3))


def _bar(m, a, b, thickness, mat=METAL, parent=None, grow=1.0, bevel=None):
    """A straight member between two points, chamfered only when it is thick enough.

    meshkit chamfers everything it makes, but a bevel as wide as the bar it is cutting
    collapses the bar into slivers, and a bar too thin to carry a chamfer reads the same
    without one - so the thin stuff (rails, door frames, lacing) stays crisp and cheap.
    """
    d = tuple(b[i] - a[i] for i in range(3))
    length = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if length < 1e-4:
        return []
    if bevel is None:
        bevel = _chamferable(thickness)
    return m.box(_mid(a, b), (thickness, thickness, length * grow), mat=mat,
                 parent=parent, rot=mk.look_rotation(d), bevel=bevel)


def _mid(a, b):
    return ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5)


def _build_crane(m, record, vis):
    """Crawler crane: wide crawlers, a slewing deck, a lattice boom and its ballast."""
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    track_h = vis["TrackH"]
    mass = _num(record.get("MassKg"), 40000.0)
    tier = datasrc.tier_of(record)
    width = _clamp(_num(record.get("TrackWidthM"), 0.0) or body_w * 0.18,
                   0.3, body_w * 0.30)
    gauge = (body_w - width) * 0.5
    belt_l = body_l * 0.94

    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * gauge, track_h * 0.5, 0.0))
        _track(m, node, side * gauge, 0.0, belt_l, width, track_h, mass,
               drive_at_rear=True)

    car_y = track_h * 0.55
    m.box((0.0, car_y, 0.0), (gauge * 2.0, track_h * 0.42, body_l * 0.30), mat=BODY)
    m.box((0.0, car_y, 0.0), (gauge * 0.8, track_h * 0.5, belt_l * 0.86), mat=BODY)

    deck = track_h * 1.02
    house_w = body_w * 0.46
    house_l = body_l * 0.52
    house_h = body_h * 0.78
    m.node("turret", pivot=(0.0, deck, 0.0))
    m.cylinder((0.0, deck + 0.08, 0.0), house_w * 0.44, 0.18, axis=1, segments=18,
               mat=METAL, parent="turret")
    m.box((0.0, deck + house_h * 0.5, -house_l * 0.06), (house_w, house_h, house_l),
          mat=BODY, parent="turret")

    cw = _clamp(mass * 0.26 / _STEEL_KG_M3, 0.3, 12.0)
    cw_w, cw_h = house_w * 1.18, house_h * 0.9
    cw_l = max(0.4, cw / (cw_w * cw_h))
    for i in (0, 1):
        m.box((0.0, deck + cw_h * 0.5,
               -house_l * 0.56 - cw_l * (0.5 + i * 1.06)),
              (cw_w, cw_h, cw_l), mat=BODY, parent="turret")

    cab_w = max(0.9, vis["CabW"] * 0.68)
    cab_l = max(0.9, vis["CabL"])
    cab_h = max(0.9, vis["CabH"])
    cab_x = -house_w * 0.5 - cab_w * 0.52
    cab_z = house_l * 0.22
    roof = _cab(m, deck + 0.18, cab_z, cab_w, cab_l, cab_h, tier, x_centre=cab_x,
                rear_window=False)
    m.nodes["cab"].parent = "turret"

    # Boom: sections of arrayed bays, base first, exactly as it is pinned together.
    boom_len = max(6.0, vis["BoomLengthM"] or body_l * 2.0)
    angle = math.radians(64.0)
    direction = (0.0, math.sin(angle), math.cos(angle))
    face = _clamp(boom_len * 0.055, 0.5, 1.9)
    chord = _clamp(face * 0.17, 0.06, 0.22)
    sections = int(_clamp(boom_len / 9.0, 2.0, 5.0))
    bay_len = _clamp(face * 1.35, 0.8, 2.6)
    bays = max(1, int(round(boom_len / sections / bay_len)))
    foot = (0.0, deck + house_h * 0.30, house_l * 0.52)

    parent = "turret"
    point = foot
    for index in range(sections):
        name = "boom_%02d" % (index + 1)
        m.node(name, pivot=point, parent=parent)
        point = _lattice(m, name, point, direction, bays, bay_len, face, chord)
        parent = name
    head = point

    m.cylinder((0.0, head[1], head[2]), face * 0.42, face * 0.5, axis=0, segments=14,
               mat=METAL, parent=parent)
    hook_y = max(track_h * 0.6, head[1] - boom_len * 0.55)
    _bar(m, (0.0, head[1] - face * 0.4, head[2]), (0.0, hook_y + face * 0.5, head[2]),
            face * 0.07, parent=parent)
    m.box((0.0, hook_y, head[2]), (face * 0.6, face * 0.9, face * 0.5), mat=METAL,
          parent=parent)

    gantry = (0.0, deck + house_h + boom_len * 0.09, -house_l * 0.30)
    for side in (-1.0, 1.0):
        _bar(m, (side * house_w * 0.38, deck + house_h * 0.9, house_l * 0.18), gantry,
                face * 0.10, parent="turret")
    _bar(m, gantry, (0.0, foot[1] + boom_len * 0.34 * math.sin(angle),
                    foot[2] + boom_len * 0.34 * math.cos(angle)), face * 0.05,
            parent="turret")

    _light_bar(m, deck + house_h, -house_l * 0.2, house_w,
               _num(record.get("LightingLumens"), 12000.0), parent="turret")
    _headlights(m, deck + house_h * 0.7, house_l * 0.5, house_w * 0.3, 0.22,
                parent="turret")
    _exhaust(m, house_w * 0.30, deck + house_h, -house_l * 0.02,
             _clamp(0.03 + _num(record.get("EnginePowerKw"), 200.0) / 3600.0, 0.05,
                    0.14), house_h * 0.42,
             str(record.get("FuelType", "")).lower() == "electric", parent="turret")
    _mount_sockets(m, record, {
        "Front": (0.0, car_y, body_l * 0.5 + 0.12),
        "Rear": (0.0, car_y, -body_l * 0.5 - 0.12),
        "Mid": (0.0, track_h * 0.3, 0.0),
        "Roof": (0.0, roof + 0.05, cab_z),
        "Tow": (0.0, car_y, -belt_l * 0.5 - 0.1),
    })
    return [("cab_col", (cab_x, deck + 0.18 + cab_h * 0.5, cab_z),
             (cab_w, cab_h, cab_l))]


# --------------------------------------------------------------------------- snowmobile
def _ski(m, x, z, length, lift, width):
    """One ski: a side profile with a curled tip, extruded across its width."""
    profile = [(0.0, -length * 0.5), (0.05, -length * 0.5), (0.05, length * 0.30),
               (lift * 0.9, length * 0.5), (lift, length * 0.46),
               (lift * 0.35, length * 0.24), (0.0, length * 0.22)]
    m.prism(profile, (x, 0.0, z), width, mat=METAL, axis=0)
    m.box((x, lift * 0.5, z - length * 0.05), (width * 0.5, lift * 0.9, width * 1.2),
          mat=METAL)


def _build_snowmobile(m, record, vis):
    """Sled: one belt down the middle, two skis, a saddle as long as it has seats."""
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    track_h = max(0.25, vis["TrackH"])
    mass = _num(record.get("MassKg"), 300.0)
    seats = int(_clamp(_num(record.get("Seats"), 1.0), 1.0, 3.0))
    width = _clamp(_num(record.get("TrackWidthM"), 0.0) or body_w * 0.4, 0.25, body_w)
    belt_l = body_l * 0.58
    belt_z = -body_l * 0.16

    # One track, but the contract wants both transforms: each half belt is its own,
    # which is honest geometry and scrolls as one surface.
    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * width * 0.25, track_h * 0.5, belt_z))
        _track(m, node, side * width * 0.25, belt_z, belt_l, width * 0.5, track_h, mass,
               drive_at_rear=False)

    tunnel_y = track_h * 0.96
    m.prism([(tunnel_y - 0.1, -belt_l * 0.52), (tunnel_y + 0.16, -belt_l * 0.52),
             (tunnel_y + 0.22, belt_l * 0.5), (tunnel_y - 0.02, belt_l * 0.52)],
            (0.0, 0.0, belt_z), width * 1.06, mat=BODY, axis=0)
    m.box((0.0, tunnel_y + 0.06, belt_z - belt_l * 0.52), (width * 1.06, 0.34, 0.1),
          mat=BODY)

    # Cowl: a loft that tapers from the bulkhead to the nose.
    nose_z = body_l * 0.5
    cowl = []
    for z, w, h, drop in ((-0.02, 1.00, 1.00, 0.0), (0.42, 0.96, 0.86, 0.04),
                          (0.78, 0.76, 0.60, 0.10), (1.00, 0.48, 0.30, 0.16)):
        zz = _lerp(tunnel_y * 0.0 + belt_z + belt_l * 0.34, nose_z, z)
        hw = body_w * 0.5 * w
        y0 = track_h * 0.42 + drop
        y1 = y0 + body_h * h * 0.62
        c = min(hw, y1 - y0) * 0.34
        cowl.append([(-hw, y0, zz), (hw, y0, zz), (hw, y1 - c, zz), (hw - c, y1, zz),
                     (-hw + c, y1, zz), (-hw, y1 - c, zz)])
    m.loft(cowl, mat=BODY)

    saddle_l = _clamp(0.52 + 0.34 * seats, 0.5, body_l * 0.5)
    saddle_y = tunnel_y + 0.2
    m.loft([[(-width * 0.42, saddle_y, belt_z + belt_l * 0.36),
             (width * 0.42, saddle_y, belt_z + belt_l * 0.36),
             (width * 0.42, saddle_y + 0.2, belt_z + belt_l * 0.36),
             (-width * 0.42, saddle_y + 0.2, belt_z + belt_l * 0.36)],
            [(-width * 0.46, saddle_y, belt_z + belt_l * 0.36 - saddle_l * 0.5),
             (width * 0.46, saddle_y, belt_z + belt_l * 0.36 - saddle_l * 0.5),
             (width * 0.46, saddle_y + 0.26, belt_z + belt_l * 0.36 - saddle_l * 0.5),
             (-width * 0.46, saddle_y + 0.26, belt_z + belt_l * 0.36 - saddle_l * 0.5)],
            [(-width * 0.40, saddle_y, belt_z + belt_l * 0.36 - saddle_l),
             (width * 0.40, saddle_y, belt_z + belt_l * 0.36 - saddle_l),
             (width * 0.40, saddle_y + 0.30, belt_z + belt_l * 0.36 - saddle_l),
             (-width * 0.40, saddle_y + 0.30, belt_z + belt_l * 0.36 - saddle_l)]],
           mat=BODY)

    for side in (-1.0, 1.0):
        m.box((side * (width * 0.5 + 0.12), tunnel_y - 0.06, belt_z + belt_l * 0.06),
              (0.26, 0.05, belt_l * 0.7), mat=METAL,
              rot=mk.unity_euler(0.0, 0.0, side * 8.0))
    cargo = _num(record.get("CargoCapacityKg"), 0.0) / _CARGO_KG_M3
    if cargo > 0.05:
        rack_l = _clamp(cargo / 0.36, 0.35, belt_l * 0.5)
        rack_z = belt_z - belt_l * 0.5 + rack_l * 0.5
        rack_y = tunnel_y + 0.42
        m.box((0.0, rack_y, rack_z), (width * 1.1, 0.05, rack_l), mat=METAL)
        for sx in (-1.0, 1.0):
            for sz in (-1.0, 1.0):
                _bar(m, (sx * width * 0.5, tunnel_y + 0.16, rack_z + sz * rack_l * 0.42),
                     (sx * width * 0.5, rack_y, rack_z + sz * rack_l * 0.42), 0.04)

    ski_z = body_l * 0.36
    for side in (-1.0, 1.0):
        _ski(m, side * body_w * 0.40, ski_z, body_l * 0.42, 0.19, 0.16)
        _bar(m, (side * body_w * 0.40, 0.14, ski_z), (side * body_w * 0.16,
                track_h * 0.62, ski_z - 0.06), 0.06)
        _bar(m, (side * body_w * 0.40, 0.30, ski_z), (side * body_w * 0.16,
                track_h * 0.86, ski_z - 0.12), 0.05)

    bar_y = track_h * 0.42 + body_h * 0.62 + 0.16
    bar_z = body_l * 0.06
    _bar(m, (0.0, bar_y - 0.16, bar_z - 0.06), (0.0, bar_y, bar_z), 0.05)
    _bar(m, (-body_w * 0.34, bar_y, bar_z), (body_w * 0.34, bar_y, bar_z), 0.04)
    for side in (-1.0, 1.0):
        m.cylinder((side * body_w * 0.28, bar_y, bar_z), 0.035, body_w * 0.12, axis=0,
                   segments=8, mat=METAL)

    screen_h = _clamp(body_h * 0.30, 0.16, 0.4)
    m.box((0.0, bar_y + screen_h * 0.4, bar_z + 0.18), (body_w * 0.46, screen_h, 0.03),
          mat=GLASS, rot=mk.unity_euler(26.0, 0.0, 0.0))
    _headlights(m, track_h * 0.42 + body_h * 0.34, nose_z - 0.08, body_w * 0.16, 0.14)
    _exhaust(m, body_w * 0.24, track_h * 0.55, belt_z + belt_l * 0.46, 0.045, 0.16,
             str(record.get("FuelType", "")).lower() == "electric")

    _mount_sockets(m, record, {
        "Front": (0.0, track_h * 0.4, nose_z + 0.06),
        "Rear": (0.0, tunnel_y, belt_z - belt_l * 0.52 - 0.06),
        "Mid": (0.0, track_h * 0.25, belt_z),
        "Roof": (0.0, bar_y + screen_h * 0.8, bar_z),
        "Tow": (0.0, tunnel_y * 0.9, belt_z - belt_l * 0.52 - 0.1),
    })
    return [("cab_col", (0.0, saddle_y + 0.2, belt_z + belt_l * 0.1),
             (body_w * 0.6, 0.8, saddle_l + 0.4))]


# --------------------------------------------------------------------------- tracked utv
def _build_utv(m, record, vis):
    """Tracked side-by-side: four bogie tracks, a ROPS cab, a bed sized by its payload."""
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    track_h = max(0.28, vis["TrackH"])
    mass = _num(record.get("MassKg"), 900.0)
    tier = datasrc.tier_of(record)
    seats = int(_clamp(_num(record.get("Seats"), 2.0), 1.0, 4.0))
    width = _clamp(_num(record.get("TrackWidthM"), 0.0) or body_w * 0.2, 0.18,
                   body_w * 0.3)
    belt_l = body_l * 0.34
    gauge = (body_w - width) * 0.5
    axle_z = body_l * 0.29

    # A track kit on each corner, not two long belts: that is what a converted UTV is.
    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * gauge, track_h * 0.5, 0.0))
        for z in (-axle_z, axle_z):
            _track(m, node, side * gauge, z, belt_l, width, track_h, mass * 0.5)

    floor = track_h * 0.92
    m.box((0.0, floor - 0.1, 0.0), (body_w * 0.62, 0.18, body_l * 0.86), mat=METAL)
    for side in (-1.0, 1.0):
        _bar(m, (side * body_w * 0.28, floor - 0.08, -axle_z),
                (side * gauge, track_h * 0.55, -axle_z), 0.1)
        _bar(m, (side * body_w * 0.28, floor - 0.08, axle_z),
                (side * gauge, track_h * 0.55, axle_z), 0.1)

    cab_l = _clamp(0.6 + 0.3 * seats, 0.7, body_l * 0.45)
    cab_w = body_w * 0.82
    cab_h = max(0.8, vis["CabH"])
    cab_z = body_l * 0.10
    roof = _cab(m, floor, cab_z, cab_w, cab_l, cab_h, tier)

    hood_z = cab_z + cab_l * 0.5 + body_l * 0.13
    m.box((0.0, floor + body_h * 0.16, hood_z), (body_w * 0.76, body_h * 0.32,
          body_l * 0.26), mat=BODY)
    m.wedge((0.0, floor + body_h * 0.16, hood_z + body_l * 0.16),
            (body_w * 0.72, body_h * 0.32, body_l * 0.1), mat=BODY, taper=0.5)

    bed = _clamp(_num(record.get("CargoCapacityKg"), 0.0) / _CARGO_KG_M3, 0.06, 3.0)
    bed_l = _clamp(body_l * 0.34, 0.5, body_l * 0.45)
    bed_w = body_w * 0.86
    bed_h = _clamp(bed / (bed_l * bed_w), 0.16, body_h * 0.5)
    bed_z = cab_z - cab_l * 0.5 - bed_l * 0.5 - 0.06
    m.box((0.0, floor + 0.04, bed_z), (bed_w, 0.08, bed_l), mat=BODY)
    for dx, dz, sx, sz in ((0.0, -bed_l * 0.5, bed_w, 0.07), (0.0, bed_l * 0.5, bed_w, 0.07),
                           (-bed_w * 0.5, 0.0, 0.07, bed_l), (bed_w * 0.5, 0.0, 0.07, bed_l)):
        m.box((dx, floor + bed_h * 0.5 + 0.06, bed_z + dz), (sx, bed_h, sz), mat=BODY)

    _light_bar(m, roof, cab_z + cab_l * 0.3, cab_w,
               _num(record.get("LightingLumens"), 6000.0), parent="cab")
    _headlights(m, floor + body_h * 0.26, hood_z + body_l * 0.2, body_w * 0.26, 0.16)
    _exhaust(m, body_w * 0.30, floor - 0.06, -body_l * 0.42, 0.05, 0.24,
             str(record.get("FuelType", "")).lower() == "electric")
    _mount_sockets(m, record, {
        "Front": (0.0, track_h * 0.5, body_l * 0.5 + 0.08),
        "Rear": (0.0, track_h * 0.5, -body_l * 0.5 - 0.08),
        "Mid": (0.0, track_h * 0.26, 0.0),
        "Roof": (0.0, roof + 0.04, cab_z),
        "Tow": (0.0, track_h * 0.45, -body_l * 0.5 - 0.06),
    })
    return [("cab_col", (0.0, floor + cab_h * 0.5, cab_z), (cab_w, cab_h, cab_l))]


# --------------------------------------------------------------------------- walk-behind
def _build_walkbehind(m, record, vis):
    """Two-stage walk-behind: auger housing, chute, engine, handlebars, no cab.

    Its wheels are what the ground drives, so they are what carries the track_L/track_R
    names the contract asks every tracked chassis to publish.
    """
    body_l, body_w, body_h = vis["BodyL"], vis["BodyW"], vis["BodyH"]
    specs = record.get("Specs") or {}
    intake = _clamp(_num(specs.get("intakeWidthM"), 0.0) or body_w * 0.9, 0.4, body_w * 1.2)
    throw = _clamp(_num(specs.get("throwDistanceM"), 10.0), 4.0, 20.0)
    wheel_r = _clamp(vis["WheelRadiusM"], 0.1, 0.4)
    power = _num(record.get("EnginePowerKw"), 8.0)

    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.node(node, pivot=(side * body_w * 0.36, wheel_r, -body_l * 0.16))
        _road_wheel(m, side * body_w * 0.36, wheel_r, -body_l * 0.16, wheel_r,
                    wheel_r * 0.5)
        m.lathe([(wheel_r * 1.04, -wheel_r * 0.22), (wheel_r * 1.04, wheel_r * 0.22)],
                (side * body_w * 0.36, wheel_r, -body_l * 0.16), segments=14, mat=METAL,
                parent=node)

    # Auger housing: an open scoop, so its profile is a C rather than a closed box.
    housing_z = body_l * 0.26
    r = body_h * 0.32
    lip = wheel_r * 0.22
    profile = [(lip, -r * 0.9), (lip, r * 0.95), (lip + 0.05, r * 0.95),
               (lip + 0.05, -r * 0.55)]
    for i in range(7):
        t = math.pi * (-0.5 + i / 6.0)
        profile.append((lip + r + math.sin(t) * r, math.cos(t) * r * 0.95))
    profile.append((lip, -r * 0.9 - 0.05))
    m.prism(profile, (0.0, 0.0, housing_z), intake, mat=BODY, axis=0)
    m.box((0.0, lip * 0.5, housing_z - r * 0.92), (intake, lip, 0.06), mat=METAL)

    m.node("blower_impeller", pivot=(0.0, lip + r, housing_z))
    m.cylinder((0.0, lip + r, housing_z), r * 0.22, intake * 0.94, axis=0, segments=10,
               mat=METAL, parent="blower_impeller")
    for i in range(6):
        t = 2.0 * math.pi * i / 6.0
        m.box((_lerp(-intake * 0.4, intake * 0.4, i / 5.0), lip + r + math.sin(t) * r * 0.5,
               housing_z + math.cos(t) * r * 0.5),
              (intake * 0.16, r * 0.62, 0.05), mat=METAL, parent="blower_impeller",
              rot=mk.unity_euler(math.degrees(t), 0.0, 0.0))

    chute_y = lip + r * 2.0
    m.node("blower_chute", pivot=(0.0, chute_y, housing_z - r * 0.1))
    m.lathe([(r * 0.55, 0.0), (r * 0.5, throw * 0.022), (r * 0.42, throw * 0.05)],
            (0.0, chute_y, housing_z - r * 0.1), segments=12, mat=BODY,
            parent="blower_chute")
    m.tube((0.0, chute_y + throw * 0.05, housing_z - r * 0.1), r * 0.46, r * 0.34,
           throw * 0.02, axis=1, segments=12, parent="blower_chute")
    m.box((0.0, chute_y + throw * 0.062, housing_z + r * 0.24),
          (r * 0.9, throw * 0.03, 0.05), mat=BODY, parent="blower_chute",
          rot=mk.unity_euler(34.0, 0.0, 0.0))

    deck_y = wheel_r * 1.5
    m.box((0.0, deck_y, -body_l * 0.02), (body_w * 0.6, 0.1, body_l * 0.34), mat=METAL)
    eng_h = _clamp(0.22 + power * 0.014, 0.24, 0.5)
    m.box((0.0, deck_y + eng_h * 0.5, -body_l * 0.04), (body_w * 0.52, eng_h,
          body_l * 0.26), mat=BODY)
    m.cylinder((body_w * 0.2, deck_y + eng_h * 0.9, -body_l * 0.16), 0.05,
               body_l * 0.16, axis=2, segments=8, mat=METAL)
    m.box((0.0, deck_y + eng_h * 1.15, -body_l * 0.02), (body_w * 0.34, eng_h * 0.5,
          body_l * 0.16), mat=BODY)

    bar_y = body_h * 0.98
    bar_z = -body_l * 0.5
    for side in (-1.0, 1.0):
        _bar(m, (side * body_w * 0.26, deck_y + 0.06, -body_l * 0.1),
             (side * body_w * 0.30, bar_y, bar_z), 0.035)
        m.cylinder((side * body_w * 0.30, bar_y, bar_z + 0.04), 0.028, body_l * 0.1,
                   axis=2, segments=8, mat=METAL)
    _bar(m, (-body_w * 0.30, bar_y, bar_z), (body_w * 0.30, bar_y, bar_z), 0.03)
    m.box((0.0, bar_y - 0.1, bar_z + body_l * 0.12), (body_w * 0.3, 0.16, 0.05),
          mat=BODY, rot=mk.unity_euler(30.0, 0.0, 0.0))
    _headlights(m, chute_y - r * 0.3, housing_z + r * 0.4, body_w * 0.16, 0.1)

    _mount_sockets(m, record, {
        "Front": (0.0, lip + r * 0.4, housing_z + r + 0.06),
        "Rear": (0.0, deck_y, -body_l * 0.3),
        "Mid": (0.0, wheel_r * 0.5, 0.0),
        "Roof": (0.0, bar_y, bar_z + body_l * 0.1),
        "Tow": (0.0, wheel_r * 0.8, -body_l * 0.32),
    })
    return []


# --------------------------------------------------------------------------- entry
_BUILDERS = {
    "groomer": _build_groomer,
    "excavator": _build_excavator,
    "crane": _build_crane,
    "snowmobile": _build_snowmobile,
    "utv": _build_utv,
    "walkbehind": _build_walkbehind,
}


def build(record, out_path):
    """Build one tracked machine from its vehicles.json record."""
    vis = datasrc.visual(record)
    silhouette = vis.get("Silhouette", "groomer")
    builder = _BUILDERS.get(silhouette, _build_groomer)
    budget = "machine_hero" if datasrc.is_hero(record) else "machine_small"
    model = mk.Model(record["Id"], budget_key=budget,
                     seed=datasrc.seed_for(record["Id"]))
    colliders = builder(model, record, vis) or []
    return export.emit(model, out_path, box_colliders=colliders,
                       extra={"family": "tracked", "kind": "machine",
                              "displayName": record.get("DisplayName", record["Id"]),
                              "sourceId": record["Id"],
                              "silhouette": silhouette})
