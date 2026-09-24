"""Parametric hard-surface mesh construction on top of Blender's Python module.

Everything a generator writes is expressed in **Unity space**: +X right, +Y up,
+Z forward, metres, origin at the ground contact centre of the model. meshkit converts
to Blender's Z-up frame on the way in and lib.export converts back on the way out, so a
generator never has to think about axis conventions.

The unit of work is a `Model`. A model owns named nodes - the articulation transforms
gameplay drives - and every primitive call routes its faces into one of them:

    m = Model("groomer_mid")
    m.node("blade_lift", pivot=(0, 0.8, 2.6))
    m.box((0, 1.2, 0), (2.4, 1.1, 4.6), mat=BODY)              # goes to the root node
    m.box((0, 0.9, 3.1), (4.3, 1.0, 0.14), parent="blade_lift") # moves with the blade

Geometry is authored in model space whatever node it belongs to; at finalise time each
node's mesh is rebased onto its own pivot. That keeps generator code readable and the
pivots exact.
"""
import math

import bpy          # noqa: F401  - importing bpy first is what puts bmesh/mathutils on the path
import bmesh
import numpy as np
from mathutils import Matrix, Vector

from config import BEVEL_SEGMENTS, BEVEL_WIDTH_M, LOD_RATIOS

# --------------------------------------------------------------------------- materials
# Three slots, in this order, on every object the pipeline emits. More than three
# materials on a machine means more than three draw calls per machine; the whole point
# of the shared trim sheet is that a category renders in one batch.
BODY, METAL, GLASS = 0, 1, 2
MAT_NAMES = ("body", "metal", "glass")
MAT_COUNT = 3


# --------------------------------------------------------------------------- axes
# Unity (x right, y up, z forward) -> Blender (x right, y, z up) with Unity forward
# landing on Blender -Y. That is a proper rotation, not a mirror, so winding, normals
# and chirality all survive; and exporting with axis_forward='-Z', axis_up='Y' maps it
# straight back, so the FBX holds literal Unity coordinates.
def to_blender(v):
    return Vector((float(v[0]), -float(v[2]), float(v[1])))


def to_unity(v):
    return (float(v[0]), float(v[2]), -float(v[1]))


# Basis change as a matrix, for conjugating rotations between the two frames.
_M = Matrix(((1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0))).to_4x4()
_MT = _M.transposed()


def unity_euler(pitch_deg, yaw_deg, roll_deg):
    """A Unity `Quaternion.Euler(pitch, yaw, roll)` as a Blender rotation matrix.

    Unity composes Y * X * Z and turns clockwise about each positive axis (it is
    left-handed) where Blender turns counter-clockwise, so every angle is negated and
    applied about its mapped axis: Unity Y (up) -> Blender Z, Unity X (right) ->
    Blender X, Unity Z (forward) -> Blender -Y.
    """
    ry = Matrix.Rotation(math.radians(-yaw_deg), 4, "Z")
    rx = Matrix.Rotation(math.radians(-pitch_deg), 4, "X")
    rz = Matrix.Rotation(math.radians(roll_deg), 4, "Y")
    return ry @ rx @ rz


def look_rotation(forward, up=(0.0, 1.0, 0.0)):
    """Blender rotation aligning model +Z (Unity forward) with a Unity-space vector.

    Built in Unity space - columns are right, up, forward - then conjugated into
    Blender's frame, which keeps the two conventions from ever being mixed by hand.
    """
    f = Vector((float(forward[0]), float(forward[1]), float(forward[2])))
    if f.length < 1e-9:
        return Matrix.Identity(4)
    f.normalize()
    u = Vector((float(up[0]), float(up[1]), float(up[2])))
    r = u.cross(f)
    if r.length < 1e-6:
        r = Vector((1.0, 0.0, 0.0)).cross(f)
        if r.length < 1e-6:
            r = Vector((1.0, 0.0, 0.0))
    r.normalize()
    u = f.cross(r).normalized()
    unity = Matrix.Identity(4)
    for row in range(3):
        unity[row][0] = r[row]
        unity[row][1] = u[row]
        unity[row][2] = f[row]
    return _M @ unity @ _MT


