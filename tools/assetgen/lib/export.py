"""Assembles a finished Blender scene and writes the FBX Unity imports.

What leaves this module is fixed by docs/ART_CONTRACT.md:

    <id>                     root, pivot at ground contact centre, identity rotation
      <id>_LOD0              the articulated hierarchy gameplay binds to
        body, track_L, ...   named transforms from the contract
      <id>_LOD1              merged static proxy, ~40 % of LOD0's triangles
      <id>_LOD2              merged static proxy, ~15 %
      <id>_col               convex hull collider (<= 250 polygons, Unity's limit)
      cab_col, att_col       box colliders, when the generator declares them
      mount_*, light_*       sockets as empties

Axis conversion: geometry is authored in Unity space and stored in Blender's Z-up frame
with Unity forward on Blender -Y; exporting with axis_forward='-Z', axis_up='Y' maps
that straight back, so the FBX holds literal Unity coordinates - verified by
tools/assetgen/selftest.py, which re-reads an exported file and checks the landmarks.
Nothing needs rotating on import.
"""
import os

import bpy
import numpy as np
from mathutils import Matrix

from config import (FBX_AXIS_FORWARD, FBX_AXIS_UP, FBX_GLOBAL_SCALE, LOD_RATIOS,
                    ROOT, rel_to_root)
from lib import meshkit, unitymeta

MAX_COLLIDER_FACES = 250          # Unity refuses convex MeshColliders above 255


def _link(obj):
    if obj.name not in bpy.context.collection.objects:
        bpy.context.collection.objects.link(obj)
    return obj


def _new_empty(name, parent=None, location=(0.0, 0.0, 0.0)):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = 0.2
    _link(obj)
    if parent is not None:
        obj.parent = parent
    obj.location = location
    return obj


def _descendants(obj):
    out = []
    stack = list(obj.children)
    while stack:
        o = stack.pop()
        out.append(o)
        stack.extend(o.children)
    return out


def convex_hull_mesh(points, name):
    """Convex hull of a point cloud, coarsened until Unity will accept it as a collider."""
    import trimesh

    pts = np.asarray(points, dtype=np.float64)
    if len(pts) < 4:
        return None
    cell = 0.0
    hull = None
    for _ in range(24):
        sample = pts if cell <= 0.0 else np.unique(np.round(pts / cell) * cell, axis=0)
        if len(sample) < 4:
            break
        try:
            hull = trimesh.PointCloud(sample).convex_hull
        except Exception:
            return None
        if len(hull.faces) <= MAX_COLLIDER_FACES:
            break
        extent = float(np.max(pts.max(axis=0) - pts.min(axis=0)))
        cell = max(extent / 24.0, cell * 1.5) if cell > 0 else extent / 24.0
    if hull is None:
        return None

    mesh = bpy.data.meshes.new(name)
    verts = [meshkit.to_blender(v) for v in hull.vertices]
    faces = [tuple(int(i) for i in f) for f in hull.faces]
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    obj = bpy.data.objects.new(name, mesh)
    _link(obj)
    return obj


def box_collider(name, center, size):
    """Axis-aligned box collider as a mesh child (`*_col`); ModelRegistry reads its bounds.

    Built straight into a mesh rather than through a `meshkit.Model`: constructing a
    Model empties the Blender document, which would take the model being assembled with
    it.
    """
    hx, hy, hz = (float(size[0]) * 0.5, float(size[1]) * 0.5, float(size[2]) * 0.5)
    corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz),
               (-hx, hy, -hz), (hx, hy, -hz), (hx, hy, hz), (-hx, hy, hz)]
    verts = [meshkit.to_blender((x + center[0], y + center[1], z + center[2]))
             for x, y, z in corners]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    obj = bpy.data.objects.new(name, mesh)
    _link(obj)
    return obj


def assemble(model, *, colliders=True, box_colliders=(), lod_ratios=LOD_RATIOS):
    """Realise a Model and build the LOD / collider / socket hierarchy around it.

    `box_colliders` is a sequence of (name, center, size) in Unity space, e.g.
    ("cab_col", (0, 2.1, 1.2), (2.3, 1.6, 2.0)).
    """
    root = model.realise()
    articulated = [c for c in list(root.children) if c.type == "MESH" or c.children]
    sockets = [c for c in list(root.children) if c not in articulated]

    lod0 = _new_empty(model.name + "_LOD0", parent=root)
    for child in articulated:
        world = child.matrix_world.copy()
        child.parent = lod0
        child.matrix_world = world

    bpy.context.view_layer.update()
    lod_meshes = [None] * len(lod_ratios)
    source = [o for o in _descendants(lod0) if o.type == "MESH"]
    tri_counts = [0] * len(lod_ratios)
    tri_counts[0] = sum(meshkit.mesh_triangles(o) for o in source)

    for level in range(1, len(lod_ratios)):
        proxy = meshkit.make_lod(source, lod_ratios[level], model.name + "_LOD%d" % level)
        if proxy is None:
            continue
        proxy.parent = root
        proxy.matrix_world = Matrix.Identity(4)
        meshkit.triangulate(proxy)
        tri_counts[level] = meshkit.mesh_triangles(proxy)
        lod_meshes[level] = proxy

    for o in source:
        meshkit.triangulate(o)
    tri_counts[0] = sum(meshkit.mesh_triangles(o) for o in source)

    hull = None
    if colliders:
        hull = convex_hull_mesh(model.vertices_world(), model.name + "_col")
        if hull is not None:
            hull.parent = root

    for name, center, size in box_colliders:
        box = box_collider(name, center, size)
        box.parent = root

    for s in sockets:
        s.parent = root

    bpy.context.view_layer.update()
    return {
        "root": root,
        "lod0": lod0,
        "triangles": tri_counts,
        "hull_faces": 0 if hull is None else len(hull.data.polygons),
        "nodes": sorted(o.name for o in _descendants(lod0)),
        "sockets": sorted(s.name for s in sockets),
    }


def write_fbx(path):
    """Export the whole scene. Unity axis conversion is baked into the export settings."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        global_scale=FBX_GLOBAL_SCALE,
        axis_forward=FBX_AXIS_FORWARD,
        axis_up=FBX_AXIS_UP,
        use_space_transform=True,
        bake_space_transform=False,
        object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        use_custom_props=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )
    unitymeta.write(path)
    return path


def bounds_of(model):
    """(min, max) of the model's Unity-space vertices."""
    pts = model.vertices_world()
    if len(pts) == 0:
        return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    return tuple(float(v) for v in pts.min(axis=0)), tuple(float(v) for v in pts.max(axis=0))


def emit(model, path, *, budget_key=None, colliders=True, box_colliders=(), extra=None):
    """Assemble, validate, export and return the manifest record for one model."""
    from lib import validate

    info = assemble(model, colliders=colliders, box_colliders=box_colliders)
    lo, hi = bounds_of(model)
    record = {
        "id": model.name,
        "path": rel_to_root(path),
        "resource": None,
        "budget": budget_key or model.budget_key,
        "triangles": info["triangles"],
        "hullFaces": info["hull_faces"],
        "nodes": info["nodes"],
        "sockets": info["sockets"],
        "boundsMin": [round(v, 4) for v in lo],
        "boundsMax": [round(v, 4) for v in hi],
    }
    if extra:
        record.update(extra)
    validate.check_model(model, record)
    write_fbx(path)
    from config import RES_ROOT, resource_path
    if os.path.abspath(path).startswith(os.path.abspath(RES_ROOT)):
        record["resource"] = resource_path(path)
    return record
