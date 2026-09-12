"""Working-machinery loops and one-shots: hydraulics, tracks, implements and lift lines.

These are the sounds a machine makes that are not its engine, and every one of them is
built on a rate that comes out of the JSON. The track rumble is the grouser pass rate at
the working speeds in vehicles.json, over the same grouser spacing the track generator
arrays its bars at. The tiller churn is the rotor speed a hydraulic motor turns at on the
flow the attachment record asks for. The bullwheel hum is the rotation rate of a wheel
sized off the haul rope, which is sized off the drive power in lifts.json. Change a
lift's LineSpeedMs and its rope note moves on the next build.

Level is not set here beyond keeping every clip in the same headroom: how loud a pump is
next to a tiller is a runtime mixer decision, and baking it into the files would only
take that decision away from whoever tunes the mix.

The synthesis toolkit lives in audio.engines; this module is what is built with it.

Run standalone from tools/assetgen:  python3 -m audio.machinery [out_dir]
"""
import math

import numpy as np

import config
from audio.engines import (bandpass, burst, dc_block, fade, highpass, lowpass, n_for,
                           noise, normalise, partial_stack, pulse_train, report,
                           resonate, rng_for, soft_clip, time_warp, tone, tuning_value,
                           wander, write_clip)
from lib import datasrc, validate

# Grousers are arrayed along a belt at this fraction of the track's height, which is the
# same rule meshes/chassis_tracked.py builds them with. A rumble whose pass rate did not
# match the bars a player can see would be the two halves of the pipeline disagreeing.
GROUSER_PITCH_FRAC = 0.14
GROUSER_PITCH_RANGE = (0.08, 0.24)

# A gear pump's tooth count falls as its displacement rises: a big pump moves more oil per
# tooth rather than spinning faster, which is why a loader's pump sits lower in pitch than
# a compact machine's.
PUMP_TEETH_RANGE = (8.0, 13.0)

# Six strands laid round a core, and a lay length of about six and a half rope diameters.
# The strand pattern passing a sheave is what a lift line actually hums at.
ROPE_STRANDS = 6
ROPE_LAY_FACTOR = 6.5

# Wheel radius over rope diameter, half the bend ratio a rope maker quotes - the same
# number meshes/lift_terminals.py sizes its bullwheels with.
BULLWHEEL_BEND_RATIO = 40.0

# A lift drive is an induction motor on a mains-frequency supply through a two-stage
# reduction, so its input shaft runs near synchronous speed whatever the lift is.
DRIVE_MOTOR_RPM = 1490.0
DRIVE_PINION_TEETH = 23


