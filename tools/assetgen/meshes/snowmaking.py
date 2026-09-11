"""Snowmaking plant: guns, stationary skids, and the network hardware behind them.

Every shape here comes out of the record. A gun is a fan gun because its Specs carry a
fan motor's worth of kilowatts and a lance because they do not; it stands on a towed
carriage because ChassisType says Towed and on a mast because it says Stationary; the
barrel grows with gunPowerKw, the mast with Visual.BoomLengthM, and a gun that is new
enough to carry its own weather head oscillates under its own drive. Nothing below names
a machine.

The network hardware - hydrants, pump houses, compressor houses - has no vehicles.json
record, so build_all reads stations.json instead. FlowLps sizes a manifold the way a pipe
is really sized, from a design velocity, and PowerKw sizes the building that has to hold
the motors, so a 200 L/s high-head plant is visibly a bigger building with fatter pipes
than a 60 L/s pump house.
"""
import math
import os

from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

SILHOUETTES = frozenset({"gun", "station"})

# A lance has no fan. Its gunPowerKw is the few hundred watts of a solenoid bank and a
# trace heater, where a fan gun's is the axial fan's motor, so the kilowatts separate the
# two families without anyone having to look at an id.
_FAN_MOTOR_KW = 5.0

# The tier at which a gun carries its own weather head and stages its own nozzles.
# Below it a valve crew walks the line; at and above it the head sweeps under its own
# oscillator drive and the carriage gains a valve box.
_AUTOMATION_TIER = 3

# BoomLengthM is the record's mast figure, but a lance and a fan gun spend it
# differently: a lance *is* the mast and carries its nozzle head at the very top, where a
# tower only has to lift a fan barrel clear of the piste and the groomers.
_LANCE_MAST_FACTOR = 1.7
_FAN_TOWER_FACTOR = 0.65

# Design velocities used to turn a flow rate into a pipe diameter. Water runs about
# 2.5 m/s in a snowmaking main and compressed air about 15 m/s in a plant manifold;
# both are the numbers a plant engineer sizes from.
_WATER_VELOCITY_MS = 2.5
_AIR_VELOCITY_MS = 15.0
_AIR_LINE_BAR = 8.0

# A hydrant marker has to be found after a metre and a half of machine-made snow and a
# storm on top of it, which is why the poles on a piste are as tall as they are.
_MARKER_POLE_M = 3.4


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


def _spec(record, key, default=0.0):
    return _num((record.get("Specs") or {}).get(key), default)


def _segments(radius):
    """Segment count for a revolved part, from how big it reads on screen.

    A 0.6 m fan shroud earns twice the sides of a 0.05 m nozzle, and the small stuff is
    where a procedural pipeline throws its budget away if nobody says no.
    """
    return int(_clamp(9.0 + radius * 20.0, 6.0, 24.0))


def _pipe_diameter(flow_m3s, velocity):
    """The diameter that carries a flow at a design velocity: A = Q / v, D = sqrt(4A/pi)."""
    if flow_m3s <= 0.0 or velocity <= 0.0:
        return 0.1
    return math.sqrt(4.0 * flow_m3s / (math.pi * velocity))


def _is_towed(record):
    return record.get("ChassisType") == "Towed"


def _is_lance(record):
    return _spec(record, "gunPowerKw", 0.0) < _FAN_MOTOR_KW


def _automated(record):
    return datasrc.tier_of(record) >= _AUTOMATION_TIER


def _station_kind(record):
    """What a stationary record actually is, read from what it does.

    The function is in the numbers: a plant with a refrigeration load and a snow density
    makes snow, a record with a storage volume is a tank, one with a pump rating pumps,
    and one that only moves air compresses.
    """
    specs = record.get("Specs") or {}
    if _num(specs.get("fuelStorageL")) > 0.0 or _num(record.get("TankL")) > 0.0:
        return "tank"
    if _num(specs.get("pumpLps")) > 0.0:
        return "pump"
    if _num(specs.get("gunPowerKw")) > 0.0:
        return "factory"
    if _num(specs.get("airM3PerMin")) > 0.0:
        return "compressor"
    return "skid"


# --------------------------------------------------------------------------- parts
def _bolt_ring(m, centre, radius, count, bolt_r, depth, axis=1, parent=None):
    """One flange bolt repeated around a circle. Anchor pads, pipe flanges, hub faces."""
    cx, cy, cz = centre
    for i in range(int(count)):
        a = 2.0 * math.pi * i / max(1, int(count))
        c, s = math.cos(a) * radius, math.sin(a) * radius
        if axis == 0:
            p = (cx, cy + c, cz + s)
        elif axis == 1:
            p = (cx + c, cy, cz + s)
        else:
            p = (cx + c, cy + s, cz)
        m.cylinder(p, bolt_r, depth, axis=axis, segments=5, mat=METAL, parent=parent)


def _nozzle_ring(m, centre, radius, count, nozzle_r, length, parent=None):
    """The nucleator ring: ONE nozzle repeated around the mouth, pointing down the barrel.

    Twenty individually modelled nozzles cost twenty times what one costs and read
    identically at 200 m, so the nub is built once and placed around the circle.
    """
    cx, cy, cz = centre
    for i in range(int(count)):
        a = 2.0 * math.pi * i / max(1, int(count))
        m.cylinder((cx + math.cos(a) * radius, cy + math.sin(a) * radius, cz),
                   nozzle_r, length, axis=2, segments=5, mat=METAL, parent=parent)
        m.cylinder((cx + math.cos(a) * radius, cy + math.sin(a) * radius,
                    cz + length * 0.42), nozzle_r * 1.5, length * 0.22, axis=2,
                   segments=5, mat=METAL, parent=parent)


def _louvre_panel(m, centre, width, height, count, face=2, parent=None, mat=METAL):
    """A weather louvre: a frame and one slat repeated down it.

    `face` is the axis the panel looks along - 2 for a radiator end, 0 for a side wall -
    which is all that changes between a compressor house wall and a skid's cooler.
    """
    cx, cy, cz = centre
    count = max(2, int(count))
    pitch = height / count
    slat = pitch * 0.78
    if face == 2:
        m.box((cx, cy, cz), (width + 0.06, height + 0.06, 0.05), mat=mat, parent=parent)
        first = m.box((cx, cy - height * 0.5 + pitch * 0.5, cz + 0.05),
                      (width, slat, 0.035), mat=mat, parent=parent, bevel=False,
                      rot=mk.unity_euler(35.0, 0.0, 0.0))
        m.array(first, count, (0.0, pitch, 0.0), parent=parent)
    else:
        m.box((cx, cy, cz), (0.05, height + 0.06, width + 0.06), mat=mat, parent=parent)
        first = m.box((cx + 0.05, cy - height * 0.5 + pitch * 0.5, cz),
                      (0.035, slat, width), mat=mat, parent=parent, bevel=False,
                      rot=mk.unity_euler(0.0, 0.0, 35.0))
        m.array(first, count, (0.0, pitch, 0.0), parent=parent)


def _lifting_lugs(m, half_x, half_z, y, parent=None):
    """Four corner lugs. Every skid on a mountain arrives under a helicopter or a crane."""
    for sx in (-1.0, 1.0):
        for sz in (-1.0, 1.0):
            x, z = sx * half_x, sz * half_z
            m.prism([(y - 0.02, z), (y + 0.20, z - sz * 0.05), (y + 0.20, z + sz * 0.13),
                     (y - 0.02, z + sz * 0.18)], (x, 0.0, 0.0), 0.05, mat=METAL,
                    parent=parent, axis=0)
            m.tube((x, y + 0.13, z + sz * 0.05), 0.045, 0.024, 0.055, axis=0,
                   segments=8, mat=METAL, parent=parent)


def _hose_reel(m, centre, radius, width, parent=None):
    """A hose drum on its bracket: two flanges, a coil, a crank and a standpipe."""
    cx, cy, cz = centre
    seg = _segments(radius)
    m.cylinder((cx, cy, cz), radius * 0.34, width * 1.02, axis=0, segments=seg,
               mat=METAL, parent=parent)
    for s in (-1.0, 1.0):
        m.cylinder((cx + s * width * 0.5, cy, cz), radius, 0.035, axis=0, segments=seg,
                   mat=BODY, parent=parent)
    m.tube((cx, cy, cz), radius * 0.87, radius * 0.41, width * 0.78, axis=0,
           segments=seg, mat=METAL, parent=parent)
    m.cylinder((cx + width * 0.62, cy, cz), 0.022, width * 0.22, axis=0, segments=6,
               mat=METAL, parent=parent)
    m.beam((cx + width * 0.72, cy, cz), (cx + width * 0.72, cy - radius * 0.7, cz),
           0.035, mat=METAL, parent=parent)
    for s in (-1.0, 1.0):
        m.box((cx + s * (width * 0.5 + 0.06), cy - radius * 0.55, cz),
              (0.05, radius * 1.1, 0.09), mat=METAL, parent=parent)


