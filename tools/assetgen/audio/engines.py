"""Engine loops and start/stop one-shots, one set per chassis class.

Fifty-eight machines do not need fifty-eight engines. What a listener can actually tell
apart is a four-cylinder diesel from a six, a naturally aspirated compact from a
turbocharged heavy, and a combustion machine from an electric one, so vehicles.json is
grouped by Category and a power band and each group gets one set: four rpm bands the
runtime crossfades between, plus a start and a stop one-shot. Move a machine's
EnginePowerKw or change its FuelType and it lands in a different set on the next build.

Everything about a set is read from the records in it. The cylinder count comes from
power and fuel, the four band rpms come from TorqueCurve, the turbo comes from whether an
engine that size would really be built with one, and the exhaust resonance comes from the
pipe length a machine that size would carry. Nothing here names a machine.

The generated engine set is placeholder, exactly as docs/ART_CONTRACT.md section 8 says.
A harmonic stack on a real firing interval, with combustion jitter, injector clatter,
intake and exhaust resonances and a turbo that spools with load, is structurally right
and audibly synthetic. It exists so the game is never silent and so the crossfade rig is
exercised end to end. Recorded material drops in at the same paths later and no code
changes.

Loops are seamless by construction rather than by crossfade. Every partial is placed on a
bin of the loop's own spectrum, so it closes a whole number of cycles; noise is the
inverse FFT of a random-phase spectrum, which repeats exactly where a run of white noise
does not; firing pulses wrap round the end of the buffer into its head. All that is left
is where the file starts, and `seam_align` rotates the buffer to its quietest sample step,
which costs nothing because a periodic buffer is the same loop read from any offset.

This module also carries the synthesis toolkit the other two audio modules use. One copy
of it lives here rather than three copies drifting apart across the package.

Run standalone from tools/assetgen:  python3 -m audio.engines [out_dir]
"""
import math
import os
import wave

import numpy as np
from scipy import signal

import config
from lib import datasrc, validate

SR = config.SAMPLE_RATE


# --------------------------------------------------------------------------- toolkit
def n_for(seconds):
    """Whole samples for a duration. A loop is an exact number of samples long, which is
    what lets every partial in it close an exact number of cycles."""
    return int(round(seconds * SR))


def time_axis(n):
    return np.arange(n, dtype=np.float64) / SR


def bins(n):
    return np.fft.rfftfreq(n, 1.0 / SR)


def cycles_for(freq, n):
    """How many whole cycles of `freq` fit in an n-sample loop, at least one."""
    return max(1, int(round(freq * n / SR)))


def loop_freq(freq, n):
    """The nearest frequency to `freq` that closes on itself in an n-sample loop."""
    return cycles_for(freq, n) * SR / n


def tone(n, freq, amp=1.0, phase=0.0):
    return amp * np.sin(2.0 * np.pi * loop_freq(freq, n) * time_axis(n) + phase)


