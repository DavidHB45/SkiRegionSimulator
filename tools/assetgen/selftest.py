#!/usr/bin/env python3
"""Checks the asset pipeline's own guarantees, the ones nothing else would notice.

    python3 tools/assetgen/selftest.py            # every check
    python3 tools/assetgen/selftest.py --keep     # leave the scratch FBX files behind
    python3 tools/assetgen/selftest.py --list     # what it checks, without running it

A generator that produces a wrong-looking machine is caught by looking at the machine.
What is caught here is the class of mistake that looks fine in every screenshot and is
wrong in the game: a model that comes out mirrored because someone "fixed" an axis, a
pivot that drifted off the ground so the machine hovers, an LOD chain that gets heavier
with distance, a collider Unity silently refuses, a build that stops being reproducible,
or a budget that is no longer enforced because the check was skipped somewhere.

The axis check is the important one. docs/ART_CONTRACT.md section 1 promises that the
exported FBX holds literal Unity coordinates and that nothing is rotated or rescaled on
import. That promise is made by two conversions that cancel - meshkit stores Unity space
in Blender's Z-up frame, the FBX exporter maps it back - and a sign error in either one
produces a file that still opens, still looks like a machine, and puts the blade on the
wrong side. So the check exports a model with landmarks on +X, +Y and +Z, reads the file
back with Blender's importer in manual-orientation mode (axis_forward='Y', axis_up='Z'
tells it to take the file verbatim rather than convert anything), and asserts each
landmark is exactly where it was authored.

Exits 0 when every check passes, 1 otherwise.
"""
import argparse
import hashlib
import os
import shutil
import sys
import tempfile
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy                                                      # noqa: E402
import numpy as np                                              # noqa: E402

import config                                                   # noqa: E402
from lib import datasrc, export, validate                       # noqa: E402
from lib import meshkit as mk                                   # noqa: E402
from lib.meshkit import BODY, METAL                             # noqa: E402

# A metre of machine round-tripped through single-precision FBX comes back within well
# under a millimetre; anything looser would let a real axis error through.
TOLERANCE_M = 1e-3


class CheckFailed(AssertionError):
    """A pipeline guarantee does not hold."""


def expect(condition, message):
    if not condition:
        raise CheckFailed(message)


def expect_close(got, want, message):
    if abs(float(got) - float(want)) > TOLERANCE_M:
        raise CheckFailed("%s: got %.6f, expected %.6f (tolerance %.4f m)"
                          % (message, got, want, TOLERANCE_M))


# --------------------------------------------------------------------------- fixtures
# Three landmarks, each off the origin on one axis only, each asymmetric in the other
# two. Position alone catches a flipped axis; the nub catches a mirror that keeps the
# centre in place. Fields: node name, marker centre, marker size, nub offset.
NUB_M = 0.16
LANDMARKS = (
    ("mark_x", (2.40, 0.35, 0.00), (0.60, 0.30, 0.30), (0.00, 0.00, 0.42)),
    ("mark_y", (0.00, 1.90, 0.00), (0.30, 0.70, 0.30), (0.36, 0.00, 0.00)),
    ("mark_z", (0.00, 0.35, 3.10), (0.30, 0.30, 0.80), (0.00, 0.36, 0.00)),
)


def landmark_model():
    """A plinth on the ground with one marker reaching out along each positive axis."""
    m = mk.Model("axis_landmark", budget_key="prop",
                 seed=datasrc.seed_for("selftest", "axis"))
    m.box((0.0, 0.15, 0.0), (1.20, 0.30, 1.20), mat=METAL)
    for name, point, size, nub in LANDMARKS:
        m.node(name, pivot=point)
        m.box(point, size, mat=BODY, parent=name)
        m.box(_nub_centre(point, nub), (NUB_M, NUB_M, NUB_M), mat=METAL, parent=name)
    m.socket("mount_Front", (0.0, 0.60, 0.70))
    return m


def _nub_centre(point, nub):
    return tuple(point[i] + nub[i] for i in range(3))