def _handwheel(m, centre, radius, axis=1, parent=None):
    """A valve handwheel: a rim, a hub and three spokes."""
    cx, cy, cz = centre
    m.tube(centre, radius, radius * 0.78, 0.035, axis=axis, segments=10, mat=METAL,
           parent=parent)
    m.cylinder(centre, radius * 0.2, 0.06, axis=axis, segments=6, mat=METAL, parent=parent)
    for i in range(3):
        a = 2.0 * math.pi * i / 3.0
        c, s = math.cos(a) * radius * 0.5, math.sin(a) * radius * 0.5
        if axis == 1:
            p, size = (cx + c, cy, cz + s), (radius * 0.9, 0.02, 0.03)
        else:
            p, size = (cx + c, cy + s, cz), (radius * 0.9, 0.03, 0.02)
        m.box(p, size, mat=METAL, parent=parent, bevel=False,
              rot=mk.unity_euler(0.0, -math.degrees(a) if axis == 1 else 0.0,
                                 math.degrees(a) if axis != 1 else 0.0))


def _coupler(m, centre, direction, radius, parent=None):
    """A quick coupler: a stub, a locking collar and a blanking cap on a chain."""
    cx, cy, cz = centre
    dx, dy, dz = direction
    axis = 0 if abs(dx) > 0.5 else (1 if abs(dy) > 0.5 else 2)
    step = (dx * 0.09, dy * 0.09, dz * 0.09)
    m.cylinder((cx + step[0], cy + step[1], cz + step[2]), radius, 0.18, axis=axis,
               segments=8, mat=METAL, parent=parent)
    m.cylinder((cx + step[0] * 1.8, cy + step[1] * 1.8, cz + step[2] * 1.8),
               radius * 1.35, 0.07, axis=axis, segments=8, mat=METAL, parent=parent)
    m.cylinder((cx + step[0] * 2.5, cy + step[1] * 2.5, cz + step[2] * 2.5),
               radius * 1.15, 0.05, axis=axis, segments=8, mat=BODY, parent=parent)


# --------------------------------------------------------------------------- undercarriages
def _tow_carriage(m, vis, mass):
    """The towable carriage under a mobile gun. Returns the deck height it offers.

    Two road wheels on a single axle, a jockey wheel under the drawbar and screw legs at
    the back: a gun is parked, levelled and left, not driven.
    """
    length = max(1.6, _num(vis.get("BodyL"), 2.2))
    width = max(1.0, _num(vis.get("BodyW"), 1.4))
    wheel_r = _clamp(_num(vis.get("WheelRadiusM"), 0.35), 0.22, 0.55)
    wheel_w = _clamp(0.14 + mass / 9000.0, 0.16, 0.32)
    rail = _clamp(0.10 + mass / 16000.0, 0.11, 0.20)
    rail_y = wheel_r + rail * 0.5 + 0.02
    deck_y = wheel_r + rail + 0.05

    for s in (-1.0, 1.0):
        m.box((s * (width * 0.5 - 0.15), rail_y, 0.0), (0.10, rail, length), mat=METAL)
    for z in (-length * 0.33, length * 0.33):
        m.box((0.0, rail_y - 0.01, z), (width - 0.36, rail * 0.68, 0.11), mat=METAL)
    m.box((0.0, deck_y - 0.03, -length * 0.06), (width - 0.34, 0.055, length * 0.52),
          mat=METAL)

    m.cylinder((0.0, wheel_r, 0.0), 0.055, width * 0.9, axis=0, segments=8, mat=METAL)
    hub_x = width * 0.5 - wheel_w * 0.5
    for s in (-1.0, 1.0):
        m.cylinder((s * hub_x, wheel_r, 0.0), wheel_r, wheel_w, axis=0, segments=14,
                   mat=METAL)
        m.cylinder((s * hub_x, wheel_r, 0.0), wheel_r * 0.44, wheel_w * 1.12, axis=0,
                   segments=10, mat=BODY)
        m.box((s * (width * 0.5 - 0.02), wheel_r + rail * 0.9, 0.0),
              (wheel_w * 1.2, 0.05, wheel_r * 1.9), mat=BODY)
        m.beam((s * (width * 0.5 - 0.15), rail_y, length * 0.42),
               (0.0, wheel_r * 0.75, 0.0), 0.06, mat=METAL)

    tip = -length * 0.5 - 0.72
    for s in (-1.0, 1.0):
        m.beam((s * (width * 0.5 - 0.15), rail_y, -length * 0.38),
               (0.0, wheel_r * 0.8, tip + 0.14), 0.075, mat=METAL)
    m.box((0.0, wheel_r * 0.8, tip + 0.1), (0.12, 0.09, 0.3), mat=METAL)
    m.tube((0.0, wheel_r * 0.8, tip), 0.085, 0.05, 0.05, axis=1, segments=10, mat=METAL)
    m.socket("hitch", (0.0, wheel_r * 0.8, tip))

    jockey = wheel_r * 0.32
    m.cylinder((0.0, wheel_r * 0.55, tip + 0.26), 0.04, wheel_r * 1.1, axis=1,
               segments=8, mat=METAL)
    m.cylinder((0.0, jockey, tip + 0.26), jockey, 0.07, axis=0, segments=10, mat=METAL)

    for s in (-1.0, 1.0):
        m.cylinder((s * (width * 0.5 - 0.15), rail_y * 0.5, length * 0.42), 0.035,
                   rail_y, axis=1, segments=6, mat=METAL)
        m.box((s * (width * 0.5 - 0.15), 0.025, length * 0.42), (0.16, 0.05, 0.16),
              mat=METAL, bevel=False)
    return deck_y


def _tower_mast(m, height):
    """A fixed gun's mast on its anchor pad. Returns the height of the bearing on top.

    Above about three metres the gun is out of reach from the snow, so the mast carries a
    service platform and a run of step bolts - which is also what makes a tower gun read
    as a tower gun and not a gun on a stick.
    """
    pad = 0.24
    m.box((0.0, pad * 0.5, 0.0), (1.30, pad, 1.30), mat=METAL)
    m.cylinder((0.0, pad + 0.03, 0.0), 0.32, 0.06, axis=1, segments=12, mat=METAL)
    _bolt_ring(m, (0.0, pad + 0.08, 0.0), 0.26, 6, 0.022, 0.10)

    top = pad + height
    m.cylinder((0.0, pad + height * 0.5, 0.0), 0.17, height, axis=1, segments=12,
               mat=BODY, radius_end=0.13)
    for k in range(4):
        m.prism([(pad + 0.04, 0.12), (pad + 0.04, 0.62), (pad + 0.66, 0.14)],
                (0.0, 0.0, 0.0), 0.05, mat=METAL, axis=0,
                rot=mk.unity_euler(0.0, 90.0 * k, 0.0))

    rungs = int(_clamp(height / 0.38, 3.0, 16.0))
    first = m.box((0.0, pad + 0.45, -0.21), (0.34, 0.035, 0.04), mat=METAL, bevel=False)
    m.array(first, rungs, (0.0, (height - 0.7) / max(1, rungs - 1), 0.0))

    if height >= 3.0:
        deck = top - 0.95
        m.tube((0.0, deck, 0.0), 0.78, 0.20, 0.05, axis=1, segments=12, mat=METAL)
        for k in range(4):
            a = math.pi * 0.5 * k + math.pi * 0.25
            c, s = math.cos(a) * 0.72, math.sin(a) * 0.72
            m.cylinder((c, deck + 0.5, s), 0.024, 1.0, axis=1, segments=6, mat=METAL)
        m.tube((0.0, deck + 0.98, 0.0), 0.75, 0.70, 0.04, axis=1, segments=12, mat=METAL)

    m.cylinder((0.0, top + 0.03, 0.0), 0.26, 0.07, axis=1, segments=12, mat=METAL)
    return top + 0.06


def _control_cabinet(m, centre, size, automated):
    """The base cabinet: contactors, and on an automated gun the valve station beside it."""
    cx, cy, cz = centre
    sx, sy, sz = size
    m.box(centre, size, mat=BODY)
    m.box((cx, cy + sy * 0.08, cz + sz * 0.52), (sx * 0.78, sy * 0.6, 0.03), mat=METAL,
          bevel=False)
    m.box((cx + sx * 0.3, cy + sy * 0.05, cz + sz * 0.56), (0.04, 0.11, 0.03),
          mat=METAL, bevel=False)
    if automated:
        m.box((cx, cy + sy * 0.34, cz + sz * 0.54), (sx * 0.45, sy * 0.2, 0.025),
              mat=GLASS, bevel=False)
        m.box((cx - sx * 0.85, cy - sy * 0.08, cz), (sx * 0.66, sy * 0.7, sz * 0.85),
              mat=BODY)
        m.cylinder((cx - sx * 0.85, cy + sy * 0.42, cz), 0.05, sy * 0.2, axis=1,
                   segments=6, mat=METAL)


