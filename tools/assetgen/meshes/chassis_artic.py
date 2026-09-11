"""Articulated chassis: the machines that steer by folding in the middle.

Every vehicles.json record with ChassisType 'Artic' is built here - today a hydrostatic
tool carrier and a 300 kW articulated tractor, tomorrow any hauler or wheel loader that
carries its steering in a centre hinge instead of a steered axle. What earns this family
its own generator is that hinge. Two frames meet on a vertical pin: the front one holds
the engine, the cab and whatever the front slot lifts, the rear one holds the load, and
gameplay yaws the whole back half through `pivot_center`. So every rear part hangs off
that node, the rear wheels with it, and the steering rams span the joint with the barrel
on one frame and the rod on the other - which is what makes the joint read as a joint
when the machine turns instead of as a painted line.

There are no steer_* nodes anywhere in this file on purpose: an articulated machine has
no steered axle, and shipping the transform would let gameplay drive a kingpin that does
not exist.

Proportions come from the record and nothing else. MassKg and TireSpec size the tyres,
Visual.BodyL/W/H lays out the two frames, CabL/W/H and Tier shape the cab, the Front
slot's MaxMassKg sizes the lift linkage, CargoCapacityKg or BucketM3 decides whether the
rear frame carries a dump body or a flat implement deck, Pto and the Rear slot decide
whether it carries a three point hitch, FuelCapacityL sizes the tank and LightingLumens
says how many work lights sit on the roof.
"""
import math

from config import BEVEL_WIDTH_M, TRI_BUDGETS
from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

# build_all routes this module by ChassisType, not by silhouette: an articulated tool
# carrier and a rigid one are both 'tractor' in vehicles.json, and only the chassis
# field tells them apart.
SILHOUETTES = frozenset()

# What a hauler body is taken to be carrying when the record states CargoCapacityKg but
# no BucketM3: wet sand and grit, the densest thing a resort actually hauls.
_AGGREGATE_KG_M3 = 1600.0


def build(record, out_path):
    """Generate the articulated machine `record` describes and write it to `out_path`."""
    machine = _Artic(record)
    machine.assemble()
    return export.emit(
        machine.m, out_path,
        box_colliders=machine.colliders(),
        extra={"family": "artic",
               "kind": "machine",
               "silhouette": machine.sil,
               "displayName": record.get("DisplayName") or record["Id"],
               "sourceId": record["Id"]})


# --------------------------------------------------------------------------- numbers
def _num(value, default=0.0):
    """A JSON field as a float; a missing, null or nonsense value falls back."""
    try:
        v = float(value)
    except (TypeError, ValueError):
        return default
    return v if math.isfinite(v) else default


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _chamferable(size):
    """Whether a part is big enough that the standard chamfer still leaves a face."""
    return size > BEVEL_WIDTH_M * 3.0


def _segments(radius):
    """Segment count for a revolved part, from how big it reads on screen."""
    return int(_clamp(round(9.0 + 10.0 * radius), 10, 22))


