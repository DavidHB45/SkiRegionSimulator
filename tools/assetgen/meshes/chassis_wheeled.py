"""Wheeled chassis: every machine in vehicles.json that rolls on tyres.

One code path serves loaders, telehandlers, trucks, tankers, mixers, lowboys, pickups,
tractors, graders, blowers, wheeled cranes and spider excavators. The record is the
model. TireSpec and MassKg size the tyres and say which axles run duals, Visual.BodyL/W/H
sizes the ladder frame, Seats and Tier shape the cab, BucketM3 sizes a loader bucket,
TankL sizes a tanker barrel from its real volume, CargoCapacityKg sizes a bed,
BoomLengthM sizes a telescopic boom and LightingLumens decides how many work lights the
machine carries. Nothing here knows a machine by name: change a number in vehicles.json
and the model changes on the next build.
"""
import math

from config import BEVEL_WIDTH_M
from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

SILHOUETTES = frozenset({
    "loader", "telehandler", "truck", "tanker", "mixer", "lowboy", "pickup",
    "tractor", "grader", "blower", "crane", "excavator",
})

# A road truck's cab is a closed shell with windows let into it; a machine cab is a
# glasshouse of pillars you can see the operator through. Which one a silhouette gets
# is the single biggest thing that tells a plow truck from a loader at 200 m.
_TRUCK_CAB = frozenset({"truck", "tanker", "mixer", "lowboy", "pickup"})

# Machines that work off-road run block tread; road silhouettes run a ribbed highway tyre.
_DEEP_TREAD = frozenset({"loader", "telehandler", "tractor", "grader", "blower",
                         "excavator", "crane"})

# Machines that lead with a nose rather than a work tool, so headlights go at the front.
_NOSE_LIGHTS = frozenset({"truck", "tanker", "mixer", "lowboy", "pickup", "tractor",
                          "grader", "blower", "crane"})

# Machines with a walkable deck behind the cab; only these get deck handrails.
_DECK_RAILS = frozenset({"truck", "tanker", "mixer", "lowboy", "blower", "grader",
                         "crane"})

# Silhouettes whose frame does not run the full length of the record's BodyL, because
# something else (a dropped deck, a bucket, a boom) occupies the rest of it.
_SHORT_FRAME = frozenset({"loader", "telehandler", "excavator", "lowboy"})


def build(record, out_path):
    """Generate the wheeled machine described by `record` and write it to `out_path`."""
    chassis = _Wheeled(record)
    chassis.assemble()
    return export.emit(
        chassis.m, out_path,
        box_colliders=[chassis.cab_collider()],
        extra={"family": chassis.family,
               "kind": "machine",
               "silhouette": chassis.sil,
               "displayName": record.get("DisplayName") or record["Id"],
               "sourceId": record["Id"]})


def _clamp(value, lo, hi):
    return lo if value < lo else (hi if value > hi else value)


def _axle_labels(count):
    """F, M, M2 ... R, so wheel_FL / wheel_ML / wheel_RR match the art contract."""
    if count <= 1:
        return ["F"]
    middle = ["M" if i == 1 else "M%d" % i for i in range(1, count - 1)]
    return ["F"] + middle + ["R"]