def _weather_head(m, base, height):
    """The mast an automated gun watches the wet-bulb with: anemometer and screen."""
    bx, by, bz = base
    m.cylinder((bx, by + height * 0.5, bz), 0.035, height, axis=1, segments=6, mat=METAL)
    top = by + height
    m.cylinder((bx, top + 0.05, bz), 0.045, 0.12, axis=1, segments=6, mat=METAL)
    for i in range(3):
        a = 2.0 * math.pi * i / 3.0
        c, s = math.cos(a) * 0.13, math.sin(a) * 0.13
        m.box((bx + c * 0.55, top + 0.14, bz + s * 0.55), (0.14, 0.012, 0.012),
              mat=METAL, bevel=False,
              rot=mk.unity_euler(0.0, -math.degrees(a), 0.0))
        m.sphere((bx + c, top + 0.14, bz + s), 0.035, segments=6, rings=4, mat=BODY)
    plates = [(0.0, 0.0)]
    for k in range(4):
        plates += [(0.085, 0.012 + k * 0.05), (0.05, 0.038 + k * 0.05)]
    plates.append((0.0, 0.22))
    m.lathe(plates, (bx, top - 0.42, bz), segments=8, mat=BODY)
    m.box((bx + 0.1, top - 0.62, bz), (0.16, 0.12, 0.1), mat=BODY)


# --------------------------------------------------------------------------- fan gun
def _build_fan_gun(m, record, vis):
    """The signature snowmaking silhouette: a ducted fan on a yoke.

    The duct is sized off the fan motor. A ducted axial fan's absorbed power goes as the
    swept area times the cube of the air speed, so holding the speed and the pressure
    rise of a snow gun roughly fixed leaves the area - and with it the diameter -
    following the cube root of the kilowatts.
    """
    power = _spec(record, "gunPowerKw", 18.0)
    mass = _num(record.get("MassKg"), 800.0)
    tier = datasrc.tier_of(record)
    boom = _num(vis.get("BoomLengthM"), 3.5)
    auto = _automated(record)

    r_in = 0.46 * (power / 18.0) ** (1.0 / 3.0)
    wall = _clamp(r_in * 0.12, 0.04, 0.09)
    r_out = r_in + wall
    barrel = r_in * 3.3
    seg = _segments(r_out)

    if _is_towed(record):
        deck_y = _tow_carriage(m, vis, mass)
        yaw_y = deck_y + _clamp(0.60 + tier * 0.09, 0.60, 1.05)
        m.cylinder((0.0, (deck_y + yaw_y) * 0.5, 0.0), 0.15, yaw_y - deck_y, axis=1,
                   segments=10, mat=METAL)
        m.cylinder((0.0, deck_y + 0.05, 0.0), 0.34, 0.08, axis=1, segments=12, mat=METAL)
        _control_cabinet(m, (0.0, deck_y + 0.34, -_num(vis.get("BodyL"), 2.2) * 0.3),
                         (0.62, 0.62, 0.34), auto)
        if auto:
            _weather_head(m, (_num(vis.get("BodyW"), 1.4) * 0.34, deck_y,
                              -_num(vis.get("BodyL"), 2.2) * 0.12), 2.0)
        base_half = (_num(vis.get("BodyW"), 1.4) * 0.5, _num(vis.get("BodyL"), 2.2) * 0.5)
        colliders = [("base_col", (0.0, deck_y * 0.5, 0.0),
                      (base_half[0] * 2.0, deck_y, base_half[1] * 2.0))]
    else:
        mast_h = boom * _FAN_TOWER_FACTOR * (0.85 + 0.075 * tier)
        yaw_y = _tower_mast(m, mast_h)
        _control_cabinet(m, (0.78, 0.62, -0.3), (0.5, 0.9, 0.42), auto)
        if auto:
            _weather_head(m, (-0.75, 0.24, 0.1), 2.2)
        colliders = [("base_col", (0.0, 0.6, 0.0), (1.3, 1.2, 1.3))]

    yoke_h = r_out + 0.26
    pitch_y = yaw_y + yoke_h
    arm_x = r_out + 0.14

    m.node("gun_yaw", pivot=(0.0, yaw_y, 0.0))
    head = "gun_yaw"
    m.tube((0.0, yaw_y + 0.05, 0.0), r_out * 0.60, r_out * 0.40, 0.13, axis=1,
           segments=12, mat=METAL, parent="gun_yaw")
    if auto:
        # The oscillator is the drive that sweeps the head across the piste. It sits
        # between the slew bearing and the trunnions, so everything above it sweeps and
        # the bearing itself does not.
        m.node("oscillator", pivot=(0.0, yaw_y + 0.12, 0.0), parent="gun_yaw")
        head = "oscillator"
        m.box((0.0, yaw_y + 0.24, -r_out * 0.55), (0.30, 0.26, 0.34), mat=BODY,
              parent=head)
        m.cylinder((0.0, yaw_y + 0.24, -r_out * 0.55 - 0.2), 0.07, 0.12, axis=2,
                   segments=8, mat=METAL, parent=head)

    for s in (-1.0, 1.0):
        m.prism([(yaw_y + 0.10, -0.22), (pitch_y + 0.11, -0.14), (pitch_y + 0.11, 0.14),
                 (yaw_y + 0.10, 0.22)], (s * arm_x, 0.0, 0.0), 0.085, mat=BODY,
                parent=head, axis=0)
        m.cylinder((s * arm_x, pitch_y, 0.0), 0.10, 0.12, axis=0, segments=10,
                   mat=METAL, parent=head)
        m.cylinder((s * arm_x * 0.62, yaw_y + 0.30, -barrel * 0.30), 0.052,
                   yoke_h * 0.55, axis=1, segments=8, mat=METAL, parent=head)
        m.cylinder((s * arm_x * 0.62, yaw_y + 0.30 + yoke_h * 0.45, -barrel * 0.30),
                   0.028, yoke_h * 0.7, axis=1, segments=6, mat=METAL, parent=head)

    m.node("gun_pitch", pivot=(0.0, pitch_y, 0.0), parent=head)
    front = barrel * 0.45
    rear = -barrel * 0.55
    m.tube((0.0, pitch_y, (front + rear) * 0.5), r_out, r_in, barrel, axis=2,
           segments=seg, mat=BODY, parent="gun_pitch")
    m.tube((0.0, pitch_y, front - 0.05), r_out * 1.13, r_in * 0.99, 0.13, axis=2,
           segments=seg, mat=BODY, parent="gun_pitch")
    m.tube((0.0, pitch_y, rear + barrel * 0.32), r_out * 1.06, r_out * 0.98, 0.09,
           axis=2, segments=seg, mat=METAL, parent="gun_pitch")
    m.cylinder((0.0, pitch_y, rear + 0.07), r_in * 0.99, 0.025, axis=2, segments=seg,
               mat=METAL, parent="gun_pitch")

    body_y = pitch_y - r_out - 0.30
    m.prism([(-r_out * 0.72, body_y - 0.30), (r_out * 0.72, body_y - 0.30),
             (r_out * 0.80, body_y + 0.12), (r_out * 0.60, body_y + 0.32),
             (-r_out * 0.60, body_y + 0.32), (-r_out * 0.80, body_y + 0.12)],
            (0.0, 0.0, rear + barrel * 0.36), barrel * 0.80, mat=BODY,
            parent="gun_pitch")
    m.cylinder((0.0, body_y - 0.06, rear + barrel * 0.78), r_out * 0.42, 0.16, axis=2,
               segments=10, mat=METAL, parent="gun_pitch")
    for s in (-1.0, 1.0):
        m.beam((s * r_out * 0.55, body_y + 0.30, rear + barrel * 0.10),
               (s * r_out * 0.78, pitch_y - r_out * 0.72, rear + barrel * 0.10),
               0.05, mat=METAL, parent="gun_pitch")
    m.cylinder((r_out * 0.55, body_y + 0.05, rear - 0.02), 0.045, barrel * 0.7, axis=2,
               segments=6, mat=METAL, parent="gun_pitch")
    m.cylinder((-r_out * 0.55, body_y + 0.05, rear - 0.02), 0.032, barrel * 0.7, axis=2,
               segments=6, mat=METAL, parent="gun_pitch")

    ring_r = r_in * 0.80
    m.tube((0.0, pitch_y, front - 0.16), ring_r + 0.035, ring_r, 0.07, axis=2,
           segments=seg, mat=METAL, parent="gun_pitch")
    nozzles = int(_clamp(8.0 + power / 3.0, 10.0, 18.0))
    _nozzle_ring(m, (0.0, pitch_y, front - 0.08), ring_r, nozzles, 0.024, 0.11,
                 parent="gun_pitch")
    for i in range(3):
        a = 2.0 * math.pi * i / 3.0 + math.pi / 6.0
        m.beam((math.cos(a) * r_in * 0.99, pitch_y + math.sin(a) * r_in * 0.99,
                rear + barrel * 0.24),
               (math.cos(a) * r_in * 0.30, pitch_y + math.sin(a) * r_in * 0.30,
                rear + barrel * 0.24), 0.05, mat=METAL, parent="gun_pitch")

    fan_z = rear + barrel * 0.24
    m.node("fan_rotor", pivot=(0.0, pitch_y, fan_z), parent="gun_pitch")
    hub_r = r_in * 0.30
    m.lathe([(0.0, -0.13), (hub_r * 0.7, -0.11), (hub_r, -0.02), (hub_r, 0.10),
             (hub_r * 0.75, 0.20), (0.0, 0.27)], (0.0, pitch_y, fan_z), segments=12,
            mat=METAL, parent="fan_rotor", axis=2)
    blades = int(_clamp(5.0 + power / 8.0, 6.0, 10.0))
    chord = r_in * 0.44
    thick = max(0.05, chord * 0.30)
    blade_len = r_in * 0.96 - hub_r
    for i in range(blades):
        deg = 360.0 * i / blades
        a = math.radians(deg)
        rot = mk.unity_euler(0.0, 0.0, deg) @ mk.unity_euler(34.0, 0.0, 0.0)
        radius = hub_r + blade_len * 0.5
        m.prism([(0.0, -chord * 0.5), (thick * 0.5, chord * 0.06), (0.0, chord * 0.5),
                 (-thick * 0.5, -chord * 0.06)],
                (math.cos(a) * radius, pitch_y + math.sin(a) * radius, fan_z),
                blade_len, mat=METAL, parent="fan_rotor", axis=0, rot=rot)

    m.socket("light_L", (-r_out * 0.7, pitch_y - r_out * 0.5, front - 0.3))
    m.socket("light_R", (r_out * 0.7, pitch_y - r_out * 0.5, front - 0.3))
    return colliders


