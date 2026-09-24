"""Ambience beds and the UI tones: weather, crowds, interiors and the interface.

The four wind speeds come out of climate.json rather than out of a fader. A wind bed that
only gets louder sounds like a volume knob; what actually happens as wind rises is that
the SPECTRUM tilts, because faster air over an edge sheds smaller, faster eddies. So each
speed is built with its own noise slope, its own low-pass corner and its own aeolian
whistle frequency, and the gust modulation comes from the WindGustFactor in the same
period records the weather system reads.

The interiors are built out of the other two modules rather than beside them. The cab bed
is the engine set the most machines share, low-passed the way a heated cab low-passes it,
and the lift-line bed carries the rope note from audio.machinery. One synthesis of a
thing, heard from wherever the player is standing.

Every clip here is normalised into the same headroom. How loud a lodge is next to a storm
is a runtime mixer decision and the files should not pre-empt it; the one exception is
night quiet, which is left lower because being quieter than everything else is the whole
content of it.

Run standalone from tools/assetgen:  python3 -m audio.ambience [out_dir]
"""
import math

import numpy as np

import config
from audio import machinery
from audio.engines import (bandpass, burst, dc_block, engine_classes, fade, highpass,
                           loop_for, lowpass, n_for, noise, normalise, partial_stack,
                           pulse_train, report, resonate, rng_for, soft_clip,
                           tuning_value, wander, write_clip)
from lib import datasrc, validate

# Speaking is roughly four syllables a second whatever the language, and that rate is
# what makes a wash of filtered noise read as people rather than as noise.
SYLLABLE_HZ = 4.0

# Vowel formants, near enough. Three resonances in these regions are the difference
# between a crowd and a hiss.
FORMANTS = ((520.0, 2.6), (1480.0, 3.2), (2570.0, 3.6))

# An aeolian tone is Strouhal number times wind speed over the thing shedding the eddies,
# and what whistles at a lift station is wire-sized: guy lines, fence wire, ski poles.
STROUHAL = 0.2
WHISTLE_DIAMETER_M = 0.004


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _num(value, default=0.0):
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


# --------------------------------------------------------------------------- the data
def wind_speeds():
    """The four wind bands, in m/s, off the climate the simulation runs on.

    Calm is the quiet tail of a season whose mean is what the period records say; breeze
    is that mean; strong is the mean gust, because a gust factor of about two is what the
    records carry; and the storm band is the wind at which guests.stormWindKmh says the
    mountain stops being fun.
    """
    periods = datasrc.load("climate").get("Periods") or []
    means = [_num(p.get("WindMeanKmh")) for p in periods] or [15.0]
    gusts = [_num(p.get("WindGustFactor"), 1.9) for p in periods] or [1.9]
    mean = sum(means) / len(means)
    gust = sum(gusts) / len(gusts)
    storm = tuning_value("guests.stormWindKmh")
    kmh = {"calm": min(means) * 0.25, "breeze": mean, "strong": mean * gust, "storm": storm}
    return {k: v / 3.6 for k, v in kmh.items()}, gust


