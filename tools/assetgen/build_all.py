#!/usr/bin/env python3
"""Entry point: read Assets/StreamingAssets/Data/*.json, write Assets/Art/Generated/.

    python3 tools/assetgen/build_all.py                 # everything
    python3 tools/assetgen/build_all.py --only meshes   # one stage
    python3 tools/assetgen/build_all.py --machines groomer_mid,loader_large
    python3 tools/assetgen/build_all.py --clean

Change a machine's mass or track width in vehicles.json and its model changes here on
the next run. Adding a machine is a JSON record plus, at most, a generator recipe -
never a modelling session.

Module contract
---------------
Every mesh module exposes:

    SILHOUETTES : frozenset[str]      which Visual.Silhouette values it claims
    def build(record, out_path) -> dict | list[dict]

`record` is the raw JSON dict; `out_path` is the .fbx to write. `build` returns the
manifest record(s) that `lib.export.emit` produced. Modules that generate assets with
no source record (props, trees, the trim sheet) expose `build_all(out_dir) -> list[dict]`.
"""
import argparse
import json
import os
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import config                                                   # noqa: E402
from lib import datasrc, unitymeta, validate                    # noqa: E402

STAGES = ("textures", "meshes", "icons", "audio")


# --------------------------------------------------------------------------- dispatch
def _mesh_modules():
    from meshes import (attachments, chassis_artic, chassis_tracked, chassis_wheeled,
                        lift_carriers, lift_terminals, lift_towers, props, snowmaking)
    return {
        "tracked": chassis_tracked,
        "wheeled": chassis_wheeled,
        "artic": chassis_artic,
        "snowmaking": snowmaking,
        "attachments": attachments,
        "lift_towers": lift_towers,
        "lift_terminals": lift_terminals,
        "lift_carriers": lift_carriers,
        "props": props,
    }


def module_for(record, mods):
    """Which chassis generator owns a machine.

    One generator per chassis family, parameterised by the JSON: all eleven groomers, the
    tracked carrier and the tracked UTV come out of the same code path, and the numbers in
    vehicles.json - mass, track width, power, seats, tier - drive the proportions.

    ChassisType decides the family and the silhouette does not get a vote, because the
    silhouette describes what a machine looks like and the chassis describes how it is
    built. An articulated tractor carries the "tractor" silhouette but is an articulated
    machine first: only chassis_artic knows how to build a centre pivot, and routing it by
    silhouette would hand it to the rigid-frame generator and quietly produce a machine
    that cannot steer.
    """
    chassis = record.get("ChassisType", "Wheeled")
    silhouette = (record.get("Visual") or {}).get("Silhouette", "truck")
    if chassis == "Artic":
        return mods["artic"]
    if silhouette in getattr(mods["snowmaking"], "SILHOUETTES", frozenset()):
        return mods["snowmaking"]          # a gun stays a gun however it is carried
    if chassis in ("Stationary", "Towed"):
        return mods["snowmaking"]
    if chassis in ("Tracked", "WalkBehind"):
        return mods["tracked"]
    return mods["wheeled"]


# --------------------------------------------------------------------------- stages
def build_textures(log):
    from textures import pbr, snow, terrain, trimsheets
    records = []
    records += trimsheets.build_all(config.TEXTURES_DIR)     # first: machines UV to it
    records += pbr.build_all(config.TEXTURES_DIR)
    records += snow.build_all(config.TEXTURES_DIR)
    records += terrain.build_all(config.TEXTURES_DIR)
    log("textures: %d maps" % len(records))
    return records