# --------------------------------------------------------------------------- lance gun
def _build_lance_gun(m, record, vis):
    """A slim vertical mast with a nozzle head on top: no fan, no noise, no barrel.

    The whole point of a lance is that the water falls a long way before it lands, so the
    mast is the machine and it must not be mistaken for a fan gun at any distance.
    """
    mass = _num(record.get("MassKg"), 300.0)
    boom = _num(vis.get("BoomLengthM"), 3.5)
    air = _spec(record, "airM3PerMin", 6.0)
    tier = datasrc.tier_of(record)
    fixed = not _is_towed(record)
    mast_h = boom * _LANCE_MAST_FACTOR
    mast_r = _clamp(0.048 + mass / 26000.0, 0.05, 0.085)

    if fixed:
        pad = 0.22
        m.box((0.0, pad * 0.5, 0.0), (0.95, pad, 0.95), mat=METAL)
        _bolt_ring(m, (0.0, pad + 0.02, 0.0), 0.30, 4, 0.02, 0.10)
        base_y = pad
        colliders = [("base_col", (0.0, 0.45, 0.0), (0.95, 0.9, 0.95))]
    else:
        base_y = _tow_carriage(m, vis, mass)
        colliders = [("base_col", (0.0, base_y * 0.5, 0.0),
                      (_num(vis.get("BodyW"), 1.4), base_y,
                       _num(vis.get("BodyL"), 2.2)))]

    m.cylinder((0.0, base_y + 0.06, 0.0), 0.24, 0.12, axis=1, segments=12, mat=METAL)
    yaw_y = base_y + 0.12

    # The swivel base is the yaw joint: a lance is aimed by turning the whole mast.
    m.node("gun_yaw", pivot=(0.0, yaw_y, 0.0))
    m.tube((0.0, yaw_y + 0.05, 0.0), 0.22, 0.12, 0.10, axis=1, segments=12, mat=METAL,
           parent="gun_yaw")
    m.cylinder((0.0, yaw_y + 0.16, 0.0), 0.16, 0.14, axis=1, segments=10, mat=BODY,
               parent="gun_yaw")

    foot = yaw_y + 0.22
    top = foot + mast_h
    m.cylinder((0.0, (foot + top) * 0.5, 0.0), mast_r, mast_h, axis=1, segments=10,
               mat=BODY, parent="gun_yaw", radius_end=mast_r * 0.82)
    for s, radius in ((-1.0, 0.026), (1.0, 0.019)):
        m.cylinder((s * (mast_r + radius + 0.012), (foot + top) * 0.5 + 0.1, 0.0),
                   radius, mast_h - 0.3, axis=1, segments=6, mat=METAL, parent="gun_yaw")
    clamps = int(_clamp(mast_h / 1.1, 3.0, 9.0))
    first = m.box((0.0, foot + 0.55, 0.0), (mast_r * 4.2, 0.05, mast_r * 1.6),
                  mat=METAL, parent="gun_yaw", bevel=False)
    m.array(first, clamps, (0.0, (mast_h - 0.9) / max(1, clamps - 1), 0.0),
            parent="gun_yaw")

    for k in range(3):
        m.prism([(foot - 0.02, mast_r), (foot - 0.02, mast_r + 0.30),
                 (foot + 0.42, mast_r + 0.02)], (0.0, 0.0, 0.0), 0.035, mat=METAL,
                parent="gun_yaw", axis=0, rot=mk.unity_euler(0.0, 120.0 * k, 0.0))

    if fixed:
        # A fixed lance is serviced in place, so it carries a climbing bracket and a
        # fall-arrest rail up the back of the mast.
        rungs = int(_clamp(mast_h / 0.34, 4.0, 22.0))
        rung = m.box((0.0, foot + 0.42, -mast_r - 0.14), (0.30, 0.03, 0.035),
                     mat=METAL, parent="gun_yaw", bevel=False)
        m.array(rung, rungs, (0.0, (mast_h - 0.8) / max(1, rungs - 1), 0.0),
                parent="gun_yaw")
        m.cylinder((0.0, (foot + top) * 0.5 + 0.1, -mast_r - 0.20), 0.016, mast_h - 0.6,
                   axis=1, segments=5, mat=METAL, parent="gun_yaw")

    # The head is where the water meets the air, and it tilts to set the throw.
    m.node("gun_pitch", pivot=(0.0, top, 0.0), parent="gun_yaw")
    head_r = mast_r * 2.3
    m.cylinder((0.0, top + 0.10, 0.0), head_r, 0.22, axis=1, segments=12, mat=BODY,
               parent="gun_pitch")
    m.lathe([(0.0, 0.0), (head_r * 0.9, 0.03), (head_r * 0.55, 0.16), (0.0, 0.20)],
            (0.0, top + 0.21, 0.0), segments=12, mat=METAL, parent="gun_pitch")
    ports = int(_clamp(6.0 + air, 8.0, 16.0))
    for i in range(ports):
        a = 2.0 * math.pi * i / ports
        c, s = math.cos(a) * head_r, math.sin(a) * head_r
        m.cylinder((c * 1.18, top + 0.06, s * 1.18), 0.017, 0.10, axis=1, segments=5,
                   mat=METAL, parent="gun_pitch")
        m.cylinder((c * 1.18, top + 0.13, s * 1.18), 0.026, 0.05, axis=1, segments=5,
                   mat=METAL, parent="gun_pitch")
    m.cylinder((0.0, top - 0.10, 0.0), mast_r * 1.5, 0.2, axis=1, segments=10,
               mat=METAL, parent="gun_pitch")
    m.box((head_r * 1.5, top - 0.06, 0.0), (0.12, 0.16, 0.14), mat=BODY,
          parent="gun_pitch")

    manifold_y = yaw_y + 0.55
    m.box((0.0, manifold_y, mast_r + 0.22), (0.44, 0.30, 0.26), mat=BODY)
    for s, radius in ((-1.0, 0.045), (1.0, 0.034)):
        m.cylinder((s * 0.14, manifold_y - 0.22, mast_r + 0.22), radius, 0.28, axis=1,
                   segments=8, mat=METAL)
        _handwheel(m, (s * 0.14, manifold_y + 0.26, mast_r + 0.22), 0.10, axis=1)
        _coupler(m, (s * 0.14, manifold_y - 0.02, mast_r + 0.36), (0.0, 0.0, 1.0),
                 radius * 0.8)
    m.box((-0.42, manifold_y + 0.08, mast_r + 0.16), (0.22, 0.3, 0.18), mat=BODY)

    _hose_reel(m, (0.0, base_y + 0.42, -0.55), 0.30, 0.26)
    return colliders


# --------------------------------------------------------------------------- skids
def _skid_enclosure(m, length, width, height, louvres, mass):
    """The container-style body every diesel skid on the mountain shares.

    A skid frame with lifting lugs at the corners, a chamfered enclosure, a cooler end
    with a louvre bank and a set of access doors: a pump set and a compressor set differ
    in what hangs off the ends, not in the box.
    """
    frame = _clamp(0.10 + mass / 22000.0, 0.12, 0.22)
    floor = frame
    top = floor + height

    for s in (-1.0, 1.0):
        m.box((s * (width * 0.5 - 0.07), frame * 0.5, 0.0), (0.14, frame, length),
              mat=METAL)
    for z in (-length * 0.34, 0.0, length * 0.34):
        m.box((0.0, frame * 0.45, z), (width - 0.22, frame * 0.72, 0.12), mat=METAL)
    m.box((0.0, floor - 0.02, 0.0), (width - 0.18, 0.05, length - 0.1), mat=METAL)
    _lifting_lugs(m, width * 0.5 - 0.07, length * 0.5 - 0.18, frame)

    chamfer = min(0.22, height * 0.2, width * 0.2)
    m.prism([(-width * 0.5, floor + 0.02), (width * 0.5, floor + 0.02),
             (width * 0.5, top - chamfer), (width * 0.5 - chamfer, top),
             (-width * 0.5 + chamfer, top), (-width * 0.5, top - chamfer)],
            (0.0, 0.0, 0.0), length, mat=BODY)

    # Doors and panel breaks down both sides: an enclosure that is one flat slab reads as
    # a crate, and the hinge lines are what say "this opens".
    door_h = height * 0.66
    for s in (-1.0, 1.0):
        for z in (-length * 0.24, length * 0.20):
            m.box((s * (width * 0.5 + 0.01), floor + 0.06 + door_h * 0.5, z),
                  (0.04, door_h, length * 0.30), mat=BODY, bevel=False)
            m.box((s * (width * 0.5 + 0.035), floor + 0.1 + door_h * 0.5,
                   z + length * 0.12), (0.03, 0.16, 0.05), mat=METAL, bevel=False)

    _louvre_panel(m, (0.0, floor + height * 0.52, -length * 0.5 - 0.03),
                  width * 0.72, height * 0.62, louvres, face=2)
    m.box((0.0, top - 0.10, -length * 0.5 + 0.22), (width * 0.5, 0.12, 0.3), mat=METAL)
    return floor, top