def _num(value, default=0.0):
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _median(values, default=0.0):
    values = [v for v in values if v > 0.0]
    if not values:
        return default
    return sorted(values)[len(values) // 2]


# --------------------------------------------------------------------------- the data
def _tracked_machines():
    return [v for v in datasrc.vehicles() if v.get("ChassisType") == "Tracked"]


def _grouser_pitch():
    heights = [_num(datasrc.visual(v).get("TrackH")) for v in _tracked_machines()]
    height = _median(heights, 0.9)
    return _clamp(height * GROUSER_PITCH_FRAC, *GROUSER_PITCH_RANGE)


def _track_speeds():
    """The three speeds a tracked machine spends its shift at, in m/s.

    Working minimum, working maximum and transit: the runtime crossfades between them the
    same way it does the engine bands, so they are read off the same records.
    """
    machines = _tracked_machines()
    slow = _median([_num((v.get("WorkingSpeedKmh") or {}).get("Min")) for v in machines], 8.0)
    mid = _median([_num((v.get("WorkingSpeedKmh") or {}).get("Max")) for v in machines], 14.0)
    fast = _median([_num(v.get("TopSpeedKmh")) for v in machines], 22.0)
    return {"slow": slow / 3.6, "mid": mid / 3.6, "fast": fast / 3.6}


def _hydraulic_flow():
    return _median([_num(v.get("HydraulicFlowLpm")) for v in datasrc.vehicles()], 120.0)


def _attachments_of(kind):
    return [a for a in datasrc.attachments() if a.get("Kind") == kind]


def _roped_lifts():
    """Lifts that run on a haul rope, which is what a bullwheel and a rope note need."""
    return [l for l in datasrc.lifts()
            if str(l.get("RopeConfiguration")) not in ("Surface", "Rack")]


def _rope_diameter(power_kw):
    """Haul rope diameter from drive power, the derivation the terminal generator uses:
    rope tension is what the motor pulls against, so power is the honest proxy for size."""
    return _clamp(0.026 * (max(power_kw, 5.0) / 120.0) ** 0.30, 0.014, 0.060)


# --------------------------------------------------------------------------- hydraulics
def _hydraulic_pump(n, rng):
    """Gear pump: tooth pass, the shaft under it, and oil through the relief valve."""
    flow = _hydraulic_flow()
    rated = tuning_value("vehicles.ratedRpm")
    teeth = _clamp(13.0 - flow / 30.0, *PUMP_TEETH_RANGE)
    shaft = rated / 60.0
    mesh = shaft * teeth

    whine = partial_stack(n, mesh, [1.0, 0.55, 0.30, 0.16, 0.09], rng)
    whine += partial_stack(n, mesh * 2.0, [0.30, 0.14], rng)
    # A pump is driven off the engine, and the engine is not perfectly steady, so neither
    # is the tooth pass. Without this the whine is a test tone.
    whine = time_warp(whine, wander(n, rng, 12.0) * 55.0)
    whine *= 0.75 + 0.25 * tone(n, shaft, 1.0)          # once-per-revolution unevenness

    oil = bandpass(noise(n, rng, tilt=0.3), 700.0, 7000.0)
    oil = resonate(oil, 1800.0, 1.1, 2.0)
    case = lowpass(noise(n, rng, tilt=0.9), 260.0)

    mix = (normalise(whine, 1.0) * 0.62 + normalise(oil, 1.0) * 0.34
           + normalise(case, 1.0) * 0.30)
    return normalise(soft_clip(highpass(dc_block(mix), 45.0, order=2), 1.1), 0.86)


def _hydraulic_valve(rng):
    """The thunk a spool valve makes when a circuit is commanded, and the flow behind it.

    Three things arrive in quick order and all three have to be there or it reads as a
    click rather than as a machine doing something: the solenoid pulling in, the spool
    hitting its stop, and the surge of oil that follows.
    """
    n = n_for(0.45)
    out = np.zeros(n)
    solenoid = bandpass(burst(0.05, 2600.0, 0.004, rng, noise_mix=0.85), 1200.0, 6000.0)
    out[:solenoid.size] += solenoid * 0.55
    spool = n_for(0.018)
    thunk = burst(0.3, 96.0, 0.05, rng, noise_mix=0.22)
    out[spool:spool + thunk.size] += thunk * 0.95
    body = burst(0.26, 168.0, 0.035, rng, noise_mix=0.35)
    out[spool:spool + body.size] += body * 0.4

    surge = bandpass(noise(n, rng, tilt=0.4), 400.0, 4500.0)
    env = np.clip(np.linspace(-0.15, 1.0, n), 0.0, 1.0) * np.exp(-np.linspace(0, 6.0, n))
    out += surge * env * 0.5
    return normalise(fade(dc_block(out), 0.002, 0.05), 0.9)


# --------------------------------------------------------------------------- running gear
def _track_rumble(n, rng, speed, pitch):
    """Grousers slapping snow at `speed`, plus the frame they are bolted to.

    The pass rate is speed over grouser spacing, so the same loop covers a nordic cat and
    a flagship: the data moves the rate. Jitter is heavy because a belt runs over snow,
    not over a machined rail, and a perfectly even slap rate sounds like a bearing fault.
    """
    rate = speed / pitch
    bar = burst(0.05, 118.0, 0.013, rng, noise_mix=0.5)
    slap = pulse_train(n, rate, bar, rng=rng, jitter=0.12,
                       amps=1.0 + rng.normal(0.0, 0.18, 7))

    grind = bandpass(noise(n, rng, tilt=0.45), 180.0, 3800.0 + 900.0 * speed)
    grind *= 0.6 + 0.4 * np.abs(tone(n, rate, 1.0))
    frame = resonate(lowpass(noise(n, rng, tilt=0.9), 240.0), 72.0, 2.4, 3.0)
    # Sprocket and idler bearings only come up out of the rumble once a machine is moving.
    whirr = partial_stack(n, max(speed / 0.9, 4.0) * 11.0, [1.0, 0.4, 0.2], rng)

    mix = (normalise(slap, 1.0) * 0.62 + normalise(grind, 1.0) * (0.26 + 0.04 * speed)
           + normalise(frame, 1.0) * 0.42
           + normalise(whirr, 1.0) * _clamp(0.05 * speed, 0.03, 0.18))
    return normalise(soft_clip(highpass(dc_block(mix), 38.0, order=4), 1.1), 0.87)


# --------------------------------------------------------------------------- implements
def _tiller_churn(n, rng):
    """A tiller rotor working: the tool rows passing, and snow being cut and thrown.

    Rotor speed is what a fixed-displacement motor turns at on the flow the attachment
    record asks for, and displacement goes up with working width because a wider rotor
    needs the torque. A 4.3 m tiller on 150 L/min lands near 600 rpm, which is where a
    real one runs.
    """
    tillers = _attachments_of("Tiller")
    flow = _median([_num(a.get("HydraulicFlowLpm")) for a in tillers], 150.0)
    width = _median([_num(a.get("WorkingWidthM")) for a in tillers], 4.3)
    displacement = 0.25 * (width / 4.3)                  # litres per revolution
    rotor = flow / max(displacement, 0.05) / 60.0        # revolutions per second
    rows = 8
    pass_rate = rotor * rows

    tooth = burst(0.03, 300.0, 0.008, rng, noise_mix=0.72)
    teeth = pulse_train(n, pass_rate, tooth, rng=rng, jitter=0.09,
                        amps=1.0 + rng.normal(0.0, 0.22, rows))
    churn = bandpass(noise(n, rng, tilt=0.5), 260.0, 6500.0)
    churn *= 0.55 + 0.45 * np.abs(tone(n, pass_rate, 1.0))
    hood = resonate(lowpass(noise(n, rng, tilt=0.8), 900.0), 128.0, 2.0, 3.2)
    drive = partial_stack(n, rotor * 3.0, [1.0, 0.5, 0.25, 0.12], rng)   # the rotor motor

    mix = (normalise(teeth, 1.0) * 0.48 + normalise(churn, 1.0) * 0.5
           + normalise(hood, 1.0) * 0.38 + normalise(drive, 1.0) * 0.18)
    return normalise(soft_clip(highpass(dc_block(mix), 40.0, order=4), 1.15), 0.88)


def _blower_roar(n, rng):
    """A snow blower head: impeller blade pass under a lot of moving air and snow.

    Tip speed comes from how far the head throws, because a thrown mass leaves the
    impeller at the speed it needs to get there. The blade pass falls out of that and the
    impeller diameter, and the roar around it is the housing and the chute.
    """
    heads = _attachments_of("BlowerHead")
    throw = _median([_num((a.get("Effects") or {}).get("ThrowDistanceM"))
                     for a in heads], 22.0)
    width = _median([_num(a.get("WorkingWidthM")) for a in heads], 2.3)
    gravity = tuning_value("vehicles.gravity")
    tip = math.sqrt(gravity * max(throw, 4.0)) * 1.35    # ballistic, with a drag margin
    diameter = _clamp(width * 0.45, 0.55, 1.4)
    blades = 6
    blade_pass = tip / (math.pi * diameter) * blades

    air = bandpass(noise(n, rng, tilt=0.42), 90.0, 9000.0)
    air *= 0.62 + 0.38 * np.abs(tone(n, blade_pass, 1.0))
    air = resonate(air, 210.0, 1.3, 2.4)                 # the chute is a resonant tube
    tonal = partial_stack(n, blade_pass, [1.0, 0.62, 0.36, 0.2, 0.1], rng)
    thump = pulse_train(n, blade_pass, burst(0.04, 88.0, 0.012, rng, noise_mix=0.4),
                        rng=rng, jitter=0.05, amps=1.0 + rng.normal(0.0, 0.12, blades))
    snow = bandpass(noise(n, rng, tilt=0.2), 2500.0, 14000.0) * 0.5

    mix = (normalise(air, 1.0) * 0.72 + normalise(tonal, 1.0) * 0.34
           + normalise(thump, 1.0) * 0.34 + normalise(snow, 1.0) * 0.2)
    return normalise(soft_clip(highpass(dc_block(mix), 40.0, order=4), 1.2), 0.9)


# --------------------------------------------------------------------------- lift line
def _bullwheel_hum(n, rng):
    """A terminal bullwheel turning: rotation, liner segments, and the drive behind it.

    Wheel radius is the rope diameter times the bend ratio, and rope diameter comes from
    drive power, so a detachable six's terminal turns slower and hums lower than a fixed
    double's - which is what a station sounds like.
    """
    lifts = _roped_lifts()
    speed = _median([_num(l.get("LineSpeedMs")) for l in lifts], 5.0)
    power = _median([_num(l.get("PowerDrawKw")) for l in lifts], 550.0)
    radius = _rope_diameter(power) * BULLWHEEL_BEND_RATIO
    rev = speed / (2.0 * math.pi * radius)
    liners = 24                                          # rubber liner segments in the rim

    body = partial_stack(n, rev * liners, [1.0, 0.5, 0.28, 0.14], rng)
    body *= 0.7 + 0.3 * np.abs(tone(n, rev, 1.0))        # one flat spot per revolution
    motor = partial_stack(n, DRIVE_MOTOR_RPM / 60.0 * 2.0, [1.0, 0.6, 0.3, 0.15, 0.08], rng)
    structure = resonate(lowpass(noise(n, rng, tilt=0.95), 400.0), 58.0, 2.6, 3.4)
    rumble = bandpass(noise(n, rng, tilt=0.6), 90.0, 2200.0)
    rumble *= 0.75 + 0.25 * np.abs(tone(n, rev * liners, 1.0))

    mix = (normalise(body, 1.0) * 0.45 + normalise(motor, 1.0) * 0.3
           + normalise(structure, 1.0) * 0.5 + normalise(rumble, 1.0) * 0.32)
    return normalise(soft_clip(highpass(dc_block(mix), 35.0, order=4), 1.1), 0.85)


def rope_drone(n, rng, speed=None, power=None):
    """The lift-line note: a haul rope singing as its strand pattern passes a sheave.

    The strand rate itself is under 20 Hz and nobody hears it. What carries is its upper
    harmonics ringing the sheave train and the tower steel, which is why a lift line has
    that steady, slightly detuned drone you can hear from a run away. Exposed here because
    ambience builds its lift-line bed on the same note.
    """
    lifts = _roped_lifts()
    speed = speed or _median([_num(l.get("LineSpeedMs")) for l in lifts], 5.0)
    power = power or _median([_num(l.get("PowerDrawKw")) for l in lifts], 550.0)
    diameter = _rope_diameter(power)
    strand_rate = speed / (ROPE_LAY_FACTOR * diameter)

    amps = [0.0] * 3 + [1.0, 0.85, 0.6, 0.75, 0.45, 0.3, 0.35, 0.2, 0.14, 0.1]
    drone = partial_stack(n, strand_rate, amps, rng, jitter_cents=6.0)
    drone = resonate(drone, strand_rate * ROPE_STRANDS, 2.0, 2.2)
    drone = time_warp(drone, wander(n, rng, 5.0) * 90.0)

    # Wind across a moving rope: the aeolian hiss that sits under the note.
    hiss = bandpass(noise(n, rng, tilt=0.35), 700.0, 6000.0)
    hiss *= 0.6 + 0.4 * (0.5 + 0.5 * wander(n, rng, 4.0))
    return normalise(drone, 1.0) * 0.8 + normalise(hiss, 1.0) * 0.22


def _rope_hum(n, rng):
    drone = highpass(dc_block(rope_drone(n, rng)), 45.0, order=4)
    return normalise(soft_clip(drone, 1.1), 0.82)


def _gearbox_whine(n, rng):
    """The reduction gearbox in a drive terminal: two mesh orders and the shafts.

    A gear mesh is never one clean tone - every tooth is a fraction out, so the mesh
    carries sidebands at the shaft rate, and those sidebands are the difference between a
    gearbox and a sine wave.
    """
    shaft = DRIVE_MOTOR_RPM / 60.0
    mesh = shaft * DRIVE_PINION_TEETH
    second = mesh / 4.1                                  # the slow stage after the first

    whine = partial_stack(n, mesh, [1.0, 0.5, 0.24, 0.12], rng)
    for side in (-2, -1, 1, 2):
        whine += partial_stack(n, mesh + side * shaft, [0.35 / abs(side)], rng)
    whine += partial_stack(n, second, [0.6, 0.3, 0.15], rng)
    whine *= 0.82 + 0.18 * tone(n, shaft, 1.0)

    oil = bandpass(noise(n, rng, tilt=0.5), 300.0, 5000.0)
    case = resonate(lowpass(noise(n, rng, tilt=0.9), 300.0), 96.0, 2.2, 2.8)

    mix = (normalise(whine, 1.0) * 0.7 + normalise(oil, 1.0) * 0.22
           + normalise(case, 1.0) * 0.3)
    return normalise(soft_clip(highpass(dc_block(mix), 45.0, order=4), 1.1), 0.84)


def grip_clack(rng):
    """A detachable grip closing on the rope: spring, jaw, and the rope taking the load.

    Two impacts about forty milliseconds apart. One impact is a hammer; two in that order
    is a mechanism, and a player standing in a terminal hears this a thousand times a day.
    Public because the lift-line ambience bed scatters the same clack through its queue.
    """
    n = n_for(0.4)
    out = np.zeros(n)
    for offset, level, freq, decay in ((0.0, 1.0, 1750.0, 0.010),
                                       (0.041, 0.8, 1180.0, 0.016),
                                       (0.049, 0.45, 420.0, 0.030)):
        hit = burst(0.22, freq, decay, rng, noise_mix=0.55)
        start = n_for(offset)
        out[start:start + hit.size] += hit[:n - start] * level
    out = resonate(out, 2400.0, 3.0, 1.8)
    rope = burst(0.3, 140.0, 0.04, rng, noise_mix=0.3)
    out[n_for(0.05):n_for(0.05) + rope.size] += rope * 0.3
    return normalise(fade(dc_block(out), 0.001, 0.06), 0.92)


def _carpet_belt(n, rng):
    """A conveyor belt lift: rubber over rollers at walking pace.

    The slowest surface lift in lifts.json is the conveyor, and its LineSpeedMs is what
    sets the roller tick rate. Everything else about a carpet is quiet, which is the
    point of it as a beginner lift.
    """
    surface = [l for l in datasrc.lifts() if str(l.get("Family")) == "Surface"]
    speed = min([_num(l.get("LineSpeedMs"), 0.8) for l in surface] or [0.8])
    roller_pitch = 0.3
    tick_rate = speed / roller_pitch

    tick = burst(0.06, 210.0, 0.014, rng, noise_mix=0.6)
    ticks = pulse_train(n, tick_rate, tick, rng=rng, jitter=0.1,
                        amps=1.0 + rng.normal(0.0, 0.15, 5))
    belt = lowpass(noise(n, rng, tilt=0.85), 700.0)
    belt = resonate(belt, 118.0, 2.0, 2.6)
    drive = partial_stack(n, DRIVE_MOTOR_RPM / 60.0 * 2.0, [1.0, 0.45, 0.2], rng)
    hum = bandpass(noise(n, rng, tilt=0.5), 300.0, 3000.0)

    mix = (normalise(ticks, 1.0) * 0.4 + normalise(belt, 1.0) * 0.6
           + normalise(drive, 1.0) * 0.26 + normalise(hum, 1.0) * 0.18)
    return normalise(soft_clip(highpass(dc_block(mix), 45.0, order=4), 1.1), 0.8)


# --------------------------------------------------------------------------- snowmaking
def _snow_gun(n, rng):
    """A gun making snow: the compressor feeding it and the nucleators tearing air apart.

    A screw compressor's rotor lobes pass at a rate set by how big the machine is - a
    bigger air end turns slower - and stations.json says how big the plant is. Over that
    sits the hiss, which is the part a guest standing under a gun actually hears.
    """
    houses = datasrc.stations().get("CompressorStations") or []
    air = _median([_num(h.get("AirM3PerMin")) for h in houses], 40.0)
    rotor_rpm = 4200.0 * (20.0 / max(air, 5.0)) ** 0.2
    lobes = 5
    lobe_pass = rotor_rpm / 60.0 * lobes

    compressor = partial_stack(n, lobe_pass, [1.0, 0.62, 0.38, 0.22, 0.12, 0.07], rng)
    compressor = time_warp(compressor, wander(n, rng, 8.0) * 40.0)
    compressor = resonate(compressor, 180.0, 1.6, 2.2)

    nucleator = bandpass(noise(n, rng, tilt=0.12), 3000.0, 16000.0)
    water = bandpass(noise(n, rng, tilt=0.45), 600.0, 5200.0)
    # Nozzles do not hiss at a constant level: the plume breathes as the air and water
    # find their balance, and that slow movement is what stops a hiss loop reading as
    # tape noise.
    breath = 0.72 + 0.28 * wander(n, rng, 3.0)
    plume = (normalise(nucleator, 1.0) * 0.62 + normalise(water, 1.0) * 0.44) * breath

    mix = normalise(compressor, 1.0) * 0.4 + plume
    return normalise(soft_clip(highpass(dc_block(mix), 50.0, order=4), 1.1), 0.88)


# --------------------------------------------------------------------------- build
def build_all(out_dir):
    """Every machinery clip, in a fixed order so the manifest is stable."""
    n = n_for(config.LOOP_SECONDS)
    records = []

    def loop(name, samples, extra=None):
        records.append(write_clip(out_dir, "machinery_" + name, samples, "machinery",
                                  loop=True, extra=extra))

    def oneshot(name, samples, extra=None):
        records.append(write_clip(out_dir, "machinery_" + name, samples, "machinery",
                                  loop=False, extra=extra))

    flow = _hydraulic_flow()
    loop("hydraulic_pump", _hydraulic_pump(n, rng_for("machinery", "hydraulic_pump")),
         {"flowLpm": round(flow, 1), "rpm": round(tuning_value("vehicles.ratedRpm"), 0)})
    oneshot("hydraulic_valve", _hydraulic_valve(rng_for("machinery", "hydraulic_valve")))

    pitch = _grouser_pitch()
    for band, speed in _track_speeds().items():
        loop("track_rumble_" + band,
             _track_rumble(n, rng_for("machinery", "track_rumble", band), speed, pitch),
             {"band": band, "speedMs": round(speed, 2),
              "grouserPitchM": round(pitch, 3),
              "passHz": round(speed / pitch, 1)})

    loop("tiller_churn", _tiller_churn(n, rng_for("machinery", "tiller_churn")))
    loop("blower_roar", _blower_roar(n, rng_for("machinery", "blower_roar")))
    loop("bullwheel_hum", _bullwheel_hum(n, rng_for("machinery", "bullwheel_hum")))
    loop("rope_hum", _rope_hum(n, rng_for("machinery", "rope_hum")))
    loop("gearbox_whine", _gearbox_whine(n, rng_for("machinery", "gearbox_whine")))
    oneshot("grip_clack", grip_clack(rng_for("machinery", "grip_clack")))
    loop("snow_gun", _snow_gun(n, rng_for("machinery", "snow_gun")))
    loop("carpet_belt", _carpet_belt(n, rng_for("machinery", "carpet_belt")))
    return records


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.AUDIO_DIR
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