# --------------------------------------------------------------------------- weather
def wind_bed(n, rng, speed, gust=1.9, shelter=0.0):
    """A wind bed at `speed` m/s, tilted rather than turned up.

    `shelter` rolls the top off the way a wall or a windscreen does, which is how the
    lodge and the cab get their own wind without a second synthesis of it.
    """
    tilt = _clamp(1.15 - 0.052 * speed, 0.40, 1.15)
    corner = _clamp(330.0 * speed ** 1.25, 260.0, 11000.0) * (1.0 - 0.72 * shelter)
    bed = lowpass(noise(n, rng, tilt=tilt, lo=12.0), max(corner, 120.0), order=2)

    # Gusting: slow, uneven, and deeper the harder it blows. A steady wind bed is the
    # single most obvious sign of a synthesised one.
    swell = wander(n, rng, 1.2)
    depth = _clamp((gust - 1.0) * 0.34 * (0.35 + speed / 14.0), 0.05, 0.62)
    bed = bed * (1.0 - depth + depth * (0.5 + 0.5 * swell) * 2.0)

    if speed > 2.0:
        whistle_hz = STROUHAL * speed / WHISTLE_DIAMETER_M
        voice = bandpass(noise(n, rng, tilt=0.2), whistle_hz * 0.6, whistle_hz * 1.9)
        voice = resonate(voice, whistle_hz, 5.0, 4.0)
        voice *= 0.35 + 0.65 * (0.5 + 0.5 * swell)
        bed = bed + normalise(voice, 1.0) * _clamp(0.06 * (speed - 2.0), 0.02, 0.34)
        # Air pushing on structure: the low buffet under a strong wind.
        buffet = resonate(lowpass(noise(n, rng, tilt=1.1, hi=140.0), 90.0), 46.0, 2.2, 3.0)
        bed = bed + normalise(buffet, 1.0) * _clamp(0.03 * speed, 0.03, 0.3)
    return bed


def _wind(n, rng, speed, gust):
    return normalise(soft_clip(highpass(dc_block(wind_bed(n, rng, speed, gust)), 26.0,
                                        order=2), 1.1), 0.86)


def _snowfall(n, rng):
    """Snow falling: almost nothing, and the almost is the point.

    Crystals landing on a hood and a jacket are a sparse tick field, and under it sits the
    hush of air full of snow, which is really the absence of the high end a clear day has.
    Tick density follows the precipitation rate in the climate periods.
    """
    periods = datasrc.load("climate").get("Periods") or []
    rate = max([_num(p.get("PrecipMmPerDayMean")) for p in periods] or [8.0])
    ticks_per_second = _clamp(rate * 4.0, 8.0, 120.0)

    tick = bandpass(burst(0.008, 5200.0, 0.0011, rng, noise_mix=0.95), 2200.0, 12000.0)
    crystals = pulse_train(n, ticks_per_second, tick, rng=rng, jitter=0.42,
                           amps=np.abs(rng.normal(0.6, 0.35, 23)))
    hush = bandpass(noise(n, rng, tilt=0.6), 120.0, 1600.0)
    air = bandpass(noise(n, rng, tilt=0.45), 900.0, 9000.0)
    air *= 0.6 + 0.4 * wander(n, rng, 1.5)

    mix = (normalise(crystals, 1.0) * 0.5 + normalise(hush, 1.0) * 0.6
           + normalise(air, 1.0) * 0.44)
    return normalise(soft_clip(highpass(dc_block(mix), 70.0, order=4), 1.1), 0.8)


# --------------------------------------------------------------------------- people
def babble(n, rng, voices=14, distance=0.0):
    """A crowd, built one voice at a time.

    Each voice is noise through three formants, gated at a syllable rate with its own
    phase and its own pitch of filter, and the sum of a dozen of those is a murmur. Doing
    it per voice rather than as one band of noise is what gives a crowd its texture: the
    level keeps moving because individual people keep starting and stopping.
    """
    out = np.zeros(n)
    for v in range(voices):
        pitch = 0.72 + 0.62 * rng.random()               # taller and shorter vocal tracts
        voice = noise(n, rng, tilt=0.55, lo=110.0, hi=5200.0)
        for freq, q in FORMANTS:
            voice = resonate(voice, freq * pitch, q, 3.2)
        syllables = wander(n, rng, SYLLABLE_HZ * 2.0, tilt=0.25)
        gate = np.clip(syllables * 1.6 + rng.uniform(-0.5, 0.25), 0.0, 1.0) ** 1.4
        out += normalise(voice, 1.0) * gate * (0.5 + 0.5 * rng.random())
    out = normalise(out, 1.0)
    if distance > 0.0:
        # Distance is a low-pass and a loss of the consonants, not a fader.
        out = lowpass(out, _clamp(6000.0 * (1.0 - distance) ** 2, 380.0, 6000.0), order=2)
        out = out + normalise(lowpass(out, 300.0), 1.0) * 0.3 * distance
    return normalise(out, 1.0)