def _exhaust_stack(m, base, height, radius):
    """A vertical stack with a rain cap. The tip is the `exhaust` emitter anchor."""
    bx, by, bz = base
    m.cylinder((bx, by + height * 0.5, bz), radius, height, axis=1, segments=8,
               mat=METAL)
    m.node("exhaust", pivot=(bx, by + height, bz))
    m.cylinder((bx, by + height + 0.04, bz), radius * 1.25, 0.07, axis=1, segments=8,
               mat=METAL, parent="exhaust")
    m.lathe([(radius * 1.5, 0.0), (radius * 1.5, 0.03), (0.0, 0.11)],
            (bx, by + height + 0.14, bz), segments=8, mat=METAL, parent="exhaust")


def _build_pump_skid(m, record, vis):
    """A diesel pump set on a skid: the enclosure, plus the wet end that makes it a pump."""
    length = max(2.0, _num(vis.get("BodyL"), 3.5))
    width = max(1.2, _num(vis.get("BodyW"), 1.8))
    height = max(1.2, _num(vis.get("BodyH"), 1.8))
    mass = _num(record.get("MassKg"), 3000.0)
    kw = _num(record.get("EnginePowerKw"), 120.0)
    lps = _spec(record, "pumpLps", 30.0)
    fuel = _num(record.get("FuelCapacityL"), 400.0)

    floor, top = _skid_enclosure(m, length, width, height,
                                 int(_clamp(kw / 22.0, 4.0, 9.0)), mass)
    _exhaust_stack(m, (width * 0.30, top - 0.04, -length * 0.30), height * 0.42, 0.065)
    m.cylinder((-width * 0.30, top + 0.13, -length * 0.16), 0.14, 0.42, axis=1,
               segments=10, mat=METAL)
    m.cylinder((-width * 0.30, top + 0.36, -length * 0.16), 0.10, 0.08, axis=1,
               segments=8, mat=METAL)

    # The wet end hangs off the front, and its pipework is sized from the duty the record
    # quotes: 30 L/s at 2.5 m/s is a DN125 line, and a bigger set gets a bigger one.
    d = _pipe_diameter(lps / 1000.0, _WATER_VELOCITY_MS)
    axis_y = floor + height * 0.42
    nose = length * 0.5
    m.lathe([(0.0, nose - 0.02), (d * 1.5, nose + 0.04), (d * 1.9, nose + 0.22),
             (d * 1.6, nose + 0.40), (0.0, nose + 0.46)], (0.0, axis_y, 0.0),
            segments=12, mat=BODY, axis=2)
    m.cylinder((0.0, axis_y, nose + 0.52), d * 0.62, 0.5, axis=2, segments=10, mat=METAL)
    m.cylinder((0.0, axis_y, nose + 0.76), d * 0.95, 0.05, axis=2, segments=10,
               mat=METAL)
    _bolt_ring(m, (0.0, axis_y, nose + 0.76), d * 0.78, 6, 0.018, 0.07, axis=2)
    m.cylinder((0.0, axis_y + d * 1.6, nose + 0.18), d * 0.5, d * 2.4, axis=1,
               segments=10, mat=METAL)
    m.cylinder((0.0, axis_y + d * 2.6, nose + 0.18), d * 0.8, 0.05, axis=1, segments=10,
               mat=METAL)
    _handwheel(m, (0.0, axis_y + d * 3.0, nose + 0.18), d * 0.9, axis=1)
    m.box((width * 0.34, axis_y + 0.2, nose - 0.06), (0.26, 0.34, 0.14), mat=BODY)
    m.cylinder((width * 0.34, axis_y + 0.42, nose - 0.01), 0.05, 0.06, axis=2,
               segments=8, mat=GLASS)

    # A day tank sized from the record's own fuel capacity, standing where the fitter
    # can reach the filler.
    side = (fuel / 1000.0 / max(0.36, length * 0.28)) ** 0.5
    if side > 0.15:
        m.box((-width * 0.5 - side * 0.55, floor + side * 0.5 + 0.05, -length * 0.12),
              (side, side, length * 0.42), mat=BODY)
        m.cylinder((-width * 0.5 - side * 0.55, floor + side + 0.09, -length * 0.26),
                   0.06, 0.1, axis=1, segments=6, mat=METAL)
    return [("skid_col", (0.0, floor + height * 0.5, 0.0), (width, height, length))]


def _build_compressor_skid(m, record, vis):
    """A diesel screw compressor on a skid: enclosure, receiver, aftercooler, manifold."""
    length = max(2.0, _num(vis.get("BodyL"), 3.5))
    width = max(1.2, _num(vis.get("BodyW"), 1.8))
    height = max(1.2, _num(vis.get("BodyH"), 1.8))
    mass = _num(record.get("MassKg"), 3000.0)
    kw = _num(record.get("EnginePowerKw"), 150.0)
    air = _spec(record, "airM3PerMin", 20.0)

    floor, top = _skid_enclosure(m, length, width, height,
                                 int(_clamp(kw / 18.0, 5.0, 11.0)), mass)
    _exhaust_stack(m, (width * 0.30, top - 0.04, -length * 0.32), height * 0.45, 0.07)

    # A receiver holds about twenty seconds of free air delivery, compressed to line
    # pressure - which is the rule of thumb a plant is actually specified by.
    volume = (air / 3.0) / _AIR_LINE_BAR
    r = _clamp((volume / (math.pi * length * 0.62)) ** 0.5, 0.18, 0.55)
    vessel = length * 0.66
    m.lathe([(0.0, -vessel * 0.5), (r * 0.62, -vessel * 0.5 + 0.08),
             (r, -vessel * 0.5 + 0.26), (r, vessel * 0.5 - 0.26),
             (r * 0.62, vessel * 0.5 - 0.08), (0.0, vessel * 0.5)],
            (0.0, top + r + 0.12, length * 0.02), segments=12, mat=BODY, axis=2)
    for sz in (-1.0, 1.0):
        m.box((0.0, top + 0.06, sz * vessel * 0.34), (r * 1.5, 0.14, 0.12), mat=METAL)
        m.cylinder((0.0, top + r * 0.4 + 0.1, sz * vessel * 0.34), r * 1.06, 0.08,
                   axis=2, segments=10, mat=METAL)

    d = _pipe_diameter((air / 60.0) / _AIR_LINE_BAR, _AIR_VELOCITY_MS)
    out_y = floor + height * 0.5
    m.cylinder((0.0, top + r + 0.12, length * 0.42), d * 0.5, length * 0.22, axis=2,
               segments=8, mat=METAL)
    m.cylinder((0.0, (out_y + top + r) * 0.5, length * 0.52), d * 0.5,
               top + r - out_y + 0.2, axis=1, segments=8, mat=METAL)
    m.cylinder((0.0, out_y, length * 0.52), d * 0.5, 0.36, axis=2, segments=8,
               mat=METAL)
    m.cylinder((0.0, out_y, length * 0.5 + 0.22), d * 0.95, 0.05, axis=2, segments=10,
               mat=METAL)
    _handwheel(m, (0.0, out_y + d * 1.6, length * 0.5 + 0.05), d * 1.1, axis=1)
    m.box((width * 0.28, out_y + 0.24, length * 0.5 + 0.04), (0.24, 0.3, 0.12), mat=BODY)

    _louvre_panel(m, (width * 0.5 + 0.02, floor + height * 0.55, length * 0.14),
                  length * 0.3, height * 0.5, 5, face=0)
    m.cylinder((-width * 0.32, top + 0.2, -length * 0.14), 0.15, 0.44, axis=1,
               segments=10, mat=METAL)
    m.cylinder((-width * 0.32, top + 0.45, -length * 0.14), 0.11, 0.09, axis=1,
               segments=8, mat=METAL)
    return [("skid_col", (0.0, floor + height * 0.5, 0.0), (width, height, length))]


