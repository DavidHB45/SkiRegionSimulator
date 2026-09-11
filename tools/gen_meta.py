#!/usr/bin/env python3
"""Deterministic Unity .meta generator for a code-first project.

Every asset under Assets/ (files and folders) must have a sibling .meta with a
stable GUID. Because no Unity Editor is available while authoring, GUIDs are
derived from the asset's project-relative path with UUIDv5 under a fixed
namespace. The same path always yields the same GUID, so:

  * running the generator twice never changes anything;
  * a GUID is generated once and never regenerated (existing metas are left
    untouched even if the derivation rule changed);
  * cross-references (scene -> script, EditorBuildSettings -> scene,
    GraphicsSettings -> shader) can be computed by the same function.

Usage:
  python3 tools/gen_meta.py            # create missing .meta files
  python3 tools/gen_meta.py --check    # exit 1 if any asset lacks a .meta or
                                       # any .meta is orphaned
  python3 tools/gen_meta.py --guid Assets/Scripts/Unity/Bootstrap.cs
"""
import os
import sys
import uuid

NAMESPACE = uuid.UUID("4b9d3f1e-2c7a-4e5b-9a8d-1f6c2e3b4a5d")

# Generated art is build output, not repository content: it is gitignored, and
# tools/assetgen writes its own .meta files (with GUIDs from guid_for below) as it
# produces each asset. Walking it here would make --check fail on a machine that has
# run `make assets` and pass on one that has not.
SKIP_DIRS = ("Assets/Art/Generated",)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")


def guid_for(rel_path: str) -> str:
    rel_path = rel_path.replace(os.sep, "/")
    return uuid.uuid5(NAMESPACE, rel_path).hex


COMMON_TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def meta_body(rel_path: str, is_dir: bool) -> str:
    guid = guid_for(rel_path)
    head = "fileFormatVersion: 2\nguid: %s\n" % guid
    if is_dir:
        return head + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + COMMON_TAIL
    ext = os.path.splitext(rel_path)[1].lower()
    if ext == ".cs":
        return (head + "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
                "  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n" + COMMON_TAIL)
    if ext == ".asmdef":
        return head + "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + COMMON_TAIL
    if ext == ".shader":
        return (head + "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n"
                "  nonModifiableTextures: []\n  preprocessorOverride: 0\n" + COMMON_TAIL)
    if ext == ".compute":
        return (head + "ComputeShaderImporter:\n  externalObjects: {}\n  preprocessorOverride: 0\n"
                "  currentAPIMask: 0\n" + COMMON_TAIL)
    if ext in (".cginc", ".hlsl"):
        return head + "ShaderIncludeImporter:\n  externalObjects: {}\n" + COMMON_TAIL
    if ext in (".json", ".txt", ".md", ".xml", ".csv"):
        return head + "TextScriptImporter:\n  externalObjects: {}\n" + COMMON_TAIL
    if ext == ".unity":
        return head + "DefaultImporter:\n  externalObjects: {}\n" + COMMON_TAIL
    return head + "DefaultImporter:\n  externalObjects: {}\n" + COMMON_TAIL


def iter_assets():
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames.sort()
        filenames.sort()
        rel_dir = os.path.relpath(dirpath, ROOT).replace(os.sep, "/")
        if any(rel_dir == s or rel_dir.startswith(s + "/") for s in SKIP_DIRS):
            dirnames[:] = []
            continue
        if rel_dir != "Assets":
            yield rel_dir, True
        for f in filenames:
            if f.endswith(".meta") or f.startswith(".") or f == "Thumbs.db":
                continue
            yield rel_dir + "/" + f, False


def main(argv):
    if "--guid" in argv:
        p = argv[argv.index("--guid") + 1]
        print(guid_for(p))
        return 0
    check = "--check" in argv
    missing = []
    orphans = []
    expected = set()
    for rel, is_dir in iter_assets():
        meta = os.path.join(ROOT, rel + ".meta")
        expected.add(meta)
        if not os.path.exists(meta):
            missing.append(rel)
            if not check:
                with open(meta, "w", newline="\n") as fh:
                    fh.write(meta_body(rel, is_dir))
                print("created", rel + ".meta")
    for dirpath, _, filenames in os.walk(ASSETS):
        for f in filenames:
            if f.endswith(".meta"):
                full = os.path.join(dirpath, f)
                if full not in expected:
                    orphans.append(os.path.relpath(full, ROOT))
    if check:
        for m in missing:
            print("MISSING META:", m)
        for o in orphans:
            print("ORPHAN META:", o)
        if missing or orphans:
            return 1
        print("meta check OK (%d assets)" % len(expected))
    elif orphans:
        for o in orphans:
            print("warning: orphan meta", o)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
