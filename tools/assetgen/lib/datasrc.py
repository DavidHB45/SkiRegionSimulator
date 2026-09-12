"""Reads the same JSON the simulation reads.

There is exactly one source of truth for what a machine is. `vehicles.json` says a
groomer masses 12 500 kg and runs 0.9 m tracks; the generator reads those numbers and
the model follows. Nothing in the pipeline hard-codes a machine, a lift or a spec.
"""
import hashlib
import json
import os
import struct

from config import DATA_DIR, MASTER_SEED

_cache = {}


def load(name):
    """Load Assets/StreamingAssets/Data/<name>.json (cached)."""
    if name in _cache:
        return _cache[name]
    path = os.path.join(DATA_DIR, name + ".json")
    if not os.path.exists(path):
        raise FileNotFoundError("data file not found: " + path)
    with open(path, "r", encoding="utf-8") as f:
        _cache[name] = json.load(f)
    return _cache[name]


def vehicles():
    """Every machine class, in file order (order-stable: the build is deterministic)."""
    return load("vehicles")["vehicles"]


def attachments():
    return load("attachments")["attachments"]


def lifts():
    return load("lifts")["lifts"]


def lift_options():
    return load("lifts")["options"]


def stations():
    return load("stations")


def construction():
    return load("construction")


def render():
    return load("render")


def tuning():
    return load("tuning")


def by_id(records, record_id):
    for r in records:
        if r.get("Id") == record_id:
            return r
    return None


# --------------------------------------------------------------------------- helpers
def visual(record):
    """The `Visual` mesh recipe block, with the same defaults MeshRecipe.cs uses."""
    v = dict(record.get("Visual") or {})
    v.setdefault("Silhouette", "truck")
    v.setdefault("BodyL", 6.0)
    v.setdefault("BodyW", 2.5)
    v.setdefault("BodyH", 1.4)
    v.setdefault("CabL", 1.8)
    v.setdefault("CabW", 2.2)
    v.setdefault("CabH", 1.3)
    v.setdefault("CabOffset", 1.5)
    v.setdefault("TrackH", 0.9)
    v.setdefault("WheelRadiusM", 0.5)
    v.setdefault("Axles", 2)
    v.setdefault("BoomLengthM", 0.0)
    v.setdefault("ColorHex", "#c0392b")
    v.setdefault("AccentHex", "#2c3e50")
    return v


def hex_to_rgb(text, fallback=(0.75, 0.24, 0.17)):
    """'#c0392b' -> linear-ish 0..1 float triple (sRGB values, no gamma applied)."""
    if not text:
        return fallback
    t = text.strip().lstrip("#")
    if len(t) == 3:
        t = "".join(c * 2 for c in t)
    if len(t) != 6:
        return fallback
    try:
        return tuple(int(t[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    except ValueError:
        return fallback


def seed_for(*parts):
    """Deterministic 32-bit seed from MASTER_SEED plus any identifying strings.

    Two runs of the pipeline must agree bit for bit, so no generator may call an
    unseeded RNG: it asks for a seed here and feeds it to numpy.random.default_rng.
    """
    h = hashlib.sha256()
    h.update(struct.pack("<i", MASTER_SEED))
    for p in parts:
        h.update(b"\x00")
        h.update(str(p).encode("utf-8"))
    return int.from_bytes(h.digest()[:4], "little")


def tier_of(record):
    return int(record.get("Tier", 1) or 1)


def is_hero(record):
    """Hero machines carry the big triangle budget: anything a player drives or parks
    next to. Small machines (sleds, guns, skids, walk-behinds) get the light budget."""
    mass = float(record.get("MassKg", 0) or 0)
    chassis = record.get("ChassisType", "Wheeled")
    if chassis in ("Stationary", "Towed", "WalkBehind"):
        return False
    return mass >= 1500.0