def _build_generic_skid(m, record, vis):
    """The fallback for a stationary or towed record with no recognisable function.

    Whatever it turns out to be, it arrives on a skid in a box, so that is what it gets.
    """
    length = max(1.5, _num(vis.get("BodyL"), 3.0))
    width = max(1.0, _num(vis.get("BodyW"), 1.8))
    height = max(1.0, _num(vis.get("BodyH"), 1.8))
    mass = _num(record.get("MassKg"), 1500.0)
    floor, top = _skid_enclosure(m, length, width, height, 5, mass)
    m.box((0.0, top + 0.09, 0.0), (width * 0.5, 0.18, length * 0.4), mat=METAL)
    return [("skid_col", (0.0, floor + height * 0.5, 0.0), (width, height, length))]


# --------------------------------------------------------------------------- tank
def _build_fuel_tank(m, record, vis):
    """A bunded double-wall tank. The litres in the record are the litres in the shell.

    Everything else follows: the bund has to hold a tank's worth of spill, the pump
    cabinet bolts to one end, and the ladder-free top carries the filler and the gauge.
    """
    litres = max(_spec(record, "fuelStorageL", 0.0),
                 _num(record.get("TankL"), 0.0),
                 _num(record.get("FuelCapacityL"), 0.0))
    if litres <= 0.0:
        litres = 1000.0
    ratio_l = max(1.0, _num(vis.get("BodyL"), 3.0))
    ratio_w = max(1.0, _num(vis.get("BodyW"), 2.0))
    ratio_h = max(1.0, _num(vis.get("BodyH"), 2.0))

    # A double-wall shell carries its rated volume in the inner tank plus the interstice
    # and the ullage above it, which is about a fifth more steel than litres.
    shell = litres / 1000.0 * 1.22
    k = (shell / (ratio_l * ratio_w * ratio_h)) ** (1.0 / 3.0)
    t_l, t_w, t_h = ratio_l * k, ratio_w * k, ratio_h * k

    bund_h = _clamp(t_h * 0.22, 0.18, 0.45)
    bund_w, bund_l = t_w + 0.5, t_l + 0.5
    m.box((0.0, 0.04, 0.0), (bund_w - 0.07, 0.08, bund_l - 0.07), mat=METAL)
    for s in (-1.0, 1.0):
        m.box((s * (bund_w * 0.5 - 0.05), bund_h * 0.5, 0.0), (0.1, bund_h, bund_l),
              mat=BODY)
        m.box((0.0, bund_h * 0.5 - 0.01, s * (bund_l * 0.5 - 0.05)),
              (bund_w - 0.22, bund_h * 0.94, 0.1), mat=BODY)

    base = 0.14
    tank_y = base + t_h * 0.5
    for s in (-1.0, 1.0):
        m.box((s * (t_w * 0.5 - 0.12), base * 0.5 + 0.04, 0.0), (0.12, base, t_l * 0.96),
              mat=METAL)
    chamfer = min(0.16, t_h * 0.18, t_w * 0.18)
    m.prism([(-t_w * 0.5, base + 0.02), (t_w * 0.5, base + 0.02),
             (t_w * 0.5, base + t_h - chamfer), (t_w * 0.5 - chamfer, base + t_h),
             (-t_w * 0.5 + chamfer, base + t_h), (-t_w * 0.5, base + t_h - chamfer)],
            (0.0, 0.0, 0.0), t_l, mat=BODY)
    strap = m.box((0.0, tank_y, -t_l * 0.3), (t_w + 0.03, t_h * 0.9, 0.05), mat=METAL,
                  bevel=False)
    m.array(strap, 3, (0.0, 0.0, t_l * 0.3))
    for s in (-1.0, 1.0):
        m.box((s * t_w * 0.28, base * 0.5, 0.0), (t_w * 0.2, base * 0.7, t_l + 0.08),
              mat=METAL, bevel=False)

    top = base + t_h
    m.cylinder((0.0, top + 0.06, -t_l * 0.22), t_w * 0.13, 0.14, axis=1, segments=10,
               mat=METAL)
    m.lathe([(0.0, 0.0), (t_w * 0.12, 0.02), (t_w * 0.10, 0.08), (0.0, 0.10)],
            (0.0, top + 0.13, -t_l * 0.22), segments=10, mat=BODY)
    m.cylinder((0.0, top + 0.05, t_l * 0.18), 0.055, 0.12, axis=1, segments=8, mat=METAL)
    m.box((0.0, top + 0.13, t_l * 0.30), (0.3, 0.18, 0.14), mat=BODY)

    gauge_x = t_w * 0.5 + 0.04
    m.cylinder((gauge_x, tank_y, t_l * 0.34), 0.022, t_h * 0.76, axis=1, segments=6,
               mat=GLASS)
    for sy in (-1.0, 1.0):
        m.box((gauge_x, tank_y + sy * t_h * 0.4, t_l * 0.34), (0.1, 0.06, 0.1),
              mat=METAL, bevel=False)

    cab_l = 0.42
    cab_z = t_l * 0.5 + cab_l * 0.5 + 0.03
    m.box((0.0, base + t_h * 0.34, cab_z), (t_w * 0.66, t_h * 0.62, cab_l), mat=BODY)
    m.box((0.0, base + t_h * 0.36, cab_z + cab_l * 0.52), (t_w * 0.48, t_h * 0.44, 0.03),
          mat=METAL, bevel=False)
    m.box((0.0, base + t_h * 0.5, cab_z + cab_l * 0.56), (t_w * 0.2, t_h * 0.12, 0.03),
          mat=GLASS, bevel=False)
    _hose_reel(m, (0.0, base + t_h * 0.78, cab_z + 0.08), t_h * 0.2, t_w * 0.3)
    _coupler(m, (t_w * 0.22, base + t_h * 0.12, cab_z + cab_l * 0.5), (0.0, 0.0, 1.0),
             0.035)
    _lifting_lugs(m, t_w * 0.5 - 0.05, t_l * 0.5 - 0.2, top - 0.02)
    return [("tank_col", (0.0, base + t_h * 0.5, 0.0), (t_w, t_h, t_l))]