def landmark_bounds(point, size, nub):
    """What the landmark occupies in Unity space, worked out from what was asked for.

    Deliberately arithmetic rather than measured off the model: an expectation read back
    through meshkit's own conversion would agree with a sign error instead of catching
    it. A chamfer shaves the corners of a box without moving any of its six face planes,
    so the box's extents are still the numbers handed to m.box().
    """
    boxes = ((point, size), (_nub_centre(point, nub), (NUB_M, NUB_M, NUB_M)))
    lo = [min(c[i] - s[i] * 0.5 for c, s in boxes) for i in range(3)]
    hi = [max(c[i] + s[i] * 0.5 for c, s in boxes) for i in range(3)]
    return lo, hi


def demo_machine(name, budget_key="prop"):
    """A tracked machine in miniature: enough hierarchy to exercise the whole export."""
    m = mk.Model(name, budget_key=budget_key,
                 seed=datasrc.seed_for("selftest", "machine", name))
    m.node("track_L", pivot=(-0.90, 0.30, 0.00))
    m.node("track_R", pivot=(0.90, 0.30, 0.00))
    m.box((0.0, 1.05, 0.0), (1.80, 0.90, 3.40), mat=BODY)
    m.box((0.0, 1.75, -0.40), (1.40, 0.50, 1.20), mat=BODY)
    for side, node in ((-1.0, "track_L"), (1.0, "track_R")):
        m.box((side * 0.90, 0.30, 0.0), (0.50, 0.55, 3.60), mat=METAL, parent=node)
        for z in (-1.55, 1.55):
            m.cylinder((side * 0.90, 0.30, z), 0.30, 0.52, axis=0, segments=20,
                       mat=METAL, parent=node)
    m.socket("mount_Front", (0.0, 0.60, 1.80))
    return m


def object_bounds_world(obj):
    """World-space bounds of an imported object, in whatever frame the file used."""
    pts = np.asarray([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices])
    return pts.min(axis=0), pts.max(axis=0)


# --------------------------------------------------------------------------- checks
def check_axis_contract(work):
    """Export landmarks, read the FBX back verbatim, assert Unity coordinates survive.

    Also asserts the exported hierarchy carries the names ModelRegistry looks for, since
    the file is open anyway and a renamed LOD is as invisible as a flipped axis.
    """
    model = landmark_model()
    path = os.path.join(work, "axis_landmark.fbx")
    export.emit(model, path, extra={"family": "prop", "kind": "prop"})
    expect(os.path.exists(path), "export.emit wrote no file at " + path)

    mk.reset_scene()
    bpy.ops.import_scene.fbx(filepath=path, use_manual_orientation=True,
                             axis_forward="Y", axis_up="Z", global_scale=1.0,
                             use_image_search=False)

    for name, point, size, nub in LANDMARKS:
        obj = bpy.data.objects.get(name)
        expect(obj is not None and obj.type == "MESH",
               "landmark '%s' is missing from the exported hierarchy" % name)
        lo, hi = object_bounds_world(obj)
        want_lo, want_hi = landmark_bounds(point, size, nub)
        for axis, label in enumerate("XYZ"):
            expect_close(lo[axis], want_lo[axis],
                         "landmark '%s' authored at %s: min %s" % (name, point, label))
            expect_close(hi[axis], want_hi[axis],
                         "landmark '%s' authored at %s: max %s" % (name, point, label))

    for suffix in ("_LOD0", "_LOD1", "_LOD2", "_col"):
        expect(bpy.data.objects.get(model.name + suffix) is not None,
               "the exported hierarchy has no '%s%s'; docs/ART_CONTRACT.md section 2 "
               "names it and Unity's importer looks for it" % (model.name, suffix))
    expect(bpy.data.objects.get("mount_Front") is not None,
           "socket 'mount_Front' did not survive export as an empty")
    return "landmarks within %.4f m on all three axes, hierarchy names intact" % TOLERANCE_M