# --------------------------------------------------------------------------- scene
def reset_scene():
    """Empty Blender document. Called once per model so nothing leaks between assets."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials):
        for item in list(block):
            block.remove(item, do_unlink=True)


def _materials():
    mats = []
    for name in MAT_NAMES:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mats.append(mat)
    return mats


# --------------------------------------------------------------------------- node
class Node:
    """One named transform in the exported hierarchy.

    `name` is the articulation contract name gameplay binds to (`blade_lift`,
    `track_L`, `bullwheel`, ...). `pivot` is where that transform sits in model space -
    the axis the part actually turns about, not the centre of its bounding box.
    """

    def __init__(self, name, pivot, parent=None, kind="mesh"):
        self.name = name
        self.pivot = Vector((float(pivot[0]), float(pivot[1]), float(pivot[2])))
        self.parent = parent
        self.kind = kind
        self.bm = bmesh.new()
        self.obj = None

    @property
    def is_empty(self):
        return len(self.bm.faces) == 0


# --------------------------------------------------------------------------- model
class Model:
    """A single exportable asset: nodes, geometry, and the metadata export needs."""

    ROOT = "__root__"

    def __init__(self, name, budget_key="prop", seed=0):
        reset_scene()
        self.name = name
        self.budget_key = budget_key
        self.rng = np.random.default_rng(seed)
        self.nodes = {}
        self.order = []
        self._node(self.ROOT, (0.0, 0.0, 0.0), parent=None)
        self.sockets = {}          # name -> Unity-space point (attachment mounts, lights)
        self.notes = []
        self._world_pts = None     # model-space vertex snapshot, taken before rebasing

    # ------------------------------------------------------------------ hierarchy
    def _node(self, name, pivot, parent):
        node = Node(name, pivot, parent)
        self.nodes[name] = node
        self.order.append(name)
        return node

    def node(self, name, pivot=(0.0, 0.0, 0.0), parent=None):
        """Declare an articulation transform. Geometry given `parent=name` moves with it."""
        if name in self.nodes:
            raise ValueError("duplicate node '%s' in model '%s'" % (name, self.name))
        if parent is None:
            parent = self.ROOT
        if parent not in self.nodes:
            raise ValueError("node '%s' has unknown parent '%s'" % (name, parent))
        return self._node(name, pivot, parent)

    def socket(self, name, point):
        """A named point of interest exported as an empty (mounts, exhausts, lights)."""
        self.sockets[name] = tuple(float(c) for c in point)

    def _bm(self, parent):
        return self.nodes[parent or self.ROOT].bm

    # ------------------------------------------------------------------ primitives
    def _emit(self, verts, faces, mat, parent):
        """Add a vertex/face soup (Unity space) into a node's bmesh with a material index."""
        bm = self._bm(parent)
        bverts = [bm.verts.new(to_blender(v)) for v in verts]
        made = []
        for f in faces:
            try:
                face = bm.faces.new([bverts[i] for i in f])
            except ValueError:
                continue          # duplicate face from a degenerate primitive: skip it
            face.material_index = mat
            face.smooth = False
            made.append(face)
        bm.verts.index_update()
        return made

    def box(self, center, size, mat=BODY, parent=None, rot=None, bevel=True):
        """Axis-aligned box, optionally rotated about its centre by a Unity euler."""
        hx, hy, hz = (float(size[0]) * 0.5, float(size[1]) * 0.5, float(size[2]) * 0.5)
        corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz),
                   (-hx, hy, -hz), (hx, hy, -hz), (hx, hy, hz), (-hx, hy, hz)]
        verts = self._place(corners, center, rot)
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                 (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        made = self._emit(verts, faces, mat, parent)
        if bevel:
            self._bevel_faces(parent, made, BEVEL_WIDTH_M)
        return made

    def wedge(self, center, size, mat=BODY, parent=None, rot=None, taper=0.0):
        """Triangular prism: full height at -Z, sloping down toward +Z.

        `taper` (0..1) keeps that fraction of the height at the +Z end, which is what a
        real blade mouldboard or a cab roof slope looks like.
        """
        hx, hy, hz = (float(size[0]) * 0.5, float(size[1]) * 0.5, float(size[2]) * 0.5)
        top = -hy + float(size[1]) * max(0.0, min(1.0, taper))
        corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz),
                   (-hx, hy, -hz), (hx, hy, -hz), (hx, top, hz), (-hx, top, hz)]
        verts = self._place(corners, center, rot)
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                 (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        made = self._emit(verts, faces, mat, parent)
        self._bevel_faces(parent, made, BEVEL_WIDTH_M)
        return made

    def cylinder(self, center, radius, length, axis=1, segments=16, mat=METAL,
                 parent=None, rot=None, radius_end=None, caps=True):
        """Cylinder or cone along a model axis (0=X, 1=Y, 2=Z) before `rot` is applied."""
        segments = max(3, int(segments))
        r1 = float(radius if radius_end is None else radius_end)
        h = float(length) * 0.5
        ring0, ring1 = [], []
        for i in range(segments):
            a = 2.0 * math.pi * i / segments
            c, s = math.cos(a), math.sin(a)
            if axis == 0:
                ring0.append((-h, c * radius, s * radius))
                ring1.append((h, c * r1, s * r1))
            elif axis == 1:
                ring0.append((c * radius, -h, s * radius))
                ring1.append((c * r1, h, s * r1))
            else:
                ring0.append((c * radius, s * radius, -h))
                ring1.append((c * r1, s * r1, h))
        verts = self._place(ring0 + ring1, center, rot)
        faces = []
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((i, j, segments + j, segments + i))
        if caps:
            faces.append(tuple(range(segments - 1, -1, -1)))
            faces.append(tuple(range(segments, segments * 2)))
        made = self._emit(verts, faces, mat, parent)
        self._smooth_round(made, axis, rot)
        return made

    def tube(self, center, outer, inner, length, axis=1, segments=16, mat=METAL,
             parent=None, rot=None):
        """Hollow cylinder: pipe, rim, bullwheel shroud, hydrant body."""
        segments = max(3, int(segments))
        h = float(length) * 0.5
        rings = []
        for r in (float(outer), float(inner)):
            for zsign in (-1.0, 1.0):
                ring = []
                for i in range(segments):
                    a = 2.0 * math.pi * i / segments
                    c, s = math.cos(a) * r, math.sin(a) * r
                    ring.append((h * zsign, c, s) if axis == 0 else
                                ((c, h * zsign, s) if axis == 1 else (c, s, h * zsign)))
                rings.append(ring)
        flat = [v for ring in rings for v in ring]
        verts = self._place(flat, center, rot)
        n = segments
        o0, o1, i0, i1 = 0, n, 2 * n, 3 * n
        faces = []
        for i in range(n):
            j = (i + 1) % n
            faces.append((o0 + i, o0 + j, o1 + j, o1 + i))       # outer wall
            faces.append((i1 + i, i1 + j, i0 + j, i0 + i))       # inner wall
            faces.append((o1 + i, o1 + j, i1 + j, i1 + i))       # +end ring
            faces.append((i0 + i, i0 + j, o0 + j, o0 + i))       # -end ring
        made = self._emit(verts, faces, mat, parent)
        self._smooth_round(made, axis, rot)
        return made

    def prism(self, profile, center, depth, mat=BODY, parent=None, rot=None, axis=2):
        """Extrude a 2D polygon (list of (a, b) in the plane perpendicular to `axis`).

        The workhorse for readable hard-surface silhouettes: cab profiles, blade
        mouldboard curves, tower cross-sections, terminal roof shapes.
        """
        pts = [(float(a), float(b)) for a, b in profile]
        if len(pts) < 3:
            raise ValueError("prism profile needs at least 3 points")
        if _signed_area(pts) < 0:
            pts.reverse()
        h = float(depth) * 0.5
        local = []
        for sign in (-1.0, 1.0):
            for a, b in pts:
                if axis == 0:
                    local.append((h * sign, a, b))
                elif axis == 1:
                    local.append((a, h * sign, b))
                else:
                    local.append((a, b, h * sign))
        verts = self._place(local, center, rot)
        n = len(pts)
        faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, j, n + j, n + i))
        made = self._emit(verts, faces, mat, parent)
        self._bevel_faces(parent, made, BEVEL_WIDTH_M * 0.8)
        return made

    def lathe(self, profile, center, segments=16, mat=METAL, parent=None, rot=None,
              axis=1):
        """Revolve a (radius, height) profile about an axis: sheaves, drums, fan shrouds."""
        segments = max(3, int(segments))
        prof = [(float(r), float(h)) for r, h in profile]
        verts, faces = [], []
        for i in range(segments):
            a = 2.0 * math.pi * i / segments
            c, s = math.cos(a), math.sin(a)
            for r, h in prof:
                if axis == 0:
                    verts.append((h, c * r, s * r))
                elif axis == 1:
                    verts.append((c * r, h, s * r))
                else:
                    verts.append((c * r, s * r, h))
        n = len(prof)
        for i in range(segments):
            j = (i + 1) % segments
            for k in range(n - 1):
                faces.append((i * n + k, j * n + k, j * n + k + 1, i * n + k + 1))
        # close the profile ends into caps if it does not already return to the axis
        if abs(prof[0][0]) > 1e-5:
            faces.append(tuple(i * n for i in range(segments - 1, -1, -1)))
        if abs(prof[-1][0]) > 1e-5:
            faces.append(tuple(i * n + (n - 1) for i in range(segments)))
        made = self._emit(self._place(verts, center, rot), faces, mat, parent)
        self._smooth_round(made, axis, rot)
        return made

    def loft(self, sections, mat=BODY, parent=None, closed_ends=True):
        """Skin a list of equal-length rings of model-space points.

        Cab shells, terminal housings and cabin bodies are lofts: three or four
        cross-sections give a form with real proportion instead of a stack of boxes.
        """
        if len(sections) < 2:
            raise ValueError("loft needs at least two sections")
        n = len(sections[0])
        if any(len(s) != n for s in sections):
            raise ValueError("loft sections must all have the same point count")
        verts = [v for s in sections for v in s]
        faces = []
        for k in range(len(sections) - 1):
            a, b = k * n, (k + 1) * n
            for i in range(n):
                j = (i + 1) % n
                faces.append((a + i, a + j, b + j, b + i))
        if closed_ends:
            faces.append(tuple(range(n - 1, -1, -1)))
            faces.append(tuple(range((len(sections) - 1) * n, len(sections) * n)))
        made = self._emit(verts, faces, mat, parent)
        self._bevel_faces(parent, made, BEVEL_WIDTH_M)
        return made

    def beam(self, a, b, thickness, mat=METAL, parent=None, square=True, segments=8):
        """A strut between two model-space points: cross-arms, hydraulic rams, railings."""
        av, bv = Vector(a), Vector(b)
        d = bv - av
        length = d.length
        if length < 1e-5:
            return []
        rot = look_rotation((d.x, d.y, d.z))
        mid = (av + bv) * 0.5
        if square:
            return self.box(mid, (thickness, thickness, length), mat=mat, parent=parent,
                            rot=rot, bevel=True)
        return self.cylinder(mid, thickness * 0.5, length, axis=2, segments=segments,
                             mat=mat, parent=parent, rot=rot)

    def sphere(self, center, radius, segments=12, rings=8, mat=BODY, parent=None):
        segments, rings = max(4, int(segments)), max(3, int(rings))
        verts, faces = [], []
        for r in range(rings + 1):
            phi = math.pi * r / rings
            y, rr = math.cos(phi) * radius, math.sin(phi) * radius
            for s in range(segments):
                th = 2.0 * math.pi * s / segments
                verts.append((math.cos(th) * rr, y, math.sin(th) * rr))
        for r in range(rings):
            for s in range(segments):
                s2 = (s + 1) % segments
                a, b = r * segments, (r + 1) * segments
                if r == 0:
                    faces.append((a + s, b + s2, b + s))
                elif r == rings - 1:
                    faces.append((a + s, a + s2, b + s))
                else:
                    faces.append((a + s, a + s2, b + s2, b + s))
        made = self._emit(self._place(verts, center, None), faces, mat, parent)
        for f in made:
            f.smooth = True
        return made

    # ------------------------------------------------------------------ composition
    def mirror_x(self, parent=None, source=None):
        """Mirror a node's geometry across the model centreline and weld it in.

        Machines are symmetric; building one side and mirroring halves the generator
        and guarantees the halves match.
        """
        node = self.nodes[source or parent or self.ROOT]
        bm = node.bm
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        ret = bmesh.ops.duplicate(bm, geom=geom)
        dup_verts = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
        dup_faces = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.scale(bm, vec=Vector((-1.0, 1.0, 1.0)), verts=dup_verts)
        bmesh.ops.reverse_faces(bm, faces=dup_faces)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        return dup_faces

    def array(self, faces, count, offset, parent=None):
        """Repeat a set of faces `count - 1` more times, each shifted by `offset`."""
        bm = self._bm(parent)
        step = to_blender(offset)
        out = []
        for k in range(1, int(count)):
            geom = list({v for f in faces for v in f.verts}) + list(faces)
            ret = bmesh.ops.duplicate(bm, geom=geom)
            verts = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
            bmesh.ops.translate(bm, vec=step * k, verts=verts)
            out += [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
        return out

    def _place(self, local, center, rot):
        cx, cy, cz = float(center[0]), float(center[1]), float(center[2])
        if rot is None:
            return [(x + cx, y + cy, z + cz) for x, y, z in local]
        out = []
        for x, y, z in local:
            p = rot @ to_blender((x, y, z))
            u = to_unity(p)
            out.append((u[0] + cx, u[1] + cy, u[2] + cz))
        return out

    def _bevel_faces(self, parent, faces, width):
        """Chamfer the edges of a freshly added primitive.

        Razor edges catch no highlight and read as noise against snow, so every hard
        surface gets a small chamfer. One segment is enough at these sizes and costs
        two triangles per edge.
        """
        if not faces or width <= 0.0:
            return
        bm = self._bm(parent)
        edges = list({e for f in faces for e in f.edges})
        if not edges:
            return
        try:
            bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET",
                            segments=BEVEL_SEGMENTS, profile=0.5, affect="EDGES",
                            clamp_overlap=True, material=-1)
        except (RuntimeError, ValueError):
            pass                  # a primitive too small to chamfer keeps its hard edges

    @staticmethod
    def _smooth_round(faces, axis, rot):
        """Shade curved side walls smooth and leave caps flat."""
        for f in faces:
            f.smooth = len(f.verts) == 4

    # ------------------------------------------------------------------ finalise
    def realise(self):
        """Turn the node graph into Blender objects. Returns the root object."""
        mats = _materials()
        root = bpy.data.objects.new(self.name, None)
        bpy.context.collection.objects.link(root)
        objects = {self.ROOT: root}

        for name in self.order:
            if name == self.ROOT:
                continue
            node = self.nodes[name]
            obj = self._build_object(node, name, mats)
            objects[name] = obj

        # Root geometry goes into a child called `body` so the root itself stays a clean
        # pivot the game can move without dragging a mesh offset along with it.
        root_node = self.nodes[self.ROOT]
        if not root_node.is_empty:
            body = self._build_object(root_node, "body", mats)
            body.parent = root
            objects["body"] = body

        for name in self.order:
            if name == self.ROOT:
                continue
            node = self.nodes[name]
            obj = objects[name]
            parent_obj = objects.get(node.parent, root)
            obj.parent = parent_obj
            parent_pivot = self.nodes[node.parent].pivot if node.parent in self.nodes else Vector()
            obj.location = to_blender(node.pivot - parent_pivot)

        for sname, point in sorted(self.sockets.items()):
            empty = bpy.data.objects.new(sname, None)
            empty.empty_display_size = 0.15
            bpy.context.collection.objects.link(empty)
            empty.parent = root
            empty.location = to_blender(point)

        bpy.context.view_layer.update()
        return root

    def _build_object(self, node, name, mats):
        mesh = bpy.data.meshes.new(name)
        bm = node.bm
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        self._snapshot(bm)
        if node.pivot.length > 1e-9:
            bmesh.ops.translate(bm, vec=-to_blender(node.pivot), verts=list(bm.verts))
        _unwrap(bm)
        bm.to_mesh(mesh)
        for m in mats:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj

    def _snapshot(self, bm):
        """Record model-space vertices before a node's mesh is rebased onto its pivot.

        Bounds, pivot checks and the collider hull all describe the assembled model, not
        the node-local meshes, so they have to be taken here."""
        if self._world_pts is None:
            self._world_pts = []
        self._world_pts.extend(to_unity(v.co) for v in bm.verts)

    # ------------------------------------------------------------------ stats
    def triangle_count(self):
        total = 0
        for name in self.order:
            bm = self.nodes[name].bm
            for f in bm.faces:
                total += max(1, len(f.verts) - 2)
        return total

    def vertices_world(self):
        """Every vertex in model space as an (n, 3) Unity-space array (hulls, bounds)."""
        if self._world_pts is not None:
            pts = self._world_pts
        else:
            pts = [to_unity(v.co) for name in self.order for v in self.nodes[name].bm.verts]
        return np.asarray(pts, dtype=np.float64) if pts else np.zeros((0, 3))