# --------------------------------------------------------------------------- factory
def _build_snow_factory(m, record, vis):
    """A plate-freezer plant: insulated container, ice drum, chiller bank, conveyor.

    The refrigeration load is the size of the machine. 400 kW of compressors needs a
    condenser bank of a certain area, and that bank is most of what you see from the
    piste, so the fan count comes straight off the kilowatts.
    """
    length = max(4.0, _num(vis.get("BodyL"), 9.0))
    width = max(2.0, _num(vis.get("BodyW"), 3.0))
    height = max(2.0, _num(vis.get("BodyH"), 3.0))
    kw = _spec(record, "gunPowerKw", 400.0)
    reach = _spec(record, "gunReachM", 15.0)
    water = _spec(record, "waterLpm", 60.0)

    plinth = 0.3
    m.box((0.0, plinth * 0.5, 0.0), (width + 0.45, plinth, length + 0.45), mat=METAL)
    top = plinth + height
    chamfer = min(0.25, height * 0.15)
    m.prism([(-width * 0.5, plinth + 0.02), (width * 0.5, plinth + 0.02),
             (width * 0.5, top - chamfer), (width * 0.5 - chamfer, top),
             (-width * 0.5 + chamfer, top), (-width * 0.5, top - chamfer)],
            (0.0, 0.0, 0.0), length, mat=BODY)

    ribs = int(_clamp(length / 1.1, 4.0, 11.0))
    for s in (-1.0, 1.0):
        rib = m.box((s * (width * 0.5 + 0.02), plinth + height * 0.5,
                     -length * 0.5 + 0.55), (0.06, height * 0.88, 0.1), mat=BODY,
                    bevel=False)
        m.array(rib, ribs, (0.0, 0.0, (length - 1.1) / max(1, ribs - 1)))
    m.box((0.0, plinth + height * 0.42, length * 0.5 + 0.03), (width * 0.42,
          height * 0.66, 0.06), mat=METAL)
    m.box((width * 0.28, plinth + height * 0.34, length * 0.5 + 0.05),
          (width * 0.2, height * 0.5, 0.05), mat=BODY)

    # The condenser bank on the roof: one fan ring repeated along the plant.
    fans = int(_clamp(kw / 70.0, 2.0, 8.0))
    fan_r = _clamp(width * 0.30, 0.35, 0.7)
    pitch = (length - fan_r * 2.4) / max(1, fans - 1) if fans > 1 else 0.0
    m.box((0.0, top + 0.16, 0.0), (width * 0.9, 0.3, length * 0.94), mat=METAL)
    for i in range(fans):
        z = -length * 0.5 + fan_r * 1.2 + pitch * i
        m.tube((0.0, top + 0.42, z), fan_r, fan_r * 0.86, 0.24, axis=1, segments=10,
               mat=METAL)
        m.cylinder((0.0, top + 0.5, z), fan_r * 0.22, 0.12, axis=1, segments=8,
                   mat=METAL)
        for k in range(4):
            deg = 90.0 * k
            a = math.radians(deg)
            m.box((math.cos(a) * fan_r * 0.5, top + 0.5, z + math.sin(a) * fan_r * 0.5),
                  (fan_r * 0.78, 0.02, fan_r * 0.34), mat=METAL, bevel=False,
                  rot=mk.unity_euler(0.0, -deg, 22.0))

    # The ice drum: the one part of a snow factory a visitor can point at.
    drum_r = height * 0.34
    m.lathe([(0.0, -width * 0.42), (drum_r * 0.8, -width * 0.44),
             (drum_r, -width * 0.40), (drum_r, width * 0.40),
             (drum_r * 0.8, width * 0.44), (0.0, width * 0.42)],
            (0.0, plinth + drum_r + 0.35, -length * 0.28), segments=16, mat=METAL,
            axis=0)
    for s in (-1.0, 1.0):
        m.cylinder((s * width * 0.46, plinth + drum_r + 0.35, -length * 0.28),
                   drum_r * 0.3, 0.16, axis=0, segments=10, mat=BODY)
        leg = plinth + drum_r + 0.35
        m.box((s * width * 0.46, leg * 0.5 + 0.02, -length * 0.28),
              (0.14, leg - 0.04, drum_r * 1.2), mat=BODY)

    # Discharge conveyor: it has to throw the snow clear of the plant, and the record
    # says how far.
    conv_l = _clamp(reach * 0.6, 4.0, 10.0)
    conv_w = width * 0.34
    rise = _clamp(conv_l * 0.42, 1.4, 3.6)
    z0 = length * 0.5 - 0.3
    y0 = plinth + height * 0.34
    zt = z0 + conv_l * 0.92
    yt = y0 + rise
    deg = math.degrees(math.atan2(rise, conv_l * 0.92))
    rot = mk.unity_euler(-deg, 0.0, 0.0)
    mid = ((z0 + zt) * 0.5, (y0 + yt) * 0.5)
    m.box((0.0, mid[1], mid[0]), (conv_w, 0.12, conv_l), mat=METAL, rot=rot)
    for s in (-1.0, 1.0):
        m.box((s * conv_w * 0.56, mid[1] + 0.16, mid[0]), (0.05, 0.34, conv_l),
              mat=BODY, rot=rot)
    for zz, yy in ((z0, y0), (zt, yt)):
        m.cylinder((0.0, yy + 0.06, zz), 0.16, conv_w * 0.98, axis=0, segments=10,
                   mat=METAL)
    m.beam((0.0, plinth, zt - conv_l * 0.3), (0.0, yt - 0.24, zt - 0.4), 0.12,
           mat=METAL)
    m.box((0.0, yt + 0.2, zt - 0.1), (conv_w * 1.1, 0.4, 0.5), mat=BODY)

    # Pipework: the chilled water loop out to the drum and back.
    d = _pipe_diameter(water / 60000.0, _WATER_VELOCITY_MS)
    for s, off in ((-1.0, 0.0), (-1.0, 0.26)):
        m.cylinder((s * (width * 0.5 + 0.18 + off), plinth + height * 0.55, 0.0),
                   max(0.05, d * 0.6), length * 0.8, axis=2, segments=8, mat=METAL)
    m.cylinder((-width * 0.5 - 0.18, plinth + height * 0.28, -length * 0.4),
               max(0.05, d * 0.6), height * 0.54, axis=1, segments=8, mat=METAL)
    m.box((-width * 0.5 - 0.3, plinth + 0.55, length * 0.22), (0.4, 1.1, 0.7), mat=BODY)
    _lifting_lugs(m, width * 0.5 - 0.05, length * 0.5 - 0.3, top - 0.06)
    return [("plant_col", (0.0, plinth + height * 0.5, 0.0), (width, height, length))]


# --------------------------------------------------------------------------- network
def _build_hydrant(m, spacing):
    """A piste hydrant: riser, valve body, couplers and a pole you can find in March."""
    m.box((0.0, 0.05, 0.0), (0.62, 0.1, 0.62), mat=METAL)
    m.cylinder((0.0, 0.30, 0.0), 0.10, 0.6, axis=1, segments=10, mat=BODY)
    m.lathe([(0.0, 0.0), (0.15, 0.04), (0.17, 0.16), (0.13, 0.26), (0.0, 0.30)],
            (0.0, 0.58, 0.0), segments=12, mat=BODY)
    m.cylinder((0.0, 0.90, 0.0), 0.075, 0.12, axis=1, segments=10, mat=METAL)
    m.lathe([(0.0, 0.0), (0.09, 0.02), (0.085, 0.07), (0.0, 0.09)], (0.0, 0.96, 0.0),
            segments=10, mat=METAL)
    _coupler(m, (0.0, 0.66, 0.13), (0.0, 0.0, 1.0), 0.05)
    _coupler(m, (0.0, 0.66, -0.13), (0.0, 0.0, -1.0), 0.035)
    _handwheel(m, (0.15, 0.80, 0.0), 0.1, axis=0)

    m.cylinder((0.22, _MARKER_POLE_M * 0.5, 0.0), 0.026, _MARKER_POLE_M, axis=1,
               segments=6, mat=BODY)
    band = m.tube((0.22, 0.8, 0.0), 0.036, 0.026, 0.2, axis=1, segments=6, mat=METAL)
    m.array(band, 4, (0.0, 0.7, 0.0))
    # A crew looks for the next hydrant from the one they are standing at, so the plate
    # on the pole is sized off how far apart the line puts them.
    flag = _clamp(spacing / 380.0, 0.15, 0.32)
    m.box((0.22 + flag * 0.5, _MARKER_POLE_M - 0.12, 0.0), (flag, flag * 1.2, 0.012),
          mat=BODY, bevel=False)
    m.box((0.22, 1.25, 0.05), (0.1, 0.14, 0.02), mat=METAL, bevel=False)


def _build_pump_house(m, station):
    """A pump house sized from its duty: the motors set the building, the flow the pipe."""
    flow = _num(station.get("FlowLps"), 60.0)
    kw = _num(station.get("PowerKw"), 260.0)

    # Floor area follows the motor list with a service aisle round it: a 260 kW pair of
    # multistage sets wants about 38 m2, and the room grows with the plant in it.
    area = _clamp(24.0 + kw * 0.055, 30.0, 170.0)
    length = math.sqrt(area * 1.7)
    width = area / length
    wall = _clamp(3.1 + kw / 900.0, 3.2, 5.4)
    rise = width * 0.16
    d = _pipe_diameter(flow / 1000.0, _WATER_VELOCITY_MS)

    m.box((0.0, 0.16, 0.0), (width + 0.7, 0.32, length + 0.7), mat=METAL)
    m.prism([(-width * 0.5, 0.28), (width * 0.5, 0.28), (width * 0.5, wall),
             (0.0, wall + rise), (-width * 0.5, wall)], (0.0, 0.0, 0.0), length,
            mat=BODY)
    m.prism([(-width * 0.52, wall - 0.06), (0.0, wall + rise + 0.08),
             (width * 0.52, wall - 0.06), (width * 0.52, wall - 0.2),
             (0.0, wall + rise - 0.08), (-width * 0.52, wall - 0.2)],
            (0.0, 0.0, 0.0), length + 0.4, mat=METAL)

    door_w = _clamp(width * 0.45, 2.4, 4.2)
    door_h = _clamp(wall * 0.72, 2.6, 4.0)
    m.box((0.0, 0.28 + door_h * 0.5, length * 0.5 + 0.03),
          (door_w, door_h, 0.08), mat=METAL)
    slat = m.box((0.0, 0.42, length * 0.5 + 0.09), (door_w - 0.1, 0.16, 0.04),
                 mat=METAL, bevel=False)
    m.array(slat, int(door_h / 0.3), (0.0, 0.3, 0.0))
    for s in (-1.0, 1.0):
        m.box((s * (door_w * 0.5 + 0.09), 0.28 + door_h * 0.52, length * 0.5 + 0.06),
              (0.12, door_h + 0.1, 0.12), mat=METAL)
    m.box((-width * 0.5 + 0.6, 0.28 + 1.05, length * 0.5 + 0.04), (0.95, 2.1, 0.06),
          mat=BODY)

    _louvre_panel(m, (0.0, wall * 0.62, -length * 0.5 - 0.04), width * 0.55, wall * 0.4,
                  5, face=2)
    _louvre_panel(m, (width * 0.5 + 0.03, wall * 0.62, -length * 0.2), length * 0.26,
                  wall * 0.38, 4, face=0)

    # The manifold outside the wall is the part that says how much water this house
    # moves, so its diameter is the real one for the duty.
    man_y = _clamp(d * 3.0, 0.8, 1.6)
    x = -width * 0.5 - d * 1.4
    m.cylinder((x, man_y, 0.0), d * 0.5, length * 0.86, axis=2, segments=12, mat=METAL)
    for z in (-length * 0.3, length * 0.3):
        m.cylinder((x, man_y, z), d * 0.78, 0.06, axis=2, segments=12, mat=METAL)
        _bolt_ring(m, (x, man_y, z), d * 0.62, 8, 0.022, 0.08, axis=2)
    m.cylinder((x + d * 0.9, man_y, -length * 0.12), d * 0.4, d * 2.0, axis=0,
               segments=10, mat=METAL)
    m.cylinder((x, man_y + d * 1.6, length * 0.12), d * 0.36, d * 2.2, axis=1,
               segments=10, mat=METAL)
    _handwheel(m, (x, man_y + d * 2.8, length * 0.12), d * 1.1, axis=1)
    for z in (-length * 0.36, 0.0, length * 0.36):
        m.box((x, man_y * 0.5, z), (d * 1.4, man_y, 0.16), mat=METAL)
    m.cylinder((x - d * 1.2, man_y * 0.6, -length * 0.42), d * 0.34, man_y * 1.2,
               axis=1, segments=8, mat=METAL)

    m.box((width * 0.5 + 0.55, 0.9, length * 0.28), (1.0, 1.8, 1.2), mat=BODY)
    m.cylinder((width * 0.5 + 0.55, 1.86, length * 0.28), 0.08, 0.16, axis=1,
               segments=6, mat=METAL)
    return [("house_col", (0.0, wall * 0.5, 0.0), (width, wall, length))]