def check_pivot_on_ground(work):
    """A machine's lowest vertex sits at y = 0, so it stands on the snow it is placed on."""
    model = demo_machine("pivot_probe")
    record = export.emit(model, os.path.join(work, "pivot_probe.fbx"),
                         extra={"family": "tracked", "kind": "machine"})
    y_min = record["boundsMin"][1]
    expect_close(y_min, 0.0, "the model's lowest vertex is not on the ground plane")
    half = (record["boundsMax"][0] - record["boundsMin"][0]) * 0.5
    centre = (record["boundsMax"][0] + record["boundsMin"][0]) * 0.5
    expect(abs(centre) <= max(0.25, half * 0.25),
           "the model is %.3f m off its own centreline in X" % centre)
    return "y_min = %.6f m, centred to %.6f m in X" % (y_min, centre)


def check_lod_ordering(work):
    """LOD1 and LOD2 are strictly lighter than LOD0, and neither is empty."""
    model = demo_machine("lod_probe")
    record = export.emit(model, os.path.join(work, "lod_probe.fbx"),
                         extra={"family": "tracked", "kind": "machine"})
    tris = record["triangles"]
    expect(len(tris) == config.LOD_COUNT,
           "expected %d LODs, the export reported %d" % (config.LOD_COUNT, len(tris)))
    expect(tris[0] > 0, "LOD0 is empty")
    for level in range(1, len(tris)):
        expect(tris[level] > 0, "LOD%d is empty" % level)
        expect(tris[level] < tris[level - 1],
               "LOD%d (%d tris) is not lighter than LOD%d (%d tris)"
               % (level, tris[level], level - 1, tris[level - 1]))
    return "LOD triangles %s" % (" > ".join(str(t) for t in tris))


def check_collider_cap(work):
    """Convex hulls stay under Unity's 255-polygon ceiling, however dense the input."""
    model = demo_machine("hull_probe")
    record = export.emit(model, os.path.join(work, "hull_probe.fbx"),
                         extra={"family": "tracked", "kind": "machine"})
    expect(0 < record["hullFaces"] <= export.MAX_COLLIDER_FACES,
           "the machine's collider has %d faces; Unity's limit is 255"
           % record["hullFaces"])

    # The machine hull is well inside the cap on its own, so the coarsening loop that
    # actually enforces it is only exercised by a point cloud dense enough to overrun.
    mk.reset_scene()
    rng = np.random.default_rng(datasrc.seed_for("selftest", "hull"))
    cloud = rng.normal(size=(6000, 3))
    cloud /= np.linalg.norm(cloud, axis=1, keepdims=True)
    stress = export.convex_hull_mesh(cloud * 2.0, "stress_col")
    expect(stress is not None, "convex_hull_mesh returned nothing for a 6000-point sphere")
    faces = len(stress.data.polygons)
    expect(faces <= export.MAX_COLLIDER_FACES,
           "a 6000-point sphere hulls to %d faces; the coarsening loop did not hold the "
           "%d cap" % (faces, export.MAX_COLLIDER_FACES))
    return "machine hull %d faces, 6000-point sphere coarsened to %d" % (
        record["hullFaces"], faces)


def check_determinism(work):
    """The same model built twice is the same model, down to the byte.

    Two runs have to agree or the pipeline stops being a build step and becomes a source
    of churn: the same commit would produce a different art pack on every machine, and
    nobody could tell a real change from a re-run. The two builds go to different
    directories deliberately, because an output path is exactly the kind of thing that
    leaks into an FBX header alongside the clock.
    """
    built = []
    for sub in ("first", "second"):
        path = os.path.join(work, sub, "determinism_probe.fbx")
        record = export.emit(demo_machine("determinism_probe"), path,
                             extra={"family": "tracked", "kind": "machine"})
        with open(path, "rb") as f:
            built.append((record, hashlib.sha256(f.read()).hexdigest()))
    (first, digest_a), (second, digest_b) = built

    expect(first["triangles"] == second["triangles"],
           "two builds of one model disagree on triangles: %s vs %s"
           % (first["triangles"], second["triangles"]))
    expect(first["boundsMin"] == second["boundsMin"]
           and first["boundsMax"] == second["boundsMax"],
           "two builds of one model disagree on bounds: %s..%s vs %s..%s"
           % (first["boundsMin"], first["boundsMax"],
              second["boundsMin"], second["boundsMax"]))
    expect(first["nodes"] == second["nodes"],
           "two builds of one model disagree on their node list")
    expect(digest_a == digest_b,
           "two builds of one model produced different files (%s vs %s); something in "
           "the export is still carrying the clock or the path"
           % (digest_a[:12], digest_b[:12]))
    return "%s triangles, identical bounds, identical bytes (%s)" % (
        first["triangles"], digest_a[:12])