def partial_stack(n, f0, amps, rng, jitter_cents=0.0):
    """Additive partials written straight into the spectrum.

    One inverse FFT instead of a hundred sine calls, and the result is periodic by
    construction: harmonic k lands on bin k*c where c is the fundamental's whole cycle
    count, so it cannot help but close. `amps` is the amplitude of each harmonic starting
    at the fundamental. Phases are random because zero phase would stack every partial
    into one impulse per cycle and spend the whole headroom on it.
    """
    spec = np.zeros(n // 2 + 1, dtype=np.complex128)
    c0 = cycles_for(f0, n)
    scale = n * 0.5
    for k, amp in enumerate(amps, start=1):
        if amp <= 0.0:
            continue
        b = k * c0
        if jitter_cents:
            b = int(round(b * (2.0 ** (rng.normal(0.0, jitter_cents) / 1200.0))))
        if b <= 0 or b >= spec.size:
            continue
        spec[b] += amp * scale * np.exp(1j * rng.uniform(0.0, 2.0 * np.pi))
    return np.fft.irfft(spec, n)


def noise(n, rng, tilt=0.0, lo=18.0, hi=None):
    """Circular noise with an f**-tilt amplitude slope, band-limited to [lo, hi].

    Built in the frequency domain because a loop has to be periodic to be seamless: the
    inverse FFT of a random-phase spectrum tiles exactly, where a run of white noise
    stops wherever it happens to be. tilt 0 is white, 0.5 pink, 1.0 brown.
    """
    hi = min(hi if hi else SR * 0.5, SR * 0.5 * 0.98)
    freqs = bins(n)
    mag = np.zeros(freqs.size)
    band = (freqs >= lo) & (freqs <= hi)
    mag[band] = freqs[band] ** (-tilt) if tilt else 1.0
    mag[0] = 0.0
    spec = mag * np.exp(1j * rng.uniform(0.0, 2.0 * np.pi, freqs.size))
    x = np.fft.irfft(spec, n)
    return x / (float(np.sqrt(np.mean(x * x))) + 1e-12)


def wander(n, rng, rate, tilt=1.0):
    """A slow random envelope in -1..1, band-limited to `rate` and periodic with the loop.

    Gusting wind, a plume breathing, an engine hunting at idle: all of them are something
    that moves over seconds rather than over milliseconds. Built from the lowest bins of
    the loop's own spectrum, so it wraps exactly, and so it cannot be asked for a movement
    slower than one cycle per loop - which is also the honest limit of what a loop can
    hold. A tilt of 1 puts the weight on the slowest movement; 0 spreads it evenly up to
    `rate`, which is what a syllable rate or a flutter wants.
    """
    count = max(1, int(round(rate * n / SR)))
    spec = np.zeros(n // 2 + 1, dtype=np.complex128)
    k = np.arange(1, min(count, spec.size - 1) + 1)
    mag = (n * 0.5) * k.astype(np.float64) ** -tilt
    spec[k] = mag * np.exp(1j * rng.uniform(0.0, 2.0 * np.pi, k.size))
    x = np.fft.irfft(spec, n)
    return x / (float(np.max(np.abs(x))) + 1e-12)


def apply_gain(x, gain):
    """Zero-phase spectral shaping. Circular, so it never smears a loop's seam the way a
    time-domain filter's start-up transient would."""
    return np.fft.irfft(np.fft.rfft(x) * gain, x.size)


def butter_gain(freqs, cutoff, order=4, kind="low"):
    """Magnitude response of a Butterworth filter, sampled at the loop's own bins.

    Designed with scipy across the audio band. A corner down at a few hertz is the one
    case that has to be handled differently: a digital Butterworth that far below Nyquist
    collapses into its own rounding error and comes back as NaN. Shaping here is
    zero-phase, so the analytic magnitude below that point is not an approximation of the
    filter, it is the same filter without the conditioning problem.
    """
    wn = cutoff / (SR * 0.5)
    if 1e-3 < wn < 0.999:
        b, a = signal.butter(order, wn, btype=kind)
        _, h = signal.freqz(b, a, worN=freqs * (2.0 * np.pi / SR))
        return np.abs(h)
    ratio = freqs / max(cutoff, 1e-6)
    roll = 1.0 / np.sqrt(1.0 + ratio ** (2 * order))
    return roll if kind == "low" else 1.0 - roll


def lowpass(x, cutoff, order=4):
    return apply_gain(x, butter_gain(bins(x.size), cutoff, order, "low"))


def highpass(x, cutoff, order=4):
    return apply_gain(x, butter_gain(bins(x.size), cutoff, order, "high"))


def bandpass(x, lo, hi, order=2):
    freqs = bins(x.size)
    return apply_gain(x, butter_gain(freqs, lo, order, "high")
                      * butter_gain(freqs, hi, order, "low"))


def resonance(freqs, f0, q):
    """Magnitude of one second-order resonance, normalised to unity at its peak.

    A pipe, an airbox and a housing all colour what passes through them by ringing at
    their own frequency; this is the cheapest honest version of that.
    """
    w = freqs / max(f0, 1e-6)
    mag = 1.0 / np.sqrt((1.0 - w * w) ** 2 + (w / max(q, 0.05)) ** 2)
    return mag / float(mag.max())


def resonate(x, f0, q, gain):
    """Lift one resonance out of a signal without touching the rest of its spectrum."""
    freqs = bins(x.size)
    return apply_gain(x, 1.0 + (gain - 1.0) * resonance(freqs, f0, q))


def burst(seconds, freq, decay, rng=None, noise_mix=0.0):
    """One decaying impact: a struck resonance, optionally roughened with noise.

    The short rise is not decoration - starting a decaying sine at full amplitude puts a
    step in the signal, and a step is a click on every single event.
    """
    m = max(4, n_for(seconds))
    t = time_axis(m)
    env = np.exp(-t / max(decay, 1e-4)) * (1.0 - np.exp(-t / 0.0006))
    body = np.sin(2.0 * np.pi * freq * t)
    if noise_mix and rng is not None:
        body = (1.0 - noise_mix) * body + noise_mix * rng.standard_normal(m)
    return body * env


def pulse_train(n, rate, pulse, rng=None, jitter=0.0, amps=None, count=None):
    """Place `pulse` at every event, wrapping the tail of the last one into the head.

    The wrap is what keeps a firing sequence loopable: an event 20 ms before the end of
    the buffer rings on into the start of it, which is exactly what it does on the next
    repeat. `count` overrides the event count so a pulse train can be locked to the
    harmonic stack's cycle count instead of rounding to its own.
    """
    count = int(count if count else max(1, round(rate * n / SR)))
    step = n / float(count)
    out = np.zeros(n)
    offsets = np.zeros(count)
    if jitter and rng is not None:
        offsets = rng.normal(0.0, jitter * step, count)
        offsets -= offsets.mean()        # keep the mean firing rate exactly on the loop
    idx = np.arange(pulse.size)
    for i in range(count):
        p = i * step + offsets[i]
        base = int(math.floor(p))
        frac = p - base
        amp = 1.0 if amps is None else amps[i % len(amps)]
        out[(base + idx) % n] += pulse * (amp * (1.0 - frac))
        out[(base + 1 + idx) % n] += pulse * (amp * frac)
    return out


def time_warp(x, deviation):
    """Resample along a wobbling time axis.

    Combustion is not a metronome: the interval between firings drifts by a per cent or
    two and that irregularity is most of what separates an engine from an organ. Warping
    the finished stack shifts every harmonic by the same amount of TIME, which is what a
    late firing really does, and stays periodic because the deviation is periodic.
    """
    n = x.size
    idx = (np.arange(n, dtype=np.float64) + deviation) % n
    return np.interp(idx, np.arange(n + 1, dtype=np.float64), np.append(x, x[0]))


def dc_block(x):
    return x - float(np.mean(x))


def normalise(x, peak=0.9):
    top = float(np.max(np.abs(x)))
    return x * (peak / top) if top > 1e-9 else x


def soft_clip(x, drive=1.0):
    """Tanh saturation. Keeps a loud mix inside the rails without the flat top of a hard
    clip, which the contract fails anyway."""
    return np.tanh(x * drive) / math.tanh(drive)


def fade(x, seconds_in=0.004, seconds_out=0.03):
    """Taper a one-shot to silence at both ends so triggering it never clicks."""
    out = x.copy()
    a = min(n_for(seconds_in), out.size // 2)
    b = min(n_for(seconds_out), out.size // 2)
    if a:
        out[:a] *= np.linspace(0.0, 1.0, a)
    if b:
        out[-b:] *= np.linspace(1.0, 0.0, b)
    return out


def seam_align(x):
    """Rotate a periodic buffer to its quietest sample step.

    Any rotation of a periodic buffer is the same loop, so this changes nothing a
    listener can hear; what it does change is which sample the file begins on, and the
    contract measures the gap between the first and the last. Rotating to the smallest
    step in the whole buffer puts the file boundary somewhere the waveform was barely
    moving.
    """
    step = np.abs(x - np.roll(x, 1))
    return np.roll(x, -int(np.argmin(step)))


def seam(x):
    """The discontinuity a player would hear at the loop point."""
    return abs(float(x[0]) - float(x[-1]))


def tuning_value(path):
    """One dotted key out of tuning.json, the same way the simulation reads it."""
    node = datasrc.tuning()
    for part in path.split("."):
        node = node[part]
    return float(node["value"])


def write_clip(out_dir, name, samples, group, loop=True, extra=None):
    """Validate, write a 48 kHz 16-bit mono WAV and return its manifest record.

    Validation runs on the float signal before anything is written, so a clip that
    clips, that is silent or that would click at its loop point fails the build instead
    of shipping.
    """
    x = np.asarray(samples, dtype=np.float64)
    if loop:
        x = seam_align(x)
    validate.check_audio(name, x, SR, loop=loop)

    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".wav")
    pcm = np.clip(np.round(x * 32767.0), -32768.0, 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(config.AUDIO_BITS // 8)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())

    record = {
        "id": name,
        "path": config.rel_to_root(path),
        "resource": None,
        "kind": "audio",
        "group": group,
        "sampleRate": SR,
        "bits": config.AUDIO_BITS,
        "channels": 1,
        "seconds": round(x.size / float(SR), 4),
        "samples": int(x.size),
        "loop": bool(loop),
        "peak": round(float(np.max(np.abs(x))), 4),
        "rms": round(float(np.sqrt(np.mean(x * x))), 4),
        "seam": round(seam(x), 5),
    }
    if os.path.abspath(path).startswith(os.path.abspath(config.RES_ROOT)):
        record["resource"] = config.resource_path(path)
    if extra:
        record.update(extra)
    return record


def rng_for(*parts):
    """A seeded generator. Two runs of the pipeline have to produce identical WAVs, so
    nothing in the package touches an unseeded RNG."""
    return np.random.default_rng(datasrc.seed_for(*parts))


# --------------------------------------------------------------------------- classes
# Power bands, in kW. The split is where the real hardware splits: a sub-70 kW machine
# carries a small three or four cylinder engine, 200 kW is the top of a working four or a
# mid six, 450 kW is about as far as a road-going six goes, and above that a blower is
# carrying an engine out of a different catalogue entirely.
POWER_BANDS = (("compact", 70.0), ("mid", 200.0), ("heavy", 450.0), ("industrial", None))

# A diesel is the fleet default and needs no tag in the name; anything else is called out
# so a sound designer replacing these files can see at a glance what a set is.
DRIVE_TAG = {"diesel": "", "gasoline": "gas", "electric": "elec", "hybrid": "hybrid"}

# Cylinder count by output, which is the thing that sets the firing interval and so most
# of the timbre. A 20 kW diesel is a three, a 60 kW one a four, anything from a working
# machine up to a big truck a six, and a 750 kW blower engine a vee twelve. Petrol runs
# the other way: a walk-behind is a single, a snowmobile a twin or a triple, and a pickup
# at 220 kW is a vee eight.
DIESEL_CYLINDERS = ((25.0, 3), (100.0, 4), (450.0, 6), (600.0, 8), (None, 12))
PETROL_CYLINDERS = ((15.0, 1), (50.0, 2), (120.0, 3), (None, 8))

# Below this a diesel is naturally aspirated; above it the machine has a turbo and the
# whine that comes with it.
TURBO_FROM_KW = 75.0

# How hard the engine is working in each band. The runtime crossfades between the four,
# so they have to differ in more than pitch: load opens the exhaust, spools the turbo and
# tilts the harmonic rolloff towards the top end.
BAND_LOAD = {"idle": 0.06, "low": 0.30, "mid": 0.62, "high": 1.0}


def _num(value, default=0.0):
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _drive(record):
    """What kind of powerplant a record describes, or None if it has none at all.

    A snow gun and a fuel cube carry no engine: they are plumbing, and the data says so
    with a fuel type of None and zero kilowatts. A diesel-electric with no pack in the
    data is a diesel here, because what a bystander hears is the engine running at its
    sweet spot, not the track motors.
    """
    power = _num(record.get("EnginePowerKw"))
    battery = _num(record.get("BatteryKwh"))
    fuel = str(record.get("FuelType") or "None")
    if fuel == "Electric" or (battery > 0.0 and power <= 0.0):
        return "electric" if power > 0.0 or battery > 0.0 else None
    if power <= 0.0 or fuel == "None":
        return None
    if battery > 0.0:
        return "hybrid"
    return "gasoline" if fuel == "Gasoline" else "diesel"


def _band(power):
    for name, ceiling in POWER_BANDS:
        if ceiling is None or power <= ceiling:
            return name
    return POWER_BANDS[-1][0]


def _cylinders(power, drive):
    table = PETROL_CYLINDERS if drive == "gasoline" else DIESEL_CYLINDERS
    for ceiling, count in table:
        if ceiling is None or power < ceiling:
            return count
    return table[-1][1]


def _median(values):
    ordered = sorted(values)
    return ordered[len(ordered) // 2]


def _rpm_bands(members):
    """The four band rpms, read off the torque curves the simulation runs on.

    Idle is the bottom of the curve and the high band is its top, because that is the
    range the engine is modelled over. The two in between hang off peak torque: an engine
    pulling hard sits at its torque peak, and the low band is halfway down to idle.
    """
    points = [p for m in members for p in (m.get("TorqueCurve") or [])]
    if not points:
        idle = tuning_value("vehicles.idleRpm")
        rated = tuning_value("vehicles.ratedRpm")
        peak = idle + (rated - idle) * 0.45
    else:
        idle = min(_num(p.get("Rpm")) for p in points)
        rated = max(_num(p.get("Rpm")) for p in points)
        peaks = []
        for m in members:
            curve = m.get("TorqueCurve") or []
            if curve:
                peaks.append(_num(max(curve, key=lambda p: _num(p.get("TorqueNm")))["Rpm"]))
        peak = sum(peaks) / len(peaks) if peaks else idle + (rated - idle) * 0.45
    return {"idle": idle, "low": 0.5 * (idle + peak), "mid": peak, "high": rated}


def engine_classes():
    """Group vehicles.json into the handful of engine sets the fleet actually needs.

    File order throughout, so the build is deterministic and a class keeps its name as
    long as the first machine that claimed it stays in the file.
    """
    order = []
    groups = {}
    for record in datasrc.vehicles():
        drive = _drive(record)
        if drive is None:
            continue
        power = _num(record.get("EnginePowerKw"))
        key = "_".join(p for p in (str(record.get("Category", "")).lower(),
                                   DRIVE_TAG[drive], _band(power)) if p)
        if key not in groups:
            groups[key] = []
            order.append(key)
        groups[key].append(record)

    classes = []
    for key in order:
        members = groups[key]
        power = _median([_num(m.get("EnginePowerKw")) for m in members])
        drive = _drive(members[0])
        cylinders = 0 if drive == "electric" else _cylinders(power, drive)
        classes.append({
            "id": key,
            "drive": drive,
            "powerKw": power,
            "cylinders": cylinders,
            "turbo": drive in ("diesel", "hybrid") and power >= TURBO_FROM_KW,
            "rpm": _rpm_bands(members),
            "machines": [m["Id"] for m in members],
        })
    return classes


def _exhaust_hz(power):
    """Quarter-wave resonance of the exhaust a machine that size would carry.

    Pipe length runs with engine size, and a closed pipe rings at c/4L, so a compact
    machine barks around 100 Hz and a big six rumbles down near 40. This one number does
    more for telling the classes apart than any amount of harmonic tweaking.
    """
    length = _clamp(0.85 * (max(power, 5.0) / 150.0) ** 0.34, 0.55, 2.6)
    return 343.0 / (4.0 * length)


def _intake_hz(power):
    """Airbox resonance: smaller box, higher note."""
    return _clamp(620.0 * (150.0 / max(power, 8.0)) ** 0.22, 210.0, 900.0)


def _turbo_hz(rpm, load):
    """Turbo shaft speed as a multiple of crank speed.

    A turbo is driven by exhaust energy, not by a gear, so it spools with load as well as
    with rpm: barely turning at idle, a couple of hundred times a second when the engine
    is pulling. The whine a bystander hears is this shaft rate and its blade passes.
    """
    return (rpm / 60.0) * (11.0 + 27.0 * load)


# --------------------------------------------------------------------------- synthesis
def _combustion_loop(spec, band, n, rng):
    """One rpm band of a combustion class.

    The body is a harmonic stack on the engine cycle, which for a four-stroke is two
    revolutions: harmonic k is order k/2, the firing order lands on k = cylinders, and
    the odd harmonics in between are the half orders that make an idle lumpy. Everything
    else is layered on top of that: the exhaust pulse per firing, injector and valve
    clatter, intake breathing and the turbo.
    """
    rpm = spec["rpm"][band]
    load = BAND_LOAD[band]
    cyl = spec["cylinders"]
    f_cycle = rpm / 120.0
    cycles = cycles_for(f_cycle, n)
    f_fire = f_cycle * cyl
    f_exhaust = _exhaust_hz(spec["powerKw"])

    k_max = int(_clamp(5200.0 / max(f_cycle, 1.0), 12, 120))
    rolloff = 1.80 - 0.55 * load
    amps = []
    for k in range(1, k_max + 1):
        amp = k ** -rolloff
        if k % cyl == 0:
            amp *= 3.4                  # the firing order and its multiples carry the note
        elif k % 2 == 0:
            amp *= 1.15                 # whole engine orders
        else:
            amp *= 0.40                 # half orders: the roughness under an idle
        amps.append(amp)
    body = partial_stack(n, f_cycle, amps, rng)
    body = resonate(body, f_exhaust, 2.2, 2.6 + 1.4 * load)

    # Per-cycle irregularity. An idle wanders more than an engine under load does, which
    # is why the depth falls as load rises.
    wobble = wander(n, rng, 14.0)
    body = time_warp(body, wobble * (SR / max(f_fire, 1.0)) * 0.045 * (1.3 - 0.6 * load))

    # No two cylinders make quite the same power, and the imbalance repeats once per
    # engine cycle, which is audible as the loping an idling diesel has.
    imbalance = 1.0 + rng.normal(0.0, 0.09, max(cyl, 1))

    fire = pulse_train(n, f_fire,
                       burst(0.055, f_exhaust, 0.011 + 0.004 * load, rng, noise_mix=0.25),
                       rng=rng, jitter=0.02 * (1.4 - load), amps=imbalance,
                       count=cyl * cycles)

    # Diesel clatter is the injectors and the valve train, twice per firing and up in the
    # 1 to 4 kHz band where a diesel sounds like a diesel and a petrol engine does not.
    if spec["drive"] in ("diesel", "hybrid"):
        tick = bandpass(burst(0.012, 2400.0, 0.0016, rng, noise_mix=0.92), 900.0, 5200.0)
        clatter = pulse_train(n, f_fire * 2.0, tick, rng=rng, jitter=0.05,
                              amps=1.0 + rng.normal(0.0, 0.2, max(cyl, 1) * 2),
                              count=cyl * 2 * cycles)
    else:
        clatter = np.zeros(n)

    intake = bandpass(noise(n, rng, tilt=0.35), _intake_hz(spec["powerKw"]) * 0.45,
                      _intake_hz(spec["powerKw"]) * 2.6)
    intake *= 0.55 + 0.45 * np.abs(tone(n, f_fire, 1.0))      # breathing, once per firing
    intake = resonate(intake, _intake_hz(spec["powerKw"]), 1.6, 3.0)

    if spec["turbo"]:
        f_turbo = _turbo_hz(rpm, load)
        turbo = (partial_stack(n, f_turbo, [1.0, 0.55, 0.30, 0.16], rng)
                 + 0.5 * bandpass(noise(n, rng), f_turbo * 0.85, f_turbo * 3.4))
        turbo = time_warp(turbo, wobble * 140.0)        # the shaft is not steady either
        turbo_gain = 0.10 + 0.42 * load ** 1.4
    else:
        turbo = np.zeros(n)
        turbo_gain = 0.0

    mix = (normalise(body, 1.0) * (0.80 + 0.15 * load)
           + normalise(fire, 1.0) * (0.45 + 0.45 * load)
           + normalise(clatter, 1.0) * (0.30 * (1.25 - 0.55 * load))
           + normalise(intake, 1.0) * (0.12 + 0.30 * load)
           + normalise(turbo, 1.0) * turbo_gain)

    # A three cylinder idling at the 800 rpm in the data fires at 20 Hz, and a six's half
    # orders sit lower still. Nothing reproduces that, and left in it eats the headroom the
    # audible part of the engine needs, so it goes steeply.
    mix = highpass(dc_block(mix), 32.0, order=4)
    mix = lowpass(mix, 4200.0 + 6500.0 * load, order=2)
    return normalise(soft_clip(mix, 1.15), 0.88)


def _electric_loop(spec, band, n, rng):
    """One rpm band of an electric class.

    There is no combustion to model, so the note comes from the things that are actually
    loud on a battery machine: the inverter switching, the torque ripple it feeds the
    motor, the reduction gear mesh, and the cooling pumps and blowers that have to run
    harder than they would on a diesel because there is no radiator fan already turning.
    """
    rpm = spec["rpm"][band]
    load = BAND_LOAD[band]
    f_elec = (rpm / 60.0) * 4.0                 # four pole pairs on a traction motor
    f_ripple = f_elec * 6.0                     # three phases, six commutation events
    f_switch = 4800.0                           # inverter carrier, fixed by the drive
    f_mesh = (rpm / 60.0) * 19.0                # single reduction, nineteen teeth

    whine = partial_stack(n, f_switch, [1.0], rng)
    for side in (-3, -2, -1, 1, 2, 3):
        # The carrier is modulated by the motor's electrical frequency, so it carries
        # sidebands that move with road speed: that is the sound of a drive working.
        whine += partial_stack(n, f_switch + side * f_elec, [0.45 / abs(side)], rng)
    whine += partial_stack(n, f_ripple, [1.2, 0.6, 0.35, 0.2], rng)
    whine += partial_stack(n, f_mesh, [0.7, 0.35, 0.18], rng)

    whine = time_warp(whine, wander(n, rng, 12.0) * 30.0)

    # Pumps and blowers: broadband, with the blade pass of the cooling fan sitting in it.
    bed = bandpass(noise(n, rng, tilt=0.45), 140.0, 2600.0)
    bed = resonate(bed, 320.0, 1.4, 2.2)
    fan = partial_stack(n, 46.0 * (7.0 + 4.0 * load), [1.0, 0.4, 0.2], rng)

    mix = (normalise(whine, 1.0) * (0.22 + 0.46 * load)
           + normalise(bed, 1.0) * (0.55 + 0.20 * load)
           + normalise(fan, 1.0) * (0.10 + 0.18 * load))
    mix = highpass(dc_block(mix), 40.0, order=4)
    return normalise(soft_clip(mix, 1.1), 0.80)


def loop_for(spec, band, n, rng):
    """One rpm band of one class, whichever kind of powerplant it has.

    The cab bed in audio.ambience is built on this, so what a player hears through the
    glass is the same engine the machine makes outside it rather than a second guess at
    the same sound.
    """
    if spec["drive"] == "electric":
        return _electric_loop(spec, band, n, rng)
    return _combustion_loop(spec, band, n, rng)


def _rpm_ramp(n, points):
    """Piecewise-linear rpm over a one-shot, given (fraction of clip, rpm) knots."""
    fracs = np.array([p[0] for p in points])
    values = np.array([p[1] for p in points])
    return np.interp(np.linspace(0.0, 1.0, n), fracs, values)


def _swept_combustion(spec, rpm_t, level_t, rng, firing_from=0.0):
    """Combustion over a changing rpm, for the start and stop one-shots.

    A one-shot has no loop to close, so this is the straightforward version: integrate
    the instantaneous cycle frequency into a phase, sum the harmonics on it, and fire a
    pulse every time the firing phase crosses another whole event. `firing_from` is where
    the engine catches - before that the crank is turning on the starter and compressing
    air, but nothing is burning.
    """
    n = rpm_t.size
    cyl = spec["cylinders"]
    f_exhaust = _exhaust_hz(spec["powerKw"])
    cycle_phase = np.cumsum(rpm_t / 120.0) / SR

    out = np.zeros(n)
    for k in range(1, 25):
        amp = k ** -1.5
        if k % cyl == 0:
            amp *= 3.2
        elif k % 2:
            amp *= 0.4
        out += amp * np.sin(2.0 * np.pi * k * cycle_phase + rng.uniform(0.0, 2.0 * np.pi))
    out = normalise(out, 1.0) * level_t

    fire_phase = cycle_phase * cyl
    events = np.searchsorted(fire_phase, np.arange(1.0, float(fire_phase[-1])))
    pulse = burst(0.06, f_exhaust, 0.014, rng, noise_mix=0.25)
    idx = np.arange(pulse.size)
    start = int(firing_from * n)
    for e in events:
        if e < start or e >= n:
            continue
        tail = min(pulse.size, n - e)
        out[e:e + tail] += pulse[idx[:tail]] * level_t[e] * (0.8 + 0.4 * rng.random())
    return out


def _start_oneshot(spec, rng):
    """Cold start: solenoid, starter cranking, catch, flare, settle to idle."""
    idle = spec["rpm"]["idle"]
    seconds = 2.4
    n = n_for(seconds)
    t = time_axis(n)

    if spec["drive"] == "electric":
        # A battery machine starts by closing a contactor and spinning its pumps up.
        rise = np.clip(t / 1.1, 0.0, 1.0)
        whine = np.sin(2.0 * np.pi * np.cumsum(300.0 + 1900.0 * rise) / SR) * 0.22 * rise
        bed = bandpass(noise(n, rng, tilt=0.45), 140.0, 2400.0) * 0.5 * rise
        clunk = np.zeros(n)
        clunk[:n_for(0.2)] = burst(0.2, 95.0, 0.035, rng, noise_mix=0.5) * 0.8
        return normalise(fade(dc_block(whine + bed + clunk)), 0.85)

    crank_rpm = 230.0
    rpm_t = _rpm_ramp(n, [(0.0, 0.0), (0.04, crank_rpm), (0.34, crank_rpm),
                          (0.42, idle * 0.55), (0.56, idle * 1.35), (0.75, idle * 1.05),
                          (1.0, idle)])
    level_t = np.clip(_rpm_ramp(n, [(0.0, 0.0), (0.04, 0.35), (0.34, 0.4),
                                    (0.42, 1.0), (0.62, 0.85), (1.0, 0.7)]), 0.0, 1.0)
    body = _swept_combustion(spec, rpm_t, level_t, rng, firing_from=0.36)

    # The starter is a small high-speed motor through a reduction: a rising whine that
    # stops dead the moment the engine catches, which is the cue that tells a player the
    # machine is running.
    starter_env = np.clip(1.0 - np.abs(np.linspace(-1.0, 3.0, n)), 0.0, 1.0)
    starter = np.sin(2.0 * np.pi * np.cumsum(np.linspace(900.0, 1450.0, n)) / SR)
    starter = starter * starter_env * 0.30
    solenoid = np.zeros(n)
    solenoid[:n_for(0.12)] = burst(0.12, 140.0, 0.02, rng, noise_mix=0.6) * 0.7

    mix = highpass(dc_block(body + starter + solenoid), 32.0, order=4)
    return normalise(fade(soft_clip(mix, 1.1)), 0.9)


def _stop_oneshot(spec, rng):
    """Shutdown: fuel cut, the last few firings, a dying rotation and a valve clunk."""
    idle = spec["rpm"]["idle"]
    seconds = 1.8
    n = n_for(seconds)

    if spec["drive"] == "electric":
        t = time_axis(n)
        fall = np.clip(1.0 - t / 1.3, 0.0, 1.0)
        whine = np.sin(2.0 * np.pi * np.cumsum(2200.0 * fall + 120.0) / SR) * 0.22 * fall
        bed = bandpass(noise(n, rng, tilt=0.45), 140.0, 2400.0) * 0.5 * fall
        clunk = np.zeros(n)
        off = n_for(1.25)
        clunk[off:off + n_for(0.25)] = burst(0.25, 88.0, 0.03, rng, noise_mix=0.5) * 0.7
        return normalise(fade(dc_block(whine + bed + clunk)), 0.8)

    rpm_t = _rpm_ramp(n, [(0.0, idle), (0.12, idle * 0.98), (0.45, idle * 0.5),
                          (0.72, idle * 0.16), (0.85, 0.0), (1.0, 0.0)])
    level_t = np.clip(_rpm_ramp(n, [(0.0, 0.75), (0.12, 0.7), (0.5, 0.4),
                                    (0.8, 0.12), (0.9, 0.0), (1.0, 0.0)]), 0.0, 1.0)
    body = _swept_combustion(spec, rpm_t, level_t, rng)

    # An engine stopping does not fade out, it stops: the last compression holds the
    # crank and the valve train lands. That clunk is the whole reason for a stop one-shot.
    clunk = np.zeros(n)
    off = n_for(1.15)
    clunk[off:off + n_for(0.3)] = (burst(0.3, _exhaust_hz(spec["powerKw"]) * 1.4, 0.045,
                                         rng, noise_mix=0.45) * 0.55)
    mix = highpass(dc_block(body + clunk), 32.0, order=4)
    return normalise(fade(soft_clip(mix, 1.1)), 0.85)


# --------------------------------------------------------------------------- build
def build_all(out_dir):
    """Every engine set, in vehicles.json order. Four loops and two one-shots each."""
    n = n_for(config.LOOP_SECONDS)
    records = []
    for spec in engine_classes():
        shared = {
            "engineClass": spec["id"],
            "drive": spec["drive"],
            "cylinders": spec["cylinders"],
            "powerKw": round(spec["powerKw"], 1),
            "turbo": spec["turbo"],
            "machines": list(spec["machines"]),
            "placeholder": True,
        }
        for band in config.ENGINE_BANDS:
            rng = rng_for("engine", spec["id"], band)
            clip = loop_for(spec, band, n, rng)
            extra = dict(shared)
            extra.update({"band": band, "rpm": round(spec["rpm"][band], 1),
                          "load": BAND_LOAD[band]})
            records.append(write_clip(out_dir, "engine_%s_%s" % (spec["id"], band),
                                      clip, "engine", loop=True, extra=extra))

        for event, maker in (("start", _start_oneshot), ("stop", _stop_oneshot)):
            rng = rng_for("engine", spec["id"], event)
            extra = dict(shared)
            extra["band"] = event
            records.append(write_clip(out_dir, "engine_%s_%s" % (spec["id"], event),
                                      maker(spec, rng), "engine", loop=False, extra=extra))
    return records


def report(records):
    print("%-38s %7s %6s %6s %8s" % ("clip", "sec", "peak", "rms", "seam"))
    for r in records:
        print("%-38s %7.2f %6.3f %6.3f %8.5f"
              % (r["id"], r["seconds"], r["peak"], r["rms"], r["seam"]))
    print("%d clips" % len(records))


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.AUDIO_DIR
    report(build_all(target))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