def _signed_area(pts):
    a = 0.0
    for i in range(len(pts)):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % len(pts)]
        a += x0 * y1 - x1 * y0
    return a * 0.5


def _unwrap(bm):
    """Box-project UVs at one texel per centimetre of surface.

    Every machine shares the industrial trim sheet, so the projection only has to be
    consistent and non-overlapping in scale - panel lines, bolt rows and weld beads come
    from the sheet, not from a per-machine unwrap.
    """
    uv = bm.loops.layers.uv.verify()
    scale = 0.5                   # metres per UV tile
    for face in bm.faces:
        n = face.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for loop in face.loops:
            co = loop.vert.co
            if az >= ax and az >= ay:
                u, v = co.x, co.y
            elif ay >= ax:
                u, v = co.x, co.z
            else:
                u, v = co.y, co.z
            loop[uv].uv = (u / scale, v / scale)


# --------------------------------------------------------------------------- lods
def make_lod(source_objects, ratio, suffix):
    """Merge a hierarchy into one mesh and decimate it to `ratio` of its triangles.

    LOD1 and LOD2 are static proxies: nothing articulates at 60 m, so collapsing the
    whole machine into a single mesh is both cheaper to draw and cheaper to cull.
    """
    bpy.ops.object.select_all(action="DESELECT")
    copies = []
    for obj in source_objects:
        if obj.type != "MESH":
            continue
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.matrix_world = obj.matrix_world.copy()
        bpy.context.collection.objects.link(copy)
        copies.append(copy)
    if not copies:
        return None
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1:
        bpy.ops.object.join()
    merged = bpy.context.view_layer.objects.active
    merged.name = suffix
    merged.data.name = suffix
    merged.matrix_world = Matrix.Identity(4)
    if ratio < 0.999:
        mod = merged.modifiers.new("decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = float(ratio)
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = merged
        bpy.ops.object.modifier_apply(modifier="decimate")
    return merged


def triangulate(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return len(mesh.polygons)


def mesh_triangles(obj):
    return sum(max(1, len(p.vertices) - 2) for p in obj.data.polygons)


LOD_SUFFIXES = tuple("_LOD%d" % i for i in range(len(LOD_RATIOS)))