def check_budget_enforced(work):
    """An over-budget model fails the build instead of shipping and costing frame time."""
    ceiling = config.TRI_BUDGETS["tree"][1]
    model = mk.Model("over_budget_probe", budget_key="tree",
                     seed=datasrc.seed_for("selftest", "budget"))
    model.cylinder((0.0, 0.50, 0.0), 0.50, 1.00, axis=1, segments=768, mat=METAL)
    expect(model.triangle_count() > ceiling,
           "the deliberately fat probe is only %d triangles, under the %d ceiling it is "
           "supposed to break" % (model.triangle_count(), ceiling))

    path = os.path.join(work, "over_budget_probe.fbx")
    try:
        export.emit(model, path, extra={"family": "prop", "kind": "prop"})
    except validate.ContractError as e:
        expect("budget" in str(e).lower() or "LOD0" in str(e),
               "an over-budget model raised the wrong ContractError: %s" % e)
        expect(not os.path.exists(path),
               "a model that failed validation was written to disk anyway")
        return "rejected at %d triangles against the %d ceiling for 'tree'" % (
            model.triangle_count(), ceiling)
    raise CheckFailed("a %d-triangle model passed the %d-triangle 'tree' budget; the "
                      "ceiling is no longer enforced"
                      % (model.triangle_count(), ceiling))


CHECKS = (
    ("axis contract", check_axis_contract),
    ("pivot on the ground", check_pivot_on_ground),
    ("LOD ordering", check_lod_ordering),
    ("collider polygon cap", check_collider_cap),
    ("determinism", check_determinism),
    ("budget enforcement", check_budget_enforced),
)


# --------------------------------------------------------------------------- driver
def main(argv=None):
    ap = argparse.ArgumentParser(description="Self-check for tools/assetgen.")
    ap.add_argument("--keep", action="store_true",
                    help="keep the scratch directory the checks export into")
    ap.add_argument("--list", action="store_true", help="list the checks and exit")
    ap.add_argument("--verbose", action="store_true",
                    help="print a traceback for a failing check")
    args = ap.parse_args(argv)

    if args.list:
        for name, fn in CHECKS:
            print("%-22s %s" % (name, (fn.__doc__ or "").strip().splitlines()[0]))
        return 0

    work = tempfile.mkdtemp(prefix="assetgen-selftest-")
    failures = []
    try:
        for name, fn in CHECKS:
            validate.reset()
            try:
                detail = fn(work)
                print("  ok   %-22s %s" % (name, detail or ""), flush=True)
            except Exception as e:                       # a check failing is the point
                failures.append((name, e))
                print("  FAIL %-22s %s" % (name, e), flush=True)
                if args.verbose or not isinstance(e, CheckFailed):
                    traceback.print_exc()
    finally:
        if args.keep:
            print("scratch files left in " + work)
        else:
            shutil.rmtree(work, ignore_errors=True)

    if failures:
        print("\n%d of %d pipeline checks failed:" % (len(failures), len(CHECKS)),
              file=sys.stderr)
        for name, e in failures:
            print("  %s: %s" % (name, e), file=sys.stderr)
        print("docs/ART_CONTRACT.md states the guarantee each check enforces.",
              file=sys.stderr)
        return 1
    print("%d pipeline checks passed" % len(CHECKS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