def _crowd(n, rng):
    murmur = babble(n, rng, voices=16, distance=0.25)
    steps = pulse_train(n, 9.0, bandpass(burst(0.09, 190.0, 0.02, rng, noise_mix=0.8),
                                         120.0, 3200.0),
                        rng=rng, jitter=0.4, amps=np.abs(rng.normal(0.7, 0.3, 17)))
    room = resonate(lowpass(noise(n, rng, tilt=0.95), 700.0), 150.0, 1.8, 2.0)
    mix = murmur * 0.8 + normalise(steps, 1.0) * 0.3 + normalise(room, 1.0) * 0.22
    return normalise(soft_clip(highpass(dc_block(mix), 45.0, order=2), 1.1), 0.84)


def _lift_line(n, rng):
    """The bottom of a lift: people, the rope note off the line, and the PA.

    The PA is a horn speaker on a long run of cable, so what carries from it between
    announcements is mains hum and the thump of the amplifier keying up - which is the
    detail that makes a queue sound like a lift queue rather than like a crowd.
    """
    murmur = babble(n, rng, voices=18, distance=0.2)
    rope = machinery.rope_drone(n, rng)
    line = lowpass(rope, 2600.0, order=2)

    clack = machinery.grip_clack(rng)
    grips = np.zeros(n)
    for offset in (0.6, 2.9, 4.4):                       # carriers coming through the drive
        start = n_for(offset)
        tail = min(clack.size, n - start)
        grips[start:start + tail] += clack[:tail] * 0.3

    pa_hum = partial_stack(n, 100.0, [1.0, 0.45, 0.2, 0.1], rng)
    pa_hum = bandpass(pa_hum, 90.0, 1200.0)
    thump = burst(0.5, 78.0, 0.09, rng, noise_mix=0.3)
    pa = np.zeros(n)
    start = n_for(1.8)
    pa[start:start + thump.size] += thump[:n - start] * 0.8
    pa = bandpass(pa + pa_hum * 0.25, 110.0, 3400.0)     # a horn has no bottom and no top

    mix = (murmur * 0.66 + normalise(line, 1.0) * 0.42 + normalise(grips, 1.0) * 0.26
           + normalise(pa, 1.0) * 0.3)
    return normalise(soft_clip(highpass(dc_block(mix), 45.0, order=2), 1.1), 0.86)


def _lodge(n, rng):
    """Inside a base lodge: a room full of people, hard surfaces, trays and ventilation."""
    murmur = babble(n, rng, voices=22, distance=0.45)
    clinks = pulse_train(n, 3.2, bandpass(burst(0.25, 3100.0, 0.035, rng, noise_mix=0.35),
                                          1400.0, 9000.0),
                         rng=rng, jitter=0.45, amps=np.abs(rng.normal(0.55, 0.3, 13)))
    trays = pulse_train(n, 1.1, burst(0.2, 420.0, 0.03, rng, noise_mix=0.75),
                        rng=rng, jitter=0.5, amps=np.abs(rng.normal(0.6, 0.3, 7)))
    hvac = resonate(lowpass(noise(n, rng, tilt=1.0), 520.0), 78.0, 2.0, 2.6)
    # A big timber room rings low and long, and that ring is most of why a lodge does not
    # sound like a street.
    room = resonate(normalise(murmur, 1.0), 190.0, 2.4, 1.8)

    mix = (murmur * 0.6 + room * 0.3 + normalise(clinks, 1.0) * 0.22
           + normalise(trays, 1.0) * 0.16 + normalise(hvac, 1.0) * 0.4)
    return normalise(soft_clip(highpass(dc_block(mix), 38.0, order=2), 1.1), 0.85)