def _build_compressor_house(m, station):
    """A compressor house: more louvre than wall, with the receivers standing outside."""
    air = _num(station.get("AirM3PerMin"), 40.0)
    kw = _num(station.get("PowerKw"), 270.0)

    area = _clamp(20.0 + kw * 0.05, 26.0, 120.0)
    length = math.sqrt(area * 1.6)
    width = area / length
    wall = _clamp(3.0 + kw / 1100.0, 3.1, 4.6)
    rise = width * 0.14

    m.box((0.0, 0.15, 0.0), (width + 0.6, 0.30, length + 0.6), mat=METAL)
    m.prism([(-width * 0.5, 0.26), (width * 0.5, 0.26), (width * 0.5, wall),
             (0.0, wall + rise), (-width * 0.5, wall)], (0.0, 0.0, 0.0), length,
            mat=BODY)
    m.prism([(-width * 0.52, wall - 0.06), (0.0, wall + rise + 0.08),
             (width * 0.52, wall - 0.06), (width * 0.52, wall - 0.2),
             (0.0, wall + rise - 0.08), (-width * 0.52, wall - 0.2)],
            (0.0, 0.0, 0.0), length + 0.36, mat=METAL)

    # A screw compressor throws nearly all its shaft power away as heat, so the wall
    # area a compressor house spends on louvres is what sets it apart from a pump house.
    banks = int(_clamp(kw / 150.0, 2.0, 5.0))
    step = length * 0.72 / max(1, banks)
    for i in range(banks):
        z = -length * 0.36 + step * (i + 0.5)
        for s in (-1.0, 1.0):
            _louvre_panel(m, (s * (width * 0.5 + 0.03), wall * 0.6, z), step * 0.8,
                          wall * 0.52, 6, face=0)
    _louvre_panel(m, (0.0, wall * 0.62, -length * 0.5 - 0.04), width * 0.62, wall * 0.5,
                  6, face=2)

    door_w = _clamp(width * 0.4, 2.2, 3.6)
    door_h = _clamp(wall * 0.7, 2.5, 3.6)
    m.box((0.0, 0.26 + door_h * 0.5, length * 0.5 + 0.03), (door_w, door_h, 0.08),
          mat=METAL)
    slat = m.box((0.0, 0.4, length * 0.5 + 0.09), (door_w - 0.1, 0.16, 0.04),
                 mat=METAL, bevel=False)
    m.array(slat, int(door_h / 0.3), (0.0, 0.3, 0.0))

    volume = (air / 3.0) / _AIR_LINE_BAR
    r = _clamp((volume / (math.pi * 2.6)) ** 0.5, 0.3, 0.8)
    vessel = _clamp(volume / (math.pi * r * r), 1.6, 4.0)
    d = _pipe_diameter((air / 60.0) / _AIR_LINE_BAR, _AIR_VELOCITY_MS)
    for side in (1.0, -1.0):
        x = side * (width * 0.5 + r * 1.5)
        m.lathe([(0.0, 0.0), (r * 0.6, 0.1), (r, 0.32), (r, vessel - 0.32),
                 (r * 0.6, vessel - 0.1), (0.0, vessel)],
                (x, 0.5, length * 0.16), segments=12, mat=BODY, axis=1)
        m.box((x, 0.25, length * 0.16), (r * 1.8, 0.5, r * 1.8), mat=METAL)
        m.cylinder((x, vessel + 0.62, length * 0.16), 0.05, 0.16, axis=1, segments=6,
                   mat=METAL)
        m.cylinder((side * (width * 0.5 + r * 0.72), 1.9, length * 0.16 - 0.85),
                   max(0.05, d * 0.5), r * 1.7, axis=0, segments=8, mat=METAL)
        m.cylinder((x, 1.45, length * 0.16 - 0.85), max(0.05, d * 0.5), 0.95, axis=1,
                   segments=8, mat=METAL)
    m.cylinder((-width * 0.5 - 0.3, 1.4, 0.0), max(0.05, d * 0.5), length * 0.7,
               axis=2, segments=8, mat=METAL)
    m.cylinder((-width * 0.5 - 0.3, 0.85, length * 0.34), max(0.05, d * 0.5), 1.1,
               axis=1, segments=8, mat=METAL)
    _handwheel(m, (-width * 0.5 - 0.3, 1.75, length * 0.34), d * 1.2, axis=1)

    for i in range(2):
        x = -width * 0.28 + i * width * 0.56
        m.cylinder((x, wall + rise * 0.5 + 0.5, -length * 0.2), 0.22, 1.0, axis=1,
                   segments=10, mat=METAL)
        m.lathe([(0.34, 0.0), (0.34, 0.04), (0.0, 0.2)],
                (x, wall + rise * 0.5 + 1.0, -length * 0.2), segments=10, mat=METAL)
    return [("house_col", (0.0, wall * 0.5, 0.0), (width, wall, length))]


# --------------------------------------------------------------------------- entry
_STATIONS = {
    "factory": _build_snow_factory,
    "tank": _build_fuel_tank,
    "pump": _build_pump_skid,
    "compressor": _build_compressor_skid,
    "skid": _build_generic_skid,
}


def build(record, out_path):
    """Build one snowmaking or stationary machine from its vehicles.json record."""
    vis = datasrc.visual(record)
    silhouette = vis.get("Silhouette", "station")
    if silhouette == "gun":
        builder = _build_lance_gun if _is_lance(record) else _build_fan_gun
        kind = "lance" if _is_lance(record) else "fan"
    elif silhouette == "station":
        kind = _station_kind(record)
        builder = _STATIONS.get(kind, _build_generic_skid)
    else:
        # build_all routes every Stationary and Towed record here whatever its
        # silhouette says, so anything unrecognised still arrives on a skid.
        kind = "skid"
        builder = _build_generic_skid

    budget = "machine_hero" if datasrc.is_hero(record) else "machine_small"
    model = mk.Model(record["Id"], budget_key=budget,
                     seed=datasrc.seed_for(record["Id"]))
    colliders = builder(model, record, vis) or []
    return export.emit(model, out_path, box_colliders=colliders,
                       extra={"family": "wheeled" if _is_towed(record) else "station",
                              "kind": "machine",
                              "displayName": record.get("DisplayName", record["Id"]),
                              "sourceId": record["Id"],
                              "silhouette": silhouette,
                              "plant": kind})


def build_all(out_dir):
    """The snowmaking network itself: hydrants, pump houses, compressor houses.

    None of these is a vehicles.json record - they are what the money in stations.json
    buys - so they are built here from those records instead, in file order.
    """
    stations = datasrc.stations()
    records = []

    model = mk.Model("hydrant", budget_key="prop", seed=datasrc.seed_for("hydrant"))
    spacing = _num(stations.get("HydrantSpacingM"), 80.0)
    _build_hydrant(model, spacing)
    records.append(export.emit(
        model, os.path.join(out_dir, "hydrant.fbx"),
        extra={"family": "station", "kind": "prop", "displayName": "Snowmaking hydrant",
               "sourceId": "hydrant", "spacingM": round(spacing, 1)}))

    for station in stations.get("PumpStations") or []:
        model = mk.Model(station["Id"], budget_key="prop",
                         seed=datasrc.seed_for(station["Id"]))
        colliders = _build_pump_house(model, station)
        records.append(export.emit(
            model, os.path.join(out_dir, station["Id"] + ".fbx"),
            box_colliders=colliders,
            extra={"family": "station", "kind": "station", "component": "pump_house",
                   "displayName": station.get("DisplayName", station["Id"]),
                   "sourceId": station["Id"]}))

    for station in stations.get("CompressorStations") or []:
        model = mk.Model(station["Id"], budget_key="prop",
                         seed=datasrc.seed_for(station["Id"]))
        colliders = _build_compressor_house(model, station)
        records.append(export.emit(
            model, os.path.join(out_dir, station["Id"] + ".fbx"),
            box_colliders=colliders,
            extra={"family": "station", "kind": "station",
                   "component": "compressor_house",
                   "displayName": station.get("DisplayName", station["Id"]),
                   "sourceId": station["Id"]}))
    return records