def build_meshes(log, only_ids=None):
    mods = _mesh_modules()
    records = []

    for record in datasrc.vehicles():
        if only_ids and record["Id"] not in only_ids:
            continue
        mod = module_for(record, mods)
        out = os.path.join(config.MACHINES_DIR, record["Id"] + ".fbx")
        records += _as_list(mod.build(record, out))
        log("  machine %-24s %s" % (record["Id"], mod.__name__.split(".")[-1]))

    for record in datasrc.attachments():
        if only_ids and record["Id"] not in only_ids:
            continue
        out = os.path.join(config.ATTACHMENTS_DIR, record["Id"] + ".fbx")
        records += _as_list(mods["attachments"].build(record, out))
        log("  attachment %-22s" % record["Id"])

    for record in datasrc.lifts():
        if only_ids and record["Id"] not in only_ids:
            continue
        folder = os.path.join(config.LIFTS_DIR, record["Id"])
        records += _as_list(mods["lift_towers"].build(record, os.path.join(folder, "tower.fbx")))
        records += _as_list(mods["lift_terminals"].build(record, folder))
        records += _as_list(mods["lift_carriers"].build(record, os.path.join(folder, "carrier.fbx")))
        log("  lift %-28s" % record["Id"])

    if not only_ids:
        # The snowmaking network is not a vehicles.json record: hydrants, pump houses and
        # compressor houses are what the money in stations.json buys, so the snowmaking
        # generator builds them from those records instead.
        records += _as_list(mods["snowmaking"].build_all(config.PROPS_DIR))
        log("  snowmaking network")
        records += _as_list(mods["props"].build_all(config.PROPS_DIR))
        log("  props")

    log("meshes: %d models" % len(records))
    return records


def build_icons(log):
    from ui import hud, icons
    records = icons.build_all(config.ICONS_DIR) + hud.build_all(config.ICONS_DIR)
    log("icons: %d images" % len(records))
    return records


def build_audio(log):
    from audio import ambience, engines, machinery
    records = (engines.build_all(config.AUDIO_DIR)
               + machinery.build_all(config.AUDIO_DIR)
               + ambience.build_all(config.AUDIO_DIR))
    log("audio: %d clips" % len(records))
    return records


def _as_list(value):
    if value is None:
        return []
    return list(value) if isinstance(value, (list, tuple)) else [value]


# --------------------------------------------------------------------------- driver
def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", default=",".join(STAGES),
                    help="comma-separated stages: " + ", ".join(STAGES))
    ap.add_argument("--machines", default="",
                    help="comma-separated record ids to rebuild (meshes only)")
    ap.add_argument("--clean", action="store_true", help="delete the output tree first")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    def log(message):
        if not args.quiet:
            print(message, flush=True)

    if args.clean and os.path.isdir(config.ART_ROOT):
        shutil.rmtree(config.ART_ROOT)
        log("cleaned " + config.rel_to_root(config.ART_ROOT))

    config.ensure_dirs()
    validate.reset()
    stages = [s.strip() for s in args.only.split(",") if s.strip()]
    unknown = [s for s in stages if s not in STAGES]
    if unknown:
        ap.error("unknown stage(s): %s" % ", ".join(unknown))
    only_ids = frozenset(i.strip() for i in args.machines.split(",") if i.strip()) or None

    started = time.time()
    manifest = {"generator": "tools/assetgen", "seed": config.MASTER_SEED, "assets": []}
    try:
        if "textures" in stages:
            manifest["assets"] += build_textures(log)
        if "meshes" in stages:
            manifest["assets"] += build_meshes(log, only_ids)
        if "icons" in stages:
            manifest["assets"] += build_icons(log)
        if "audio" in stages:
            manifest["assets"] += build_audio(log)
    except validate.ContractError as e:
        print("\nCONTRACT VIOLATION: %s" % e, file=sys.stderr)
        print("docs/ART_CONTRACT.md describes the rule that failed.", file=sys.stderr)
        return 1

    manifest["assets"].sort(key=lambda r: (r.get("path") or "", r.get("id") or ""))
    manifest["elapsedSeconds"] = round(time.time() - started, 2)
    with open(config.MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
        f.write("\n")

    problems = validate.issues()
    with open(config.REPORT_PATH, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"warnings": problems}, f, indent=1, sort_keys=True)
        f.write("\n")

    count = unitymeta.write_tree(config.ART_ROOT)
    log("%d assets, %d .meta files, %d warnings, %.1fs"
        % (len(manifest["assets"]), count, len(problems), manifest["elapsedSeconds"]))
    for p in problems[:20]:
        log("  warning [%s] %s: %s" % (p["kind"], p["asset"], p["message"]))
    if len(problems) > 20:
        log("  ... %d more in %s" % (len(problems) - 20, config.rel_to_root(config.REPORT_PATH)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