# --------------------------------------------------------------------------- interiors
def _cab(n, rng):
    """Inside a heated cab: the engine through the glass, the heater, and the shell.

    The engine bed is the set the most machines share, because that is the cab a player
    is most often sitting in, and it is the same synthesis the exterior loop uses. A cab
    does not attenuate evenly - glass and insulation take the top off and leave the low
    orders, which is why this is a low-pass and a resonance rather than a fader.
    """
    classes = engine_classes()
    spec = max(classes, key=lambda c: len(c["machines"]))
    engine = loop_for(spec, "low", n, rng_for("ambience", "cab", spec["id"]))
    muffled = lowpass(engine, 420.0, order=4)
    muffled = muffled + lowpass(engine, 1600.0, order=2) * 0.26
    muffled = resonate(muffled, 96.0, 2.0, 2.4)          # the cab shell's own note

    fan = bandpass(noise(n, rng, tilt=0.5), 200.0, 4200.0)
    fan = resonate(fan, 520.0, 1.6, 2.2)
    fan *= 0.86 + 0.14 * wander(n, rng, 2.5)
    outside = wind_bed(n, rng, 5.0, shelter=0.85) * 0.5
    rattle = pulse_train(n, 2.3, burst(0.09, 260.0, 0.018, rng, noise_mix=0.8),
                         rng=rng, jitter=0.5, amps=np.abs(rng.normal(0.4, 0.3, 5)))

    mix = (normalise(muffled, 1.0) * 0.7 + normalise(fan, 1.0) * 0.6
           + normalise(outside, 1.0) * 0.24 + normalise(rattle, 1.0) * 0.12)
    return normalise(soft_clip(highpass(dc_block(mix), 30.0, order=2), 1.1), 0.87)


def _night(n, rng):
    """The mountain after the lifts stop: wind, one machine working somewhere, nothing else.

    Left quieter than the other beds on purpose. Everything else here is normalised into
    the same headroom because the mixer sets level, but a night bed that arrives at the
    same peak as a storm has had the one thing it is for taken out of it.
    """
    classes = engine_classes()
    spec = max(classes, key=lambda c: len(c["machines"]))
    far = loop_for(spec, "mid", n, rng_for("ambience", "night", spec["id"]))
    # Two kilometres of cold air is a long low-pass, and the distance moves with the wind.
    far = lowpass(far, 240.0, order=4)
    far *= 0.45 + 0.55 * (0.5 + 0.5 * wander(n, rng, 0.6))

    air = wind_bed(n, rng, 2.2, gust=1.6)
    settle = pulse_train(n, 0.8, bandpass(burst(0.12, 900.0, 0.02, rng, noise_mix=0.9),
                                          400.0, 6000.0),
                         rng=rng, jitter=0.5, amps=np.abs(rng.normal(0.3, 0.2, 5)))

    mix = (normalise(air, 1.0) * 0.7 + normalise(far, 1.0) * 0.26
           + normalise(settle, 1.0) * 0.12)
    return normalise(highpass(dc_block(mix), 28.0, order=2), 0.55)


# --------------------------------------------------------------------------- interface
def _ui_tone(n, freqs, decay, rng, start=0.0, level=1.0, bright=0.35):
    """One struck note of the interface set: a few partials with a bell's decay.

    The interface is the sound a player hears most often in a shift, so these are soft
    sines rather than square edges, and each one decays instead of stopping.
    """
    out = np.zeros(n)
    offset = n_for(start)
    room = n - offset
    if room <= 0:
        return out
    seconds = room / float(config.SAMPLE_RATE)
    for i, freq in enumerate(freqs):
        body = (burst(seconds, freq, decay, rng)
                + burst(seconds, freq * 2.0, decay * 0.4, rng) * bright)
        tail = min(body.size, room)
        out[offset:offset + tail] += body[:tail] * (level * (0.85 ** i))
    return out