class _Artic:
    """One articulated machine under construction. Numbers first, then geometry."""

    def __init__(self, record):
        v = datasrc.visual(record)
        self.rec = record
        self.v = v
        self.sil = str(v["Silhouette"] or "tractor").lower()
        self.tier = int(_clamp(datasrc.tier_of(record), 1, 5))
        self.mass = max(600.0, _num(record.get("MassKg"), 6000.0))
        self.power = _num(record.get("EnginePowerKw"), 0.0)
        self.seats = max(1, int(record.get("Seats") or 1))
        self.lumens = _num(record.get("LightingLumens"), 0.0)
        self.flow = _num(record.get("HydraulicFlowLpm"), 0.0)
        self.fuel_l = _num(record.get("FuelCapacityL"), 0.0)
        self.pto = bool(record.get("Pto"))
        self.slots = list(record.get("AttachmentSlots") or [])
        self.positions = [s.get("Position") for s in self.slots]

        self.L = max(2.0, _num(v["BodyL"], 5.0))
        self.W = max(1.0, _num(v["BodyW"], 2.4))
        self.H = max(0.5, _num(v["BodyH"], 1.5))
        self.wr = max(0.25, _num(v["WheelRadiusM"], 0.7))

        tire = record.get("TireSpec") or {}
        width = _num(tire.get("WidthM"), 0.0)
        if width <= 0.0:
            # No tyre table on the record: each corner of an articulated machine carries
            # a quarter of its mass, and the footprint a tyre needs grows with that load.
            width = 0.14 + 0.34 * (self.mass / 8000.0) ** 0.55
        self.tw = _clamp(width, 0.12, self.wr * 0.95)

        # A hauler states its payload; a tool carrier states none, and the difference is
        # what the rear frame gets built as.
        bucket = _num(record.get("BucketM3"), 0.0)
        cargo = _num(record.get("CargoCapacityKg"), 0.0)
        self.body_m3 = bucket if bucket > 0.0 else cargo / _AGGREGATE_KG_M3
        self.rear_kind = "dump" if self.body_m3 > 0.3 else "deck"

        self.budget = "machine_hero" if datasrc.is_hero(record) else "machine_small"
        self.m = mk.Model(record["Id"], budget_key=self.budget,
                          seed=datasrc.seed_for(record["Id"]))

        # Panel seams the detail pass runs bolt rows along, as (a, b, bolt axis, node).
        self.seams = []
        self.mount_front = None
        self.mount_rear = None
        self.mount_tow = None
        self.exhaust_top = None
        self._layout()

    # ------------------------------------------------------------------ numbers
    def _layout(self):
        """Everything that is arithmetic rather than geometry, in one place."""
        self.rail_h = _clamp(0.10 + self.H * 0.20, 0.16, 0.46)
        self.frame_y = self.wr * 0.80
        self.deck_y = self.frame_y + self.rail_h * 0.5
        self.hinge_y = self.frame_y
        self.rail_x = self.W * 0.24
        self.rail_w = _clamp(0.07 + self.W * 0.045, 0.09, 0.22)

        # Tyres sit inside the body width, so what is left between them is the channel
        # the frame, the hood and on a short machine the cab all have to fit into.
        self.hub_x = self.W * 0.5 - self.tw * 0.52
        self.inner_x = max(self.W * 0.17, self.hub_x - self.tw * 0.5)

        self.cl = max(0.7, _num(self.v["CabL"], 1.5))
        self.ch = max(0.8, _num(self.v["CabH"], 1.4)) * (0.94 + 0.03 * self.tier)

        clear = self.wr * 0.22 + 0.14           # room for the yoke behind the cab
        gap = 0.10 + 0.03 * self.L              # tyre to bodywork clearance
        cab_z0 = clear
        cab_z1 = cab_z0 + self.cl
        front_axle = cab_z1 + self.wr + gap
        rear_axle = -(self.wr + gap)
        tail = rear_axle - self._rear_reach(gap)
        nose = front_axle + self.wr * 1.0

        # BodyL is the length the machine is allowed to be. When the cab and the front
        # tyres will not fit end to end the axle slides back under the cab, which is
        # exactly what a compact carrier does - and why its cab ends up narrow enough to
        # run between the wheels rather than over them.
        excess = (nose - tail) - self.L
        if excess > 0.0:
            pull = min(excess, max(0.0, front_axle - (cab_z0 + self.wr * 0.35)))
            front_axle -= pull
            nose = front_axle + self.wr * 1.0
            over = (nose - tail) - self.L
            if over > 0.0:
                nose -= min(over, self.wr * 0.45)

        self.pz = -(nose + tail) * 0.5          # the hinge, once the machine is centred
        self.front_z = front_axle + self.pz
        self.rear_z = rear_axle + self.pz
        self.nose_z = nose + self.pz
        self.tail_z = tail + self.pz
        self.cab_z = (cab_z0 + cab_z1) * 0.5 + self.pz

        cw = max(0.7, _num(self.v["CabW"], 1.6)) * (0.86 + 0.09 * min(self.seats, 3))
        cw = _clamp(max(cw, self.W * 0.56), 0.9, self.W * 0.82)

        # Where the cab ends up relative to the front tyres decides the whole machine.
        # A short record cannot fit a cab behind its front wheels, so the cab drops
        # between them and has to be narrow enough to run in that channel - a compact
        # carrier. A long one carries its cab clear behind the tyres, so it can be full
        # width and ride at tyre-top height with a tall bonnet ahead of it, which is
        # what a big four wheel drive looks like.
        self.straddle = (self.front_z - self.wr < self.cab_z + self.cl * 0.5
                         and self.wr * 2.0 > self.deck_y)
        if self.straddle:
            cw = min(cw, (self.inner_x - 0.03) * 2.0)
            self.cab_y = self.deck_y + 0.02
            hood_ceiling = 0.45
        else:
            self.cab_y = max(self.deck_y + 0.02, self.wr * 2.0 - self.ch * 0.22)
            hood_ceiling = 0.55
        self.cw = max(0.92, cw)

        # The hood stays inside the tyres in X, because the front wheels are taller than
        # the frame and a wide bonnet would sit in them; in Y it stands proud of them,
        # or the fenders bury it and the machine reads as four wheels with a cab
        # balanced between them.
        self.hood_x = min(self.W * 0.31, self.inner_x - 0.015)
        self.hood_y0 = self.frame_y - self.rail_h * 0.10
        self.hood_top = _clamp(self.wr * 2.0 + self.ch * 0.22,
                               self.cab_y + self.ch * 0.28,
                               self.cab_y + self.ch * hood_ceiling)
        self.hood_z0 = self.cab_z + self.cl * 0.5

    def _rear_reach(self, gap):
        """How far behind the rear axle the tail sits, which the load body decides.

        A dump body has to fit between the tail and the hinge - it cannot reach past the
        joint or it would swing into the cab on full lock - so its length is what sets
        the rear overhang rather than the other way round."""
        if self.rear_kind != "dump":
            return self.wr * 1.15
        self.body_w = self.W * 0.94
        self.body_h = _clamp(0.30 + self.body_m3 ** (1.0 / 3.0) * 0.26, 0.35, 1.5)
        self.body_l = _clamp(self.body_m3 / max(0.2, self.body_w * self.body_h),
                             1.0, self.L * 0.55)
        return max(self.wr * 1.15, self.body_l - self.wr * 0.75 - gap + 0.10)

    def colliders(self):
        """The box colliders export bolts on beside the convex hull."""
        return [("cab_col", (0.0, self.cab_y + self.ch * 0.5, self.cab_z),
                 (self.cw, self.ch, self.cl))]

    # ------------------------------------------------------------------ assembly
    def assemble(self):
        self.m.node("pivot_center", pivot=(0.0, self.hinge_y, self.pz))
        self._wheels()
        self._front_frame()
        self._rear_frame()
        self._hinge()
        self._engine_bay()
        self._cab()
        self._front_linkage()
        self._rear_body()
        self._fenders()
        self._lamps()
        self._sockets()
        self._fill_budget()

    # ------------------------------------------------------------------ wheels
    def _wheels(self):
        """Four equal tyres. The rear pair hang off the hinge, so folding the joint
        takes the back axle with it the way the real drivetrain does."""
        for label, z, parent in (("F", self.front_z, None),
                                 ("R", self.rear_z, "pivot_center")):
            for side in (-1.0, 1.0):
                hub = (side * self.hub_x, self.wr, z)
                node = "wheel_" + label + ("L" if side < 0 else "R")
                self.m.node(node, pivot=hub, parent=parent)
                self._tyre(hub, side, node)
                self._rim(hub, side, node)
            self._axle(z, parent)

    def _tyre(self, center, side, node):
        """A lathed carcass with real sidewalls and a chevron bar tread.

        The bars are the contact patch, so the carcass crown tucks under them and the
        lowest point of the machine lands exactly on the ground plane."""
        segments = _segments(self.wr)
        hw = self.tw * 0.5
        bead = self.wr * 0.58
        lug_h = self.wr * 0.075
        crown = self.wr - lug_h * 0.5
        profile = [(bead, -hw * 0.50), (crown * 0.72, -hw * 0.94),
                   (crown * 0.95, -hw * 1.0), (crown, -hw * 0.80),
                   (crown, hw * 0.80), (crown * 0.95, hw * 1.0),
                   (crown * 0.72, hw * 0.94), (bead, hw * 0.50)]
        self.m.lathe(profile, center, segments=segments, axis=0, mat=METAL, parent=node)

        bars = int(_clamp(round(2.0 * math.pi * self.wr / 0.44), 9, 20))
        bar_l = 2.0 * math.pi * self.wr / bars * 0.52
        # A flat bar on a round carcass stands proud at its corners, so it is seated on
        # the chord rather than the radius and the tyre rests exactly on y = 0.
        seat = math.sqrt(max(0.0, self.wr ** 2 - (bar_l * 0.5) ** 2)) - lug_h
        chamfer = _chamferable(lug_h * 2.0)
        # The skew has to happen before the bar is swung round to its place on the
        # carcass, or it lifts out of the tangent plane instead of angling across it.
        skew = mk.unity_euler(0.0, side * 19.0, 0.0)
        for k in range(bars):
            a = 2.0 * math.pi * k / bars
            rot = mk.unity_euler(math.degrees(a), 0.0, 0.0) @ skew
            self.m.box((center[0], center[1] + seat * math.cos(a),
                        center[2] - seat * math.sin(a)),
                       (self.tw * 0.80, lug_h * 2.0, bar_l),
                       mat=METAL, parent=node, bevel=chamfer, rot=rot)

    def _rim(self, center, side, node):
        segments = max(8, _segments(self.wr) - 3)
        bead = self.wr * 0.58
        self.m.tube(center, bead * 0.98, self.wr * 0.30, self.tw * 0.55,
                    axis=0, segments=segments, mat=METAL, parent=node)
        self.m.cylinder((center[0] + side * self.tw * 0.06, center[1], center[2]),
                        self.wr * 0.32, self.tw * 0.62, axis=0, segments=10,
                        mat=METAL, parent=node)
        studs = int(_clamp(round(4.0 + 6.0 * self.wr), 6, 10))
        circle = self.wr * 0.21
        nut = _clamp(self.wr * 0.055, 0.02, 0.055)
        for k in range(studs):
            a = 2.0 * math.pi * k / studs
            self.m.cylinder((center[0] + side * self.tw * 0.34,
                             center[1] + math.cos(a) * circle,
                             center[2] - math.sin(a) * circle),
                            nut, nut * 1.1, axis=0, segments=6, mat=METAL, parent=node)

    def _axle(self, z, parent):
        """Axle housing, final drives and the diff bulge, all of it drive: an
        articulated machine is four wheel drive by construction."""
        span = (self.hub_x - self.tw * 0.35) * 2.0
        self.m.cylinder((0.0, self.wr, z), _clamp(self.wr * 0.17, 0.06, 0.19), span,
                        axis=0, segments=10, mat=METAL, parent=parent)
        self.m.cylinder((0.0, self.wr, z), _clamp(self.wr * 0.32, 0.13, 0.36),
                        _clamp(self.wr * 0.55, 0.20, 0.6), axis=2, segments=12,
                        mat=METAL, parent=parent)
        for side in (-1.0, 1.0):
            self.m.cylinder((side * (self.hub_x - self.tw * 0.30), self.wr, z),
                            _clamp(self.wr * 0.26, 0.10, 0.30), self.tw * 0.34,
                            axis=0, segments=10, mat=METAL, parent=parent)

    # ------------------------------------------------------------------ frames
    def _rails(self, z0, z1, parent):
        """Two longitudinal rails and their cross members: one half of the chassis."""
        length = max(0.4, z1 - z0)
        mid = (z0 + z1) * 0.5
        for side in (-1.0, 1.0):
            self.m.box((side * self.rail_x, self.frame_y, mid),
                       (self.rail_w, self.rail_h, length), mat=METAL, parent=parent)
            self.seams.append(((side * self.rail_x, self.frame_y + self.rail_h * 0.5,
                                z0 + 0.12),
                               (side * self.rail_x, self.frame_y + self.rail_h * 0.5,
                                z1 - 0.12), 1, parent))
        members = int(_clamp(round(length / 1.1), 2, 5))
        for k in range(members):
            z = z0 + length * (k + 0.5) / members
            self.m.box((0.0, self.frame_y, z),
                       (self.rail_x * 2.0, self.rail_h * 0.58, self.rail_w * 0.9),
                       mat=METAL, parent=parent)

    def _front_frame(self):
        z0 = self.pz + self.wr * 0.18
        z1 = self.nose_z - 0.03
        self._rails(z0, z1, None)
        # The bellyplate under the engine, and the skid the nose needs when the machine
        # is pushing something heavier than itself.
        self.m.box((0.0, self.frame_y - self.rail_h * 0.42, (z0 + z1) * 0.5 + 0.05),
                   (self.rail_x * 1.9, self.rail_h * 0.22, (z1 - z0) * 0.82),
                   mat=METAL)
        self.m.box((0.0, self.frame_y + self.rail_h * 0.1, self.nose_z - 0.06),
                   (self.hood_x * 1.7, self.rail_h * 1.25, 0.11), mat=METAL)
        # Hydraulic oil rides on the front frame beside the engine; HydraulicFlowLpm
        # says how big a reservoir the machine needs to keep it cool.
        tank_r = _clamp(0.10 + self.flow / 2400.0, 0.11, 0.26)
        tank_l = _clamp(self.flow / 260.0, 0.4, 1.1)
        self.m.cylinder((self.rail_x + tank_r * 0.7, self.deck_y + tank_r * 0.7,
                         self.hood_z0 + tank_l * 0.5),
                        tank_r, tank_l, axis=2, segments=10, mat=METAL)

    def _rear_frame(self):
        z0 = self.tail_z + 0.03
        z1 = self.pz - self.wr * 0.14
        self._rails(z0, z1, "pivot_center")
        # Fuel rides low between the rear rails, sized straight off FuelCapacityL. It
        # hangs under the frame, so what it may not do is hang below the axles: a tank
        # that grounds out is the one thing a machine on a piste cannot afford.
        if self.fuel_l > 20.0:
            vol = self.fuel_l / 1000.0
            top = self.frame_y - self.rail_h * 0.55
            height = _clamp(top - self.wr * 0.30, 0.12, 0.56)
            width = min(self.rail_x * 1.75, self.inner_x * 1.5)
            length = _clamp(vol / max(0.05, height * width), 0.35, (z1 - z0) * 0.7)
            self.m.box((0.0, top - height * 0.5, (z0 + z1) * 0.5),
                       (width, height, length), mat=METAL, parent="pivot_center")

    # ------------------------------------------------------------------ hinge
    def _hinge(self):
        """The joint the family is named for.

        The yoke plates belong to the front frame and the tongue between them to the
        rear, so yawing `pivot_center` swings the back half inside the yoke exactly the
        way the pin does. Two rams cross the joint to drive it."""
        y, z = self.hinge_y, self.pz
        plate = _clamp(self.rail_h * 0.30, 0.05, 0.13)
        gap = self.rail_h * 0.95
        yoke_w = self.rail_x * 2.0 + self.rail_w * 1.4
        yoke_l = self.wr * 0.62
        for sign in (-1.0, 1.0):
            self.m.box((0.0, y + sign * (gap * 0.5 + plate * 0.5), z + yoke_l * 0.22),
                       (yoke_w, plate, yoke_l), mat=METAL)
        self.m.box((0.0, y, z - yoke_l * 0.16),
                   (yoke_w * 0.72, gap * 0.9, yoke_l * 1.1), mat=METAL,
                   parent="pivot_center")
        pin_r = _clamp(self.W * 0.030, 0.035, 0.09)
        self.m.cylinder((0.0, y, z), pin_r, gap + plate * 3.4, axis=1, segments=10,
                        mat=METAL)
        for sign in (-1.0, 1.0):
            self.m.cylinder((0.0, y + sign * (gap * 0.5 + plate * 0.55), z),
                            pin_r * 1.9, plate * 1.5, axis=1, segments=10, mat=METAL)
        self.seams.append(((-yoke_w * 0.42, y + gap * 0.5 + plate, z),
                           (yoke_w * 0.42, y + gap * 0.5 + plate, z), 1, None))

        reach = self.wr * 0.80
        radius = _clamp(self.W * 0.028, 0.035, 0.075)
        for side in (-1.0, 1.0):
            self._split_ram((side * (self.rail_x + self.rail_w * 0.9),
                             y + self.rail_h * 0.18, z + reach),
                            (side * (self.rail_x * 0.62), y + self.rail_h * 0.18,
                             z - reach), radius)

    def _split_ram(self, a, b, radius, barrel_parent=None, rod_parent="pivot_center"):
        """A ram whose two halves live on different frames.

        Every other module draws a ram as one rigid piece because both its ends move
        together. A steering ram's do not: the barrel is bolted to one frame and the rod
        pinned to the other, so it has to be built as two parts or it will swing as a
        stick when the joint folds."""
        length = math.dist(a, b)
        if length < 0.12:
            return
        rot = mk.look_rotation((b[0] - a[0], b[1] - a[1], b[2] - a[2]))
        mid = tuple((a[i] + b[i]) * 0.5 for i in range(3))
        barrel_c = tuple(a[i] + (mid[i] - a[i]) * 0.52 for i in range(3))
        rod_c = tuple(mid[i] + (b[i] - mid[i]) * 0.48 for i in range(3))
        self.m.cylinder(barrel_c, radius, length * 0.58, axis=2, segments=10,
                        mat=METAL, parent=barrel_parent, rot=rot)
        self.m.cylinder(rod_c, radius * 0.52, length * 0.60, axis=2, segments=8,
                        mat=METAL, parent=rod_parent, rot=rot)
        self.m.tube(a, radius * 1.25, radius * 0.45, radius * 0.9, axis=2, segments=8,
                    mat=METAL, parent=barrel_parent, rot=rot)
        self.m.tube(b, radius * 0.95, radius * 0.35, radius * 0.8, axis=2, segments=8,
                    mat=METAL, parent=rod_parent, rot=rot)

    # ------------------------------------------------------------------ front frame
    def _ring(self, half, y0, y1, z, chamfer, top_scale):
        """One raked cross-section of a shell, for the hood and the cab roof."""
        top = half * top_scale
        c = min(chamfer, half * 0.4, (y1 - y0) * 0.4)
        pts = [(half - c, y0), (half, y0 + c), (top, y1 - c), (top - c, y1),
               (-top + c, y1), (-top, y1 - c), (-half, y0 + c), (-half + c, y0)]
        return [(x, y, z) for x, y in pts]

    def _engine_bay(self):
        """The bonnet over the engine. EnginePowerKw is already in BodyH and BodyL by
        the time it gets here; what the number decides directly is the size of the
        exhaust and how much of the grille is radiator."""
        z0, z1 = self.hood_z0, self.nose_z
        length = max(0.30, z1 - z0)
        y0, y1 = self.hood_y0, self.hood_top
        chamfer = self.hood_x * 0.22
        drop = (y1 - y0) * 0.17            # the bonnet slopes away toward the grille
        sections = [
            self._ring(self.hood_x, y0, y1, z0 - 0.02, chamfer, 0.94),
            self._ring(self.hood_x * 0.99, y0, y1 - drop * 0.35,
                       z0 + length * 0.55, chamfer, 0.90),
            self._ring(self.hood_x * 0.88, y0, y1 - drop, z1, chamfer, 0.86),
        ]
        self.m.loft(sections, mat=BODY)
        self.seams.append(((0.0, y1 + 0.01, z0 + 0.08),
                           (0.0, y1 - drop * 0.9, z1 - 0.08), 1, None))
        for side in (-1.0, 1.0):
            self.seams.append(((side * self.hood_x * 0.92, (y0 + y1) * 0.5, z0 + 0.08),
                               (side * self.hood_x * 0.82, (y0 + y1) * 0.5 - drop * 0.4,
                                z1 - 0.10), 0, None))
        # Radiator grille: a louvre count that grows with the heat the engine rejects.
        face_h = (y1 - drop) - y0
        louvres = int(_clamp(round(2.0 + self.power / 55.0), 3, 8))
        for k in range(louvres):
            y = y0 + face_h * (k + 0.65) / (louvres + 0.5)
            self.m.box((0.0, y, z1 + 0.005),
                       (self.hood_x * 1.45, face_h / (louvres + 1.5) * 0.6, 0.05),
                       mat=METAL, bevel=False)
        self._exhaust()

    def _exhaust(self):
        """A stack up the side of the cab, sized by the gas the engine has to shift."""
        if self.power < 1.0:
            return
        radius = _clamp(0.035 + self.power / 7000.0, 0.04, 0.13)
        # Up the outside of the cab's front pillar, clear of the bonnet it would
        # otherwise run inside, and far enough back to miss the front tyre.
        x = max(self.hood_x + radius * 1.15, self.cw * 0.5 + radius * 1.1)
        z = self.cab_z + self.cl * (-0.20 if self.straddle else 0.42)
        base = self.hood_y0 + 0.05
        top = self.cab_y + self.ch * 0.96
        self.m.cylinder((x, (base + top) * 0.5, z), radius, top - base, axis=1,
                        segments=8, mat=METAL)
        self.m.cylinder((x, top, z), radius * 1.3, radius * 1.5, axis=1, segments=8,
                        mat=METAL)
        self.exhaust_top = (x, top + radius * 0.8, z)

    # ------------------------------------------------------------------ cab
    def _cab(self):
        """A glasshouse on the front frame: floor, corner pillars, roof and glass.

        Tier is the cab's age. A tier-1 machine keeps an upright boxy screen with a flat
        roof; a tier-5 gets a raked wraparound with a narrow roof and a deep screen."""
        cz, cy, cl, cw, ch = self.cab_z, self.cab_y, self.cl, self.cw, self.ch
        rise = cy - self.deck_y
        if rise > 0.06:
            # A cab carried at tyre-top height stands on a riser over the drivetrain.
            # It belongs to the frame, not to the cab node, so a tilt cab lifts off it.
            self.m.box((0.0, (self.deck_y + cy) * 0.5 + 0.02, cz),
                       (cw * 0.74, rise + 0.06, cl * 0.82), mat=BODY)
        self.m.node("cab", pivot=(0.0, cy, cz - cl * 0.5))
        rake = ch * (0.03 + 0.052 * self.tier)
        top_scale = 1.0 - 0.026 * self.tier
        zr, zf = cz - cl * 0.5, cz + cl * 0.5
        y1 = cy + ch

        self.m.box((0.0, cy + 0.05, cz), (cw, 0.10, cl), mat=METAL, parent="cab")
        # The painted band under the door glass. Without it the cab reads as a pane of
        # glass hung between four posts rather than as something welded up.
        skirt = _clamp(ch * 0.14, 0.12, 0.30)
        self.m.box((0.0, cy + 0.06 + skirt * 0.5, cz), (cw * 0.99, skirt, cl * 0.96),
                   mat=BODY, parent="cab")
        self.glass_y0 = cy + 0.08 + skirt
        post = _clamp(cw * 0.06, 0.05, 0.13)
        for side in (-1.0, 1.0):
            x = side * (cw * 0.5 - post * 0.5)
            xt = side * (cw * 0.5 * top_scale - post * 0.5)
            self.m.beam((x, cy + 0.09, zf - post * 0.5),
                        (xt, y1 - 0.05, zf - rake - post * 0.5), post, mat=BODY,
                        parent="cab")
            self.m.beam((x, cy + 0.09, zr + post * 0.5),
                        (xt, y1 - 0.05, zr + rake * 0.35 + post * 0.5), post, mat=BODY,
                        parent="cab")
            self.m.box((x, cy + ch * 0.5, cz), (post * 0.82, ch, post * 0.82),
                       mat=BODY, parent="cab")
            self.seams.append(((side * cw * 0.46, cy + 0.04, zr + 0.09),
                               (side * cw * 0.46, cy + 0.04, zf - 0.09), 0, "cab"))
        # A raked cab has a roof much shorter than its floor, and the work lights have
        # to land on the roof it actually has rather than where the cab centre is.
        self.roof_z0 = zr + rake * 0.35
        self.roof_z1 = zf - rake
        self.roof_y = y1 + 0.11
        roof = [self._ring(cw * 0.5 * top_scale, y1 - 0.03, self.roof_y,
                           self.roof_z0, cw * 0.10, 0.93),
                self._ring(cw * 0.5 * top_scale, y1 - 0.03, self.roof_y, self.roof_z1,
                           cw * 0.10, 0.93)]
        self.m.loft(roof, mat=BODY, parent="cab")
        self._cab_glass(zr, zf, cy, cw, ch, rake, top_scale)
        self._cab_interior(zr, zf, cy, cw, cl)

    def _cab_glass(self, zr, zf, cy, cw, ch, rake, top_scale):
        y0 = self.glass_y0
        y1 = cy + ch * 0.90
        mid = (y0 + y1) * 0.5
        height = y1 - y0
        frac = (mid - cy) / max(1e-6, ch)
        pitch = math.degrees(math.atan2(rake, max(0.2, ch)))
        self.m.box((0.0, mid, zf - rake * frac - 0.02), (cw * 0.84, height, 0.035),
                   mat=GLASS, parent="cab", rot=mk.unity_euler(pitch, 0.0, 0.0),
                   bevel=False)
        for side in (-1.0, 1.0):
            self.m.box((side * (cw * 0.25 * (1.0 + top_scale) - 0.02), mid,
                        (zr + zf) * 0.5), (0.035, height, (zf - zr) * 0.74),
                       mat=GLASS, parent="cab", bevel=False)
        self.m.box((0.0, mid, zr + rake * 0.35 * frac + 0.025),
                   (cw * 0.78, height * 0.84, 0.035), mat=GLASS, parent="cab",
                   bevel=False)

    def _cab_interior(self, zr, zf, cy, cw, cl):
        """Seats, column and dash: behind glass this much is actually visible, and
        Seats is the record saying how many to build."""
        seats = min(3, self.seats)
        width = cw / (seats + 0.7)
        for k in range(seats):
            x = (k - (seats - 1) * 0.5) * width
            z = (zr + zf) * 0.5 - cl * 0.08
            self.m.box((x, cy + 0.38, z), (width * 0.64, 0.12, width * 0.70),
                       mat=METAL, parent="cab")
            self.m.box((x, cy + 0.64, z - width * 0.33), (width * 0.60, 0.52, 0.10),
                       mat=METAL, parent="cab", rot=mk.unity_euler(-9.0, 0.0, 0.0))
        self.m.wedge((0.0, cy + 0.54, zf - cl * 0.17), (cw * 0.80, 0.28, cl * 0.20),
                     mat=METAL, parent="cab")
        wheel_r = _clamp(cw * 0.13, 0.10, 0.20)
        self.m.tube((0.0, cy + 0.74, zf - cl * 0.27), wheel_r, wheel_r * 0.76, 0.035,
                    axis=2, segments=10, mat=METAL, parent="cab",
                    rot=mk.unity_euler(26.0, 0.0, 0.0))

    # ------------------------------------------------------------------ front tool
    def _front_linkage(self):
        """The lift frame the Front slot mounts on, if the record carries one.

        It pivots at the nose and parks lowered, which is where an attachment expects to
        find `mount_Front`. The slot's MaxMassKg sizes the links and the rams: a 900 kg
        front hitch is a different piece of steel from a 3 t one."""
        slot = self._slot("Front")
        if slot is None:
            self.mount_front = (0.0, self.wr * 0.55, self.nose_z + 0.12)
            return
        capacity = _num(slot.get("MaxMassKg"), 600.0)
        thick = _clamp(0.05 + 0.085 * (capacity / 3000.0) ** 0.45, 0.07, 0.19)
        pivot_y = self.frame_y - self.rail_h * 0.10
        pivot_z = self.nose_z - 0.02
        reach = _clamp(self.wr * 0.75, 0.40, 1.0)
        tip_y = max(0.16, self.wr * 0.30)
        tip_z = pivot_z + reach
        link_x = min(self.hood_x * 0.72, self.inner_x - thick)

        self.m.node("boom_01", pivot=(0.0, pivot_y, pivot_z))
        for side in (-1.0, 1.0):
            self.m.beam((side * link_x, pivot_y, pivot_z),
                        (side * link_x * 0.86, tip_y + thick, tip_z), thick,
                        mat=BODY, parent="boom_01")
            self._split_ram((side * link_x * 1.08, pivot_y + self.rail_h * 0.95,
                             pivot_z - 0.10),
                            (side * link_x * 0.92, tip_y + thick * 1.4,
                             (pivot_z + tip_z) * 0.5), thick * 0.42,
                            barrel_parent=None, rod_parent="boom_01")
        self.m.cylinder((0.0, tip_y + thick, tip_z), thick * 0.5, link_x * 1.7,
                        axis=0, segments=8, mat=METAL, parent="boom_01")
        plate_h = _clamp(self.wr * 0.80, 0.35, 1.0)
        self.m.box((0.0, tip_y + plate_h * 0.5, tip_z + thick * 0.6),
                   (link_x * 1.85, plate_h, thick * 0.9), mat=METAL, parent="boom_01")
        for side in (-1.0, 1.0):
            self.m.box((side * link_x * 0.55, tip_y + plate_h * 0.9,
                        tip_z + thick * 0.2),
                       (thick * 1.3, plate_h * 0.3, thick * 1.5), mat=METAL,
                       parent="boom_01", bevel=False)
        self.mount_front = (0.0, tip_y + plate_h * 0.35, tip_z + thick * 1.2)

    # ------------------------------------------------------------------ rear body
    def _rear_body(self):
        if self.rear_kind == "dump":
            self._dump_body()
        else:
            self._implement_deck()
        self._drawbar()

    def _implement_deck(self):
        """A flat deck over the rear frame with a three point hitch on the tail: what a
        tool carrier that spreads, tows and mounts a rear implement actually carries."""
        z0 = self.tail_z + 0.12
        z1 = self.pz - self.wr * 0.20
        length = max(0.3, z1 - z0)
        width = min(self.W * 0.86, self.inner_x * 2.0)
        self.m.box((0.0, self.deck_y + 0.04, (z0 + z1) * 0.5), (width, 0.08, length),
                   mat=METAL, parent="pivot_center")
        for side in (-1.0, 1.0):
            self.m.box((side * (width * 0.5 - 0.04), self.deck_y + 0.12,
                        (z0 + z1) * 0.5), (0.07, 0.14, length * 0.96),
                       mat=BODY, parent="pivot_center")
            self.seams.append(((side * width * 0.46, self.deck_y + 0.09, z0 + 0.1),
                               (side * width * 0.46, self.deck_y + 0.09, z1 - 0.1),
                               1, "pivot_center"))
        # A locker on the deck: ballast, chains and the spreader's controls live there.
        box_l = _clamp(length * 0.45, 0.3, 1.1)
        self.m.box((0.0, self.deck_y + 0.08 + self.rail_h * 0.75,
                    _clamp(self.rear_z, z0 + box_l * 0.5, z1 - box_l * 0.5)),
                   (width * 0.82, self.rail_h * 1.5, box_l), mat=BODY,
                   parent="pivot_center")
        self._three_point()

    def _three_point(self):
        """Lower links, lift rods, a top link and the PTO stub when the record has one.

        The Rear slot is what the linkage exists for, so its MaxMassKg sizes the steel
        exactly the way the Front slot sizes the lift frame."""
        slot = self._slot("Rear")
        if slot is None:
            self.mount_rear = (0.0, self.wr * 0.55, self.tail_z - 0.10)
            return
        capacity = _num(slot.get("MaxMassKg"), 800.0)
        thick = _clamp(0.05 + 0.075 * (capacity / 4000.0) ** 0.45, 0.06, 0.17)
        y = self.wr * 0.45
        x = min(self.W * 0.27, self.inner_x * 0.85)
        z = self.tail_z
        ball_z = z - self.wr * 0.55
        for side in (-1.0, 1.0):
            self.m.beam((side * x * 0.74, y * 1.30, z + self.wr * 0.5),
                        (side * x, y * 0.86, ball_z), thick, mat=METAL,
                        parent="pivot_center")
            self.m.tube((side * x, y * 0.86, ball_z), thick * 0.9, thick * 0.34,
                        thick * 1.1, axis=2, segments=8, mat=METAL,
                        parent="pivot_center")
            # Rockshaft arm and the lift rod hanging off it onto the lower link.
            self.m.beam((side * x * 0.66, y * 2.35, z + self.wr * 0.30),
                        (side * x * 0.92, y * 1.95, z - self.wr * 0.12), thick * 0.8,
                        mat=METAL, parent="pivot_center")
            self.m.cylinder((side * x * 0.95, y * 1.45, z - self.wr * 0.16),
                            thick * 0.34, y * 1.05, axis=1, segments=6, mat=METAL,
                            parent="pivot_center")
        self.m.cylinder((0.0, y * 2.35, z + self.wr * 0.30), thick * 0.6, x * 1.5,
                        axis=0, segments=8, mat=METAL, parent="pivot_center")
        self.m.beam((0.0, y * 2.55, z + self.wr * 0.55), (0.0, y * 2.05, z - 0.04),
                    thick * 0.85, mat=METAL, parent="pivot_center")
        if self.pto:
            stub_r = _clamp(self.W * 0.032, 0.04, 0.09)
            self.m.cylinder((0.0, y * 1.25, z - 0.14), stub_r, 0.26, axis=2,
                            segments=8, mat=METAL, parent="pivot_center")
            self.m.tube((0.0, y * 1.25, z - 0.05), stub_r * 1.7, stub_r * 1.05, 0.10,
                        axis=2, segments=10, mat=METAL, parent="pivot_center")
        self.mount_rear = (0.0, y * 0.86, ball_z)

    def _dump_body(self):
        """A tipping body sized from the payload the record states.

        Side walls are prisms so the body has a shaped top line rather than four flat
        plates, and the headboard carries the cab guard every hauler needs."""
        z0 = self.tail_z + 0.10
        z1 = min(z0 + self.body_l, self.pz - self.wr * 0.25)
        length = max(0.5, z1 - z0)
        width = min(self.body_w, self.inner_x * 2.4)
        floor_y = self.deck_y + 0.06
        height = self.body_h
        wall = _clamp(width * 0.035, 0.05, 0.10)
        self.m.box((0.0, floor_y, (z0 + z1) * 0.5), (width, 0.10, length),
                   mat=METAL, parent="pivot_center")
        side_profile = [(floor_y - 0.02, z0), (floor_y + height * 0.72, z0),
                        (floor_y + height, z0 + length * 0.45),
                        (floor_y + height, z1), (floor_y - 0.02, z1)]
        for side in (-1.0, 1.0):
            self.m.prism(side_profile, (side * (width * 0.5 - wall * 0.5), 0.0, 0.0),
                         wall, mat=BODY, axis=0, parent="pivot_center")
            self.seams.append(((side * (width * 0.5 - wall), floor_y + height * 0.55,
                                z0 + 0.12),
                               (side * (width * 0.5 - wall), floor_y + height * 0.80,
                                z1 - 0.12), 0, "pivot_center"))
        self.m.box((0.0, floor_y + height * 0.5, z1 - wall * 0.6),
                   (width - wall * 2.4, height, wall), mat=BODY, parent="pivot_center")
        self.m.box((0.0, floor_y + height * 0.36, z0 + wall * 0.6),
                   (width - wall * 2.4, height * 0.72, wall), mat=BODY,
                   parent="pivot_center")
        self.m.wedge((0.0, floor_y + height * 1.25, z1 + 0.04),
                     (width * 0.92, height * 0.5, self.wr * 0.5), mat=BODY,
                     parent="pivot_center", rot=mk.unity_euler(0.0, 180.0, 0.0))
        ribs = int(_clamp(round(length / 0.7), 2, 6))
        for k in range(ribs):
            z = z0 + length * (k + 0.5) / ribs
            for side in (-1.0, 1.0):
                self.m.box((side * (width * 0.5 + 0.015),
                            floor_y + height * 0.45, z),
                           (0.05, height * 0.84, wall * 1.5), mat=BODY,
                           parent="pivot_center", bevel=False)
        # The body tips on pins at the tail, so the hoist reaches up from the frame.
        for side in (-1.0, 1.0):
            self._split_ram((side * self.rail_x * 0.9, self.frame_y, self.rear_z),
                            (side * self.rail_x * 0.8, floor_y - 0.04,
                             z0 + length * 0.42),
                            _clamp(self.W * 0.032, 0.04, 0.09),
                            barrel_parent="pivot_center", rod_parent="pivot_center")
        self.mount_rear = (0.0, self.deck_y, z0 - 0.12)

    def _drawbar(self):
        """The tow point every one of these machines carries, under the tail."""
        y = self.wr * 0.42
        z = self.tail_z + self.wr * 0.25
        bar = _clamp(self.W * 0.07, 0.08, 0.19)
        self.m.box((0.0, y, z - self.wr * 0.30), (bar * 1.5, bar, self.wr * 1.1),
                   mat=METAL, parent="pivot_center")
        pin_z = z - self.wr * 0.82
        self.m.cylinder((0.0, y, pin_z), bar * 0.34, bar * 2.1, axis=1, segments=8,
                        mat=METAL, parent="pivot_center")
        self.mount_tow = (0.0, y, pin_z - bar * 0.6)
        if self.mount_rear is None:
            self.mount_rear = (0.0, y, pin_z)

    # ------------------------------------------------------------------ trim
    def _fenders(self):
        """An arch over each tyre, prismed along X from a ring profile in (y, z).

        The front pair belong to the front frame and the rear pair to the hinge, so the
        machine keeps its mudguards where its wheels are when the joint folds."""
        for z, parent, forward in ((self.front_z, None, True),
                                   (self.rear_z, "pivot_center", False)):
            # A mudguard hugging the tyre, not a body panel: an arch any deeper than
            # this outgrows the bonnet and becomes the widest thing on the machine.
            outer = self.wr * 1.12
            inner = self.wr * 1.045
            start, span = 0.20, (0.56 if forward else 0.64)
            points, steps = [], 7
            for k in range(steps + 1):
                a = math.pi * (start + span * k / steps)
                points.append((math.sin(a) * outer, math.cos(a) * outer))
            for k in range(steps, -1, -1):
                a = math.pi * (start + span * k / steps)
                points.append((math.sin(a) * inner, math.cos(a) * inner))
            for side in (-1.0, 1.0):
                self.m.prism(points, (side * self.hub_x, self.wr, z), self.tw * 1.22,
                             mat=BODY, axis=0, parent=parent)
                self.seams.append(((side * self.hub_x, self.wr + outer * 0.94,
                                    z - outer * 0.5),
                                   (side * self.hub_x, self.wr + outer * 0.94,
                                    z + outer * 0.5), 0, parent))

    def _lamps(self):
        """Headlights in the hood nose, work lights on the roof. LightingLumens says how
        many the machine carries, so the 30 kW-lumen tractor bristles with them."""
        lamp_w = self.hood_x * 0.42
        for side in (-1.0, 1.0):
            tag = "light_" + ("L" if side < 0 else "R")
            point = (side * self.hood_x * 0.62,
                     self.hood_top - (self.hood_top - self.hood_y0) * 0.22,
                     self.nose_z - 0.02)
            self.m.socket(tag, point)
            self.m.box(point, (lamp_w, lamp_w * 0.62, 0.07), mat=GLASS, bevel=False)
        count = int(_clamp(round(self.lumens / 6000.0), 2, 8))
        roof_y = self.roof_y + 0.04
        inset = min(0.12, (self.roof_z1 - self.roof_z0) * 0.25)
        for k in range(count):
            x = ((k % 2) * 2.0 - 1.0) * self.cw * (0.17 + 0.14 * (k // 2))
            z = self.roof_z1 - inset if k < 2 else self.roof_z0 + inset
            self.m.box((x, roof_y, z), (0.17, 0.13, 0.10), mat=BODY, parent="cab")
            self.m.box((x, roof_y, z + (0.06 if k < 2 else -0.06)),
                       (0.14, 0.10, 0.03), mat=GLASS, parent="cab", bevel=False)
            if k < 2:
                self.m.socket("light_work_" + ("L" if k % 2 == 0 else "R"),
                              (x, roof_y, z + 0.09))
        self.m.cylinder((self.cw * 0.30, roof_y + 0.10, self.roof_z0 + inset * 1.6),
                        0.08, 0.13, axis=1, segments=8, mat=GLASS, parent="cab")
        self._mirrors()

    def _mirrors(self):
        arm = _clamp(self.cw * 0.24, 0.16, 0.45)
        y = self.cab_y + self.ch * 0.80
        z = self.cab_z + self.cl * 0.40
        for side in (-1.0, 1.0):
            x = side * (self.cw * 0.5 + 0.02)
            self.m.beam((x, y, z), (x + side * arm, y + 0.09, z - 0.05), 0.04,
                        mat=METAL, parent="cab")
            self.m.box((x + side * arm, y - 0.02, z - 0.05), (0.06, 0.32, 0.15),
                       mat=METAL, parent="cab")

    def _sockets(self):
        """One empty per attachment slot the record declares, plus the fixed anchors."""
        for position in self.positions:
            if not position:
                continue
            self.m.socket("mount_" + position, self._mount_point(position))
        if "Tow" in self.positions:
            self.m.socket("hitch", self._mount_point("Tow"))
        if self.exhaust_top:
            self.m.socket("exhaust", self.exhaust_top)

    def _mount_point(self, position):
        if position == "Front":
            return self.mount_front or (0.0, self.wr * 0.5, self.nose_z + 0.1)
        if position == "Rear":
            return self.mount_rear or (0.0, self.wr * 0.5, self.tail_z - 0.1)
        if position == "Mid":
            return (0.0, self.deck_y, self.pz - self.wr * 0.4)
        if position == "Roof":
            return (0.0, self.cab_y + self.ch + 0.14, self.cab_z)
        return self.mount_tow or (0.0, self.wr * 0.42, self.tail_z - 0.12)

    def _slot(self, position):
        for slot in self.slots:
            if slot.get("Position") == position:
                return slot
        return None

    # ------------------------------------------------------------------ budget
    def _fill_budget(self):
        """Detail until the mesh reaches its budget floor.

        Real fittings first - the ladder the cab needs, mudflaps, the handrail along the
        deck - and only then bolt rows along the seams the build recorded as it went.
        Nothing here invents a shape; it adds what a machine this size actually carries
        until the mesh is worth its LOD0 slot."""
        floor, ceiling = TRI_BUDGETS[self.budget]
        target = floor * 1.15
        for detail in (self._steps, self._mudflaps, self._handrail, self._bolt_rows):
            if self.m.triangle_count() >= target:
                return
            detail(target - self.m.triangle_count(), ceiling)

    def _steps(self, gap, ceiling):
        if self.cab_y < 0.5:
            return
        z = self.cab_z + self.cl * (-0.20 if self.straddle else 0.42)
        count = int(_clamp(round(self.cab_y / 0.35), 1, 4))
        for side in (-1.0, 1.0):
            x = side * (self.cw * 0.5 + 0.05)
            for k in range(count):
                y = self.cab_y * (k + 0.6) / (count + 0.4)
                self.m.box((x, y, z), (0.25, 0.045, 0.28), mat=METAL, bevel=False)
            self.m.beam((x, 0.12, z + 0.15), (x, self.cab_y + 0.34, z + 0.15), 0.05,
                        mat=METAL, square=False, segments=8)

    def _mudflaps(self, gap, ceiling):
        for z, parent in ((self.front_z, None), (self.rear_z, "pivot_center")):
            for side in (-1.0, 1.0):
                self.m.box((side * self.hub_x, self.wr * 0.40, z - self.wr * 1.22),
                           (self.tw * 1.18, self.wr * 0.78, 0.03), mat=METAL,
                           parent=parent, bevel=False)

    def _handrail(self, gap, ceiling):
        """A grab rail up each side of the rear deck, which anyone loading it needs."""
        if self.rear_kind != "deck":
            return
        z0 = self.tail_z + 0.25
        z1 = self.pz - self.wr * 0.35
        if z1 - z0 < 0.6:
            return
        width = min(self.W * 0.86, self.inner_x * 2.0)
        y = self.deck_y + 0.1
        for side in (-1.0, 1.0):
            x = side * (width * 0.5 + 0.03)
            self.m.beam((x, y + 0.62, z0), (x, y + 0.62, z1), 0.045, mat=METAL,
                        parent="pivot_center", square=False, segments=8)
            posts = int(_clamp(round((z1 - z0) / 0.9), 2, 4))
            for k in range(posts):
                z = z0 + (z1 - z0) * k / max(1, posts - 1)
                self.m.box((x, y + 0.31, z), (0.045, 0.62, 0.045), mat=METAL,
                           parent="pivot_center", bevel=False)

    def _bolt_rows(self, gap, ceiling):
        """Hex heads along the seams the build recorded, spread over all of them rather
        than piling onto the first one."""
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
