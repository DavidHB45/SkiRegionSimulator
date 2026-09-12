"""Lift terminals: the station at each end of a line, in drive and in return trim.

A terminal is where the lift families have to separate at a glance, and the thing that
separates them is length. A fixed-grip station is a short, tall shed over one bullwheel.
A detachable is a twenty-metre housing, because a carrier arriving at line speed has to
be slowed to walking pace, carried round the contour on a bank of tyres and accelerated
back out again, and the faster the line runs the longer that takes. A tram has no
through-running terminal at all: it is a station house with a drive sheave and the
track ropes dead-ended into anchor blocks. None of that is switched on a lift id.

The numbers that set the proportions, all read off the record:

    PowerDrawKw             haul rope diameter, and through the bend ratio a rope is
                            allowed the bullwheel diameter, which sets the width of the
                            whole station
    LineSpeedMs,
    LoadingSpeedMs          how long the deceleration and acceleration rails have to be
    SeatsOrCabinCapacity    how far a carrier hangs below the rail, so rail and roof
                            height, and how big a tram platform or a rail car has to be
    CapacityPph             how wide the boarding lane is and how many gates it has
    CapexTerminalDrive,
    CapexTerminalReturn     how much plant each end carries
    StaffRequired           whether the end has an operator booth
    CarrierSpacingM         how far apart the tyres have to hand a carrier on
    CabinBarnCapex          whether there is a garage spur, and how big the barn is
    RopeConfiguration       Tri and Bi carry track ropes, so they carry anchorages
    Tier                    an old station is a boxy shed, a modern one is a shell

The drive end and the return end are the same station with different plant: the drive
carries the motor room, the gearbox and the sheave train, the return carries the
tensioning carriage and whatever pulls it - a counterweight on the older tiers, a
hydraulic ram on the newer ones.
"""
import math
import os

from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

# Terminals are addressed by lift record, not by a machine's Visual.Silhouette.
SILHOUETTES = frozenset()

# What a terminal actually slows a carrier at. Above about 1 m/s^2 a hanging carrier
# swings, so this is a comfort limit rather than a mechanical one, and it is what makes
# a fast lift's terminal long.
_DECEL_MS2 = 0.9

# Drive tyres along a decel or accel rail. The pitch has to be short enough that two
# tyres always hold the grip plate between them.
_TYRE_PITCH_M = 1.0

# Clearance from the bullwheel rim to the inside of the housing wall.
_SHELL_CLEAR_M = 1.0

# A rope may only be bent round a wheel so far before its strands fret, so a bullwheel's
# size is a fixed multiple of the rope on it: these are wheel radius over rope diameter,
# half the bend ratio a rope maker quotes. Detachables buy the bigger wheel because the
# grips have to ride round it as well as the rope does.
_BEND_RATIO = {"detachable": 50.0, "fixed": 40.0, "surface_rope": 40.0,
               "tram": 40.0, "rail": 34.0}


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


def _seg(radius):
    """Sides on a revolved part, from how big it reads on screen.

    A 2 m bullwheel earns twice the sides of a 0.15 m drive tyre, and the small stuff is
    where a procedural pipeline throws its budget away if nobody says no.
    """
    return int(_clamp(8.0 + radius * 7.0, 8.0, 24.0))


def _terminal_kind(record):
    """Which station this lift builds, from what the record says the lift is.

    The only non-obvious test is the surface one. A conveyor has no span worth the name
    because it lies on the ground on a light frame; a rope tow, a platter and a T-bar
    all span between towers. MaxSpanM says which of the two a surface lift is without
    anyone having to name it.
    """
    family = str(record.get("Family") or "Chair")
    grip = str(record.get("Grip") or "Fixed")
    if family == "Rail":
        return "rail"
    if family == "Aerial" and str(record.get("RopeConfiguration")) == "Reversible":
        return "tram"
    if family == "Surface":
        return "belt" if _num(record.get("MaxSpanM"), 100.0) <= 40.0 else "surface_rope"
    return "detachable" if grip == "Detachable" else "fixed"


def _rope_diameter(record):
    """Haul rope diameter from the drive power.

    Rope tension is what the motor has to pull against, so power is the honest proxy for
    rope size, and rope size is what everything else in a terminal is built around.
    """
    power = max(5.0, _num(record.get("PowerDrawKw"), 100.0))
    return _clamp(0.026 * (power / 120.0) ** 0.30, 0.014, 0.060)


def _bullwheel_radius(record, kind):
    return _rope_diameter(record) * _BEND_RATIO.get(kind, 40.0)


def _track_ropes(record):
    """Track ropes per strand: a tricable carries two, a bicable one, a monocable none."""
    return {"Tri": 2, "Bi": 1}.get(str(record.get("RopeConfiguration")), 0)


def _carrier_drop(record):
    """How far a carrier's floor hangs below the rail.

    A chair hangs on a short hanger and its seat is barely below the grip; a cabin hangs
    on a long one and has a two-and-a-half metre body under that, which is why a gondola
    station is a storey taller than a chairlift station for the same rope.
    """
    seats = _num(record.get("SeatsOrCabinCapacity"), 4.0)
    family = str(record.get("Family") or "Chair")
    if family in ("Gondola", "Aerial") or seats >= 8.0:
        return _clamp(3.6 + seats * 0.05, 3.8, 5.2)
    return _clamp(2.9 + seats * 0.06, 3.0, 3.5)


def _deck_height(record):
    """Boarding deck above the snow. Cabins load off a raised deck, chairs off a ramp."""
    return 0.34 if str(record.get("Family") or "Chair") == "Chair" else 0.55


def _staff(record, end):
    """Operators rostered at this end of the lift."""
    staff = record.get("StaffRequired") or {}
    key = "Bottom" if end == "drive" else "Top"
    return int(_num(staff.get(key), 0.0))


def _lane_count(record):
    """Maze lanes behind the gates, from throughput. A 4 800 an hour lift needs three."""
    return int(_clamp(_num(record.get("CapacityPph"), 1200.0) / 1400.0, 1.0, 3.0))


def _maze(m, load_x, deck, z0, z1, record):
    """The queue maze: one fence per lane boundary, so the width is the throughput."""
    lanes = _lane_count(record)
    for i in range(lanes + 1):
        x = load_x + (i - lanes * 0.5) * 1.1
        _handrail(m, (x, 0.0, z0), (x, 0.0, z1), posts_every=1.3)


def _board_length(record, compressed):
    """How long the loading zone has to be, from how far apart the carriers arrive.

    Carriers close up as they slow into a terminal, so the gap a rider steps into is
    CarrierSpacingM scaled by loading speed over line speed. `compressed` says whether
    this family slows its carriers down at all: a fixed grip does not, so its loading
    zone is a fraction of the line spacing instead of the whole of it.
    """
    spacing = _num(record.get("CarrierSpacingM"), 12.0)
    if spacing <= 0.0:
        return 5.0
    if not compressed:
        return _clamp(spacing * 0.35, 3.0, 7.0)
    line = max(1.0, _num(record.get("LineSpeedMs"), 5.0))
    load = _clamp(_num(record.get("LoadingSpeedMs"), 1.0), 0.25, line)
    return _clamp(spacing * (load / line) * 1.4, 4.0, 12.0)


# --------------------------------------------------------------------------- parts
def _pad(m, x, z, size=(1.1, 0.26, 1.1), parent=None):
    """A concrete footing. Everything a terminal stands on starts here at y = 0."""
    return m.box((x, size[1] * 0.5, z), size, mat=METAL, parent=parent)


def _column(m, x, z, top, thick=0.34, parent=None, foot=True):
    """A square column from a footing up to a frame, with the footing under it."""
    made = m.box((x, top * 0.5 + 0.1, z), (thick, top - 0.2, thick), mat=METAL,
                 parent=parent)
    if foot:
        made += _pad(m, x, z, (thick * 2.6, 0.24, thick * 2.6), parent=parent)
    return made


def _handrail(m, a, b, height=1.05, parent=None, posts_every=1.6):
    """Posts and two rails along a line. Terminals are covered in these."""
    ax, ay, az = a
    bx, by, bz = b
    span = math.hypot(bx - ax, bz - az)
    if span < 0.4:
        return []
    count = int(_clamp(span / posts_every + 1.0, 2.0, 14.0))
    step = ((bx - ax) / (count - 1), 0.0, (bz - az) / (count - 1))
    post = m.box((ax, ay + height * 0.5, az), (0.06, height, 0.06), mat=METAL,
                 parent=parent, bevel=False)
    made = post + m.array(post, count, step, parent=parent)
    for frac in (1.0, 0.55):
        made += m.beam((ax, ay + height * frac, az), (bx, by + height * frac, bz),
                       0.05, mat=METAL, parent=parent)
    return made


def _ladder(m, x, z, top, parent=None, face=1.0):
    """A cage ladder up a column: two stiles and a rung array."""
    made = []
    for side in (-1.0, 1.0):
        made += m.beam((x + side * 0.22, 0.3, z), (x + side * 0.22, top, z), 0.05,
                       mat=METAL, parent=parent)
    rungs = int(_clamp((top - 0.5) / 0.30, 3.0, 22.0))
    rung = m.box((x, 0.5, z), (0.46, 0.035, 0.035), mat=METAL, parent=parent,
                 bevel=False)
    made += rung + m.array(rung, rungs, (0.0, (top - 0.7) / max(1, rungs - 1), 0.0),
                           parent=parent)
    made += m.beam((x, 0.3, z + face * 0.02), (x, top, z + face * 0.02), 0.05,
                   mat=METAL, parent=parent)
    return made


def _stair(m, x, z0, z1, top, width=1.2, parent=None):
    """A flight up to a boarding deck, as an arrayed tread plus a stringer each side."""
    rise = max(0.2, top)
    steps = int(_clamp(rise / 0.19, 2.0, 14.0))
    run = abs(z1 - z0)
    direction = 1.0 if z1 >= z0 else -1.0
    tread = m.box((x, rise / steps * 0.5, z0 + direction * run / steps * 0.5),
                  (width, 0.05, run / steps * 0.95), mat=METAL, parent=parent,
                  bevel=False)
    made = tread + m.array(tread, steps, (0.0, rise / steps, direction * run / steps),
                           parent=parent)
    for side in (-1.0, 1.0):
        made += m.beam((x + side * width * 0.5, 0.06, z0),
                       (x + side * width * 0.5, rise, z1), 0.12, mat=METAL,
                       parent=parent)
    return made