def _ui_click(rng):
    n = n_for(0.07)
    tick = bandpass(burst(0.05, 2600.0, 0.0025, rng, noise_mix=0.75), 900.0, 9000.0)
    out = np.zeros(n)
    out[:tick.size] += tick[:n] * 0.9
    out += _ui_tone(n, (2100.0,), 0.010, rng, level=0.5, bright=0.2)
    return normalise(fade(dc_block(out), 0.0008, 0.012), 0.72)


def _ui_confirm(rng):
    n = n_for(0.34)
    out = (_ui_tone(n, (784.0, 1568.0), 0.07, rng, start=0.0, level=0.7)
           + _ui_tone(n, (1046.5, 2093.0), 0.10, rng, start=0.085, level=0.8))
    return normalise(fade(dc_block(out), 0.002, 0.05), 0.8)


def _ui_cancel(rng):
    n = n_for(0.34)
    out = (_ui_tone(n, (622.3,), 0.07, rng, start=0.0, level=0.8, bright=0.25)
           + _ui_tone(n, (415.3,), 0.12, rng, start=0.08, level=0.85, bright=0.2))
    return normalise(fade(dc_block(out), 0.002, 0.05), 0.78)


def _ui_alert(rng):
    """Something needs attention now: three hard pulses, which is what an alarm is."""
    n = n_for(0.78)
    out = np.zeros(n)
    for i in range(3):
        out += _ui_tone(n, (880.0, 1760.0, 2640.0), 0.055, rng, start=0.22 * i,
                        level=0.85, bright=0.6)
    return normalise(fade(dc_block(out), 0.002, 0.04), 0.88)


def _ui_warning(rng):
    """Something is going wrong but not yet: two falling tones, lower and slower."""
    n = n_for(0.62)
    out = (_ui_tone(n, (466.2, 932.3), 0.12, rng, start=0.0, level=0.85, bright=0.45)
           + _ui_tone(n, (349.2, 698.5), 0.16, rng, start=0.2, level=0.85, bright=0.4))
    return normalise(fade(dc_block(out), 0.002, 0.05), 0.82)


def _ui_success(rng):
    n = n_for(0.8)
    out = np.zeros(n)
    for i, freq in enumerate((523.3, 659.3, 784.0, 1046.5)):
        out += _ui_tone(n, (freq, freq * 2.0), 0.16, rng, start=0.055 * i,
                        level=0.75, bright=0.3)
    return normalise(fade(dc_block(out), 0.002, 0.08), 0.84)


# --------------------------------------------------------------------------- build
def build_all(out_dir):
    """Every ambience bed and every interface tone, in a fixed order."""
    n = n_for(config.AMBIENCE_SECONDS)
    speeds, gust = wind_speeds()
    records = []

    for band in ("calm", "breeze", "strong", "storm"):
        speed = speeds[band]
        clip = _wind(n, rng_for("ambience", "wind", band), speed, gust)
        records.append(write_clip(out_dir, "ambience_wind_" + band, clip, "ambience",
                                  loop=True,
                                  extra={"band": band, "speedMs": round(speed, 2),
                                         "speedKmh": round(speed * 3.6, 1),
                                         "gustFactor": round(gust, 2)}))

    for name, maker in (("snowfall", _snowfall), ("crowd", _crowd),
                        ("lift_line", _lift_line), ("lodge", _lodge),
                        ("cab", _cab), ("night", _night)):
        clip = maker(n, rng_for("ambience", name))
        records.append(write_clip(out_dir, "ambience_" + name, clip, "ambience",
                                  loop=True))

    for name, maker in (("click", _ui_click), ("confirm", _ui_confirm),
                        ("cancel", _ui_cancel), ("alert", _ui_alert),
                        ("warning", _ui_warning), ("success", _ui_success)):
        clip = maker(rng_for("ui", name))
        records.append(write_clip(out_dir, "ui_" + name, clip, "ui", loop=False))
    return records


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.AUDIO_DIR
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