class _Wheeled:
    """One machine under construction. Numbers first, then geometry."""

    def __init__(self, record):
        v = datasrc.visual(record)
        self.rec = record
        self.v = v
        self.sil = str(v["Silhouette"] or "truck").lower()
        self.tier = int(_clamp(datasrc.tier_of(record), 1, 5))
        self.mass = max(150.0, float(record.get("MassKg") or 1000.0))
        self.power = float(record.get("EnginePowerKw") or 0.0)
        self.seats = max(1, int(record.get("Seats") or 1))
        self.cargo = float(record.get("CargoCapacityKg") or 0.0)
        self.tank_l = float(record.get("TankL") or 0.0)
        self.bucket_m3 = float(record.get("BucketM3") or 0.0)
        self.boom_len = float(v["BoomLengthM"] or 0.0)
        self.lumens = float(record.get("LightingLumens") or 0.0)
        self.pto = bool(record.get("Pto"))
        self.slots = [s.get("Position") for s in (record.get("AttachmentSlots") or [])]

        self.L = max(1.2, float(v["BodyL"]))
        self.W = max(0.7, float(v["BodyW"]))
        self.H = max(0.35, float(v["BodyH"]))
        self.wr = max(0.15, float(v["WheelRadiusM"]))
        self.axles = max(2, int(v["Axles"] or 2))

        tire = record.get("TireSpec") or {}
        self.tire_count = int(tire.get("Count") or 0)
        width = float(tire.get("WidthM") or 0.0)
        if width <= 0.0:
            # No tyre table on this record: carry the axle load on a footprint that
            # grows with it, the way a real tyre selection does.
            load = self.mass / float(self.axles * 2)
            width = 0.18 + 0.42 * (load / 6000.0) ** 0.6
        self.tw = _clamp(width, 0.11, self.wr * 1.2)

        self.artic = record.get("ChassisType") == "Artic"
        self.family = "artic" if self.artic else "wheeled"
        self.budget = "machine_hero" if datasrc.is_hero(record) else "machine_small"
        self.m = mk.Model(record["Id"], budget_key=self.budget,
                          seed=datasrc.seed_for(record["Id"]))

        # Panel seams the detail pass runs bolt rows along, in model space, as
        # (a, b, bolt axis, owning node).
        self.seams = []
        self.cab_parent = None
        self.cab_node = "cab"
        self.exhaust_top = None
        self.nose_z = self.L * 0.5
        self.wheel_parents = {}
        self.boom_last = None
        self.boom_tip = None

        self._layout()

    # ------------------------------------------------------------------ numbers
    def _layout(self):
        """Everything that is arithmetic rather than geometry, in one place."""
        L, W, H = self.L, self.W, self.H
        self.rail_h = _clamp(0.09 + H * 0.14, 0.13, 0.34)
        self.rail_w = _clamp(0.06 + W * 0.035, 0.07, 0.17)
        self.rail_x = W * 0.31
        self.frame_y = self.wr * 0.92
        self.deck_y = self.frame_y + self.rail_h

        self.ax = self._axle_layout()
        front_z = self.ax[0]["z"]
        rear_z = self.ax[-1]["z"]

        if self.sil in _SHORT_FRAME:
            self.frame_z = (rear_z - self.wr * 1.5, front_z + self.wr * 1.5)
        else:
            self.frame_z = (-0.48 * L, 0.46 * L)

        self.cab_x = 0.0
        self.cl = max(0.6, float(self.v["CabL"]))
        # Seats sets how wide the cab has to be, inside what the body can carry.
        self.cw = min(W * 0.99,
                      max(0.6, float(self.v["CabW"])) * (0.86 + 0.09 * min(self.seats, 3)))
        self.ch = max(0.7, float(self.v["CabH"])) * (0.94 + 0.03 * self.tier)
        self.cz = float(self.v["CabOffset"])
        self.cab_style = "truck" if self.sil in _TRUCK_CAB else "glass"

        if self.sil in ("loader", "telehandler"):
            self.cy = self.wr * 0.80 + H * 0.62
        elif self.sil == "tractor":
            self.cy = self.wr * 0.88
        elif self.sil == "grader":
            # A grader's operator sits behind the blade circle and ahead of the engine,
            # not over the front axle where the record's generic CabOffset would put him.
            self.cy = self.deck_y + H * 0.45
            self.cz = self.ax[-1]["z"] + self.wr * 1.6 + self.cl * 0.5 + 0.1
        elif self.sil == "excavator":
            self.cy = self.deck_y + H * 0.20
        elif self.sil == "crane":
            self.cy = self.deck_y + self.rail_h * 0.6
        else:
            self.cy = self.deck_y + 0.05

        # Keep the cab on the machine however odd the record's CabOffset is.
        self.cz = _clamp(self.cz, -0.5 * L + self.cl * 0.5, 0.5 * L - self.cl * 0.5)

    def _axle_layout(self):
        """Axle centres along Z, front first, with radius, duals and steering per axle."""
        L, n, sil = self.L, self.axles, self.sil
        step = max(2.25 * self.wr, 0.055 * L)

        if sil == "lowboy":
            # Tractor unit up front, trailer bogie under the tail of the dropped deck.
            front = [0.42 * L, 0.27 * L][:max(1, min(2, n - 1))]
            rear_c, steer_n = -0.35 * L, 1
        elif sil in ("loader", "telehandler", "excavator"):
            front, rear_c, steer_n = [0.08 * L], -0.30 * L, (0 if sil == "loader" else 1)
        elif sil == "tractor":
            front, rear_c, steer_n = [0.33 * L], -0.22 * L, 1
        elif sil == "grader":
            front, rear_c, steer_n = [0.44 * L], -0.33 * L, 1
        elif n >= 4:
            front, rear_c, steer_n = [0.36 * L, 0.21 * L], -0.29 * L, 2
        else:
            front, rear_c, steer_n = [0.34 * L], -0.27 * L, 1

        rear_n = max(1, n - len(front))
        rear = [rear_c + ((rear_n - 1) * 0.5 - i) * step for i in range(rear_n)]
        zs = list(front[:n]) + rear

        dual_axles = 0
        if self.tire_count > 2 * n:
            dual_axles = min(n - 1, (self.tire_count - 2 * n) // 2)

        out = []
        for i, z in enumerate(zs):
            radius = self.wr
            if sil == "tractor" and i == 0:
                radius = self.wr * 0.62          # tractors run a small steering axle
            elif sil == "grader" and i == 0:
                radius = self.wr * 0.94
            out.append({
                "z": z,
                "r": radius,
                "w": self.tw * (0.85 if radius < self.wr else 1.0),
                "dual": i >= len(zs) - dual_axles and dual_axles > 0,
                "steer": (not self.artic) and sil != "loader" and i < steer_n,
                "drive": i >= len(front),
            })
        return out

    # ------------------------------------------------------------------ assembly
    def assemble(self):
        if self.artic:
            self.m.node("pivot_center", pivot=(0.0, self.deck_y * 0.75, 0.0))
        if self.sil == "excavator":
            self._spider_legs()          # legs first: the wheels hang off their knees
        self._wheels()
        self._frame()
        getattr(self, "_body_" + self.sil, self._body_default)()
        self._cab()
        self._lamps()
        self._hoses()
        self._sockets()
        self._fill_budget()

    # ------------------------------------------------------------------ wheels
    def _ring_segments(self, radius):
        return int(_clamp(round(9.0 + 11.0 * radius), 10, 22))

    def _hub(self, axle, side):
        """Where an axle's hub sits. Both the wheel and anything that reaches for it -
        a spider leg, a steering knuckle - has to agree on this one point."""
        inset = axle["w"] * (1.02 if axle["dual"] else 0.52)
        return (side * (self.W * 0.5 - inset), axle["r"], axle["z"])

    def _wheels(self):
        labels = _axle_labels(len(self.ax))
        deep = self.sil in _DEEP_TREAD
        # Tread blocks are the most repeated shape on the machine, so how many tyres it
        # carries decides whether they can afford a chamfer.
        tyres = sum(4 if a["dual"] else 2 for a in self.ax)
        for i, axle in enumerate(self.ax):
            r, w = axle["r"], axle["w"]
            for side in (-1.0, 1.0):
                tag = labels[i] + ("L" if side < 0 else "R")
                hub = self._hub(axle, side)
                offsets = (-w * 0.54, w * 0.54) if axle["dual"] else (0.0,)
                parent = self._wheel_parent(i, tag, hub, side)
                node = "wheel_" + tag
                self.m.node(node, pivot=hub, parent=parent)
                for dx in offsets:
                    self._tyre((hub[0] + dx, hub[1], hub[2]), r, w, deep,
                               tyres <= 4, node)
                self._rim((hub[0] + offsets[-1], hub[1], hub[2]), r, w, node)
            self._axle_beam(axle)

    def _wheel_parent(self, index, tag, hub, side):
        """Steered wheels hang off a kingpin node; an artic machine's front wheels hang
        off the articulation joint instead, because it steers by folding; a spider's
        wheels hang off the leg that carries them."""
        if tag in self.wheel_parents:
            return self.wheel_parents[tag]
        if self.artic and not self.ax[index]["drive"]:
            return "pivot_center"
        if not self.ax[index]["steer"]:
            return None
        steer = "steer_" + tag
        self.m.node(steer, pivot=(hub[0] - side * self.tw * 0.28, hub[1], hub[2]))
        return steer

    def _tyre(self, center, radius, width, deep, chamfer, node):
        """A lathed tyre with a real sidewall and block tread. The carcass profile is
        what stops a wheel reading as a can; a road tyre keeps its shoulder ribs and a
        shallow block, a working machine gets a deep one."""
        segments = self._ring_segments(radius)
        hw = width * 0.5
        bead = radius * 0.60
        # The tread blocks are the contact patch, so the carcass crown tucks under them
        # and the lowest point of the machine lands exactly on the ground plane.
        lug_h = radius * (0.075 if deep else 0.04)
        crown = radius - lug_h * 0.55
        profile = [(bead, -hw * 0.54), (crown * 0.76, -hw * 0.97),
                   (crown * 0.96, -hw * 1.0), (crown, -hw * 0.82)]
        if deep:
            profile.append((crown, hw * 0.82))
        else:
            profile += [(crown * 0.96, -hw * 0.30), (crown * 0.96, hw * 0.30),
                        (crown, hw * 0.82)]
        profile += [(crown * 0.96, hw * 1.0), (crown * 0.76, hw * 0.97),
                    (bead, hw * 0.54)]
        self.m.lathe(profile, center, segments=segments, axis=0, mat=METAL, parent=node)
        pitch = 0.42 if deep else 0.24
        lugs = int(_clamp(round(2.0 * math.pi * radius / pitch), 8, 22))
        lug_l = 2.0 * math.pi * radius / lugs * 0.58
        seat = radius - lug_h
        chamfer = chamfer and lug_h * 2.0 > BEVEL_WIDTH_M * 3.5
        for k in range(lugs):
            a = 2.0 * math.pi * k / lugs
            stagger = (k % 2) * 2.0 - 1.0
            self.m.box((center[0] + stagger * width * 0.11,
                        center[1] + seat * math.cos(a),
                        center[2] - seat * math.sin(a)),
                       (width * 0.74, lug_h * 2.0, lug_l),
                       mat=METAL, parent=node, bevel=chamfer,
                       rot=mk.unity_euler(math.degrees(a), 0.0, 0.0))

    def _rim(self, center, radius, width, node):
        segments = max(8, self._ring_segments(radius) - 2)
        self.m.tube(center, radius * 0.60, radius * 0.30, width * 0.52,
                    axis=0, segments=segments, mat=METAL, parent=node)
        self.m.cylinder(center, radius * 0.32, width * 0.66, axis=0,
                        segments=8, mat=METAL, parent=node)
        studs = int(_clamp(round(4.0 + 6.0 * radius), 5, 10))
        circle = radius * 0.44
        nut = _clamp(radius * 0.055, 0.018, 0.05)
        for k in range(studs):
            a = 2.0 * math.pi * k / studs
            self.m.cylinder((center[0] + width * 0.34,
                             center[1] + math.cos(a) * circle,
                             center[2] - math.sin(a) * circle),
                            nut, nut * 1.2, axis=0, segments=6, mat=METAL, parent=node)

    def _axle_beam(self, axle):
        span = self.W - self.tw * 1.4
        self.m.cylinder((0.0, axle["r"], axle["z"]), _clamp(self.wr * 0.16, 0.05, 0.16),
                        span, axis=0, segments=8, mat=METAL)
        if axle["drive"]:
            self.m.cylinder((0.0, axle["r"], axle["z"]),
                            _clamp(self.wr * 0.34, 0.12, 0.34),
                            _clamp(self.wr * 0.5, 0.18, 0.5), axis=2, segments=10,
                            mat=METAL)

    # ------------------------------------------------------------------ frame
    def _frame(self):
        """Two longitudinal rails and their cross members. Under a truck body this is
        the whole reason the machine reads as a truck rather than a box on wheels."""
        z0, z1 = self.frame_z
        length = max(0.6, z1 - z0)
        mid = (z0 + z1) * 0.5
        y = self.frame_y + self.rail_h * 0.5
        for side in (-1.0, 1.0):
            self.m.box((side * self.rail_x, y, mid),
                       (self.rail_w, self.rail_h, length), mat=METAL)
            self.seams.append(((side * self.rail_x, y + self.rail_h * 0.5, z0 + 0.1),
                               (side * self.rail_x, y + self.rail_h * 0.5, z1 - 0.1),
                               1, None))
        members = int(_clamp(round(length / 1.3), 3, 7))
        for k in range(members):
            z = z0 + length * (k + 0.5) / members
            self.m.box((0.0, y, z), (self.rail_x * 2.0, self.rail_h * 0.62,
                                     self.rail_w * 0.85), mat=METAL)
        # Fuel and hydraulic tanks live between the rails on every machine here.
        tank_r = _clamp(self.rail_h * 0.85, 0.16, 0.42)
        self.m.cylinder((self.rail_x + tank_r * 0.9, self.frame_y + tank_r, mid - length * 0.1),
                        tank_r, min(length * 0.35, 1.8), axis=2, segments=10, mat=METAL)
        self.m.box((-self.rail_x - tank_r * 0.9, self.frame_y + tank_r, mid - length * 0.1),
                   (tank_r * 1.4, tank_r * 1.8, min(length * 0.3, 1.5)), mat=METAL)

    # ------------------------------------------------------------------ cab
    def _cab_ring(self, width, y0, y1, z_bottom, z_top, chamfer, top_scale, x0=0.0):
        """One cross-section of a cab shell, raked between its floor and roof lines."""
        hw = width * 0.5
        tw = hw * top_scale
        c = min(chamfer, hw * 0.4, (y1 - y0) * 0.4)
        profile = [(hw - c, y0), (hw, y0 + c), (tw, y1 - c), (tw - c, y1),
                   (-tw + c, y1), (-tw, y1 - c), (-hw, y0 + c), (-hw + c, y0)]
        span = max(1e-6, y1 - y0)
        return [(x + x0, y, z_bottom + (z_top - z_bottom) * (y - y0) / span)
                for x, y in profile]

    def _cab(self):
        cz, cy, cl, cw, ch = self.cz, self.cy, self.cl, self.cw, self.ch
        cx = self.cab_x
        parent = self.cab_parent
        if self.cab_node:
            self.m.node(self.cab_node, pivot=(cx, cy, cz + cl * 0.5), parent=parent)
            parent = self.cab_node
        # Tier is the cab's age: a tier-1 machine keeps a boxy upright screen, a tier-5
        # gets a raked wraparound with a narrow roof.
        rake = ch * (0.03 + 0.055 * self.tier)
        top_scale = 1.0 - 0.024 * self.tier
        chamfer = cw * (0.03 + 0.022 * self.tier)
        zf, zr = cz + cl * 0.5, cz - cl * 0.5
        if self.cab_style == "truck":
            self._cab_solid(parent, zr, zf, cy, cw, ch, rake, top_scale, chamfer, cx)
        else:
            self._cab_glasshouse(parent, zr, zf, cy, cw, ch, rake, top_scale, cx)
        for side in (-1.0, 1.0):
            self.seams.append(((cx + side * cw * 0.45, cy + 0.03, zr + 0.08),
                               (cx + side * cw * 0.45, cy + 0.03, zf - 0.08), 0, parent))
            self.seams.append(((cx + side * cw * 0.34, cy + ch + 0.02, zr + rake * 0.4),
                               (cx + side * cw * 0.34, cy + ch + 0.02, zf - rake),
                               1, parent))

    def _cab_solid(self, parent, zr, zf, cy, cw, ch, rake, top_scale, chamfer, cx):
        y1 = cy + ch
        sections = [
            self._cab_ring(cw * 0.96, cy, y1, zr, zr + rake * 0.45, chamfer, top_scale, cx),
            self._cab_ring(cw, cy, y1, zr + (zf - zr) * 0.35,
                           zr + (zf - zr) * 0.35 + rake * 0.1, chamfer, top_scale, cx),
            self._cab_ring(cw, cy, y1, zf - (zf - zr) * 0.28,
                           zf - (zf - zr) * 0.28 - rake * 0.35, chamfer, top_scale, cx),
            self._cab_ring(cw * 0.94, cy, y1, zf, zf - rake, chamfer, top_scale, cx),
        ]
        self.m.loft(sections, mat=BODY, parent=parent)
        self.m.box((cx, y1 + 0.03, (zr + zf) * 0.5 - rake * 0.2),
                   (cw * top_scale * 0.98, 0.07, (zf - zr) * 0.86), mat=BODY, parent=parent)
        self._cab_glass(parent, zr, zf, cy, cw, ch, rake, top_scale, True, cx)

    def _cab_glasshouse(self, parent, zr, zf, cy, cw, ch, rake, top_scale, cx):
        """A machine cab: floor, corner pillars, roof, and glass in between."""
        y1 = cy + ch
        self.m.box((cx, cy + 0.05, (zr + zf) * 0.5), (cw, 0.10, zf - zr),
                   mat=METAL, parent=parent)
        post = _clamp(cw * 0.055, 0.05, 0.12)
        for side in (-1.0, 1.0):
            x = cx + side * (cw * 0.5 - post * 0.5)
            xt = cx + side * (cw * 0.5 * top_scale - post * 0.5)
            self.m.beam((x, cy + 0.08, zf - post * 0.5),
                        (xt, y1 - 0.04, zf - rake - post * 0.5), post, mat=BODY,
                        parent=parent)
            self.m.beam((x, cy + 0.08, zr + post * 0.5),
                        (xt, y1 - 0.04, zr + rake * 0.4 + post * 0.5), post, mat=BODY,
                        parent=parent)
            self.m.box((x, cy + ch * 0.5, (zr + zf) * 0.5),
                       (post * 0.8, ch, post * 0.8), mat=BODY, parent=parent)
        roof_ring0 = self._cab_ring(cw * top_scale, y1 - 0.02, y1 + 0.10, zr + rake * 0.4,
                                    zr + rake * 0.4 + 0.04, cw * 0.10, 0.94, cx)
        roof_ring1 = self._cab_ring(cw * top_scale, y1 - 0.02, y1 + 0.10, zf - rake,
                                    zf - rake - 0.04, cw * 0.10, 0.94, cx)
        self.m.loft([roof_ring0, roof_ring1], mat=BODY, parent=parent)
        self._cab_glass(parent, zr, zf, cy, cw, ch, rake, top_scale, False, cx)
        self._cab_interior(parent, zr, zf, cy, cw, ch, cx)

    def _cab_glass(self, parent, zr, zf, cy, cw, ch, rake, top_scale, solid, cx):
        y0 = cy + (ch * 0.42 if solid else 0.14)
        y1 = cy + ch - ch * 0.12
        mid = (y0 + y1) * 0.5
        height = y1 - y0
        frac = (mid - cy) / max(1e-6, ch)
        pitch = math.degrees(math.atan2(rake, max(0.2, ch)))
        self.m.box((cx, mid, zf - rake * frac - 0.015),
                   (cw * 0.86, height, 0.035), mat=GLASS, parent=parent,
                   rot=mk.unity_euler(pitch, 0.0, 0.0), bevel=False)
        for side in (-1.0, 1.0):
            self.m.box((cx + side * (cw * 0.5 * (1.0 + top_scale) * 0.5 - 0.02), mid,
                        (zr + zf) * 0.5),
                       (0.035, height, (zf - zr) * 0.72), mat=GLASS, parent=parent,
                       bevel=False)
        self.m.box((cx, mid, zr + rake * 0.4 * frac + 0.02),
                   (cw * 0.78, height * 0.82, 0.035), mat=GLASS, parent=parent,
                   bevel=False)

    def _cab_interior(self, parent, zr, zf, cy, cw, ch, cx):
        """Seats, column and dash. Behind a glasshouse these are actually visible, and
        Seats is the record telling us how many to build."""
        seats = min(3, self.seats)
        width = cw / (seats + 0.6)
        for k in range(seats):
            x = cx + (k - (seats - 1) * 0.5) * width
            z = (zr + zf) * 0.5 - (zf - zr) * 0.08
            self.m.box((x, cy + 0.36, z), (width * 0.66, 0.12, width * 0.72),
                       mat=METAL, parent=parent)
            self.m.box((x, cy + 0.62, z - width * 0.34), (width * 0.62, 0.52, 0.10),
                       mat=METAL, parent=parent,
                       rot=mk.unity_euler(-8.0, 0.0, 0.0))
        self.m.wedge((cx, cy + 0.52, zf - (zf - zr) * 0.16),
                     (cw * 0.82, 0.28, (zf - zr) * 0.22), mat=METAL, parent=parent)
        wheel_r = _clamp(cw * 0.12, 0.10, 0.20)
        self.m.tube((cx, cy + 0.72, zf - (zf - zr) * 0.26), wheel_r, wheel_r * 0.78,
                    0.035, axis=2, segments=10, mat=METAL, parent=parent,
                    rot=mk.unity_euler(25.0, 0.0, 0.0))

    # ------------------------------------------------------------------ bodies
    def _engine_deck(self, z0, z1, top_y, width_scale=0.9, mat=BODY):
        """The hood over the engine, with a louvred face at its far end."""
        width = self.W * width_scale
        centre = (z0 + z1) * 0.5
        height = max(0.2, top_y - self.deck_y)
        self.m.box((0.0, self.deck_y + height * 0.5, centre),
                   (width, height, abs(z1 - z0)), mat=mat)
        face = min(z0, z1) - 0.01 if z1 < z0 else max(z0, z1) + 0.01
        louvres = int(_clamp(round(height / 0.14), 3, 8))
        for k in range(louvres):
            y = self.deck_y + height * (k + 0.6) / (louvres + 0.4)
            self.m.box((0.0, y, face), (width * 0.72, height / (louvres + 1.2) * 0.55,
                                        0.05), mat=METAL, bevel=False)
        self.seams.append(((0.0, self.deck_y + height, min(z0, z1) + 0.1),
                           (0.0, self.deck_y + height, max(z0, z1) - 0.1), 1, None))
        for side in (-1.0, 1.0):
            self.seams.append(((side * width * 0.5, self.deck_y + height * 0.55,
                                min(z0, z1) + 0.1),
                               (side * width * 0.5, self.deck_y + height * 0.55,
                                max(z0, z1) - 0.1), 0, None))
        return self.deck_y + height

    def _body_default(self):
        """Anything without a silhouette of its own: a flat deck and a cargo box."""
        top = self._engine_deck(self.cz - self.cl * 0.5, self.frame_z[1],
                                self.deck_y + self.H * 0.55)
        z0, z1 = self.frame_z[0], self.cz - self.cl * 0.5
        self._cargo_box(z0, z1, max(0.25, self.H * 0.5))
        self._fenders()
        return top

    def _cargo_box(self, z0, z1, height, ribs=True):
        length = abs(z1 - z0)
        if length < 0.3:
            return
        centre = (z0 + z1) * 0.5
        width = self.W * 0.96
        wall = _clamp(width * 0.03, 0.05, 0.09)
        self.m.box((0.0, self.deck_y + 0.05, centre), (width, 0.10, length), mat=METAL)
        for side in (-1.0, 1.0):
            self.m.box((side * (width * 0.5 - wall * 0.5), self.deck_y + height * 0.5 + 0.05,
                        centre), (wall, height, length), mat=BODY)
        for end in (z0, z1):
            sign = 1.0 if end > centre else -1.0
            # The headboard runs into the side walls rather than butting up against
            # them: exactly coincident faces weld into non-manifold edges.
            self.m.box((0.0, self.deck_y + height * 0.5 + 0.05, end - sign * wall * 0.5),
                       (width - wall * 0.6, height, wall), mat=BODY)
        if ribs:
            count = int(_clamp(round(length / 0.55), 2, 9))
            for k in range(count):
                z = z0 + (z1 - z0) * (k + 0.5) / count
                for side in (-1.0, 1.0):
                    self.m.box((side * (width * 0.5 + 0.02),
                                self.deck_y + height * 0.5 + 0.05, z),
                               (0.05, height * 0.92, wall * 1.6), mat=BODY, bevel=False)
        for side in (-1.0, 1.0):
            self.seams.append(((side * width * 0.5, self.deck_y + height * 0.9, z0 + 0.1),
                               (side * width * 0.5, self.deck_y + height * 0.9, z1 - 0.1),
                               0, None))
        self.seams.append(((0.0, self.deck_y + 0.12, z0 + 0.1),
                           (0.0, self.deck_y + 0.12, z1 - 0.1), 1, None))

    def _fenders(self, axles=None, arc_scale=1.22):
        """An arched shell over each wheel, prismed from a ring profile. Extruded along
        X, so the profile is (y, z): height first, then fore and aft."""
        if axles is None:
            # A truck carries its rear wheels under the body, where an arch would push
            # up through the bed floor; only the steering axle gets one.
            axles = ([a for a in self.ax if a["steer"]] if self.sil in _TRUCK_CAB
                     else self.ax)
        for axle in axles:
            outer = axle["r"] * arc_scale
            inner = axle["r"] * 1.07
            points, steps = [], 7
            for k in range(steps + 1):
                a = math.pi * (0.10 + 0.80 * k / steps)
                points.append((math.sin(a) * outer, math.cos(a) * outer))
            for k in range(steps, -1, -1):
                a = math.pi * (0.10 + 0.80 * k / steps)
                points.append((math.sin(a) * inner, math.cos(a) * inner))
            for side in (-1.0, 1.0):
                x = side * (self.W * 0.5 - axle["w"] * (0.9 if axle["dual"] else 0.42))
                self.m.prism(points, (x, axle["r"], axle["z"]), axle["w"] * 1.2,
                             mat=BODY, axis=0)
                self.seams.append(((x, axle["r"] + outer * 0.92,
                                    axle["z"] - outer * 0.55),
                                   (x, axle["r"] + outer * 0.92,
                                    axle["z"] + outer * 0.55), 0, None))

    # -- loader ---------------------------------------------------------------
    def _body_loader(self):
        L, W, H = self.L, self.W, self.H
        waist = self.ax[0]["z"] - self.wr * 1.15
        rear_top = self.deck_y + H * 0.95
        # Rear frame: engine bay and counterweight, the mass the bucket works against.
        self.m.box((0.0, self.deck_y + H * 0.5, (self.frame_z[0] + waist) * 0.5),
                   (W * 0.86, H, waist - self.frame_z[0]), mat=BODY)
        self.m.wedge((0.0, self.deck_y + H * 0.45, self.frame_z[0] - H * 0.22),
                     (W * 0.78, H * 0.9, H * 0.5), mat=METAL,
                     rot=mk.unity_euler(0.0, 180.0, 0.0))
        self._engine_deck(waist, self.frame_z[0] + 0.05, rear_top, 0.8)
        # Front frame and the articulation pin between the two halves.
        front_z1 = self.ax[0]["z"] + self.wr * 1.1
        self.m.box((0.0, self.frame_y + H * 0.3, (waist + front_z1) * 0.5),
                   (W * 0.62, H * 0.62, front_z1 - waist), mat=BODY)
        self.m.cylinder((0.0, self.deck_y + H * 0.1, waist), _clamp(W * 0.07, 0.08, 0.22),
                        H * 0.9, axis=1, segments=10, mat=METAL)
        for side in (-1.0, 1.0):
            self.seams.append(((side * W * 0.43, self.deck_y + H * 0.75,
                                self.frame_z[0] + 0.2),
                               (side * W * 0.43, self.deck_y + H * 0.75, waist - 0.1),
                               0, None))
            self.seams.append(((side * W * 0.31, self.frame_y + H * 0.45, waist + 0.1),
                               (side * W * 0.31, self.frame_y + H * 0.45, front_z1),
                               0, None))
        self._fenders()

        pin_y = self.cy + self.ch * 0.30
        pin_z = self.cz - self.cl * 0.45
        tip_z = 0.46 * L
        tip_y = self.wr * 0.95
        self.m.node("boom_01", pivot=(0.0, pin_y, pin_z))
        arm_x = W * 0.34
        thick = _clamp(W * 0.075, 0.08, 0.26)
        knee = (-arm_x, self.deck_y + H * 0.55, self.ax[0]["z"] + self.wr * 0.6)
        self.m.beam((-arm_x, pin_y, pin_z), knee, thick, mat=BODY, parent="boom_01")
        self.m.beam(knee, (-arm_x, tip_y + H * 0.28, tip_z), thick, mat=BODY,
                    parent="boom_01")
        self.m.mirror_x(parent="boom_01")
        self.m.cylinder((0.0, tip_y + H * 0.28, tip_z), thick * 0.55, arm_x * 2.0,
                        axis=0, segments=8, mat=METAL, parent="boom_01")
        self.m.cylinder((0.0, knee[1] + thick * 0.4, knee[2] - thick),
                        thick * 0.45, arm_x * 1.9, axis=0, segments=8, mat=METAL,
                        parent="boom_01")
        for side in (-1.0, 1.0):
            self._ram((side * arm_x * 0.72, self.deck_y + H * 0.25, waist + 0.1),
                      (side * arm_x * 0.98, knee[1] - thick * 0.5, knee[2] - 0.15),
                      thick * 0.42, parent="boom_01")

        self.m.node("bucket", pivot=(0.0, tip_y + H * 0.28, tip_z), parent="boom_01")
        self._bucket(tip_z, tip_y + H * 0.28, W * 0.99, self.bucket_m3 or W * 0.25)
        self._ram((0.0, knee[1] + thick * 1.6, knee[2] - thick * 0.2),
                  (0.0, tip_y + H * 0.55, tip_z - thick), thick * 0.5, parent="boom_01")
        self.mount_front = (0.0, tip_y, tip_z + 0.1)

    def _bucket(self, pin_z, pin_y, width, volume):
        """A C-section shell scaled so its opening holds BucketM3 at a 0.55 fill factor."""
        unit = [(0.42, -0.10), (-0.14, -0.14), (-0.33, 0.08), (-0.36, 0.70),
                (-0.26, 0.70), (-0.22, 0.14), (-0.02, 0.02), (0.42, 0.06)]
        span_y = max(p[0] for p in unit) - min(p[0] for p in unit)
        span_z = max(p[1] for p in unit) - min(p[1] for p in unit)
        scale = math.sqrt(max(0.04, volume) / max(0.05, width * 0.55 * span_y * span_z))
        profile = [(y * scale, z * scale) for y, z in unit]
        self.m.prism(profile, (0.0, pin_y, pin_z), width, mat=BODY, axis=0,
                     parent="bucket")
        edge_z = pin_z + 0.70 * scale
        edge_y = pin_y - 0.33 * scale
        teeth = int(_clamp(round(width / 0.36), 3, 9))
        for k in range(teeth):
            x = (k - (teeth - 1) * 0.5) * (width * 0.92 / teeth)
            self.m.box((x, edge_y + 0.02, edge_z + 0.06),
                       (width * 0.32 / teeth, 0.07, 0.22), mat=METAL, parent="bucket",
                       bevel=False)
        for side in (-1.0, 1.0):
            self.m.box((side * width * 0.5, pin_y - 0.10 * scale, pin_z + 0.30 * scale),
                       (0.04, 0.62 * scale, 0.74 * scale), mat=BODY, parent="bucket",
                       bevel=False)

    def _ram(self, a, b, radius, parent=None):
        """Hydraulic ram: barrel and rod, drawn between two anchor points."""
        ax, ay, az = a
        bx, by, bz = b
        mid = ((ax + bx) * 0.5, (ay + by) * 0.5, (az + bz) * 0.5)
        length = math.dist(a, b)
        if length < 0.12:
            return
        rot = mk.look_rotation((bx - ax, by - ay, bz - az))
        self.m.cylinder(((ax + mid[0]) * 0.5, (ay + mid[1]) * 0.5, (az + mid[2]) * 0.5),
                        radius, length * 0.56, axis=2, segments=10, mat=METAL,
                        parent=parent, rot=rot)
        self.m.cylinder(((bx + mid[0]) * 0.5, (by + mid[1]) * 0.5, (bz + mid[2]) * 0.5),
                        radius * 0.55, length * 0.55, axis=2, segments=8, mat=METAL,
                        parent=parent, rot=rot)

    # -- telehandler ----------------------------------------------------------
    def _body_telehandler(self):
        L, W, H = self.L, self.W, self.H
        # Cab down one side, engine pod down the other, boom in the channel between
        # them: that asymmetry is the whole reason a telehandler reads as one.
        chassis_top = self.deck_y + H * 0.42
        self.m.box((0.0, (self.deck_y + chassis_top) * 0.5, 0.0),
                   (W * 0.88, chassis_top - self.deck_y,
                    self.frame_z[1] - self.frame_z[0]), mat=BODY)
        pod_x, pod_h = W * 0.29, H * 0.40
        pod_top = chassis_top + pod_h
        self.m.box((pod_x, chassis_top + pod_h * 0.5, -L * 0.22),
                   (W * 0.40, pod_h, L * 0.42), mat=BODY)
        louvres = int(_clamp(round(pod_h / 0.18), 3, 6))
        for k in range(louvres):
            self.m.box((pod_x + W * 0.20, chassis_top + pod_h * (k + 0.7) / (louvres + 0.5),
                        -L * 0.22), (0.05, pod_h / (louvres + 1.4), L * 0.30),
                       mat=METAL, bevel=False)
        self.m.wedge((0.0, self.deck_y + H * 0.22, self.frame_z[0] - H * 0.16),
                     (W * 0.8, H * 0.44, H * 0.36), mat=METAL,
                     rot=mk.unity_euler(0.0, 180.0, 0.0))
        self.cab_x = -W * 0.29
        self.cw = min(self.cw, W * 0.42)
        self.cy = chassis_top
        self._fenders()
        boom = max(2.0, self.boom_len or L * 0.9)
        pivot = (W * 0.04, chassis_top + H * 0.52, self.frame_z[0] + boom * 0.05)
        self._telescope(pivot, boom, -15.0, W * 0.22, retract=0.70)
        self.seams.append(((pod_x, pod_top, -L * 0.38), (pod_x, pod_top, L * 0.16),
                           1, None))
        # The head jogs back to the centreline so the forks straddle the machine even
        # though the boom itself rides down one side.
        tip = self.boom_tip
        self.m.node("fork_carriage", pivot=tip, parent=self.boom_last)
        self.m.box((0.0, tip[1], tip[2]), (W * 0.62, H * 0.5, 0.10), mat=METAL,
                   parent="fork_carriage")
        self.m.beam(tip, (0.0, tip[1], tip[2]), W * 0.14, mat=METAL,
                    parent="fork_carriage")
        for side in (-1.0, 1.0):
            tag = "fork_" + ("L" if side < 0 else "R")
            self.m.node(tag, pivot=(side * W * 0.2, tip[1], tip[2]),
                        parent="fork_carriage")
            self.m.box((side * W * 0.2, tip[1] - H * 0.12, tip[2] + 0.06),
                       (0.11, H * 0.55, 0.07), mat=METAL, parent=tag)
            self.m.box((side * W * 0.2, tip[1] - H * 0.38, tip[2] + L * 0.10),
                       (0.11, 0.07, L * 0.22), mat=METAL, parent=tag)
        self.mount_front = (0.0, tip[1] - H * 0.38, tip[2] + L * 0.10)

    def _telescope(self, pivot, length, pitch_deg, base_size, parent=None, retract=0.48):
        """Nested boom sections that slide along +Z. Retracted, each one shows its tip;
        `retract` is how much of the extended length the stack folds down to."""
        count = int(_clamp(round(length / 4.0), 2, 4))
        retracted = length * retract
        rot = mk.unity_euler(pitch_deg, 0.0, 0.0) if abs(pitch_deg) > 1e-3 else None
        direction = (0.0, math.sin(math.radians(pitch_deg)), math.cos(math.radians(pitch_deg)))
        node = parent
        tip = pivot
        for k in range(count):
            name = "boom_%02d" % (k + 1)
            start = retracted * 0.07 * k
            seg = retracted * (1.0 - 0.1 * k)
            centre = tuple(pivot[i] + direction[i] * (start + seg * 0.5) for i in range(3))
            self.m.node(name, pivot=tuple(pivot[i] + direction[i] * start for i in range(3)),
                        parent=node)
            size = base_size * (1.0 - 0.13 * k)
            self.m.box(centre, (size, size * 1.05, seg), mat=BODY, parent=name, rot=rot)
            node = name
            tip = tuple(pivot[i] + direction[i] * (start + seg) for i in range(3))
        self.boom_last = node
        self.boom_tip = tip
        return node

    # -- truck ----------------------------------------------------------------
    def _body_truck(self):
        H = self.H
        z_bed0, z_bed1 = self.frame_z[0] + 0.05, self.cz - self.cl * 0.55
        height = _clamp(0.35 + self.cargo / 26000.0 * 1.5, 0.35, 1.7)
        if self.cargo >= 4000.0:
            self._cargo_box(z_bed0, z_bed1, height)
            self.m.cylinder((0.0, self.deck_y + 0.25, z_bed1 - 0.3),
                            _clamp(self.W * 0.06, 0.07, 0.18), 0.9, axis=1,
                            segments=8, mat=METAL)
        else:
            # A service or plow body: low deck, side lockers, a hopper if it spreads.
            self._cargo_box(z_bed0, z_bed1, max(0.30, height * 0.8), ribs=False)
            for side in (-1.0, 1.0):
                self.m.box((side * self.W * 0.46, self.deck_y + H * 0.30,
                            (z_bed0 + z_bed1) * 0.5),
                           (self.W * 0.10, H * 0.5, (z_bed1 - z_bed0) * 0.55), mat=BODY)
        self._fenders()
        self._nose_plow_frame()

    def _body_pickup(self):
        H, W = self.H, self.W
        # A pickup is a low bonnet and a low bed either side of a tall cab; that
        # contrast is the whole silhouette.
        hood_z1 = self.cz - self.cl * 0.5
        hood_h = H * 0.55
        self.m.box((0.0, self.deck_y + hood_h * 0.5, (hood_z1 + self.nose_z) * 0.5),
                   (W * 0.95, hood_h, self.nose_z - hood_z1), mat=BODY)
        grille = int(_clamp(round(hood_h / 0.12), 3, 6))
        for k in range(grille):
            self.m.box((0.0, self.deck_y + hood_h * (k + 0.8) / (grille + 1.2),
                        self.nose_z - 0.02),
                       (W * 0.7, hood_h / (grille + 1.6), 0.05), mat=METAL, bevel=False)
        self.m.box((0.0, self.deck_y + H * 0.12, self.nose_z - 0.06),
                   (W * 0.96, H * 0.22, 0.12), mat=METAL)
        z0, z1 = self.frame_z[0] + 0.02, self.cz + self.cl * 0.5 - self.cl
        self._cargo_box(z0, min(z1, self.cz - self.cl * 0.52),
                        max(0.35, H * 0.45), ribs=False)
        self._fenders()
        self._nose_plow_frame()

    def _nose_plow_frame(self):
        """A plow push frame, but only on a machine the record says mounts something
        at the front."""
        if "Front" not in self.slots:
            return
        y = max(0.28, self.wr * 0.62)
        z = self.nose_z - 0.05
        for side in (-1.0, 1.0):
            self.m.beam((side * self.rail_x, self.frame_y + self.rail_h * 0.5,
                         self.frame_z[1] - 0.4),
                        (side * self.W * 0.28, y, z), _clamp(self.W * 0.05, 0.06, 0.14),
                        mat=METAL)
        self.m.box((0.0, y, z), (self.W * 0.62, _clamp(self.H * 0.3, 0.12, 0.3), 0.10),
                   mat=METAL)
        self.mount_front = (0.0, y, z + 0.08)

    # -- tanker ---------------------------------------------------------------
    def _body_tanker(self):
        H, W = self.H, self.W
        z0, z1 = self.frame_z[0] + 0.15, self.cz - self.cl * 0.55
        deck = max(0.8, z1 - z0)
        volume = max(0.4, self.tank_l / 1000.0)
        a = W * 0.45
        length = deck * 0.92
        b = volume / (math.pi * a * length)
        b = _clamp(b, 0.30, H * 1.35)
        length = _clamp(volume / (math.pi * a * b), 0.8, deck)
        cz = (z0 + z1) * 0.5
        base = self.deck_y + b + 0.10
        rings = 14

        def ellipse(scale, z):
            pts = []
            for k in range(rings):
                t = 2.0 * math.pi * k / rings
                pts.append((math.cos(t) * a * scale, base + math.sin(t) * b * scale, z))
            return pts

        half = length * 0.5
        self.m.loft([ellipse(0.84, cz - half), ellipse(1.0, cz - half + b * 0.45),
                     ellipse(1.0, cz + half - b * 0.45), ellipse(0.84, cz + half)],
                    mat=BODY)
        baffles = int(_clamp(round(length / 1.1), 2, 5))
        for k in range(baffles):
            z = cz - half + length * (k + 0.5) / baffles
            profile = [(math.cos(2.0 * math.pi * i / 10) * a * 1.04,
                        math.sin(2.0 * math.pi * i / 10) * b * 1.05) for i in range(10)]
            self.m.prism(profile, (0.0, base, z), 0.06, mat=METAL, axis=2)
        # Catwalk and rear pump cabinet.
        self.m.box((0.0, base + b + 0.06, cz), (a * 0.5, 0.05, length * 0.8), mat=METAL,
                   bevel=False)
        for side in (-1.0, 1.0):
            self.m.beam((side * a * 0.28, base + b + 0.08, cz - length * 0.38),
                        (side * a * 0.28, base + b + 0.08, cz + length * 0.38),
                        0.05, mat=METAL, square=False, segments=8)
            for k in range(3):
                z = cz + (k - 1) * length * 0.36
                self.m.box((side * a * 0.28, base + b + 0.32, z), (0.05, 0.5, 0.05),
                           mat=METAL, bevel=False)
        self.m.box((0.0, self.deck_y + H * 0.35, z0 - 0.25),
                   (W * 0.8, H * 0.7, 0.5), mat=BODY)
        self.m.cylinder((0.0, self.deck_y + H * 0.35, z0 - 0.52),
                        _clamp(H * 0.22, 0.12, 0.3), 0.3, axis=2, segments=10, mat=METAL)
        self._fenders()
        self._nose_plow_frame()
        self.seams.append(((0.0, base + b, cz - half + 0.1),
                           (0.0, base + b, cz + half - 0.1), 1, None))

    # -- mixer ----------------------------------------------------------------
    def _body_mixer(self):
        H, W = self.H, self.W
        z0, z1 = self.frame_z[0] + 0.3, self.cz - self.cl * 0.55
        length = max(1.2, z1 - z0)
        # The drum's mouth is at the rear and rides high, which is why a mixer stands
        # nose down; everything at the tail hangs off that one point.
        tilt = -14.0
        radius = W * 0.44
        base = self.deck_y + radius * 0.9
        cz = (z0 + z1) * 0.5
        rot = mk.unity_euler(tilt, 0.0, 0.0)
        segments = 14
        drum = [(radius * 0.44, -length * 0.5), (radius * 0.82, -length * 0.28),
                (radius, length * 0.02), (radius * 0.92, length * 0.26),
                (radius * 0.30, length * 0.5)]
        self.m.lathe(drum, (0.0, base, cz), segments=segments, axis=2, mat=BODY, rot=rot)
        self._mixer_fins(base, cz, length, radius, rot)
        mouth = self._rotate_into([[(0.0, 0.0, -length * 0.48)]], (0.0, base, cz), rot)[0][0]
        hopper = (mouth[0], mouth[1] + radius * 0.22, mouth[2])
        self.m.lathe([(radius * 0.62, radius * 0.62), (radius * 0.24, 0.0)], hopper,
                     segments=10, axis=1, mat=METAL)
        for side in (-1.0, 1.0):
            self.m.beam((side * W * 0.34, self.deck_y, z0 + 0.2),
                        (side * radius * 0.45, hopper[1] + radius * 0.5, hopper[2]),
                        _clamp(W * 0.05, 0.06, 0.14), mat=METAL)
        self.m.wedge((0.0, mouth[1] - radius * 0.75, mouth[2] - radius * 0.55),
                     (radius * 0.9, radius * 0.7, radius * 1.3), mat=METAL,
                     rot=mk.unity_euler(0.0, 180.0, 0.0))
        self.m.cylinder((0.0, self.deck_y + H * 0.75, z1 - 0.25),
                        _clamp(H * 0.25, 0.14, 0.35), W * 0.62, axis=0, segments=10,
                        mat=METAL)
        for side in (-1.0, 1.0):
            self.m.beam((side * W * 0.42, self.deck_y + 0.05, cz - length * 0.3),
                        (side * W * 0.34, base - radius * 0.3, cz - length * 0.3),
                        _clamp(W * 0.05, 0.06, 0.14), mat=METAL)
            self.m.beam((side * W * 0.42, self.deck_y + 0.05, cz + length * 0.3),
                        (side * W * 0.34, base - radius * 0.2, cz + length * 0.3),
                        _clamp(W * 0.05, 0.06, 0.14), mat=METAL)
        self._fenders()
        self._nose_plow_frame()

    def _mixer_fins(self, base, cz, length, radius, rot):
        """Two helical mixing fins lofted along the drum, the shape that makes a mixer
        read as a mixer rather than a tank."""
        steps = 9
        fin_w = radius * 0.14
        for start in (0.0, math.pi):
            sections = []
            for k in range(steps):
                t = k / float(steps - 1)
                angle = start + t * math.pi * 1.15
                r = radius * (0.30 + 0.70 * math.sin(math.pi * (0.12 + 0.76 * t)))
                z = -length * 0.44 + length * 0.88 * t
                cx, cy = math.cos(angle), math.sin(angle)
                ring = []
                for dx, dy in ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5)):
                    ring.append((cx * (r + dy * fin_w) + (-cy) * dx * 0.04,
                                 cy * (r + dy * fin_w) + cx * dx * 0.04,
                                 z + dx * fin_w * 0.9))
                sections.append(ring)
            placed = self._rotate_into(sections, (0.0, base, cz), rot)
            self.m.loft(placed, mat=METAL)

    def _rotate_into(self, sections, centre, rot):
        out = []
        for ring in sections:
            moved = []
            for x, y, z in ring:
                p = rot @ mk.to_blender((x, y, z))
                u = mk.to_unity(p)
                moved.append((u[0] + centre[0], u[1] + centre[1], u[2] + centre[2]))
            out.append(moved)
        return out

    # -- lowboy ---------------------------------------------------------------
    def _body_lowboy(self):
        H, W = self.H, self.W
        tractor_z0 = self.ax[1]["z"] - self.wr * 1.4 if len(self.ax) > 1 else 0.0
        self._engine_deck(self.cz - self.cl * 0.5, self.nose_z - 0.1,
                          self.deck_y + H * 0.7, 0.92)
        self.m.box((0.0, self.deck_y + 0.12, (tractor_z0 + self.cz) * 0.5),
                   (W * 0.8, 0.22, self.cz - tractor_z0), mat=METAL)
        self.m.cylinder((0.0, self.deck_y + 0.28, tractor_z0 + 0.35),
                        _clamp(W * 0.16, 0.2, 0.45), 0.10, axis=1, segments=12, mat=METAL)
        # Gooseneck down to the dropped deck, then the deck and its loading ramp.
        deck_y = max(0.28, self.wr * 0.55)
        deck_z1 = tractor_z0 - 0.2
        deck_z0 = self.ax[-1]["z"] - self.wr * 0.4
        self.m.wedge((0.0, (deck_y + self.deck_y) * 0.5 + 0.05, deck_z1 + 0.35),
                     (W * 0.86, self.deck_y - deck_y + 0.2, 0.9), mat=BODY,
                     rot=mk.unity_euler(0.0, 180.0, 0.0))
        length = max(0.8, deck_z1 - deck_z0)
        self.m.box((0.0, deck_y, (deck_z0 + deck_z1) * 0.5), (W * 0.98, 0.18, length),
                   mat=BODY)
        for side in (-1.0, 1.0):
            self.m.box((side * W * 0.46, deck_y + 0.16, (deck_z0 + deck_z1) * 0.5),
                       (0.07, 0.16, length), mat=METAL)
        beams = int(_clamp(round(length / 0.9), 3, 9))
        for k in range(beams):
            z = deck_z0 + length * (k + 0.5) / beams
            self.m.box((0.0, deck_y - 0.14, z), (W * 0.92, 0.12, 0.10), mat=METAL,
                       bevel=False)
        self.m.box((0.0, deck_y + 0.34, self.ax[-1]["z"]), (W * 0.98, 0.5,
                                                            self.wr * 2.6), mat=BODY)
        self.m.wedge((0.0, deck_y * 0.5, self.frame_z[0] - 0.5),
                     (W * 0.9, deck_y, 1.1), mat=METAL)
        self._fenders()
        self.seams.append(((0.0, deck_y + 0.1, deck_z0 + 0.2),
                           (0.0, deck_y + 0.1, deck_z1 - 0.2), 1, None))

    # -- tractor --------------------------------------------------------------
    def _body_tractor(self):
        H, W = self.H, self.W
        rear_r = self.ax[-1]["r"]
        nose_z = self.nose_z
        hood_z0 = self.cz - self.cl * 0.5
        # Waisted bonnet: narrow over the front axle so the steering wheels can turn.
        waist = W * 0.38
        wide = W * 0.52
        top = self.cy + H * 0.32
        sections = []
        for z, w, h in ((nose_z, waist * 0.86, top - H * 0.20),
                        (nose_z - (nose_z - hood_z0) * 0.42, waist, top - H * 0.06),
                        (hood_z0 + 0.05, wide, top)):
            y0 = self.wr * 0.55
            sections.append(self._cab_ring(w, y0, h, z, z, w * 0.12, 0.82))
        self.m.loft(sections, mat=BODY)
        self.seams.append(((0.0, top - H * 0.2, hood_z0),
                           (0.0, top - H * 0.3, nose_z), 1, None))
        self.m.box((0.0, self.wr * 0.62, (nose_z + hood_z0) * 0.5),
                   (W * 0.30, self.wr * 0.7, nose_z - hood_z0), mat=METAL)
        self._fenders(axles=self.ax[-1:], arc_scale=1.28)
        self._fenders(axles=self.ax[:1], arc_scale=1.16)
        # Three point hitch and, when the record says the machine has one, a PTO stub.
        rear = self.frame_z[0]
        hitch_y = rear_r * 0.55
        for side in (-1.0, 1.0):
            self.m.beam((side * W * 0.26, hitch_y * 1.4, rear + 0.35),
                        (side * W * 0.30, hitch_y, rear - 0.25),
                        _clamp(W * 0.05, 0.06, 0.13), mat=METAL)
            self.m.cylinder((side * W * 0.20, hitch_y * 2.2, rear + 0.2),
                            _clamp(W * 0.035, 0.04, 0.09), 0.5, axis=2, segments=8,
                            mat=METAL, rot=mk.unity_euler(28.0, 0.0, 0.0))
        self.m.beam((0.0, rear_r * 1.15, rear + 0.45), (0.0, rear_r * 1.05, rear - 0.1),
                    _clamp(W * 0.045, 0.05, 0.11), mat=METAL)
        if self.pto:
            self.m.cylinder((0.0, hitch_y * 1.25, rear - 0.16),
                            _clamp(W * 0.035, 0.045, 0.09), 0.22, axis=2, segments=8,
                            mat=METAL)
        self.mount_rear = (0.0, hitch_y, rear - 0.3)

    # -- grader ---------------------------------------------------------------
    def _body_grader(self):
        H, W = self.H, self.W
        front_z = self.ax[0]["z"]
        rear_z = self.ax[-1]["z"]
        beam_y = self.deck_y + H * 0.25
        # The arched front frame that carries the circle, and the engine over the bogie.
        self.m.beam((0.0, self.ax[0]["r"] * 1.25, front_z + self.wr * 0.4),
                    (0.0, beam_y, front_z * 0.1), _clamp(W * 0.14, 0.16, 0.4), mat=BODY)
        self.m.box((0.0, beam_y, (front_z * 0.1 + rear_z) * 0.5),
                   (W * 0.34, H * 0.55, abs(rear_z - front_z * 0.1)), mat=BODY)
        self._engine_deck(rear_z + self.wr * 1.6, self.frame_z[0], self.deck_y + H * 0.95,
                          0.78)
        # Rear tandem bogie cases, the giveaway of a grader's back end.
        bogie_z = (self.ax[-1]["z"] + self.ax[-2]["z"]) * 0.5 if len(self.ax) > 2 else rear_z
        bogie_l = (abs(self.ax[-1]["z"] - self.ax[-2]["z"]) if len(self.ax) > 2 else 0.0)
        for side in (-1.0, 1.0):
            x = side * (W * 0.5 - self.tw * 1.15)
            self.m.box((x, self.wr * 1.0, bogie_z),
                       (self.tw * 0.5, self.wr * 0.75, bogie_l + self.wr * 1.6),
                       mat=METAL)
        circle_z = front_z - self.L * 0.22
        circle_y = _clamp(self.wr * 0.9, 0.4, 1.1)
        self.m.node("blade_lift", pivot=(0.0, beam_y, front_z * 0.1))
        for side in (-1.0, 1.0):
            self.m.beam((side * W * 0.16, beam_y, front_z * 0.1),
                        (side * W * 0.30, circle_y + 0.25, circle_z),
                        _clamp(W * 0.05, 0.07, 0.14), mat=METAL, parent="blade_lift")
        self.m.tube((0.0, circle_y + 0.2, circle_z), W * 0.40, W * 0.30, 0.12,
                    axis=1, segments=16, mat=METAL, parent="blade_lift")
        # The mouldboard is split at the centreline so each half can carry one of the
        # contract's blade_angle transforms; both turn about the circle, not about
        # themselves, so the rest pose is one straight blade.
        for side in (-1.0, 1.0):
            tag = "blade_angle_" + ("L" if side < 0 else "R")
            self.m.node(tag, pivot=(0.0, circle_y + 0.2, circle_z), parent="blade_lift")
            self._mouldboard(tag, side, W * 0.65, 0.04, circle_z + 0.08)
        for side in (-1.0, 1.0):
            self._ram((side * W * 0.30, beam_y + H * 0.3, front_z * 0.1 + 0.2),
                      (side * W * 0.32, circle_y + 0.3, circle_z + 0.1),
                      _clamp(W * 0.04, 0.05, 0.1), parent="blade_lift")
        self._fenders()
        self.mount_rear = (0.0, self.wr * 0.7, self.frame_z[0] - 0.2)

    def _mouldboard(self, node, side, half, y_edge, z):
        """Half a curved mouldboard, prismed along X from a J profile in (y, z), with
        its cutting edge sitting at `y_edge` above the ground."""
        scale = _clamp(self.H * 0.42 + 0.25, 0.35, 0.95)
        unit = [(-0.50, 0.14), (-0.10, 0.02), (0.20, 0.02), (0.42, 0.18), (0.50, 0.34),
                (0.42, 0.40), (0.34, 0.24), (0.14, 0.08), (-0.10, 0.08), (-0.50, 0.20)]
        y = y_edge + 0.5 * scale
        profile = [(a * scale, b * scale) for a, b in unit]
        # The halves overlap across the centreline so the seam between the two nodes
        # never opens up; separate nodes never weld, so the overlap is free.
        self.m.prism(profile, (side * (half * 0.5 - 0.01), y, z), half + 0.02,
                     mat=BODY, axis=0, parent=node)
        self.m.box((side * half * 0.5, y - 0.50 * scale, z + 0.17 * scale),
                   (half * 0.98, 0.09, 0.15), mat=METAL, parent=node, bevel=False)
        self.seams.append(((side * half * 0.08, y + 0.44 * scale, z + 0.34 * scale),
                           (side * half * 0.94, y + 0.44 * scale, z + 0.34 * scale),
                           1, node))

    # -- blower ---------------------------------------------------------------
    def _body_blower(self):
        H, W = self.H, self.W
        head_d = _clamp(self.wr * 2.2, 0.7, 1.8)
        head_z = self.nose_z - head_d * 0.5
        head_h = _clamp(self.wr * 2.3, 0.8, 2.0)
        head_y = head_h * 0.5
        self.m.box((0.0, head_y, head_z), (W * 1.02, head_h, head_d), mat=BODY)
        self.m.box((0.0, 0.06, head_z + head_d * 0.45), (W * 1.02, 0.14, 0.12),
                   mat=METAL, bevel=False)
        # The impeller sits in the mouth of the head and spins about Z.
        self.m.node("blower_impeller", pivot=(0.0, head_y, head_z + head_d * 0.30))
        blades = int(_clamp(round(head_h * 4.0), 4, 8))
        r = head_h * 0.42
        self.m.cylinder((0.0, head_y, head_z + head_d * 0.30), r * 0.28, head_d * 0.4,
                        axis=2, segments=10, mat=METAL, parent="blower_impeller")
        for k in range(blades):
            a = 2.0 * math.pi * k / blades
            self.m.box((math.cos(a) * r * 0.6, head_y + math.sin(a) * r * 0.6,
                        head_z + head_d * 0.30),
                       (r * 0.9, 0.06, head_d * 0.34), mat=METAL,
                       parent="blower_impeller", bevel=False,
                       rot=mk.unity_euler(0.0, 0.0, math.degrees(a)))
        chute_y = head_y + head_h * 0.5
        self.m.node("blower_chute", pivot=(0.0, chute_y, head_z))
        chute_h = _clamp(H * 1.1, 0.7, 1.8)
        self.m.lathe([(head_h * 0.30, 0.0), (head_h * 0.26, chute_h * 0.55),
                      (head_h * 0.20, chute_h)], (0.0, chute_y, head_z), segments=12,
                     axis=1, mat=BODY, parent="blower_chute")
        self.m.wedge((0.0, chute_y + chute_h, head_z - head_h * 0.18),
                     (head_h * 0.44, head_h * 0.34, head_h * 0.5), mat=BODY,
                     parent="blower_chute")
        deck_top = self._engine_deck(self.frame_z[0], self.cz - self.cl * 0.5,
                                     self.deck_y + H * 0.85, 0.86)
        if self.power > 250.0:
            # The big units carry a second engine just for the blower head.
            self.m.box((0.0, deck_top + H * 0.28, self.frame_z[0] + self.L * 0.16),
                       (W * 0.72, H * 0.56, self.L * 0.22), mat=BODY)
        self.m.beam((0.0, self.frame_y + self.rail_h * 0.5, self.frame_z[1]),
                    (0.0, head_y * 0.7, head_z), _clamp(W * 0.10, 0.12, 0.3), mat=METAL)
        self._fenders()
        self.mount_front = (0.0, head_y * 0.5, self.nose_z + 0.05)

    # -- wheeled crane --------------------------------------------------------
    def _body_crane(self):
        H, W = self.H, self.W
        # A narrow deep box frame, not a flat deck: that is what a crane carrier is.
        self.m.box((0.0, self.deck_y + H * 0.26, 0.0),
                   (W * 0.54, H * 0.66, self.frame_z[1] - self.frame_z[0]), mat=BODY)
        carrier_z = self.ax[0]["z"] + self.wr * 0.2
        self._cab_at(None, carrier_z, self.deck_y + H * 0.55, self.cl * 0.9,
                     min(W * 0.9, self.cw), self.ch * 0.85, "truck", None)
        self._outriggers()
        turret_z = self.ax[-1]["z"] + self.wr * 0.6
        turret_y = self.deck_y + H * 0.55
        self.m.node("turret", pivot=(0.0, turret_y, turret_z))
        self.m.cylinder((0.0, turret_y - H * 0.1, turret_z), W * 0.30, H * 0.3,
                        axis=1, segments=14, mat=METAL, parent="turret")
        self.m.box((0.0, turret_y + H * 0.32, turret_z - self.L * 0.06),
                   (W * 0.80, H * 0.64, self.L * 0.28), mat=BODY, parent="turret")
        self.m.box((0.0, turret_y + H * 0.26, turret_z - self.L * 0.22),
                   (W * 0.86, H * 0.56, self.L * 0.12), mat=METAL, parent="turret")
        boom = max(4.0, self.boom_len or self.L)
        pivot = (0.0, turret_y + H * 0.42, turret_z - self.L * 0.10)
        self._telescope(pivot, boom, 5.0, W * 0.24, parent="turret")
        self.m.cylinder((0.0, pivot[1], pivot[2]), W * 0.09, W * 0.7, axis=0,
                        segments=10, mat=METAL, parent="turret")
        self._ram((0.0, turret_y + H * 0.05, turret_z + self.L * 0.02),
                  (0.0, pivot[1] + boom * 0.05, pivot[2] + boom * 0.16),
                  _clamp(W * 0.06, 0.08, 0.18), parent="turret")
        # Boom rest over the nose, so the retracted boom lies on something.
        self.m.beam((0.0, self.deck_y + H * 0.6, self.frame_z[1] - self.wr),
                    (0.0, pivot[1] - W * 0.2, self.frame_z[1] - self.wr),
                    _clamp(W * 0.10, 0.12, 0.3), mat=METAL)
        self.cab_parent = "turret"
        self.cy = turret_y + H * 0.08
        self.cz = turret_z + self.L * 0.06
        self.cw = min(self.cw, W * 0.40)
        self.cab_x = -W * 0.30
        self.cab_style = "glass"
        self._fenders()

    def _outriggers(self):
        span = self.W * 0.5
        y = self.frame_y + self.rail_h * 0.4
        for side in (-1.0, 1.0):
            for z in (self.frame_z[1] - self.wr * 1.2, self.frame_z[0] + self.wr * 1.2):
                self.m.box((side * (span + 0.18), y, z), (0.9, self.rail_h * 0.8, 0.34),
                           mat=METAL)
                self.m.cylinder((side * (span + 0.55), y - self.rail_h * 0.2, z),
                                _clamp(self.W * 0.06, 0.07, 0.16), self.rail_h * 1.2,
                                axis=1, segments=8, mat=METAL)
                self.m.box((side * (span + 0.55), y - self.rail_h * 0.85, z),
                           (0.42, 0.10, 0.42), mat=METAL)

    # -- spider excavator -----------------------------------------------------
    def _body_excavator(self):
        H, W = self.H, self.W
        chassis_y = self.deck_y
        self.m.box((0.0, chassis_y + H * 0.18, 0.0),
                   (W * 0.5, H * 0.36, self.L * 0.5), mat=METAL)
        turret_y = chassis_y + H * 0.36
        self.m.node("turret", pivot=(0.0, turret_y, 0.0))
        self.m.cylinder((0.0, turret_y, 0.0), W * 0.30, H * 0.16, axis=1, segments=14,
                        mat=METAL, parent="turret")
        self.m.box((0.0, turret_y + H * 0.32, -self.L * 0.12),
                   (W * 0.78, H * 0.6, self.L * 0.42), mat=BODY, parent="turret")
        self.m.wedge((0.0, turret_y + H * 0.30, -self.L * 0.34),
                     (W * 0.74, H * 0.55, self.L * 0.2), mat=METAL, parent="turret")
        boom = max(2.0, self.boom_len or self.L)
        foot = (0.0, turret_y + H * 0.25, self.L * 0.10)
        elbow = (0.0, foot[1] + boom * 0.42, foot[2] + boom * 0.22)
        wrist = (0.0, elbow[1] - boom * 0.10, elbow[2] + boom * 0.30)
        thick = _clamp(W * 0.13, 0.14, 0.4)
        self.m.node("boom_01", pivot=foot, parent="turret")
        self.m.beam(foot, elbow, thick, mat=BODY, parent="boom_01")
        self.m.node("boom_02", pivot=elbow, parent="boom_01")
        self.m.beam(elbow, wrist, thick * 0.78, mat=BODY, parent="boom_02")
        self._ram(foot, (elbow[0], elbow[1] * 0.7, elbow[2] * 0.9), thick * 0.34,
                  parent="boom_01")
        self._ram((elbow[0], elbow[1] + thick, elbow[2]),
                  (wrist[0], wrist[1] + thick * 0.6, wrist[2] * 0.7), thick * 0.3,
                  parent="boom_02")
        self.m.node("bucket", pivot=wrist, parent="boom_02")
        self._bucket_excavator(wrist, thick)
        self.cab_parent = "turret"
        self.cy = turret_y + H * 0.06
        self.cz = self.L * 0.02
        self.cw = min(self.cw, W * 0.42)
        self.cab_style = "glass"
        self.mount_rear = (0.0, chassis_y * 0.6, -self.L * 0.5)

    def _bucket_excavator(self, wrist, thick):
        width = _clamp((self.bucket_m3 or 0.2) ** 0.34 * 0.95, 0.3, 1.4)
        depth = _clamp((self.bucket_m3 or 0.2) ** 0.34 * 1.2, 0.35, 1.6)
        unit = [(0.20, -0.04), (-0.10, -0.16), (-0.34, -0.02), (-0.40, 0.34),
                (-0.30, 0.34), (-0.26, 0.04), (-0.06, -0.02), (0.20, 0.10)]
        profile = [(a * depth * 2.0, b * depth * 2.0) for a, b in unit]
        self.m.prism(profile, wrist, width, mat=BODY, axis=0, parent="bucket")
        teeth = int(_clamp(round(width / 0.22), 3, 6))
        for k in range(teeth):
            x = (k - (teeth - 1) * 0.5) * (width * 0.9 / teeth)
            self.m.box((x, wrist[1] - 0.78 * depth, wrist[2] + 0.68 * depth),
                       (width * 0.3 / teeth, 0.09, 0.18), mat=METAL, parent="bucket",
                       bevel=False)

    def _spider_legs(self):
        """Four legs, each a hip and a knee, with a wheel hanging off the knee. Built
        before the wheels so wheel_FL and friends can be parented into them."""
        H, W = self.H, self.W
        hip_y = self.deck_y + H * 0.1
        for index, ztag in ((0, "F"), (len(self.ax) - 1, "R")):
            axle = self.ax[index]
            for side in (-1.0, 1.0):
                suffix = "L" if side < 0 else "R"
                tag = "leg_%s%s" % (ztag, suffix)
                hub = self._hub(axle, side)
                hip = (side * W * 0.22, hip_y, axle["z"])
                knee = (side * W * 0.40, hip_y * 0.62,
                        axle["z"] + (0.14 if ztag == "F" else -0.14))
                self.m.node(tag, pivot=hip)
                self.m.beam(hip, knee, _clamp(W * 0.09, 0.1, 0.26), mat=BODY, parent=tag)
                lower = tag + "_02"
                self.m.node(lower, pivot=knee, parent=tag)
                self.m.beam(knee, (hub[0], hub[1] + axle["r"] * 0.45, hub[2]),
                            _clamp(W * 0.07, 0.08, 0.2), mat=METAL, parent=lower)
                self._ram(hip, (knee[0] * 0.9, knee[1] + H * 0.16, knee[2]),
                          _clamp(W * 0.035, 0.045, 0.1), parent=tag)
                self.wheel_parents[_axle_labels(len(self.ax))[index] + suffix] = lower

    # ------------------------------------------------------------------ trim
    def _lamps(self):
        """Headlights at the nose, work lights on the roof. LightingLumens says how
        many the machine carries, so a 40 kW-lumen airport blower bristles with them."""
        # A truck leads with a nose and carries its headlights there. A loader or an
        # excavator leads with its work tool, so the lights live on the cab instead.
        housed = self.sil in _NOSE_LIGHTS
        lamp_parent = None if housed else (self.cab_node or self.cab_parent)
        for side in (-1.0, 1.0):
            tag = "light_" + ("L" if side < 0 else "R")
            if housed:
                point = (side * self.W * 0.33,
                         _clamp(self.deck_y + 0.1, 0.4, self.cy + 0.2),
                         self.nose_z - 0.04)
            else:
                point = (self.cab_x + side * self.cw * 0.36, self.cy + self.ch * 0.92,
                         self.cz + self.cl * 0.5 - 0.05)
            self.m.socket(tag, point)
            self.m.box(point, (self.W * 0.12, self.W * 0.08, 0.08), mat=GLASS,
                       parent=lamp_parent, bevel=False)
        count = int(_clamp(round(self.lumens / 6000.0), 2, 8))
        roof_y = self.cy + self.ch + 0.10
        parent = self.cab_node or self.cab_parent
        for k in range(count):
            x = self.cab_x + ((k % 2) * 2.0 - 1.0) * self.cw * (0.16 + 0.15 * (k // 2))
            z = self.cz + self.cl * (0.42 if k < 2 else -0.40)
            self.m.box((x, roof_y, z), (0.18, 0.14, 0.10), mat=BODY, parent=parent)
            self.m.box((x, roof_y, z + (0.06 if k < 2 else -0.06)),
                       (0.15, 0.11, 0.03), mat=GLASS, parent=parent, bevel=False)
            if k < 2:
                self.m.socket("light_work_" + ("L" if k % 2 == 0 else "R"),
                              (x, roof_y, z + 0.1))
        self.m.cylinder((self.cab_x, roof_y + 0.10, self.cz - self.cl * 0.45), 0.09,
                        0.14, axis=1, segments=8, mat=GLASS, parent=parent)
        self._exhaust()
        self._mirrors()

    def _exhaust(self):
        if self.power < 1.0:
            return
        x = self.cab_x + self.cw * 0.5 + _clamp(self.W * 0.04, 0.05, 0.12)
        z = self.cz - self.cl * 0.35
        base = self.deck_y
        top = self.cy + self.ch * (1.15 if self.sil in _DECK_RAILS else 0.55)
        radius = _clamp(0.035 + self.power / 9000.0, 0.04, 0.12)
        self.m.cylinder((x, (base + top) * 0.5, z), radius, top - base, axis=1,
                        segments=8, mat=METAL)
        self.m.cylinder((x, top, z), radius * 1.25, radius * 1.4, axis=1, segments=8,
                        mat=METAL)
        self.exhaust_top = (x, top + radius, z)

    def _mirrors(self):
        arm = _clamp(self.cw * 0.20, 0.14, 0.42)
        y = self.cy + self.ch * 0.78
        z = self.cz + self.cl * 0.42
        parent = self.cab_node or self.cab_parent
        for side in (-1.0, 1.0):
            x = self.cab_x + side * (self.cw * 0.5 + 0.02)
            self.m.beam((x, y, z), (x + side * arm, y + 0.08, z - 0.05), 0.04,
                        mat=METAL, parent=parent)
            self.m.box((x + side * arm, y - 0.02, z - 0.05), (0.07, 0.34, 0.16),
                       mat=METAL, parent=parent)

    def _steps(self, gap, ceiling):
        """A ladder up to the cab door, which every machine this tall needs."""
        if self.cy < 0.55:
            return
        z = self.cz - self.cl * 0.15
        count = int(_clamp(round(self.cy / 0.35), 1, 4))
        for side in (-1.0, 1.0):
            x = self.cab_x + side * (self.cw * 0.5 + 0.04)
            for k in range(count):
                y = self.cy * (k + 0.6) / (count + 0.4)
                self.m.box((x, y, z), (0.26, 0.045, 0.30), mat=METAL, bevel=False)
            self.m.beam((x, 0.1, z + 0.16), (x, self.cy + 0.35, z + 0.16), 0.05,
                        mat=METAL, square=False, segments=8)

    def _mudflaps(self, gap, ceiling):
        axle = self.ax[-1]
        z = axle["z"] - axle["r"] * 1.25
        for side in (-1.0, 1.0):
            x = side * (self.W * 0.5 - axle["w"] * (0.9 if axle["dual"] else 0.5))
            self.m.box((x, axle["r"] * 0.42, z), (axle["w"] * 1.25, axle["r"] * 0.8,
                                                  0.03), mat=METAL, bevel=False)

    def _sockets(self):
        """One empty per attachment slot the record declares, plus the fixed anchors."""
        for position in self.slots:
            if not position:
                continue
            self.m.socket("mount_" + position, self._mount_point(position))
        if "Tow" in self.slots:
            self.m.socket("hitch", self._mount_point("Tow"))
        if self.exhaust_top:
            self.m.socket("exhaust", self.exhaust_top)

    def _mount_point(self, position):
        low = max(0.3, self.wr * 0.72)
        if position == "Front":
            return getattr(self, "mount_front", (0.0, low, self.nose_z + 0.05))
        if position == "Rear":
            return getattr(self, "mount_rear", (0.0, low, self.frame_z[0] - 0.15))
        if position == "Mid":
            return (0.0, self.deck_y, 0.0)
        if position == "Roof":
            return (self.cab_x, self.cy + self.ch + 0.12, self.cz)
        return (0.0, low * 0.85, self.frame_z[0] - 0.1)

    def cab_collider(self):
        """The cab's box collider as export.assemble wants it: (name, centre, size)."""
        return ("cab_col",
                (self.cab_x, self.cy + self.ch * 0.5, self.cz),
                (self.cw, self.ch, self.cl))

    def _cab_at(self, node, cz, cy, cl, cw, ch, style, parent):
        """Build one cab at an explicit place; used for a crane's carrier cab."""
        saved = (self.cab_node, self.cz, self.cy, self.cl, self.cw, self.ch,
                 self.cab_style, self.cab_parent, self.cab_x)
        self.cab_node, self.cz, self.cy = node, cz, cy
        self.cl, self.cw, self.ch = cl, cw, ch
        self.cab_style, self.cab_parent, self.cab_x = style, parent, 0.0
        self._cab()
        (self.cab_node, self.cz, self.cy, self.cl, self.cw, self.ch,
         self.cab_style, self.cab_parent, self.cab_x) = saved

    # ------------------------------------------------------------------ budget
    def _fill_budget(self):
        """Detail until the mesh reaches its budget floor.

        The order matters: real parts first - a ladder, mudflaps, deck rails - and only
        then bolt rows along the panel seams the build recorded as it went. Nothing here
        invents a shape; it adds the fittings a machine of this size actually carries
        until the mesh is dense enough to be worth its LOD0 slot.
        """
        from config import TRI_BUDGETS

        floor, ceiling = TRI_BUDGETS[self.budget]
        target = floor * 1.06
        for detail in (self._steps, self._mudflaps, self._handrails, self._bolt_rows):
            if self.m.triangle_count() >= target:
                return
            detail(target - self.m.triangle_count(), ceiling)

    def _handrails(self, gap, ceiling):
        if self.sil not in _DECK_RAILS:
            return
        z0 = self.frame_z[0]
        z1 = min(self.frame_z[1], self.cz - self.cl * 0.5)
        if z1 - z0 < 1.2:
            return
        y = self.deck_y + 0.05
        for side in (-1.0, 1.0):
            x = side * self.W * 0.47
            self.m.beam((x, y + 0.9, z0 + 0.3), (x, y + 0.9, z1 - 0.3), 0.05, mat=METAL,
                        square=False, segments=8)
            posts = int(_clamp(round((z1 - z0) / 1.1), 2, 6))
            for k in range(posts):
                z = z0 + 0.3 + (z1 - z0 - 0.6) * k / max(1, posts - 1)
                self.m.box((x, y + 0.45, z), (0.05, 0.9, 0.05), mat=METAL, bevel=False)

    def _bolt_rows(self, gap, ceiling):
        """Hex heads along the seams the build recorded, spread evenly over all of them
        rather than piling onto the first one."""
        if not self.seams:
            return
        wanted = int(gap / 20.0) + 2
        radius = _clamp(0.012 + self.W * 0.005, 0.014, 0.028)
        spacing = _clamp(0.085 + 0.022 * self.W, 0.10, 0.17)
        capacity = [int(_clamp(round(math.dist(a, b) / spacing), 1, 40))
                    for a, b, _, _ in self.seams]
        share = min(1.0, wanted / float(max(1, sum(capacity))))
        for (a, b, axis, node), cap in zip(self.seams, capacity):
            if self.m.triangle_count() > ceiling * 0.9:
                return
            count = max(1, int(round(cap * share)))
            for k in range(count):
                t = (k + 0.5) / count
                point = tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
                self.m.cylinder(point, radius, radius * 1.2, axis=axis, segments=6,
                                mat=METAL, parent=node)

    def _hoses(self):
        """Hydraulic runs down the frame. HydraulicFlowLpm says how much plumbing the
        machine carries, so a 150 lpm loader shows more of it than a 45 lpm tractor."""
        flow = float(self.rec.get("HydraulicFlowLpm") or 0.0)
        runs = int(_clamp(round(flow / 45.0), 0, 4))
        z0, z1 = self.frame_z
        for k in range(runs):
            x = self.rail_x * 0.55 - k * 0.055
            y = self.deck_y + 0.03
            for part in range(3):
                t0 = part / 3.0
                t1 = (part + 1) / 3.0
                sag = 0.06
                a = (x, y - math.sin(t0 * math.pi) * sag, z0 + (z1 - z0) * (0.15 + 0.7 * t0))
                b = (x, y - math.sin(t1 * math.pi) * sag, z0 + (z1 - z0) * (0.15 + 0.7 * t1))
                self.m.beam(a, b, 0.03 + k * 0.004, mat=METAL, square=False, segments=8)