def _bullwheel(m, center, radius, node="bullwheel", parent=None, grooves=1,
               spokes=True):
    """The wheel itself, on its own node, spinning about Y as the contract requires.

    The rim is lathed rather than boxed because a bullwheel's edge is the one part of a
    terminal a rider looks straight at while queueing, and a faceted disc with a flange
    on it is what that has to read as.
    """
    m.node(node, pivot=center, parent=parent)
    seg = _seg(radius)
    rim = _clamp(radius * 0.16, 0.14, 0.34)
    lip = rim * (1.9 if grooves > 1 else 1.35)
    profile = [(radius * 0.16, -lip * 0.8),
               (radius - rim * 0.9, -rim * 0.55),
               (radius, -lip),
               (radius * 0.965, 0.0),
               (radius, lip),
               (radius - rim * 0.9, rim * 0.55),
               (radius * 0.16, lip * 0.8)]
    made = m.lathe(profile, center, segments=seg, mat=METAL, parent=node, axis=1)
    made += m.cylinder(center, radius * 0.2, lip * 3.0, axis=1, segments=max(8, seg // 2),
                       mat=METAL, parent=node)
    if spokes and radius > 0.8:
        count = 6 if radius < 2.2 else 8
        for i in range(count):
            a = 2.0 * math.pi * i / count
            inner = (center[0] + math.cos(a) * radius * 0.22, center[1],
                     center[2] + math.sin(a) * radius * 0.22)
            outer = (center[0] + math.cos(a) * radius * 0.92, center[1],
                     center[2] + math.sin(a) * radius * 0.92)
            made += m.beam(inner, outer, rim * 0.55, mat=METAL, parent=node)
    return made


def _sheave_train(m, points, radius, parent=None, start=1):
    """Deflection sheaves inside a terminal. They spin about X and number uphill-first."""
    made = []
    for i, point in enumerate(points):
        name = "sheave_%02d" % (start + i)
        m.node(name, pivot=point, parent=parent)
        profile = [(radius * 0.25, -radius * 0.22), (radius * 0.9, -radius * 0.16),
                   (radius, -radius * 0.1), (radius, radius * 0.1),
                   (radius * 0.9, radius * 0.16), (radius * 0.25, radius * 0.22)]
        made += m.lathe(profile, point, segments=_seg(radius), mat=METAL, parent=name,
                        axis=0)
    return made


def _gate(m, name, base, reach, height=1.02, parent=None):
    """One leaf of a boarding gate: the post it swings on and the arm it swings.

    The arm is authored where it sits and the node's pivot is the post centre, so the
    game rotating the node about Y opens the gate about the right line.
    """
    m.node(name, pivot=base, parent=parent)
    made = m.cylinder((base[0], base[1] + height * 0.5, base[2]), 0.055, height, axis=1,
                      segments=8, mat=METAL, parent=name)
    for frac in (0.95, 0.5):
        made += m.beam((base[0], base[1] + height * frac, base[2]),
                       (base[0] + reach, base[1] + height * frac, base[2]), 0.055,
                       mat=BODY, parent=name)
    made += m.box((base[0] + reach * 0.98, base[1] + height * 0.72, base[2]),
                  (0.1, 0.26, 0.09), mat=BODY, parent=name)
    return made


def _ram(m, a, b, radius, parent=None):
    """A hydraulic ram: a barrel over the first half and a bright rod over the rest."""
    ax, ay, az = a
    bx, by, bz = b
    mid = (_lerp(ax, bx, 0.55), _lerp(ay, by, 0.55), _lerp(az, bz, 0.55))
    made = m.beam(a, mid, radius * 2.0, mat=METAL, parent=parent, square=False,
                  segments=10)
    made += m.beam((_lerp(ax, bx, 0.45), _lerp(ay, by, 0.45), _lerp(az, bz, 0.45)), b,
                   radius * 1.1, mat=METAL, parent=parent, square=False, segments=8)
    return made


def _counterweight(m, center, drop, weight=(0.9, 1.1, 0.9), parent=None):
    """A tension counterweight hanging in its guide frame, which is what the old ones do."""
    cx, cy, cz = center
    made = []
    # A twelve-metre guide column is not the same stick as a two-metre one.
    guide = _clamp(0.09 + cy * 0.025, 0.13, 0.4)
    for side in (-1.0, 1.0):
        made += m.beam((cx + side * (weight[0] * 0.5 + 0.16), 0.1, cz),
                       (cx + side * (weight[0] * 0.5 + 0.16), cy + 0.4, cz), guide,
                       mat=METAL, parent=parent)
    made += _pad(m, cx, cz, (weight[0] + 1.0, 0.3, weight[2] + 0.8), parent=parent)
    block_y = max(weight[1] * 0.5 + 0.34, cy - drop)
    made += m.box((cx, block_y, cz), weight, mat=METAL, parent=parent)
    made += m.box((cx, block_y + weight[1] * 0.62, cz),
                  (weight[0] * 0.92, weight[1] * 0.28, weight[2] * 0.92), mat=METAL,
                  parent=parent)
    made += m.beam((cx, block_y + weight[1] * 0.7, cz), (cx, cy, cz), 0.07, mat=METAL,
                   parent=parent, square=False, segments=6)
    return made


def _cabinet(m, center, size, parent=None):
    """A control or electrical cabinet, with the panel face glazed so it reads as one."""
    made = m.box(center, size, mat=BODY, parent=parent)
    made += m.box((center[0], center[1] + size[1] * 0.12, center[2] + size[2] * 0.52),
                  (size[0] * 0.62, size[1] * 0.38, 0.04), mat=GLASS, parent=parent)
    made += m.box((center[0], center[1] + size[1] * 0.54, center[2]),
                  (size[0] * 1.08, size[1] * 0.08, size[2] * 1.12), mat=METAL,
                  parent=parent)
    return made


def _booth(m, center, size, parent=None):
    """An operator booth: a glazed box with a door frame and a flat roof."""
    cx, cy, cz = center
    sx, sy, sz = size
    made = m.box((cx, cy + sy * 0.22, cz), (sx, sy * 0.44, sz), mat=BODY, parent=parent)
    made += m.box((cx, cy + sy * 0.72, cz), (sx * 0.98, sy * 0.5, sz * 0.98),
                  mat=GLASS, parent=parent)
    made += m.box((cx, cy + sy + 0.04, cz), (sx * 1.12, 0.1, sz * 1.12), mat=BODY,
                  parent=parent)
    made += m.beam((cx - sx * 0.5, cy, cz - sz * 0.5), (cx - sx * 0.5, cy + sy, cz - sz * 0.5),
                   0.09, mat=METAL, parent=parent)
    return made


def _shelter(m, center, size, parent=None):
    """A glazed waiting shelter with a bench in it. Every station platform has one."""
    cx, cy, cz = center
    sx, sy, sz = size
    made = m.box((cx, cy + sy * 0.5, cz - sz * 0.5), (sx, sy, 0.08), mat=GLASS,
                 parent=parent)
    for side in (-1.0, 1.0):
        made += m.box((cx + side * sx * 0.5, cy + sy * 0.5, cz), (0.08, sy, sz),
                      mat=GLASS, parent=parent)
        made += m.beam((cx + side * sx * 0.5, cy, cz + sz * 0.5),
                       (cx + side * sx * 0.5, cy + sy, cz + sz * 0.5), 0.1, mat=METAL,
                       parent=parent)
    made += m.box((cx, cy + sy + 0.06, cz), (sx * 1.1, 0.12, sz * 1.1), mat=BODY,
                  parent=parent)
    made += m.box((cx, cy + 0.46, cz - sz * 0.28), (sx * 0.8, 0.07, 0.42), mat=BODY,
                  parent=parent)
    made += m.box((cx, cy + 0.23, cz - sz * 0.28), (sx * 0.72, 0.46, 0.08), mat=METAL,
                  parent=parent)
    return made


def _roof(m, z0, z1, width, eave, rise, thickness=0.16, curved=False, mat=BODY,
          parent=None, overhang=0.28, gables=False):
    """A roof slab over a station: the ridge runs along the line, the slopes shed sideways.

    That orientation is not cosmetic. A roof ridged across the line leaves a triangle of
    open air over each long side wall, and a station is long. `gables` closes the two
    ends, which is what the walls under them cannot do.

    A tier-1 station gets a straight gable because that is what an old shed is; from
    tier 3 the profile is an arc, which is the single cheapest thing that separates a
    modern station from an old one at 200 m.
    """
    half = width * 0.5 + overhang
    if curved:
        steps = 6
        top = [(_lerp(-half, half, i / steps),
                eave + rise * math.sin(math.pi * i / steps)) for i in range(steps + 1)]
    else:
        top = [(-half, eave), (0.0, eave + rise), (half, eave)]
    profile = top + [(x, y - thickness) for x, y in reversed(top)]
    made = m.prism(profile, (0.0, 0.0, (z0 + z1) * 0.5), (z1 - z0) + overhang * 2.0,
                   mat=mat, parent=parent, axis=2)
    if gables and rise > 0.05 and half > 1e-6:
        scale = (width * 0.5) / half
        gable = [(x * scale, y - thickness * 1.05) for x, y in top]
        for z in (z0 + 0.07, z1 - 0.07):
            made += m.prism(gable, (0.0, 0.0, z), 0.14, mat=mat, parent=parent, axis=2)
    return made


def _shell_ring(x_half, y_lo, y_hi, z, chamfer=0.24):
    """One cross-section of a lofted housing: a chamfered rectangle across the line.

    Kept deliberately square-shouldered. A housing lofted through soft rounded sections
    comes out looking like an upturned boat; the real thing is a clad box with its
    corners knocked off and a flat roof to shed snow.
    """
    c = min(chamfer, x_half * 0.4, (y_hi - y_lo) * 0.4)
    return [(-x_half + c, y_lo, z), (x_half - c, y_lo, z),
            (x_half, y_lo + c, z), (x_half, y_hi - c * 1.15, z),
            (x_half - c * 1.15, y_hi, z), (-x_half + c * 1.15, y_hi, z),
            (-x_half, y_hi - c * 1.15, z), (-x_half, y_lo + c, z)]


def _tyre_bank(m, x, y, z0, z1, radius=0.15, parent=None):
    """The tyre drive along a rail, as ONE tyre and its bracket arrayed down the run.

    Detachable terminals propel carriers with rubber tyres bearing down on a plate on
    top of the grip, so the tyres lie flat and turn about a vertical axis, driven by the
    shaft line above them. Sixty modelled tyres would be sixty modelled tyres; one
    arrayed tyre is the same silhouette for a twentieth of the budget.
    """
    run = abs(z1 - z0)
    count = int(_clamp(run / _TYRE_PITCH_M + 1.0, 2.0, 34.0))
    step = (z1 - z0) / (count - 1)
    tyre = m.cylinder((x, y, z0), radius, 0.085, axis=1, segments=8, mat=METAL,
                      parent=parent)
    tyre += m.box((x, y + 0.17, z0), (0.09, 0.25, 0.09), mat=METAL, parent=parent,
                  bevel=False)
    made = tyre + m.array(tyre, count, (0.0, 0.0, step), parent=parent)
    made += m.beam((x, y + 0.30, z0 - step * 0.4), (x, y + 0.30, z1 + step * 0.4), 0.1,
                   mat=METAL, parent=parent)
    return made


def _rail(m, x, y, z0, z1, parent=None, gauge=0.18):
    """The running rail a detachable grip hangs from, plus the beam carrying it."""
    made = m.box((x, y, (z0 + z1) * 0.5), (gauge, 0.11, abs(z1 - z0)), mat=METAL,
                 parent=parent)
    made += m.box((x, y + 0.19, (z0 + z1) * 0.5), (gauge * 0.45, 0.28, abs(z1 - z0)),
                  mat=METAL, parent=parent)
    return made


# --------------------------------------------------------------------------- belts
def _build_belt(record, end):
    """Magic carpet and covered conveyor: a covered drum at the end of a belt frame.

    There is no rope and no bullwheel on a conveyor. What the contract's `bullwheel`
    node binds to here is the belt tracking roller, which really does stand on a
    vertical axis at the nose of the drive and really does turn while the belt runs, so
    the one transform gameplay spins about Y spins something that should be spinning.
    """
    drive = end == "drive"
    width = _clamp(0.55 + _num(record.get("CapacityPph"), 1200.0) / 4000.0, 0.6, 1.1)
    drum_r = _clamp(width * 0.26, 0.18, 0.3)
    covered = _num(record.get("WeatherExposure"), 1.0) < 0.6
    deck_y = drum_r + 0.1
    half = width * 0.5
    frame_x = half + 0.11
    z_nose, z_tail = -1.5, 4.2

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # The belt itself is the node that scrolls; the cleats ride on it.
    m.node("carpet_belt", pivot=(0.0, deck_y, 0.0))
    m.box((0.0, deck_y, (z_nose + 0.6 + z_tail) * 0.5),
          (width, 0.05, z_tail - z_nose - 0.6), mat=METAL, parent="carpet_belt")
    cleat = m.box((0.0, deck_y + 0.05, z_nose + 0.9), (width * 0.94, 0.035, 0.05),
                  mat=METAL, parent="carpet_belt", bevel=False)
    m.array(cleat, int((z_tail - z_nose - 1.4) / 0.42) + 1, (0.0, 0.0, 0.42),
            parent="carpet_belt")

    # Drive drum, its hood, and the frame the whole thing hangs on.
    m.cylinder((0.0, drum_r + 0.07, z_nose + 0.42), drum_r, width + 0.12, axis=0,
               segments=14, mat=METAL)
    for side in (-1.0, 1.0):
        m.box((side * frame_x, deck_y * 0.55, (z_nose + z_tail) * 0.5),
              (0.1, deck_y * 1.1, z_tail - z_nose), mat=BODY)
        m.box((side * frame_x, deck_y + 0.13, (z_nose + z_tail) * 0.5),
              (0.13, 0.2, z_tail - z_nose), mat=BODY)
    hood = [(deck_y + 0.05, z_nose - 0.15), (drum_r * 2.0 + 0.34, z_nose + 0.25),
            (drum_r * 2.0 + 0.34, z_nose + 0.95), (deck_y + 0.05, z_nose + 1.15)]
    m.prism(hood + [(y - 0.07, z) for y, z in reversed(hood)], (0.0, 0.0, 0.0),
            width + 0.34, mat=BODY, axis=0)

    # Skids, cross members and rollers: a carpet sits on the snow, it is not founded in
    # it, so the frame is a ladder of members carrying the belt bed between two skids.
    for side in (-1.0, 1.0):
        m.box((side * frame_x, 0.055, (z_nose + z_tail) * 0.5),
              (0.26, 0.11, (z_tail - z_nose) * 0.94), mat=METAL)
    roller = m.cylinder((0.0, 0.16, z_nose + 1.4), 0.06, width + 0.04, axis=0,
                        segments=8, mat=METAL)
    m.array(roller, int((z_tail - z_nose - 2.0) / 0.45) + 1, (0.0, 0.0, 0.45))
    member = m.box((0.0, 0.1, z_nose + 1.3), (width + 0.3, 0.09, 0.09), mat=METAL,
                   bevel=False)
    m.array(member, int((z_tail - z_nose - 1.8) / 0.8) + 1, (0.0, 0.0, 0.8))
    for side in (-1.0, 1.0):
        m.prism([(deck_y - 0.14, z_nose + 0.9), (deck_y + 0.16, z_nose + 0.9),
                 (deck_y + 0.16, z_tail - 0.2), (deck_y - 0.14, z_tail - 0.2)],
                (side * (frame_x + 0.07), 0.0, 0.0), 0.06, mat=BODY, axis=0)

    # The apron. Both ends of a conveyor face the line with the belt and turn it round
    # on the drum at the outboard end, so on both of them riders step on and off past
    # the drum, at -Z, and the ramp is always on that side.
    apron = [(0.0, z_nose - 2.4), (deck_y - 0.02, z_nose - 0.5),
             (deck_y - 0.02, z_nose), (0.0, z_nose)]
    m.prism(apron, (0.0, 0.0, 0.0), width + 0.5, mat=METAL, axis=0)

    # The comb at the nose: the finger plate riders' ski tips ride over as the belt
    # turns under, and the scraper that keeps snow from being carried round with it.
    finger = m.box((-width * 0.5 + 0.06, deck_y + 0.035, z_nose + 0.78),
                   (0.075, 0.04, 0.26), mat=METAL, bevel=False)
    m.array(finger, int(width / 0.115), (0.115, 0.0, 0.0))
    m.box((0.0, deck_y - 0.13, z_nose + 0.5), (width + 0.06, 0.1, 0.12), mat=METAL)

    # Belt tracking roller on its vertical axis - the contract's bullwheel.
    m.node("bullwheel", pivot=(frame_x + 0.12, deck_y + 0.12, z_nose + 1.0))
    m.cylinder((frame_x + 0.12, deck_y + 0.3, z_nose + 1.0), 0.085, 0.34, axis=1,
               segments=10, mat=METAL, parent="bullwheel")

    for side in (-1.0, 1.0):
        _handrail(m, (side * (frame_x + 0.22), deck_y - 0.1, z_nose + 0.4),
                  (side * (frame_x + 0.22), deck_y - 0.1, z_tail - 0.2), height=1.0,
                  posts_every=1.1)

    if covered:
        # A covered conveyor is a tunnel: glazed hoops the length of the drive frame.
        arch = []
        for i in range(9):
            a = math.pi * i / 8.0
            arch.append((-math.cos(a) * (half + 0.42), deck_y + 0.1 + math.sin(a) * 1.75))
        inner = [(x * 0.955, deck_y + 0.1 + (y - deck_y - 0.1) * 0.955)
                 for x, y in reversed(arch)]
        m.prism(arch + inner, (0.0, 0.0, (z_nose + z_tail) * 0.5), z_tail - z_nose,
                mat=GLASS, axis=2)
        # One hoop arrayed down the tunnel. The members are round because meshkit only
        # chamfers boxes, and a chamfer rebuilds the faces m.array was handed.
        hoop = []
        for i in range(8):
            a0, a1 = math.pi * i / 8.0, math.pi * (i + 1) / 8.0
            hoop += m.beam((-math.cos(a0) * (half + 0.45), deck_y + 0.1 + math.sin(a0) * 1.78,
                            z_nose + 0.3),
                           (-math.cos(a1) * (half + 0.45), deck_y + 0.1 + math.sin(a1) * 1.78,
                            z_nose + 0.3), 0.09, mat=BODY, square=False, segments=6)
        m.array(hoop, int((z_tail - z_nose - 0.6) / 1.3) + 1, (0.0, 0.0, 1.3))

    # Plant and controls. The drive end carries the gearmotor and the operator's post;
    # the return end carries the belt take-up and nothing else.
    _cabinet(m, (half + 0.85, 0.62, z_nose + 2.2), (0.42, 1.15, 0.36))
    m.cylinder((half + 0.85, 1.34, z_nose + 2.2), 0.09, 0.2, axis=1, segments=8,
               mat=GLASS)
    if drive:
        m.cylinder((-(half + 0.5), drum_r + 0.07, z_nose + 0.42), drum_r * 0.9, 0.5,
                   axis=0, segments=10, mat=METAL)
        m.box((-(half + 1.0), drum_r + 0.2, z_nose + 0.5), (0.55, 0.5, 0.75), mat=BODY)
        m.box((-(half + 0.5), drum_r + 0.1, z_nose + 0.42), (0.26, drum_r * 2.4, 1.0),
              mat=BODY)
    else:
        m.box((-(half + 0.75), 0.55, z_nose + 0.6), (0.5, 1.0, 0.6), mat=BODY)
        _ram(m, (0.0, deck_y + 0.12, z_nose + 1.5), (0.0, deck_y + 0.12, z_nose + 0.55),
             0.05)
        for side in (-1.0, 1.0):
            m.box((side * (frame_x + 0.24), deck_y + 0.02, z_nose + 0.9),
                  (0.14, 0.3, 1.4), mat=METAL)

    # Gates, fencing and the sign gantry, all on the apron side with the ramp.
    lane_z = z_nose - 1.5
    for side in (-1.0, 1.0):
        _gate(m, "gate_L" if side < 0 else "gate_R",
              (side * (frame_x + 0.28), 0.0, lane_z), -side * 0.7)
        _handrail(m, (side * (frame_x + 0.9), 0.0, lane_z - 2.4),
                  (side * (frame_x + 0.9), 0.0, lane_z + 0.2), height=1.05,
                  posts_every=1.0)
        m.cylinder((side * (frame_x + 0.45), 0.55, lane_z - 0.3), 0.05, 1.1,
                   axis=1, segments=8, mat=BODY)
    m.box((0.0, 1.9, lane_z - 0.3), (1.5, 0.55, 0.08), mat=BODY)
    for side in (-1.0, 1.0):
        m.beam((side * 0.6, 0.0, lane_z - 0.3), (side * 0.6, 2.2, lane_z - 0.3), 0.08,
               mat=METAL)

    m.socket("light_L", (-(frame_x + 0.3), 2.3, z_nose + 1.0))
    m.socket("light_R", (frame_x + 0.3, 2.3, z_nose + 1.0))
    colliders = [("station_col", (0.0, 0.9, (z_nose + z_tail) * 0.5),
                  (width + 1.0, 1.8, z_tail - z_nose))]
    return m, colliders


# --------------------------------------------------------------------------- surface
def _build_surface_rope(record, end):
    """Rope tow, platter and T-bar: a mast with a horizontal bullwheel on top.

    Mast height comes off the drive capex rather than off the end being built, because
    the rope has to leave both ends at the same height: a cheap rope tow drive is a low
    frame you can touch and a T-bar drive is a four-metre mast, and the return mast is
    whatever the drive mast is.
    """
    drive = end == "drive"
    radius = _bullwheel_radius(record, "surface_rope")
    capex = _num(record.get("CapexTerminalDrive"), 100000.0)
    mast = _clamp(2.2 + capex / 90000.0, 2.4, 5.0)
    power = _num(record.get("PowerDrawKw"), 40.0)
    tier = int(_num(record.get("Tier"), 1.0))
    hub = mast + 0.2

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # Mast, footing and the guys that stop a surface drive walking downhill. A taller
    # mast is a fatter mast: it is the same rope pulling on a longer lever.
    m.cylinder((0.0, mast * 0.5 + 0.2, 0.0), _clamp(0.13 + mast * 0.045, 0.18, 0.34),
               mast - 0.4, axis=1, segments=12, mat=METAL)
    m.box((0.0, 0.28, 0.0), (1.5, 0.56, 1.5), mat=METAL)
    for sx, sz in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        m.beam((sx * 0.18, mast * 0.62, sz * 0.18), (sx * 1.15, 0.3, sz * 1.15), 0.13,
               mat=METAL)
        _pad(m, sx * 1.15, sz * 1.15, (0.7, 0.24, 0.7))

    # The bullwheel lies flat on top of the mast; the rope wraps it in plan.
    _bullwheel(m, (0.0, hub, 0.0), radius, grooves=1)
    m.cylinder((0.0, hub - 0.42, 0.0), radius * 0.28, 0.55, axis=1, segments=10,
               mat=METAL)
    # A guard ring round the rim: a surface lift's wheel is at head height over a queue.
    m.tube((0.0, hub, 0.0), radius + 0.34, radius + 0.26, 0.55, axis=1, segments=14,
           mat=METAL)
    for i in range(4):
        a = math.pi * 0.5 * i + math.pi * 0.25
        m.beam((math.cos(a) * radius * 0.3, hub - 0.3, math.sin(a) * radius * 0.3),
               (math.cos(a) * (radius + 0.3), hub - 0.05, math.sin(a) * (radius + 0.3)),
               0.08, mat=METAL)

    # A maintenance deck under the wheel, reached by the ladder up the mast.
    deck_r = radius + 0.55
    m.box((0.0, hub - 0.75, 0.0), (deck_r * 2.0, 0.1, deck_r * 1.3), mat=METAL)
    _handrail(m, (-deck_r, hub - 0.7, -deck_r * 0.65), (deck_r, hub - 0.7, -deck_r * 0.65),
              height=1.0)
    _handrail(m, (-deck_r, hub - 0.7, deck_r * 0.65), (deck_r, hub - 0.7, deck_r * 0.65),
              height=1.0)
    _ladder(m, 0.0, -0.42, hub - 0.85, face=-1.0)

    # Rope catchers: the hooked arms that stop a derailed rope reaching the ground, and
    # the one part of a surface lift's head that reads from below.
    for sx in (-1.0, 1.0):
        m.beam((sx * (radius * 0.4), hub + 0.1, 0.0), (sx * (radius + 0.5), hub + 0.1, 0.0),
               0.1, mat=METAL)
        for sz in (-1.0, 1.0):
            m.beam((sx * (radius + 0.5), hub + 0.1, 0.0),
                   (sx * (radius + 0.35), hub - 0.35, sz * (radius * 0.75)), 0.09,
                   mat=METAL)
            m.box((sx * (radius + 0.32), hub - 0.42, sz * (radius * 0.8)),
                  (0.3, 0.14, 0.5), mat=METAL)

    # The boarding lane, the switchgear and the operator all sit at a fixed offset each
    # side of the mast, so the station stays balanced about the rope whichever end it is.
    lane = 0.55 + _num(record.get("SeatsOrCabinCapacity"), 1.0) * 0.28
    flank = max(radius + 1.5, lane + 1.3)
    _cabinet(m, (flank, 0.85, -1.2),
             (_clamp(0.7 + power / 260.0, 0.8, 1.6), 1.6,
              _clamp(0.5 + power / 400.0, 0.55, 1.1)))
    m.box((flank, 1.72, -1.2), (0.3, 0.18, 0.3), mat=METAL)

    if drive:
        # Gearbox under the wheel, motor beside it, and the switchgear on the ground.
        m.cylinder((0.0, hub - 1.25, 0.0), radius * 0.42, 0.9, axis=1, segments=12,
                   mat=METAL)
        m.box((0.0, hub - 1.35, -0.95), (0.75, 0.7, 1.2), mat=BODY)
        m.cylinder((0.0, hub - 1.35, -1.75), 0.3, 0.9, axis=2, segments=10, mat=METAL)

        # Boarding: a lane past the gates to the pick-up point beside the mast.
        for side in (-1.0, 1.0):
            _gate(m, "gate_L" if side < 0 else "gate_R",
                  (side * lane, 0.0, -radius - 1.5), -side * (lane - 0.3))
            _handrail(m, (side * (lane + 0.45), 0.0, -radius - 5.2),
                      (side * (lane + 0.45), 0.0, -radius - 1.7), posts_every=1.2)
        m.box((0.0, 0.035, -radius - 3.2), (lane * 2.0, 0.07, 3.2), mat=METAL)
    else:
        # The return end takes up the slack: an older lift on a counterweight, a newer
        # one on a ram, and either way the carriage has to be able to travel.
        for side in (-1.0, 1.0):
            m.box((side * (radius + 0.75), hub - 0.55, 0.0), (0.16, 0.26, 2.6),
                  mat=METAL)
        if tier >= 2:
            _ram(m, (0.0, hub - 0.42, 1.9), (0.0, hub - 0.42, 0.55), 0.09)
            m.box((0.0, hub - 0.42, 2.25), (0.6, 0.5, 0.5), mat=METAL)
        else:
            _counterweight(m, (0.0, hub - 0.5, 1.9), 1.5, weight=(0.7, 0.9, 0.7))
        # An unloading ramp: riders let go at the top and ski off to the side, past the
        # fence that keeps them out of the return wheel.
        ramp = [(0.0, radius + 3.4), (0.34, radius + 0.9), (0.34, radius + 0.2),
                (0.0, radius + 0.2)]
        m.prism(ramp, (0.0, 0.0, 0.0), 3.4, mat=METAL, axis=0)
        m.box((-(radius + 1.3), 0.6, -0.6), (0.5, 1.2, 0.5), mat=BODY)
        for side in (-1.0, 1.0):
            _handrail(m, (side * 1.9, 0.0, radius + 0.4), (side * 1.9, 0.0, radius + 3.6),
                      posts_every=1.1)

    if _staff(record, end) > 0:
        booth_z = -radius - 2.6 if drive else radius + 2.2
        _booth(m, (-flank, 0.0, booth_z), (1.3, 2.2, 1.6))
        m.box((-flank, 0.05, booth_z), (1.7, 0.1, 2.0), mat=METAL)

    m.socket("light_L", (-0.5, mast - 0.3, 0.0))
    m.socket("light_R", (0.5, mast - 0.3, 0.0))
    colliders = [("station_col", (0.0, mast * 0.5, 0.0),
                  (max(2.2, radius * 2.0), mast, max(2.2, radius * 2.0)))]
    return m, colliders


# --------------------------------------------------------------------------- fixed
def _build_fixed(record, end):
    """Fixed grip: a compact shed over one big bullwheel, short and tall.

    This is the read that has to survive next to a detachable. The shed is barely longer
    than the wheel it covers, it stands on four legs, and the roof is up where a
    twenty-metre housing's roof is - so from the bottom of the run one station is a hut
    and the other is a hangar.
    """
    drive = end == "drive"
    radius = _bullwheel_radius(record, "fixed")
    tier = int(_num(record.get("Tier"), 1.0))
    seats = _num(record.get("SeatsOrCabinCapacity"), 2.0)
    deck = _deck_height(record)
    rail_y = deck + _carrier_drop(record)
    rope_y = rail_y + 0.75
    half = radius + _SHELL_CLEAR_M * 0.75
    z_back = -(radius + 1.35)
    z_front = radius + 1.5
    modern = tier >= 3
    eave = rope_y + 0.95
    roof_rise = _clamp(0.5 + seats * 0.06, 0.6, 1.2)

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # Four legs and the frame they carry. The frame is what the wheel bolts to.
    for sx in (-1.0, 1.0):
        for sz in (-1.0, 1.0):
            _column(m, sx * (half - 0.3), sz * (radius + 0.8), rope_y - 0.55,
                    thick=_clamp(0.22 + radius * 0.1, 0.26, 0.42))
    m.box((0.0, rope_y - 0.62, 0.0), (half * 2.0 - 0.3, 0.34, (z_front - z_back) * 0.82),
          mat=METAL)
    for sx in (-1.0, 1.0):
        m.beam((sx * (half - 0.3), rope_y - 0.8, z_back + 0.6),
               (sx * (half - 0.3), rope_y - 0.8, z_front - 0.6), 0.22, mat=METAL)

    _bullwheel(m, (0.0, rope_y, 0.0), radius)

    # Walls and roof. A tier-1 shed is a cladding box with a gable; from tier 3 the
    # walls carry a glazed band and the roof is an arc.
    for sx in (-1.0, 1.0):
        wall = [(rope_y - 0.5, z_back), (eave, z_back), (eave, z_front),
                (rope_y - 0.5, z_front)]
        m.prism(wall, (sx * half, 0.0, 0.0), 0.12, mat=BODY, axis=0)
        if modern:
            m.box((sx * (half - 0.02), eave - 0.42, 0.0),
                  (0.06, 0.52, (z_front - z_back) * 0.7), mat=GLASS)
    m.prism([(rope_y - 0.5, z_back), (eave, z_back), (eave, z_back + 0.12),
             (rope_y - 0.5, z_back + 0.12)], (0.0, 0.0, 0.0), half * 2.0, mat=BODY,
            axis=0)
    # The line end is not open to the weather: it is clad down to a slot the ropes and
    # the grips run out through.
    slot, jamb = radius * 0.55, half - 0.05
    for sx in (-1.0, 1.0):
        m.box((sx * (slot + jamb) * 0.5, (rope_y - 0.5 + eave) * 0.5, z_front - 0.08),
              (jamb - slot, eave - rope_y + 0.5, 0.12), mat=BODY)
    m.box((0.0, eave - 0.3, z_front - 0.08), (jamb * 2.0, 0.46, 0.12), mat=BODY)
    _roof(m, z_back, z_front, half * 2.0, eave, roof_rise, curved=modern, gables=True)
    rib = []
    for sx in (-1.0, 1.0):
        rib += m.box((sx * (half + 0.07), (rope_y - 0.5 + eave) * 0.5, z_back + 0.5),
                     (0.12, eave - rope_y + 0.4, 0.15), mat=BODY, bevel=False)
    m.array(rib, int((z_front - z_back) / 1.1), (0.0, 0.0, 1.1))

    if drive:
        # Motor room hung off the back of the frame, gearbox under the wheel, and the
        # sheave train that takes the rope out of the wheel and on to the line.
        m.cylinder((0.0, rope_y - 1.0, 0.0), radius * 0.36, 1.1, axis=1, segments=12,
                   mat=METAL)
        m.box((0.0, rope_y - 1.25, z_back - 0.7), (half * 1.5, 1.5, 1.6), mat=BODY)
        m.cylinder((0.0, rope_y - 1.25, z_back - 1.6), 0.36, 1.2, axis=2, segments=12,
                   mat=METAL)
        _sheave_train(m, [(sx * radius, rope_y + 0.2, z_front - 0.3) for sx in (-1.0, 1.0)],
                      _clamp(radius * 0.3, 0.22, 0.45))
    else:
        # A bare wheel on a tensioning carriage: the rails it rides, what pushes it, and
        # the anchor that takes the reaction.
        for sx in (-1.0, 1.0):
            m.box((sx * (radius + 0.45), rope_y - 0.45, 0.0), (0.2, 0.3, radius * 2.4),
                  mat=METAL)
            m.box((sx * (radius + 0.45), rope_y - 0.2, -radius * 0.6), (0.3, 0.24, 0.5),
                  mat=METAL)
        if tier >= 3:
            _ram(m, (0.0, rope_y - 0.42, z_back - 0.2), (0.0, rope_y - 0.42, -radius * 0.4),
                 _clamp(radius * 0.09, 0.08, 0.14))
            m.box((0.0, rope_y - 0.42, z_back - 0.55), (0.8, 0.7, 0.6), mat=METAL)
        else:
            _counterweight(m, (0.0, rope_y - 0.5, z_back - 0.9), rope_y * 0.45,
                           weight=(1.0, 1.3, 1.0))
        _sheave_train(m, [(sx * radius, rope_y + 0.18, z_front - 0.4) for sx in (-1.0, 1.0)],
                      _clamp(radius * 0.28, 0.2, 0.4))

    # Every end has a control cabinet on the machinery side; it is the panel the
    # operator stops the lift from, so it is not optional plant.
    _cabinet(m, (half + 0.9, 0.9, z_back + 0.4), (0.6, 1.7, 0.9))

    # Boarding. Riders come up the outgoing strand from behind the station, through the
    # gates, and the carrier picks them up as it leaves the wheel.
    lane = _clamp(0.5 + seats * 0.26, 0.7, 1.8)
    load_x = radius
    board = _board_length(record, compressed=False)
    z_load = z_back - 0.3 - board * 0.5
    m.box((load_x, deck * 0.5, z_load), (lane * 2.2, deck, board), mat=METAL)
    m.prism([(0.0, z_load - board * 0.5 - 0.9), (deck, z_load - board * 0.5 - 0.1),
             (deck, z_load - board * 0.5), (0.0, z_load - board * 0.5)],
            (load_x, 0.0, 0.0), lane * 2.2, mat=METAL, axis=0)
    for side in (-1.0, 1.0):
        _gate(m, "gate_L" if side < 0 else "gate_R",
              (load_x + side * lane, deck, z_load - board * 0.3), -side * (lane - 0.22))
    _maze(m, load_x, deck, z_load - board * 0.5 - 3.4, z_load - board * 0.4, record)
    if _staff(record, end) > 0:
        _booth(m, (-(half + 0.95), deck, z_back - 1.6), (1.4, 2.3, 1.7))
        m.box((-(half + 0.95), deck * 0.5, z_back - 1.6), (1.7, deck, 2.0), mat=METAL)

    m.socket("light_L", (-half, eave - 0.2, z_front - 0.4))
    m.socket("light_R", (half, eave - 0.2, z_front - 0.4))
    colliders = [("station_col", (0.0, (eave + roof_rise) * 0.5, (z_back + z_front) * 0.5),
                  (half * 2.2, eave + roof_rise, z_front - z_back))]
    return m, colliders


# --------------------------------------------------------------------------- detachable
def _build_detachable(record, end):
    """Detachable and 3S: a long housing with a decel rail, a contour and an accel rail.

    The length is the whole point and it is computed, not chosen: a carrier arriving at
    LineSpeedMs has to be down to LoadingSpeedMs by the time it reaches the contour, and
    at the comfort limit above that takes (v^2 - u^2) / 2a metres. A five-metre-a-second
    quad needs thirteen of them; a seven-metre-a-second funitel needs nearly thirty, and
    its station is half as long again as the chairlift's next door.
    """
    drive = end == "drive"
    tri = str(record.get("RopeConfiguration")) == "Tri"
    radius = _bullwheel_radius(record, "detachable") * (1.25 if tri else 1.0)
    line = max(1.0, _num(record.get("LineSpeedMs"), 5.0))
    load = _clamp(_num(record.get("LoadingSpeedMs"), 1.0), 0.25, line * 0.5)
    tier = int(_num(record.get("Tier"), 3.0))
    seats = _num(record.get("SeatsOrCabinCapacity"), 4.0)
    deck = _deck_height(record)
    rail_y = deck + _carrier_drop(record) + (3.6 if tri else 0.0)
    rope_y = rail_y + 0.8
    decel = _clamp((line * line - load * load) / (2.0 * _DECEL_MS2), 6.0, 30.0)
    accel = decel * 0.94
    half = radius + _SHELL_CLEAR_M
    z_bw = 0.0
    z_line = max(decel, accel) + radius * 0.6
    z_back = -(radius + 1.8)
    shell_lo = rail_y + 0.32
    shell_hi = shell_lo + _clamp(2.1 + seats * 0.04, 2.2, 3.1)
    garage = _num(record.get("CabinBarnCapex"), 0.0) > 0.0

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # The housing: a lofted shell that holds its section the length of the rails and
    # draws down to a nose at the line end, which is what a clad terminal cover does and
    # what a stack of boxes cannot. Over the contour the cowl stops short so the
    # bullwheel shows under it - a wheel nobody can see is a wheel the game spins for
    # nothing.
    nose = half * 0.72
    cowl = rope_y + 0.22
    sections = [
        _shell_ring(half * 0.95, cowl, shell_hi - 0.12, z_back),
        _shell_ring(half, cowl, shell_hi, z_back + radius * 0.7),
        _shell_ring(half, shell_lo, shell_hi, z_bw + radius * 0.9),
        _shell_ring(half, shell_lo, shell_hi, z_line - 2.4),
        _shell_ring(nose, shell_lo + 0.2, shell_hi - 0.35, z_line),
        _shell_ring(nose * 0.9, shell_lo + 0.35, shell_hi - 0.55, z_line + 0.7),
    ]
    m.loft(sections, mat=BODY)
    # The slot in the nose the ropes and the grips run out through, and the skirt rail
    # along the bottom edge that stops the flank reading as one blank panel.
    m.box((0.0, shell_lo + 0.5, z_line + 0.74), (nose * 1.3, 0.5, 0.12), mat=METAL)
    for sx in (-1.0, 1.0):
        m.beam((sx * (half - 0.05), shell_lo + 0.12, z_back + radius * 0.7),
               (sx * (half - 0.05), shell_lo + 0.12, z_line - 1.6), 0.26, mat=METAL)
    if tier >= 4:
        # A modern station wears a glazed band down the flank; an older one is cladding.
        for sx in (-1.0, 1.0):
            m.box((sx * half, shell_hi - 0.55, z_line * 0.3),
                  (0.07, 0.5, z_line * 0.9), mat=GLASS)

    # Legs. They splay outward because the shell is wider than the footing pattern.
    leg_z = [z_back + 0.6, z_bw + radius * 0.8, z_line * 0.62, z_line - 0.3]
    for z in leg_z:
        for sx in (-1.0, 1.0):
            m.beam((sx * (half - 0.45), shell_lo + 0.1, z),
                   (sx * (half + 0.25), 0.25, z), _clamp(radius * 0.16, 0.26, 0.46),
                   mat=METAL)
            _pad(m, sx * (half + 0.25), z, (1.0, 0.3, 1.0))
    for sx in (-1.0, 1.0):
        m.beam((sx * (half + 0.2), 1.3, leg_z[0]), (sx * (half + 0.2), 1.3, leg_z[-1]),
               0.16, mat=METAL)

    # Running gear under the shell: the rails, the grip rail and the tyre banks. The
    # incoming strand decelerates, the outgoing strand accelerates.
    for sx in (-1.0, 1.0):
        _rail(m, sx * radius, rail_y, z_bw - radius * 0.2, z_line + 0.4)
        run = decel if sx < 0 else accel
        _tyre_bank(m, sx * radius, rail_y + 0.34, z_bw + radius * 0.5, run + radius * 0.5)
    # The contour: the carriers curve round the wheel on a bent rail with its own tyres.
    curve = []
    steps = 8
    for i in range(steps + 1):
        a = math.pi * (0.5 + i / steps)
        curve.append((math.cos(a) * radius, rail_y, math.sin(a) * radius + z_bw))
    for i in range(steps):
        m.beam(curve[i], curve[i + 1], 0.16, mat=METAL)
        if i % 2 == 0:
            m.cylinder(((curve[i][0] + curve[i + 1][0]) * 0.5, rail_y + 0.34,
                        (curve[i][2] + curve[i + 1][2]) * 0.5), 0.15, 0.085, axis=1,
                       segments=8, mat=METAL)

    _bullwheel(m, (0.0, rope_y, z_bw), radius,
               grooves=2 if str(record.get("RopeConfiguration")) == "Funitel" else 1)

    # Boarding deck under the shell on the outgoing side, with the gates at its back.
    lane = _clamp(0.55 + seats * 0.22, 0.8, 2.4)
    load_x = radius
    board = _board_length(record, compressed=True)
    m.box((load_x, deck * 0.5, z_bw + board * 0.5 - 1.0), (lane * 2.2, deck, board),
          mat=METAL)
    m.prism([(0.0, z_bw - 2.4), (deck, z_bw - 1.5), (deck, z_bw - 1.3), (0.0, z_bw - 1.3)],
            (load_x, 0.0, 0.0), lane * 2.2, mat=METAL, axis=0)
    for side in (-1.0, 1.0):
        _gate(m, "gate_L" if side < 0 else "gate_R",
              (load_x + side * lane, deck, z_bw - 0.9), -side * (lane - 0.25))
    _maze(m, load_x, deck, z_bw - 4.6, z_bw - 1.1, record)
    if _staff(record, end) > 0:
        _booth(m, (-(half + 1.0), deck, z_bw + 1.4), (1.5, 2.4, 1.8))
        m.box((-(half + 1.0), deck * 0.5, z_bw + 1.4), (1.8, deck, 2.1), mat=METAL)

    if drive:
        # Motor room at the back, gearbox on the wheel shaft, sheaves at the nose.
        m.box((0.0, shell_lo - 1.35, z_back - 0.9), (half * 1.5, 2.5, 2.4), mat=BODY)
        m.cylinder((0.0, rope_y - 1.3, z_bw), radius * 0.3, 1.5, axis=1, segments=14,
                   mat=METAL)
        m.cylinder((0.0, shell_lo - 1.35, z_back - 2.1), 0.42, 1.5, axis=2, segments=12,
                   mat=METAL)
        _ladder(m, -(half + 0.3), z_back - 0.9, shell_lo - 0.4)
        _cabinet(m, (half + 1.5, 0.95, z_back + 0.3), (0.7, 1.8, 1.0))
    else:
        # Tensioning carriage: the whole wheel assembly rides back on rails, pushed by a
        # ram on the newer tiers and hung on a counterweight on the older ones.
        for sx in (-1.0, 1.0):
            m.box((sx * (radius + 0.55), rope_y - 0.6, z_bw - radius * 0.3),
                  (0.26, 0.34, radius * 3.0), mat=METAL)
        if tier >= 4:
            _ram(m, (0.0, rope_y - 0.55, z_back - 0.4), (0.0, rope_y - 0.55, -radius * 0.5),
                 _clamp(radius * 0.1, 0.1, 0.18))
            m.box((0.0, rope_y - 0.55, z_back - 0.8), (1.1, 0.9, 0.7), mat=METAL)
        else:
            _counterweight(m, (0.0, rope_y - 0.6, z_back - 1.2), rope_y * 0.5,
                           weight=(1.3, 1.6, 1.3))
    _sheave_train(m, [(sx * radius, rope_y + 0.3, z_line + 0.5) for sx in (-1.0, 1.0)],
                  _clamp(radius * 0.22, 0.25, 0.5))

    # A bicable or a tricable carries track ropes, and a track rope does not go round
    # anything: it dead-ends into its own anchorage beside the haul rope machinery. On a
    # tricable there are two of them a side and they are massive, and that separation is
    # the whole silhouette of a 3S.
    ropes = _track_ropes(record)
    bulk = 1.0 if tri else 0.72
    for sx in (-1.0, 1.0):
        for i in range(ropes):
            ax = sx * (half + 1.6 * bulk + i * 3.6)
            head = rope_y + 2.3 * bulk - i * 0.9
            m.box((ax, 1.05 * bulk, z_back - 0.6),
                  (2.2 * bulk, 2.1 * bulk, 3.0 * bulk), mat=METAL)
            m.beam((ax, 2.0 * bulk, z_back - 0.6), (ax, head, z_bw + 1.2), 0.55 * bulk,
                   mat=METAL)
            m.beam((ax, 2.0 * bulk, z_back + 1.6), (ax, head - 0.4, z_bw + 1.0),
                   0.3 * bulk, mat=METAL)
            m.cylinder((ax, head, z_bw + 2.0), 0.42 * bulk, 1.6, axis=2, segments=10,
                       mat=METAL, radius_end=0.16)
            m.cylinder((ax, head, z_bw + 3.4), 0.14, 1.4, axis=2, segments=8, mat=METAL)
            if not drive:
                _counterweight(m, (ax, head - 0.6, z_back - 2.4), head * 0.45,
                               weight=(1.6 * bulk, 2.0 * bulk, 1.6 * bulk))
    if garage and drive:
        # The garage spur leaves the contour at the back of the station and runs out to
        # the side, where the barn stands.
        spur = []
        for i in range(6):
            t = i / 5.0
            spur.append((_lerp(-radius * 0.2, half + 2.4, t * t),
                         rail_y, _lerp(z_bw - radius * 0.9, z_back - 3.2, t)))
        for i in range(len(spur) - 1):
            m.beam(spur[i], spur[i + 1], 0.17, mat=METAL)
        for point in (spur[3], spur[-1]):
            m.beam((point[0], 0.3, point[2]), (point[0], rail_y + 0.95, point[2]), 0.28,
                   mat=METAL)
            _pad(m, point[0], point[2], (1.0, 0.3, 1.0))
        mid_x = (spur[3][0] + spur[-1][0]) * 0.5
        mid_z = (spur[3][2] + spur[-1][2]) * 0.5
        m.box((mid_x, rail_y + 1.05, mid_z), (3.0, 0.14, abs(spur[-1][2] - spur[3][2]) + 1.6),
              mat=BODY)

    m.socket("light_L", (-half, shell_lo - 0.2, z_line - 0.6))
    m.socket("light_R", (half, shell_lo - 0.2, z_line - 0.6))
    colliders = [("station_col", (0.0, (shell_lo + shell_hi) * 0.5,
                                  (z_back + z_line) * 0.5),
                  (half * 2.2, shell_hi - shell_lo, z_line - z_back))]
    return m, colliders


# --------------------------------------------------------------------------- tram
def _build_tram(record, end):
    """Aerial tram: a station house, not a terminal - nothing runs through it.

    A reversible tram parks its carriage in the house, opens the doors onto a platform
    sized for the load it carries, and sends it back. The track ropes never move: they
    dead-end into anchor blocks at the back of the house, and on the return end they
    hang off tensioning weights, which is why one station of a tram is fatter than the
    other.
    """
    drive = end == "drive"
    capacity = _num(record.get("SeatsOrCabinCapacity"), 60.0)
    radius = _bullwheel_radius(record, "tram")
    height = _clamp(9.0 + capacity * 0.045, 11.0, 17.0)
    platform_y = _clamp(2.6 + capacity * 0.012, 2.8, 4.4)
    area = capacity * 0.55
    width = _clamp(math.sqrt(area * 1.3), 6.0, 11.0)
    depth = _clamp(area / width, 5.5, 12.0)
    half = width * 0.5 + 1.2
    z_back = -(depth * 0.5 + 2.6)
    z_front = depth * 0.5 + 1.4

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # Shell: two side walls, a back wall and a roof. The line end stays open, because
    # that is the portal the carriage swings in through.
    for sx in (-1.0, 1.0):
        m.prism([(0.0, z_back), (height, z_back), (height, z_front), (0.0, z_front)],
                (sx * half, 0.0, 0.0), 0.3, mat=BODY, axis=0)
        m.box((sx * (half + 0.1), height * 0.68, (z_back + z_front) * 0.5),
              (0.14, height * 0.2, depth * 0.7), mat=GLASS)
        m.box((sx * (half + 0.1), platform_y + 1.6, (z_back + z_front) * 0.5),
              (0.14, 1.5, depth * 0.55), mat=GLASS)
    m.prism([(0.0, z_back), (height, z_back), (height, z_back + 0.3), (0.0, z_back + 0.3)],
            (0.0, 0.0, 0.0), half * 2.0, mat=BODY, axis=0)
    _roof(m, z_back, z_front, half * 2.0, height, _clamp(height * 0.1, 0.9, 1.8),
          thickness=0.24, curved=True, gables=True)
    # The portal at the line end: a header and two jambs framing the opening.
    m.prism([(height * 0.72, z_front), (height, z_front), (height, z_front - 0.3),
             (height * 0.72, z_front - 0.3)], (0.0, 0.0, 0.0), half * 2.0, mat=BODY,
            axis=0)
    for sx in (-1.0, 1.0):
        m.box((sx * (half - 0.55), height * 0.36, z_front - 0.15),
              (0.7, height * 0.72, 0.4), mat=BODY)

    # Mullions down the glazed band, and the purlins across the roof they hang from.
    mullion = []
    for sx in (-1.0, 1.0):
        mullion += m.box((sx * (half + 0.19), height * 0.68, z_back + 1.0),
                         (0.2, height * 0.24, 0.16), mat=BODY, bevel=False)
        mullion += m.box((sx * (half + 0.19), height * 0.32, z_back + 1.0),
                         (0.2, height * 0.5, 0.16), mat=BODY, bevel=False)
    m.array(mullion, max(3, int(depth / 1.6)), (0.0, 0.0, 1.6))
    purlin = m.box((0.0, height - 0.18, z_back + 1.2), (half * 2.0, 0.16, 0.2),
                   mat=METAL, bevel=False)
    m.array(purlin, max(3, int((z_front - z_back) / 2.0)), (0.0, 0.0, 2.0))
    m.box((-half * 0.55, 1.25, z_back + 0.2), (1.3, 2.5, 0.18), mat=BODY)
    m.box((-half * 0.55, 2.75, z_back - 0.4), (2.0, 0.14, 1.2), mat=BODY)

    # Docking platform, its edge, the stairs down and the rails round it.
    m.box((0.0, platform_y - 0.12, (z_back + z_front) * 0.5 + 0.4),
          (width, 0.24, depth), mat=METAL)
    m.box((0.0, platform_y - 0.02, z_front - 0.6), (width * 0.96, 0.08, 0.3), mat=BODY)
    for sx in (-1.0, 1.0):
        for z in (z_back + 0.9, (z_back + z_front) * 0.5, z_front - 1.2):
            _column(m, sx * (width * 0.5 - 0.4), z, platform_y - 0.2, thick=0.3)
    _handrail(m, (-width * 0.5, platform_y, z_back + 0.6),
              (width * 0.5, platform_y, z_back + 0.6), posts_every=1.5)
    _handrail(m, (-width * 0.5, platform_y, z_front - 0.4),
              (-width * 0.3, platform_y, z_front - 0.4), posts_every=1.2)
    _handrail(m, (width * 0.3, platform_y, z_front - 0.4),
              (width * 0.5, platform_y, z_front - 0.4), posts_every=1.2)
    # The dock barrier across the front of the platform, which is this family's gate.
    for side in (-1.0, 1.0):
        _gate(m, "gate_L" if side < 0 else "gate_R",
              (side * width * 0.29, platform_y, z_front - 0.4), -side * width * 0.26)
    _stair(m, -width * 0.25, z_back + 0.4, z_back - 2.4, platform_y, width=1.6)

    # The machine floor is a real floor with a walkway round the sheave on it, because
    # a tram's plant is up in the roof where the rope comes in, not down at the deck.
    floor_y = height - 4.6
    m.box((0.0, floor_y, (z_back + z_front) * 0.5), (width * 0.9, 0.2, depth * 0.6),
          mat=METAL)
    _handrail(m, (-width * 0.45, floor_y + 0.1, z_back + 1.4),
              (width * 0.45, floor_y + 0.1, z_back + 1.4), posts_every=1.6)
    _ladder(m, width * 0.36, z_back + 1.0, floor_y)
    for sx in (-1.0, 1.0):
        m.beam((sx * (half - 0.2), floor_y, z_back + 0.6),
               (sx * (half - 0.2), floor_y, z_front - 1.0), 0.24, mat=METAL)

    # Drive sheave in the machine floor at the top of the house. A tram's haul rope
    # comes up to the machine floor and turns in plan, so the wheel lies flat.
    sheave_y = height - 2.2
    _bullwheel(m, (0.0, sheave_y, z_back + radius + 0.9), radius)
    m.box((0.0, sheave_y - 1.1, (z_back + z_front) * 0.4),
          (width * 0.8, 0.3, depth * 0.55), mat=METAL)
    if drive:
        m.box((0.0, sheave_y - 1.5, z_back + 1.2), (width * 0.5, 1.6, 2.0), mat=BODY)
        m.cylinder((0.0, sheave_y - 1.5, z_back + 2.6), 0.55, 1.8, axis=2, segments=12,
                   mat=METAL)
        m.cylinder((0.0, sheave_y - 0.9, z_back + radius + 0.9), radius * 0.3, 0.9,
                   axis=1, segments=12, mat=METAL)
    _sheave_train(m, [(sx * radius * 0.8, sheave_y - 0.1, z_front - 1.0)
                      for sx in (-1.0, 1.0)], _clamp(radius * 0.3, 0.3, 0.6))

    # Track rope anchorages: blocks at the back with the dead-end sockets splayed out of
    # them, and on the return end the weights that keep the ropes tight.
    for sx in (-1.0, 1.0):
        ax = sx * (half + 1.6)
        m.box((ax, 1.3, z_back - 1.2), (2.4, 2.6, 3.4), mat=METAL)
        socket_y = height * 0.62
        m.beam((ax, 2.4, z_back - 1.2), (ax, socket_y, z_back + 2.2), 0.5, mat=METAL)
        m.cylinder((ax, socket_y, z_back + 3.0), 0.44, 1.5, axis=2, segments=10,
                   mat=METAL, radius_end=0.17)
        m.cylinder((ax, socket_y, z_back + 4.4), 0.15, 1.6, axis=2, segments=8,
                   mat=METAL)
        if not drive:
            _counterweight(m, (ax, socket_y - 0.8, z_back - 3.4), socket_y * 0.5,
                           weight=(1.8, 2.2, 1.8))
        else:
            m.box((ax, 3.1, z_back - 1.2), (2.0, 1.0, 2.8), mat=METAL)

    if _staff(record, end) > 0:
        _booth(m, (width * 0.5 - 1.3, platform_y, z_back + 1.6), (1.8, 2.5, 2.0))

    m.socket("light_L", (-half + 0.4, height - 0.6, z_front - 0.8))
    m.socket("light_R", (half - 0.4, height - 0.6, z_front - 0.8))
    colliders = [("station_col", (0.0, height * 0.5, (z_back + z_front) * 0.5),
                  (half * 2.0, height, z_front - z_back))]
    return m, colliders


# --------------------------------------------------------------------------- rail
def _build_rail(record, end):
    """Funicular, cog railway and inclined elevator: a platform, a canopy and a rail stub.

    The drive end carries the haulage plant; a rack railway has its drive in the vehicle
    instead, so its plant room is a service room with a capstan in it rather than a hall
    with a drum. The contract wants a `bullwheel` on every terminal, and on a funicular
    that is honest: the haul rope turns in plan around the driving sheave before it goes
    to the drum.
    """
    drive = end == "drive"
    capacity = _num(record.get("SeatsOrCabinCapacity"), 60.0)
    radius = _bullwheel_radius(record, "rail") * (1.0 if _num(
        record.get("CapexTerminalDrive"), 0.0) > 1000000.0 else 0.6)
    rack = str(record.get("RopeConfiguration")) == "Rack"
    length = _clamp(8.0 + capacity * 0.09, 10.0, 26.0)
    platform_y = _clamp(0.7 + capacity * 0.003, 0.75, 1.1)
    gauge = _clamp(1.0 + capacity * 0.006, 1.1, 1.9)
    width = gauge + 3.6
    z_back = -(length * 0.5 + 2.0)
    z_front = length * 0.5
    canopy_y = platform_y + 3.2

    m = mk.Model("%s_terminal_%s" % (record["Id"], end), budget_key="lift_terminal",
                 seed=_seed(record, end))

    # Platform deck beside the track, with its edge strip and the ramp down to the snow.
    deck_x = gauge * 0.5 + 1.5
    m.box((deck_x, platform_y * 0.5, (z_back + z_front) * 0.5),
          (2.8, platform_y, length), mat=METAL)
    m.box((deck_x - 1.36, platform_y - 0.03, (z_back + z_front) * 0.5),
          (0.22, 0.08, length * 0.98), mat=BODY)
    m.prism([(0.0, z_back - 3.0), (platform_y, z_back - 0.6), (platform_y, z_back),
             (0.0, z_back)], (deck_x, 0.0, 0.0), 2.8, mat=METAL, axis=0)

    # Track: rails on sleepers, and the rack bar down the middle where there is one.
    sleeper = m.box((0.0, 0.09, z_back + 0.5), (gauge + 0.9, 0.18, 0.24), mat=METAL,
                    bevel=False)
    sleepers = int((z_front + 6.0 - z_back) / 0.75)
    m.array(sleeper, sleepers, (0.0, 0.0, 0.75))
    for sx in (-1.0, 1.0):
        m.box((sx * gauge * 0.5, 0.24, (z_back + z_front + 6.0) * 0.5),
              (0.12, 0.14, z_front + 6.0 - z_back), mat=METAL)
    if rack:
        tooth = m.box((0.0, 0.3, z_back + 0.6), (0.22, 0.12, 0.1), mat=METAL,
                      bevel=False)
        m.array(tooth, int((z_front + 6.0 - z_back) / 0.24), (0.0, 0.0, 0.24))
        m.box((0.0, 0.22, (z_back + z_front + 6.0) * 0.5),
              (0.26, 0.1, z_front + 6.0 - z_back), mat=METAL)
    m.box((0.0, 0.55, z_front + 5.7), (gauge + 0.6, 0.8, 0.5), mat=BODY)

    # Canopy over the platform on its row of columns, one every few metres so the slab
    # reads as carried rather than as a plane hanging in the air.
    bays = max(2, int(length / 4.5))
    for i in range(bays + 1):
        z = z_back + 1.2 + (length - 0.4) * i / bays
        _column(m, deck_x + 1.1, z, canopy_y, thick=0.26, foot=False)
        m.beam((deck_x + 1.1, canopy_y, z), (-gauge * 0.5 - 0.3, canopy_y + 0.5, z),
               0.2, mat=METAL)
    for x, y in ((-gauge * 0.5 - 1.05, canopy_y + 0.5), (deck_x + 1.45, canopy_y + 0.83)):
        m.beam((x, y, z_back - 0.2), (x, y, z_front + 0.2), 0.22, mat=BODY)
    # A station canopy is a one-way fall away from the platform, not a gable.
    canopy = [(-gauge * 0.5 - 1.1, canopy_y + 0.62), (deck_x + 1.5, canopy_y + 0.95),
              (deck_x + 1.5, canopy_y + 0.82), (-gauge * 0.5 - 1.1, canopy_y + 0.49)]
    m.prism(canopy, (0.0, 0.0, (z_back + z_front) * 0.5), z_front - z_back + 0.6,
            mat=BODY, axis=2)
    _handrail(m, (deck_x + 1.4, platform_y, z_back + 0.6),
              (deck_x + 1.4, platform_y, z_front - 0.6), posts_every=2.0)
    # Ticket barrier where the ramp reaches the platform.
    for side in (-1.0, 1.0):
        _gate(m, "gate_L" if side < 0 else "gate_R",
              (deck_x + side * 1.25, platform_y, z_back + 2.4), -side * 0.95)
    _handrail(m, (-gauge * 0.5 - 1.1, 0.0, z_back + 0.4),
              (-gauge * 0.5 - 1.1, 0.0, z_front + 2.0), posts_every=2.2)
    _shelter(m, (deck_x + 0.25, platform_y, (z_back + z_front) * 0.5 + 1.8),
             (2.2, 2.3, 1.5))

    # The tactile strip along the platform edge, and the signal at the mouth of the
    # station. Both are small, and both are what says "railway" rather than "shed".
    stud = m.box((deck_x - 1.36, platform_y + 0.04, z_back + 0.9), (0.17, 0.04, 0.17),
                 mat=BODY, bevel=False)
    m.array(stud, int(length / 0.7), (0.0, 0.0, 0.7))
    m.cylinder((-gauge * 0.5 - 0.7, 1.4, z_front + 3.2), 0.09, 2.8, axis=1, segments=8,
               mat=METAL)
    m.box((-gauge * 0.5 - 0.7, 3.0, z_front + 3.2), (0.3, 0.8, 0.26), mat=BODY)
    m.cylinder((-gauge * 0.5 - 0.7, 3.2, z_front + 3.34), 0.08, 0.06, axis=2,
               segments=8, mat=GLASS)

    # Plant. A haulage drum room at the drive end, a return sheave pit at the other.
    hall_z = z_back - 4.2
    hall_h = 4.6 if drive else 3.0
    m.box((0.0, hall_h * 0.5, hall_z), (width, hall_h, 5.2), mat=BODY)
    _roof(m, hall_z - 2.8, hall_z + 2.8, width, hall_h, 0.7, curved=False, gables=True)
    m.box((deck_x * 0.6, 1.1, hall_z + 2.6), (1.2, 2.2, 0.2), mat=GLASS)
    _bullwheel(m, (0.0, 1.5, hall_z), radius)
    if drive and not rack:
        m.cylinder((-width * 0.22, 1.8, hall_z - 1.3), radius * 0.95, 2.2, axis=0,
                   segments=14, mat=METAL)
        m.box((width * 0.26, 1.1, hall_z - 1.4), (1.6, 2.0, 1.8), mat=BODY)
        m.cylinder((width * 0.26, 1.1, hall_z - 2.5), 0.42, 1.0, axis=2, segments=10,
                   mat=METAL)
    elif drive:
        m.box((width * 0.24, 1.1, hall_z - 1.2), (1.5, 2.0, 2.0), mat=BODY)
        m.cylinder((-width * 0.24, 1.2, hall_z - 1.2), 0.5, 1.4, axis=0, segments=10,
                   mat=METAL)
    else:
        _ram(m, (0.0, 1.5, hall_z - 2.0), (0.0, 1.5, hall_z - 0.7), 0.12)
    _sheave_train(m, [(sx * gauge * 0.5, 0.6, z_back - 0.8) for sx in (-1.0, 1.0)],
                  _clamp(radius * 0.35, 0.22, 0.5))

    if _staff(record, end) > 0:
        _booth(m, (deck_x + 0.2, platform_y, z_front - 2.2), (1.6, 2.4, 1.8))
    m.box((deck_x - 0.4, canopy_y - 0.5, z_back + 2.6), (1.8, 0.6, 0.1), mat=BODY)
    m.box((deck_x + 0.5, platform_y + 1.1, z_back + 1.0), (0.1, 2.2, 0.1), mat=METAL)
    m.box((deck_x + 0.5, platform_y + 2.2, z_back + 1.0), (0.9, 0.5, 0.09), mat=BODY)

    m.socket("light_L", (deck_x + 1.2, canopy_y, z_back + 1.2))
    m.socket("light_R", (deck_x + 1.2, canopy_y, z_front - 1.2))
    colliders = [("station_col", (deck_x * 0.4, canopy_y * 0.5, (z_back + z_front) * 0.5),
                  (width, canopy_y, z_front - z_back))]
    return m, colliders


# --------------------------------------------------------------------------- barn
def _build_barn(record):
    """The cabin garage, for the lifts whose record pays for one.

    CabinBarnCapex is the cost of a steel shed, so it is also its floor area at what a
    shed costs per square metre up a mountain, and that area is what decides how many
    storage rails fit and how many carriers hang on each. The cabins themselves are not
    modelled here - lift_carriers owns the cabin, and the game parks real ones on these
    rails.
    """
    capex = _num(record.get("CabinBarnCapex"), 0.0)
    seats = _num(record.get("SeatsOrCabinCapacity"), 8.0)
    cabin_w = _clamp(0.75 + seats * 0.14, 1.1, 3.2)
    area = _clamp(capex / 2500.0, 70.0, 1900.0)
    width = _clamp(math.sqrt(area * 0.55), 7.0, 34.0)
    length = _clamp(area / width, 9.0, 60.0)
    rails = int(_clamp(width / (cabin_w + 1.3), 2.0, 8.0))
    stored = max(int(_num(record.get("CarriersPerHaulRope"), 1.0)),
                 rails * int(length / (cabin_w + 0.45)))
    rail_y = _clamp(cabin_w * 1.6 + 2.2, 4.0, 7.0)
    eave = rail_y + 1.4
    half_w, half_l = width * 0.5, length * 0.5
    spacing = width / (rails + 1)
    door_w = _clamp(cabin_w + 1.6, 2.6, 5.0)
    door_x = -half_w + spacing * (rails // 2 + 1)      # the rail the spur runs in on

    m = mk.Model("%s_barn" % record["Id"], budget_key="lift_terminal",
                 seed=_seed(record, "barn"))

    # Shed: slab, clad walls, portal frames and a roof. The line end has the door the
    # spur runs in through.
    m.box((0.0, 0.11, 0.0), (width + 0.8, 0.22, length + 0.8), mat=METAL)
    for sx in (-1.0, 1.0):
        m.prism([(0.2, -half_l), (eave, -half_l), (eave, half_l), (0.2, half_l)],
                (sx * half_w, 0.0, 0.0), 0.22, mat=BODY, axis=0)
        m.box((sx * half_w, eave - 0.9, 0.0), (0.08, 0.7, length * 0.8), mat=GLASS)
    m.prism([(0.2, -half_l), (eave, -half_l), (eave, -half_l + 0.22), (0.2, -half_l + 0.22)],
            (0.0, 0.0, 0.0), width, mat=BODY, axis=0)
    _roof(m, -half_l, half_l, width, eave, _clamp(width * 0.12, 0.9, 2.6), gables=True)
    # The spur end: a header over the opening, a wall panel either side of it, and the
    # shutter rolled up out of the way, because the door only shuts when the line does.
    m.prism([(rail_y + 0.6, half_l), (eave, half_l), (eave, half_l - 0.22),
             (rail_y + 0.6, half_l - 0.22)], (0.0, 0.0, 0.0), width, mat=BODY, axis=0)
    for edge in ((-half_w, door_x - door_w * 0.5), (door_x + door_w * 0.5, half_w)):
        panel = edge[1] - edge[0]
        if panel > 0.3:
            m.box(((edge[0] + edge[1]) * 0.5, (rail_y + 0.6) * 0.5, half_l - 0.11),
                  (panel, rail_y + 0.6, 0.22), mat=BODY)
    m.cylinder((door_x, rail_y + 0.95, half_l - 0.42), 0.3, door_w, axis=0, segments=10,
               mat=METAL)
    apron = m.box((door_x, rail_y + 0.62, half_l - 0.42), (door_w - 0.1, 0.2, 0.07),
                  mat=BODY, bevel=False)
    m.array(apron, 3, (0.0, -0.24, 0.0))
    if door_x - door_w * 0.5 + half_w > 1.8:
        m.box((-half_w + 0.9, 1.05, half_l - 0.14), (1.1, 2.1, 0.14), mat=BODY)

    # Portal frames across the shed, one per bay.
    bays = int(_clamp(length / 4.5, 2.0, 10.0))
    for i in range(bays + 1):
        z = -half_l + length * i / bays
        for sx in (-1.0, 1.0):
            m.beam((sx * (half_w - 0.14), 0.25, z), (sx * (half_w - 0.14), eave, z),
                   0.22, mat=METAL)
        m.beam((-half_w + 0.14, eave, z), (half_w + 0.0, eave, z), 0.2, mat=METAL)

    # Storage rails with their trolleys. One trolley is arrayed down each rail: the
    # carriers are the carrier model's job, the parking hardware is this one's. The
    # middle rail keeps going out through the door and becomes the spur from the
    # terminal, which is also why it is not a separate box butted onto the end of one.
    per_rail = max(2, int(length / (cabin_w + 0.45)))
    for i in range(rails):
        x = -half_w + spacing * (i + 1)
        z_out = half_l + 2.8 if i == rails // 2 else half_l - 0.3
        m.box((x, rail_y, (z_out - half_l + 0.3) * 0.5),
              (0.2, 0.16, z_out + half_l - 0.3), mat=METAL)
        m.box((x, rail_y + 0.24, 0.0), (0.09, 0.32, length - 0.6), mat=METAL)
        for z in (-half_l + 1.0, 0.0, half_l - 1.0):
            m.beam((x, rail_y + 0.4, z), (x, eave - 0.1, z), 0.1, mat=METAL)
        if i == rails // 2:
            m.beam((x, rail_y - 0.12, half_l + 2.3), (x, 0.3, half_l + 2.3), 0.32,
                   mat=METAL)
            _pad(m, x, half_l + 2.3, (1.1, 0.3, 1.1))
        trolley = m.box((x, rail_y - 0.28, -half_l + 0.9),
                        (0.26, 0.34, 0.4), mat=METAL, bevel=False)
        trolley += m.cylinder((x, rail_y - 0.62, -half_l + 0.9), 0.07, 0.2, axis=0,
                              segments=6, mat=METAL)
        m.array(trolley, per_rail, (0.0, 0.0, (length - 1.8) / max(1, per_rail - 1)))

    # Roof trusses over the bays, and the cladding ribs that stiffen the side walls.
    truss = []
    for sx in (-1.0, 1.0):
        truss += m.beam((sx * (half_w - 0.2), eave - 0.5, -half_l + 0.4),
                        (0.0, eave - 0.2, -half_l + 0.4), 0.12, mat=METAL, square=False,
                        segments=5)
        for k in range(3):
            t0, t1 = k / 3.0, (k + 1) / 3.0
            truss += m.beam((sx * (half_w - 0.2) * (1.0 - t0), eave - 0.1, -half_l + 0.4),
                            (sx * (half_w - 0.2) * (1.0 - t1),
                             eave - 0.5 + 0.3 * t1, -half_l + 0.4), 0.08, mat=METAL,
                            square=False, segments=4)
    m.array(truss, bays + 1, (0.0, 0.0, length / bays))
    rib = m.box((-half_w - 0.05, (eave + 0.2) * 0.5, -half_l + 0.5),
                (0.1, eave - 0.4, 0.16), mat=BODY, bevel=False)
    rib += m.box((half_w + 0.05, (eave + 0.2) * 0.5, -half_l + 0.5),
                 (0.1, eave - 0.4, 0.16), mat=BODY, bevel=False)
    m.array(rib, int(length / 2.2), (0.0, 0.0, 2.2))

    # Ridge lights and gutters. A barn this size is lit from the roof, not the walls.
    ridge = m.box((0.0, eave + _clamp(width * 0.12, 0.9, 2.6) - 0.1, -half_l + 1.6),
                  (width * 0.3, 0.1, 1.2), mat=GLASS, bevel=False)
    m.array(ridge, max(2, bays - 1), (0.0, 0.0, length / max(2, bays - 1) * 0.9))
    for sx in (-1.0, 1.0):
        m.box((sx * (half_w + 0.3), eave - 0.12, 0.0), (0.24, 0.2, length + 0.4),
              mat=METAL)

    # A maintenance bay along one wall with its walkway.
    m.box((-half_w + 1.4, 0.65, -half_l + 2.2), (2.4, 1.3, 4.0), mat=BODY)
    m.box((-half_w + 1.4, 1.34, -half_l + 2.2), (2.6, 0.12, 4.2), mat=METAL)
    _handrail(m, (-half_w + 2.7, 0.0, -half_l + 0.3), (-half_w + 2.7, 0.0, half_l - 0.3),
              posts_every=2.4)

    m.socket("light_L", (-half_w + 0.4, eave - 0.4, half_l - 0.6))
    m.socket("light_R", (half_w - 0.4, eave - 0.4, half_l - 0.6))
    colliders = [("barn_col", (0.0, eave * 0.5, 0.0), (width, eave, length))]
    return m, colliders, stored


# --------------------------------------------------------------------------- driver
def _seed(record, part):
    """Per-asset seed. Nothing here rolls dice yet, but the model carries one so that
    anything that later wants jitter gets it from the same deterministic source."""
    return datasrc.seed_for(record.get("Id"), "terminal", part)


_BUILDERS = {
    "belt": _build_belt,
    "surface_rope": _build_surface_rope,
    "fixed": _build_fixed,
    "detachable": _build_detachable,
    "tram": _build_tram,
    "rail": _build_rail,
}


def build(record, out_dir):
    """Both terminals of one lift, plus the cabin barn when the record pays for one.

    build_all hands this the lift's folder rather than a file, because a lift is a set
    of models: ModelRegistry looks inside the folder for tower, terminal_drive,
    terminal_return and carrier.
    """
    kind = _terminal_kind(record)
    builder = _BUILDERS[kind]
    records = []
    for end in ("drive", "return"):
        model, colliders = builder(record, end)
        extra = {
            "family": "lift_terminal",
            "kind": "lift",
            "displayName": "%s %s terminal" % (record.get("DisplayName") or record["Id"],
                                               end),
            "sourceId": record["Id"],
            "liftFamily": record.get("Family"),
            "terminalKind": kind,
            "terminalEnd": end,
        }
        records.append(export.emit(model, os.path.join(out_dir, "terminal_%s.fbx" % end),
                                   box_colliders=colliders, extra=extra))

    if _num(record.get("CabinBarnCapex"), 0.0) > 0.0:
        model, colliders, stored = _build_barn(record)
        extra = {
            "family": "lift_barn",
            "kind": "lift",
            "displayName": "%s cabin barn" % (record.get("DisplayName") or record["Id"]),
            "sourceId": record["Id"],
            "liftFamily": record.get("Family"),
            "storedCarriers": stored,
        }
        records.append(export.emit(model, os.path.join(out_dir, "barn.fbx"),
                                   box_colliders=colliders, extra=extra))
    return records
