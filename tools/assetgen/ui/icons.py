"""Catalogue icons: one per machine class, lift type and attachment, drawn from the data.

An icon is a side-on silhouette assembled from the same `Visual` recipe the mesh
generators read, so the picture in the shop list and the machine that turns up in the
yard are the same shape. Nothing here knows a machine by name. A groomer runs on tracks
because its ChassisType says Tracked, its cab sits where CabOffset puts it, it carries a
blade because the front slot's default attachment is a blade, and its tier picks the
accent colour. Change a number in vehicles.json and the icon changes with the model.

Everything is authored as an SVG document built from small shape helpers and rasterised
by cairosvg at the three scales in config. One grid, one stroke weight, one corner
radius, one safe area: a set of icons that disagree about any of those reads as a set of
icons from different games, which matters more than any single drawing.

The ink outline is drawn as a pass underneath the fills rather than as a stroke on each
shape. Overlapping parts then merge into one silhouette with a single rim around the
whole machine instead of a cage of internal lines, and the parts still read apart
because their fills differ. That is the only way one stroke weight survives a 64 px
groomer with seven parts stacked inside 50 units.

Naming is docs/ART_CONTRACT.md section 8: machine_<id>@2x.png, lift_<id>@4x.png,
attachment_<id>@1x.png.

Run standalone from tools/assetgen:  python3 -m ui.icons [out_dir]
"""
import io
import math
import os

import cairosvg
from PIL import Image

import config
from lib import datasrc, validate

GRID = config.ICON_GRID
STROKE = config.ICON_STROKE
RADIUS = config.ICON_RADIUS

# The box every catalogue icon is fitted into, and the plinth it stands on, in grid units
# with y measured up from the bottom. The gap between the two is one ink rim wide, so the
# machine and its plinth touch without merging.
SAFE = (5.0, 10.0, 59.0, 57.0)
PLINTH = (9.0, 3.0, 55.0, 6.5)

INK = "#111923"

COLOURS = {
    "ink": INK,
    "shell": "#dde4ec",        # painted bodywork
    "shell_dim": "#a9b6c5",    # unpainted panels, arms, decks
    "dark": "#39434f",         # tracks, tyres, frames, rubber
    "glass": "#78b0d8",
    "snow": "#f3f8fc",
}

# Tier is the one number a player compares straight down a shop list, so it gets the
# colour: grey at tier 1 through to gold at tier 5, on the plinth and on the machine's
# working end.
TIER_ACCENT = ("#8d9aa8", "#57bd7d", "#4aa6e8", "#a97ce0", "#efb134")


def clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def accent_for_tier(tier):
    return TIER_ACCENT[int(clamp(tier - 1, 0, len(TIER_ACCENT) - 1))]


# --------------------------------------------------------------------------- sketching
class Sketch:
    """Shapes in author space, y up, emitted onto the icon grid.

    Catalogue icons are authored in metres straight out of the record and fitted to the
    safe area; HUD glyphs are authored on the grid itself and emitted as drawn. Both go
    through the same emitter, so both get the same rim, the same corner radius and the
    same stroke weight whatever space they were drawn in.
    """

    def __init__(self):
        self.ops = []

    def rect(self, x, y, w, h, style="shell"):
        self.ops.append(("rect", (x, y, w, h), style))

    def poly(self, points, style="shell"):
        self.ops.append(("poly", tuple(points), style))

    def circle(self, cx, cy, r, style="dark"):
        self.ops.append(("circle", (cx, cy, r), style))

    def line(self, points, style="ink"):
        """A stroke, not a shape: ropes, bars, hatching, anything with no area."""
        self.ops.append(("line", tuple(points), style))

    def beam(self, a, b, thickness, style="shell_dim"):
        """A strut between two points. Arms, booms and masts are never axis aligned, and
        a rect rotated by hand in every caller is how an icon set drifts out of true."""
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dy) or 1.0
        px, py = -dy / n * thickness * 0.5, dx / n * thickness * 0.5
        self.poly([(a[0] + px, a[1] + py), (b[0] + px, b[1] + py),
                   (b[0] - px, b[1] - py), (a[0] - px, a[1] - py)], style)

    def arc(self, cx, cy, r, start_deg, end_deg, thickness, style="shell_dim",
            segments=10):
        """A curved band: chutes, bubbles, blower housings and the hoods on cabs."""
        outer, inner = [], []
        for i in range(segments + 1):
            a = math.radians(start_deg + (end_deg - start_deg) * i / segments)
            outer.append((cx + math.cos(a) * (r + thickness * 0.5),
                          cy + math.sin(a) * (r + thickness * 0.5)))
            inner.append((cx + math.cos(a) * (r - thickness * 0.5),
                          cy + math.sin(a) * (r - thickness * 0.5)))
        self.poly(outer + inner[::-1], style)

    def fan(self, cx, cy, r, start_deg, end_deg, style="shell_dim", segments=12):
        """A filled pie slice: spray cones, fan mouths, sun rays."""
        pts = [(cx, cy)]
        for i in range(segments + 1):
            a = math.radians(start_deg + (end_deg - start_deg) * i / segments)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
        self.poly(pts, style)

    def bounds(self):
        xs, ys = [], []
        for kind, geom, _ in self.ops:
            if kind == "rect":
                x, y, w, h = geom
                xs += [x, x + w]
                ys += [y, y + h]
            elif kind == "circle":
                cx, cy, r = geom
                xs += [cx - r, cx + r]
                ys += [cy - r, cy + r]
            else:
                xs += [p[0] for p in geom]
                ys += [p[1] for p in geom]
        if not xs:
            return 0.0, 0.0, 1.0, 1.0
        return min(xs), min(ys), max(xs), max(ys)

    def extend(self, other):
        self.ops.extend(other.ops)


def _fit(sketch, fill):
    """Scale a sketch into the safe area, centred across and standing on the plinth.

    Ground anchored rather than centred, because every catalogue subject has a bottom
    that means something: tracks, tyres, a tower base, a blade's cutting edge. Centring
    would float a low wide machine in the middle of the box and leave it looking like it
    was dropped in from another set.
    """
    x0, y0, x1, y1 = SAFE
    bx0, by0, bx1, by1 = sketch.bounds()
    s = min((x1 - x0) / max(bx1 - bx0, 1e-6), (y1 - y0) / max(by1 - by0, 1e-6)) * fill
    return s, (x0 + x1) * 0.5 - (bx0 + bx1) * 0.5 * s, y0 - by0 * s


def size_fill(value, lo, hi):
    """How much of the safe area a subject fills, from a size in its own units.

    Fitting every icon to the same box would make a 330 kg snowmobile and an 80 t crawler
    crane the same size on screen. Ranking them by the log of their own number keeps the
    fleet's order visible while leaving the smallest icon large enough to read.
    """
    t = clamp((math.log(max(value, lo)) - math.log(lo))
              / (math.log(hi) - math.log(lo)), 0.0, 1.0)
    return 0.78 + 0.22 * t


# --------------------------------------------------------------------------- emitting
def _num(v):
    return "%.2f" % (v + 0.0)


def _geometry(kind, geom, xform):
    """One shape's SVG geometry attributes, with the author-space transform applied here
    rather than as an SVG transform: a transform would scale the stroke too, and the
    whole set depends on one stroke weight."""
    s, tx, ty = xform
    if kind == "rect":
        x, y, w, h = geom
        gw, gh = w * s, h * s
        rx = min(RADIUS, gw * 0.3, gh * 0.3)
        return "rect", ('x="%s" y="%s" width="%s" height="%s" rx="%s"'
                        % (_num(x * s + tx), _num(GRID - ((y + h) * s + ty)),
                           _num(gw), _num(gh), _num(rx)))
    if kind == "circle":
        cx, cy, r = geom
        return "circle", ('cx="%s" cy="%s" r="%s"'
                          % (_num(cx * s + tx), _num(GRID - (cy * s + ty)), _num(r * s)))
    pts = " ".join("%s,%s" % (_num(p[0] * s + tx), _num(GRID - (p[1] * s + ty)))
                   for p in geom)
    return ("polyline" if kind == "line" else "polygon"), 'points="%s"' % pts


def _passes(sketch, xform, accent):
    """Rim pass, then fills, then strokes. The order is the style: see the module docstring."""
    shapes, lines = [], []
    for kind, geom, style in sketch.ops:
        tag, attrs = _geometry(kind, geom, xform)
        if kind == "line":
            lines.append((tag, attrs, style))
        else:
            shapes.append((tag, attrs, style))
    out = []
    for tag, attrs, _ in shapes:
        out.append('<%s %s fill="none" stroke="%s" stroke-width="%s"/>'
                   % (tag, attrs, INK, _num(STROKE)))
    for tag, attrs, style in shapes:
        out.append('<%s %s fill="%s"/>'
                   % (tag, attrs, accent if style == "accent" else COLOURS[style]))
    for tag, attrs, style in lines:
        out.append('<%s %s fill="none" stroke="%s" stroke-width="%s"/>'
                   % (tag, attrs, accent if style == "accent" else COLOURS[style],
                      _num(STROKE)))
    return out


def document(sketch, accent=TIER_ACCENT[2], fit=True, fill=1.0, plinth=False):
    """The finished SVG for one icon."""
    body = []
    if plinth:
        x0, y0, x1, y1 = PLINTH
        base = Sketch()
        base.rect(x0, y0, x1 - x0, y1 - y0, "accent")
        body += _passes(base, (1.0, 0.0, 0.0), accent)
    body += _passes(sketch, _fit(sketch, fill) if fit else (1.0, 0.0, 0.0), accent)
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" '
            'viewBox="0 0 %d %d">\n<g stroke-linejoin="round" stroke-linecap="round">\n'
            % (GRID, GRID, GRID, GRID)
            + "\n".join(body) + "\n</g>\n</svg>\n")


def rasterise(svg, out_dir, name, group, extra=None):
    """Write <name>@1x/@2x/@4x.png and return one manifest record per file."""
    os.makedirs(out_dir, exist_ok=True)
    records = []
    for scale in config.ICON_SCALES:
        px = GRID * scale
        data = cairosvg.svg2png(bytestring=svg.encode("utf-8"),
                                output_width=px, output_height=px)
        image = Image.open(io.BytesIO(data)).convert("RGBA")
        asset = "%s@%dx" % (name, scale)
        validate.check_texture(asset, image, expect_size=px, expect_channels=4)
        path = os.path.join(out_dir, asset + ".png")
        image.save(path, format="PNG", optimize=True)
        record = {
            "id": asset,
            "path": config.rel_to_root(path),
            "resource": config.resource_path(path),
            "kind": "icon",
            "group": group,
            "size": px,
            "scale": scale,
        }
        if extra:
            record.update(extra)
        records.append(record)
    return records


# --------------------------------------------------------------------------- machines
# Kinds that read as a mouldboard from the side, and kinds that read as a rotor housing.
# The lists exist so an icon asks the attachment record what it is instead of asking the
# machine's id what it probably carries.
BLADE_KINDS = ("Blade", "Blade12Way", "UBlade", "VPlow", "PlowWings", "PlowStraight",
               "BoxPusher", "ParkBlade")
TILL_KINDS = ("Tiller", "TrackSetter", "PipeCutter", "Mulcher")
BUCKET_KINDS = ("SnowBucket", "LightBucket", "Grapple")

_ATTACHMENT_INDEX = None


def attachment_by_id(att_id):
    global _ATTACHMENT_INDEX
    if _ATTACHMENT_INDEX is None:
        _ATTACHMENT_INDEX = {a["Id"]: a for a in datasrc.attachments()}
    return _ATTACHMENT_INDEX.get(att_id or "")


def default_attachment(rec, position):
    """The implement a machine ships with in that slot, or None."""
    for slot in rec.get("AttachmentSlots") or []:
        if slot.get("Position") == position:
            return attachment_by_id(slot.get("DefaultAttachmentId"))
    return None


def _belt(sk, x0, x1, h):
    """A track belt from the side, with the raised front idler every winter cat has.

    The bogie count comes from the belt's own length, which is the icon's version of the
    rule the mesh generators follow: a longer belt carries more road wheels.
    """
    sk.poly([(x0, h * 0.40), (x0 + h * 0.45, 0.0), (x1 - h * 0.95, 0.0),
             (x1, h * 0.62), (x1, h * 1.02), (x0, h * 1.02)], "dark")
    sk.circle(x0 + h * 0.48, h * 0.48, h * 0.38, "shell_dim")
    sk.circle(x1 - h * 0.46, h * 0.58, h * 0.32, "shell_dim")
    span = (x1 - h * 1.5) - (x0 + h * 1.2)
    count = int(clamp(round(span / (h * 1.15)), 2, 5))
    if span > 0:
        for i in range(count):
            x = x0 + h * 1.2 + span * i / max(count - 1, 1)
            sk.circle(x, h * 0.24, h * 0.19, "shell_dim")


def _axles(length, r, count):
    """Axle centres along a wheeled chassis: one steering axle up front until there are
    four, then two, with the rest grouped as a rear bogie."""
    count = max(1, int(count))
    if count == 1:
        return [0.0]
    front = [length * 0.34]
    if count >= 4:
        front.append(length * 0.34 - r * 2.35)
    rear_count = count - len(front)
    rear = [-length * 0.42 + i * r * 2.35 for i in range(max(rear_count, 0))]
    return sorted(rear + front)


def _wheels(sk, length, r, count):
    xs = _axles(length, r, count)
    for x in xs:
        sk.circle(x, r, r, "dark")
        sk.circle(x, r, r * 0.44, "shell_dim")
    return xs


def _cab(sk, cx, cl, ch, base, tier, glazing=True):
    """Cab shell, glazing and roof cap.

    A tier 1 cab is a boxy upright; a tier 5 rakes its windscreen forward into a
    wraparound, the same progression the mesh generators build in three dimensions. The
    roof cap is what keeps the cab from melting into the bodywork underneath it, since
    both are painted the same colour on a real machine and in this palette.
    """
    cl = max(cl, 0.6)
    ch = max(ch, 0.5)
    rake = 0.10 + 0.05 * tier
    back, front = cx - cl * 0.5, cx + cl * 0.5
    top_front = front - cl * rake
    sk.poly([(back, base), (front, base), (top_front, base + ch),
             (back + cl * 0.06, base + ch)], "shell")
    if glazing:
        inset = min(0.16, cl * 0.12)
        sk.poly([(back + cl * 0.16, base + ch * 0.26), (front - inset, base + ch * 0.20),
                 (top_front - inset * 0.6, base + ch - inset * 1.4),
                 (back + cl * 0.16, base + ch - inset * 1.4)], "glass")
    sk.poly([(back + cl * 0.02, base + ch - 0.05), (top_front - 0.02, base + ch - 0.05),
             (top_front - 0.02, base + ch + 0.14), (back + cl * 0.02, base + ch + 0.14)],
            "shell_dim")

def _blade(sk, x, h, clearance):
    """A mouldboard from the side: concave forward, cutting edge on the ground.

    Drawn with real thickness. A blade one line thick disappears at 64 px, and the blade
    is the first thing that says whether a machine pushes snow or carries it.
    """
    sk.beam((x + 0.10, h * 0.45), (x - 1.15, clearance * 0.80), 0.22, "dark")
    sk.poly([(x + 0.78, 0.02), (x + 0.30, 0.0), (x - 0.05, h * 0.42),
             (x + 0.22, h), (x + 0.88, h), (x + 0.58, h * 0.48)], "accent")

def _tiller(sk, x, w, h, clearance):
    """Rear tiller or track setter: rotor housing, finisher flap, lift arm."""
    sk.beam((x + 0.55, clearance + 0.30), (x - w * 0.5, h * 0.95), 0.22, "shell_dim")
    sk.rect(x - w, 0.22, w, h, "accent")
    sk.poly([(x - w - 0.60, 0.0), (x - w * 0.30, 0.06), (x - w * 0.30, 0.32),
             (x - w - 0.60, 0.36)], "shell_dim")

def _hopper(sk, x, w, h, clearance):
    """A spreader hopper narrows to its spinner, which is what tells it from a tank."""
    sk.poly([(x - w, clearance + h), (x, clearance + h), (x - w * 0.28, clearance + 0.16),
             (x - w * 0.72, clearance + 0.16)], "accent")
    sk.circle(x - w * 0.5, clearance - 0.05, 0.26, "shell_dim")


def _tank(sk, x, w, h, base):
    """A horizontal tank: a rect between two end caps reads as a cylinder at any size."""
    r = h * 0.5
    sk.circle(x - w + r, base + r, r, "shell")
    sk.circle(x - r, base + r, r, "shell")
    sk.rect(x - w + r, base, w - 2 * r, h, "shell")


def _blower_head(sk, x, w, h, direction=1.0):
    """Intake housing, auger and chute. The mouth is the whole point of the silhouette."""
    sk.rect(min(x, x + direction * w), 0.04, w, h, "accent")
    sk.circle(x + direction * w * 0.5, h * 0.48, h * 0.30, "shell_dim")
    sk.arc(x + direction * w * 0.5, h, h * 0.80, 90.0, 175.0 if direction > 0 else 5.0,
           h * 0.34, "shell_dim")

def _winch(sk, cx, cy):
    """Roof winch: a drum and the rope leaving it uphill, which is how a winch cat is
    told from the same machine without one at a glance."""
    sk.rect(cx - 0.60, cy, 1.2, 0.55, "shell_dim")
    sk.circle(cx, cy + 0.28, 0.36, "accent")
    sk.line([(cx, cy + 0.28), (cx + 1.5, cy + 1.1)], "ink")

def _front_implement(sk, rec, nose, clearance):
    att = default_attachment(rec, "Front")
    if att is None:
        return
    kind = att.get("Kind")
    width = float(att.get("WorkingWidthM") or 1.0)
    if kind in BLADE_KINDS:
        _blade(sk, nose + 0.30, 1.05 + width * 0.18, clearance)
    elif kind == "BlowerHead":
        _blower_head(sk, nose + 0.05, 1.05, 0.75 + width * 0.28)
    elif kind == "Broom":
        sk.circle(nose + 0.75, 0.50, 0.50, "accent")
        sk.beam((nose + 0.75, 0.50), (nose - 0.7, clearance * 0.8), 0.18, "dark")

def _rear_implement(sk, rec, tail, clearance):
    att = default_attachment(rec, "Rear")
    if att is None:
        return
    kind = att.get("Kind")
    width = float(att.get("WorkingWidthM") or 1.0)
    if kind in TILL_KINDS:
        _tiller(sk, tail - 0.05, 1.35 + width * 0.14, 0.80 + width * 0.09, clearance)
    elif kind == "Spreader":
        _hopper(sk, tail - 0.1, 1.7, 1.05, clearance)
    elif kind == "BrineTank":
        _tank(sk, tail - 0.1, 2.6, 1.25, clearance + 0.15)
    elif kind == "BlowerHead":
        _blower_head(sk, tail - 0.05, 1.05, 0.75 + width * 0.28, direction=-1.0)

def _roof_implement(sk, rec, roof_y, cx):
    att = default_attachment(rec, "Roof")
    if att is not None and att.get("Kind") == "Winch":
        _winch(sk, cx, roof_y)


def _build_groomer(sk, rec, vis, tier):
    length, height, track = vis["BodyL"], vis["BodyH"], vis["TrackH"]
    x0, x1 = -length * 0.46, length * 0.46
    _belt(sk, x0, x1, track)
    # The hood slopes away in front of the cab on every cat, and that slope is most of
    # what separates the profile from a dozer's flat deck.
    sk.poly([(x0 - 0.05, track), (x1 * 0.62, track), (x1 * 0.98, track + height * 0.55),
             (x1 * 0.98, track + height * 0.80), (x0 - 0.05, track + height)], "shell")
    sk.rect(x0, track + height, length * 0.42, height * 0.34, "shell_dim")
    _cab(sk, vis["CabOffset"], vis["CabL"], vis["CabH"], track + height, tier)
    if rec.get("Category") == "Construction":
        # A dozer's blade is part of the machine rather than an attachment, which is why
        # the record gives it no front slot. Draw it anyway: a tracked chassis with no
        # blade reads as a carrier.
        _blade(sk, x1 + 0.30, 1.85, track)
        sk.beam((x0 - 0.1, track * 0.9), (x0 - 1.2, track * 0.30), 0.26, "shell_dim")
    else:
        _front_implement(sk, rec, x1, track)
    _rear_implement(sk, rec, x0, track)
    _roof_implement(sk, rec, track + height + vis["CabH"], vis["CabOffset"])

def _build_truck(sk, rec, vis, tier):
    height = vis["BodyH"]
    radius = vis["WheelRadiusM"]
    sil = vis["Silhouette"]
    deck = radius + 0.30
    # A 16 m lowboy drawn to scale is a 5 px tall smear. Long rigs are foreshortened to
    # roughly four times their own drawn height, which keeps the axles and the drop deck
    # readable while a longer rig still draws longer than a shorter one.
    length = min(vis["BodyL"], (deck + max(height, vis["CabH"])) * 4.0)
    _wheels(sk, length, radius, max(2, vis["Axles"]))
    sk.rect(-length * 0.48, deck - 0.24, length * 0.96, 0.28, "dark")
    cab_x = clamp(vis["CabOffset"], 0.0, length * 0.5 - vis["CabL"] * 0.5)
    body_front = cab_x - vis["CabL"] * 0.5 - 0.15
    body_back = -length * 0.46
    span = max(body_front - body_back, 0.8)
    if sil == "tanker":
        _tank(sk, body_front, span, height * 1.25, deck + 0.12)
    elif sil == "mixer":
        # A mixer drum is a cone lying down: wide at the chute end, tapered over the cab.
        sk.poly([(body_back + 0.30, deck + 0.20), (body_front, deck + 0.70),
                 (body_front, deck + height * 1.10), (body_back + 0.30, deck + height * 1.60)],
                "shell")
        sk.circle(body_back + 0.40, deck + height * 0.90, height * 0.70, "shell")
        sk.poly([(body_back - 0.05, deck + 1.05), (body_back - 0.85, deck + 0.45),
                 (body_back - 0.50, deck + 0.05), (body_back + 0.15, deck + 0.60)], "accent")
    elif sil == "lowboy":
        sk.rect(body_back, deck - 0.62, span * 0.70, 0.34, "shell")
        sk.poly([(body_back + span * 0.62, deck - 0.28), (body_front, deck + 0.70),
                 (body_front - span * 0.16, deck + 0.70), (body_back + span * 0.48, deck - 0.28)],
                "shell_dim")
        sk.rect(body_back + span * 0.10, deck - 0.34, span * 0.30, 0.30, "accent")
    elif sil == "pickup":
        sk.rect(body_back, deck, span, height * 0.80, "shell")
        sk.rect(body_back, deck + height * 0.80, span * 0.96, 0.16, "shell_dim")
    else:
        # A tipper body is taller than the frame it sits on and slopes at the headboard.
        sk.poly([(body_back - 0.25, deck), (body_front, deck),
                 (body_front, deck + height * 1.25), (body_back - 0.25, deck + height * 1.25),
                 (body_back - 0.70, deck + height * 0.65)], "shell")
    _cab(sk, cab_x, vis["CabL"], vis["CabH"], deck, tier)
    _front_implement(sk, rec, length * 0.5, deck * 0.55)
    if sil not in ("tanker", "mixer", "lowboy"):
        _rear_implement(sk, rec, body_back - 0.2, deck)

def _build_tractor(sk, rec, vis, tier):
    """Small front wheel, big drive wheel, long hood, cab over the back axle.

    The wheels go on last. A tractor's drive wheel stands proud of the body, and hiding
    it behind the fender is what made the first pass read as a van.
    """
    length, height = vis["BodyL"], vis["BodyH"]
    radius = vis["WheelRadiusM"]
    front_r = radius * 0.58
    hood = front_r * 0.85
    sk.poly([(-length * 0.10, hood), (length * 0.44, hood),
             (length * 0.44, hood + height * 0.42), (-length * 0.10, hood + height * 0.62)],
            "shell")
    sk.rect(-length * 0.34, radius * 0.45, length * 0.34, height * 0.95, "shell")
    _cab(sk, -length * 0.20, vis["CabL"], vis["CabH"], radius * 0.45 + height * 0.95, tier)
    sk.rect(-length * 0.02, hood + height * 0.55, 0.18, height * 0.50, "dark")
    if rec.get("ChassisType") == "Artic":
        # An articulated frame hinges in the middle, so the icon shows the joint the
        # chassis generator has to build a pivot_center for.
        sk.circle(-length * 0.02, hood + height * 0.20, 0.30, "accent")
    sk.circle(-length * 0.26, radius, radius, "dark")
    sk.circle(-length * 0.26, radius, radius * 0.44, "shell_dim")
    sk.circle(length * 0.38, front_r, front_r, "dark")
    sk.circle(length * 0.38, front_r, front_r * 0.44, "shell_dim")
    _front_implement(sk, rec, length * 0.5, front_r * 0.9)
    _rear_implement(sk, rec, -length * 0.46, radius * 0.6)

def _build_loader(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    radius = vis["WheelRadiusM"]
    _wheels(sk, length, radius, 2)
    deck = radius * 0.55
    sk.rect(-length * 0.46, deck, length * 0.52, height, "shell")
    _cab(sk, vis["CabOffset"], vis["CabL"], vis["CabH"], deck + height, tier)
    att = default_attachment(rec, "Front")
    kind = (att or {}).get("Kind", "SnowBucket")
    width = float((att or {}).get("WorkingWidthM") or 2.4)
    if vis["Silhouette"] == "telehandler" or kind == "Forks":
        # A telehandler's boom runs from the back of the machine over the cab, so its
        # reach comes off BoomLengthM rather than the chassis length.
        boom = max(vis["BoomLengthM"], length * 0.8)
        tip = (length * 0.30 + boom * 0.55, deck + height * 1.6)
        sk.beam((-length * 0.38, deck + height * 0.9), tip, 0.42, "shell_dim")
        sk.rect(tip[0] - 0.1, tip[1] - 1.1, 0.22, 1.2, "shell_dim")
        sk.rect(tip[0] - 0.1, tip[1] - 1.1, 1.5, 0.20, "accent")
        sk.rect(tip[0] - 0.1, tip[1] - 0.55, 1.5, 0.20, "accent")
    else:
        arm_root = (-length * 0.14, deck + height * 0.95)
        arm_tip = (length * 0.44, deck + 0.25)
        sk.beam(arm_root, arm_tip, 0.30, "shell_dim")
        sk.beam((-length * 0.24, deck + height * 0.35), (length * 0.10, deck + 0.70),
                0.18, "dark")
        bx, by = arm_tip
        depth = 0.55 + width * 0.14
        sk.poly([(bx - 0.1, by + depth * 1.2), (bx + depth * 0.9, by + depth * 1.2),
                 (bx + depth * 1.05, by + depth * 0.15), (bx + 0.15, by - 0.20),
                 (bx - 0.1, by + 0.10)], "accent")


def _build_grader(sk, rec, vis, tier):
    """A grader is a long frame with the blade slung under the middle of it, and that gap
    between the front axle and the engine is the whole silhouette."""
    length, height = vis["BodyL"], vis["BodyH"]
    radius = vis["WheelRadiusM"]
    frame_y = radius * 1.30
    sk.poly([(-length * 0.30, frame_y - 0.22), (length * 0.48, frame_y - 0.02),
             (length * 0.48, frame_y + 0.30), (-length * 0.30, frame_y + 0.34)], "shell_dim")
    sk.rect(-length * 0.48, radius * 0.70, length * 0.36, height * 1.05, "shell")
    _cab(sk, -length * 0.14, vis["CabL"], vis["CabH"], radius * 0.70 + height * 1.05, tier)
    # The mouldboard hangs off a turntable circle halfway along the frame.
    sk.circle(length * 0.06, frame_y - 0.05, 0.42, "shell_dim")
    sk.poly([(-length * 0.04, radius * 0.70), (length * 0.18, radius * 0.16),
             (length * 0.30, radius * 0.42), (length * 0.08, radius * 0.96)], "accent")
    sk.circle(length * 0.46, radius, radius, "dark")
    sk.circle(length * 0.46, radius, radius * 0.44, "shell_dim")
    for x in (-length * 0.42, -length * 0.42 + radius * 2.35):
        sk.circle(x, radius, radius, "dark")
        sk.circle(x, radius, radius * 0.44, "shell_dim")

def _build_excavator(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    track = vis["TrackH"]
    x0, x1 = -length * 0.46, length * 0.46
    if rec.get("ChassisType") == "Tracked":
        _belt(sk, x0, x1, track)
        base = track
    else:
        # A spider excavator stands on wheeled legs, so the chassis is outriggers.
        radius = vis["WheelRadiusM"]
        _wheels(sk, length, radius, 2)
        sk.beam((x1 * 0.2, radius * 1.5), (x1 * 0.95, radius * 0.2), 0.22, "shell_dim")
        sk.beam((x0 * 0.2, radius * 1.5), (x0 * 0.95, radius * 0.2), 0.22, "shell_dim")
        base = radius * 1.2
    sk.rect(x0 * 0.85, base, length * 0.72, height, "shell")
    sk.rect(x0 * 0.9, base + height, length * 0.35, height * 0.28, "shell_dim")
    _cab(sk, vis["CabOffset"] - length * 0.12, vis["CabL"], vis["CabH"], base + height,
         tier, glazing=True)
    boom = max(vis["BoomLengthM"], 2.0)
    root = (length * 0.10, base + height * 0.75)
    knee = (root[0] + boom * 0.52, root[1] + boom * 0.52)
    tip = (knee[0] + boom * 0.42, base * 0.35)
    sk.beam(root, knee, boom * 0.13, "shell")
    sk.beam(knee, tip, boom * 0.10, "shell_dim")
    sk.poly([(tip[0] - 0.15, tip[1] + boom * 0.10), (tip[0] + boom * 0.16, tip[1] + boom * 0.06),
             (tip[0] + boom * 0.20, tip[1] - boom * 0.12), (tip[0] - 0.20, tip[1] - boom * 0.06)],
            "accent")


def _build_crane(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    if rec.get("ChassisType") == "Tracked":
        track = vis["TrackH"]
        _belt(sk, -length * 0.46, length * 0.46, track)
        base = track
    else:
        radius = vis["WheelRadiusM"]
        _wheels(sk, length, radius, max(2, vis["Axles"]))
        base = radius + 0.30
        sk.rect(-length * 0.48, base - 0.24, length * 0.96, 0.28, "dark")
    sk.rect(-length * 0.42, base, length * 0.64, height, "shell")
    _cab(sk, vis["CabOffset"] + length * 0.22, vis["CabL"], vis["CabH"], base, tier)
    # A 30 m boom drawn to scale would shrink the carrier to nothing, so the drawn boom is
    # capped against the chassis. A longer boom still draws longer, just not to scale.
    boom = clamp(vis["BoomLengthM"], 4.0, length * 0.85)
    root = (-length * 0.34, base + height)
    tip = (root[0] + boom * 0.72, root[1] + boom * 0.70)
    sk.beam(root, tip, 0.48, "shell")
    sk.beam((root[0] + boom * 0.16, root[1] + boom * 0.16),
            (tip[0] - boom * 0.16, tip[1] - boom * 0.15), 0.18, "dark")
    sk.line([(tip[0], tip[1]), (tip[0], base + height * 0.5)], "ink")
    sk.rect(tip[0] - 0.32, base + height * 0.15, 0.64, 0.40, "accent")

def _build_blower(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    radius = vis["WheelRadiusM"]
    _wheels(sk, length, radius, max(2, vis["Axles"]))
    deck = radius * 0.85
    sk.rect(-length * 0.46, deck, length * 0.62, height, "shell")
    _cab(sk, vis["CabOffset"] - length * 0.26, vis["CabL"], vis["CabH"], deck + height, tier)
    # The intake head is as tall as the machine and as wide as the record says: a blower
    # is a mouth on wheels, and the mouth has to dominate or it reads as a truck.
    intake = float((rec.get("Specs") or {}).get("intakeWidthM") or 1.6)
    mouth = 0.75 + intake * 0.45
    front = length * 0.12
    sk.rect(front, 0.04, length * 0.34, mouth, "accent")
    sk.circle(front + length * 0.17, mouth * 0.50, mouth * 0.30, "shell_dim")
    sk.arc(front + length * 0.05, mouth * 0.95, mouth * 0.95, -5.0, 115.0,
           mouth * 0.34, "shell_dim")

def _build_walkbehind(sk, rec, vis, tier):
    """A walk-behind blower is an auger housing, a small engine deck and handlebars, and
    the handlebars are what say a person walks behind it rather than sits on it."""
    length, height = vis["BodyL"], vis["BodyH"]
    radius = vis["WheelRadiusM"]
    intake = float((rec.get("Specs") or {}).get("intakeWidthM") or 0.8)
    mouth = 0.30 + intake * 0.55
    sk.rect(length * 0.02, 0.02, length * 0.42, mouth, "accent")
    sk.circle(length * 0.23, mouth * 0.5, mouth * 0.30, "shell_dim")
    sk.rect(-length * 0.34, radius * 0.5, length * 0.40, height * 0.42, "shell")
    sk.arc(length * 0.02, mouth * 0.9, mouth * 0.95, 10.0, 140.0, mouth * 0.32, "shell_dim")
    sk.circle(-length * 0.20, radius, radius, "dark")
    sk.circle(-length * 0.20, radius, radius * 0.45, "shell_dim")
    sk.beam((-length * 0.24, radius * 0.5 + height * 0.42),
            (-length * 0.60, height * 1.15), 0.16, "dark")
    sk.beam((-length * 0.60, height * 1.15), (-length * 0.34, height * 1.15), 0.16, "dark")

def _build_gun(sk, rec, vis, tier):
    """A snow gun. A fan gun has a motor worth kilowatts and a barrel; a lance runs on
    compressed air and is a pole with nozzle heads, so gunPowerKw tells them apart."""
    specs = rec.get("Specs") or {}
    fan = float(specs.get("gunPowerKw") or 0.0) >= 5.0
    mast = max(vis["BoomLengthM"], 2.5) * (0.55 if fan else 0.90)
    if rec.get("ChassisType") == "Stationary":
        sk.poly([(-0.70, 0.0), (0.70, 0.0), (0.26, 0.55), (-0.26, 0.55)], "shell_dim")
        base = 0.45
    else:
        radius = vis["WheelRadiusM"]
        sk.circle(-0.55, radius, radius, "dark")
        sk.circle(-0.55, radius, radius * 0.45, "shell_dim")
        sk.rect(-1.15, radius * 0.80, 2.1, 0.40, "shell_dim")
        sk.beam((0.95, radius * 1.0), (1.85, radius * 0.50), 0.16, "dark")
        base = radius * 1.2
    sk.beam((0.0, base), (0.0, base + mast), 0.34, "shell")
    top = base + mast
    if fan:
        # The spray cone comes off the record's own cone half angle, so a wide angle gun
        # visibly throws wider than a narrow one.
        cone = float(specs.get("coneHalfAngleDeg") or 25.0)
        sk.circle(0.0, top + 0.95, 1.00, "shell")
        sk.circle(0.0, top + 0.95, 0.52, "dark")
        sk.rect(-0.30, top + 0.05, 0.60, 0.55, "accent")
        sk.fan(0.95, top + 0.95, 2.6, -cone + 16.0, cone + 16.0, "snow")
    else:
        sk.rect(-0.20, base, 0.40, mast, "shell_dim")
        for i in range(4):
            sk.circle(0.36, base + mast * (0.44 + 0.17 * i), 0.19, "accent")
        sk.fan(0.45, top, 1.8, -14.0, 44.0, "snow")

def _build_station(sk, rec, vis, tier):
    """Fixed plant: a fuel cube, a pump or compressor skid, or the snow factory.

    What each one is comes off its Specs rather than its id: a record with fuelStorageL is
    a tank in a bund, one with waterLpm is a plant hall, one with pumpLps is a pump skid.
    Buildings are drawn as polygons so their corners stay square while the machinery on
    them keeps the set's corner radius.
    """
    length, height = vis["BodyL"], vis["BodyH"]
    specs = rec.get("Specs") or {}
    if specs.get("fuelStorageL"):
        sk.poly([(-length * 0.52, 0.0), (length * 0.52, 0.0),
                 (length * 0.46, height * 0.22), (-length * 0.46, height * 0.22)], "dark")
        sk.poly([(-length * 0.42, height * 0.18), (length * 0.42, height * 0.18),
                 (length * 0.42, height * 0.95), (-length * 0.42, height * 0.95)], "shell")
        sk.rect(-length * 0.30, height * 0.40, length * 0.26, height * 0.34, "accent")
        sk.poly([(length * 0.06, height * 0.95), (length * 0.30, height * 0.95),
                 (length * 0.30, height * 1.12), (length * 0.06, height * 1.12)], "shell_dim")
        sk.beam((length * 0.18, height * 1.05), (length * 0.18, height * 1.45), 0.22,
                "shell_dim")
        sk.line([(length * 0.18, height * 1.45), (length * 0.46, height * 1.45)], "ink")
        return
    if specs.get("waterLpm"):
        sk.poly([(-length * 0.46, 0.0), (length * 0.46, 0.0),
                 (length * 0.46, height), (-length * 0.46, height)], "shell")
        sk.poly([(-length * 0.54, height), (length * 0.54, height),
                 (length * 0.30, height * 1.40), (-length * 0.30, height * 1.40)],
                "shell_dim")
        sk.rect(-length * 0.38, height * 0.30, length * 0.28, height * 0.42, "glass")
        sk.rect(-length * 0.04, 0.0, length * 0.22, height * 0.58, "dark")
        sk.beam((length * 0.40, height * 0.82), (length * 0.92, height * 0.46), 0.50,
                "accent")
        sk.fan(length * 0.92, height * 0.46, height * 0.95, -58.0, 6.0, "snow")
        return
    # A skid is a frame, the machine bolted to it and the pipework that says which machine
    # it is: an impeller volute for a pump, a cooler pack for a compressor.
    sk.poly([(-length * 0.52, 0.0), (length * 0.52, 0.0),
             (length * 0.46, height * 0.24), (-length * 0.46, height * 0.24)], "dark")
    sk.poly([(-length * 0.42, height * 0.20), (length * 0.42, height * 0.20),
             (length * 0.42, height * 0.86), (-length * 0.42, height * 0.86)], "shell")
    if specs.get("pumpLps"):
        sk.circle(length * 0.18, height * 0.53, height * 0.30, "accent")
        sk.beam((length * 0.18, height * 0.53), (length * 0.18, height * 1.16), 0.28,
                "shell_dim")
        sk.beam((-length * 0.40, height * 0.53), (length * 0.18, height * 0.53), 0.24,
                "shell_dim")
    else:
        sk.rect(length * 0.00, height * 0.32, length * 0.36, height * 0.46, "accent")
        for i in range(3):
            x = length * (0.06 + 0.11 * i)
            sk.line([(x, height * 0.38), (x, height * 0.72)], "ink")
        sk.beam((-length * 0.28, height * 0.86), (-length * 0.28, height * 1.22), 0.26,
                "shell_dim")
    sk.poly([(-length * 0.44, height * 0.84), (length * 0.44, height * 0.84),
             (length * 0.44, height * 1.02), (-length * 0.44, height * 1.02)], "shell_dim")

def _build_snowmobile(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    track = max(vis["TrackH"], 0.30)
    sk.poly([(-length * 0.48, track * 0.35), (-length * 0.42, 0.0), (length * 0.02, 0.0),
             (length * 0.06, track * 0.9), (-length * 0.48, track * 0.9)], "dark")
    sk.circle(-length * 0.40, track * 0.45, track * 0.36, "shell_dim")
    # The ski is the snowmobile: nothing else in the fleet has a curled runner up front.
    sk.poly([(length * 0.12, 0.02), (length * 0.46, 0.02), (length * 0.50, 0.30),
             (length * 0.40, 0.16), (length * 0.12, 0.16)], "shell_dim")
    sk.beam((length * 0.22, 0.10), (length * 0.12, track * 1.1), 0.12, "dark")
    sk.poly([(-length * 0.40, track * 0.8), (length * 0.22, track * 0.8),
             (length * 0.34, track + height * 0.45), (length * 0.02, track + height * 0.75),
             (-length * 0.34, track + height * 0.60)], "shell")
    sk.poly([(length * 0.04, track + height * 0.72), (length * 0.26, track + height * 0.50),
             (length * 0.30, track + height * 1.05), (length * 0.06, track + height * 1.10)],
            "glass")
    sk.rect(-length * 0.30, track + height * 0.58, length * 0.26, height * 0.22, "accent")


def _build_utv(sk, rec, vis, tier):
    length, height = vis["BodyL"], vis["BodyH"]
    if rec.get("ChassisType") == "Tracked":
        track = max(vis["TrackH"], 0.35)
        _belt(sk, -length * 0.46, length * 0.46, track)
        base = track
    else:
        radius = vis["WheelRadiusM"]
        _wheels(sk, length, radius, 2)
        base = radius * 0.75
    sk.poly([(-length * 0.42, base), (length * 0.44, base),
             (length * 0.40, base + height * 0.48), (-length * 0.42, base + height * 0.54)],
            "shell")
    sk.rect(-length * 0.40, base + height * 0.54, length * 0.26, height * 0.16, "shell_dim")
    # An open cab under a bar: the cage is the silhouette, so it stays thin and square
    # rather than closing up into a cab.
    roof = base + height * 1.35
    sk.beam((-length * 0.22, base + height * 0.50), (-length * 0.16, roof), 0.12, "dark")
    sk.beam((length * 0.20, base + height * 0.48), (length * 0.08, roof), 0.12, "dark")
    sk.beam((-length * 0.20, roof), (length * 0.10, roof), 0.12, "accent")
    sk.poly([(-length * 0.08, base + height * 0.50), (length * 0.16, base + height * 0.50),
             (length * 0.08, roof - 0.12), (-length * 0.08, roof - 0.12)], "glass")

MACHINE_FAMILIES = {
    "groomer": _build_groomer,
    "truck": _build_truck,
    "tractor": _build_tractor,
    "loader": _build_loader,
    "grader": _build_grader,
    "excavator": _build_excavator,
    "crane": _build_crane,
    "blower": _build_blower,
    "walkbehind": _build_walkbehind,
    "gun": _build_gun,
    "station": _build_station,
    "snowmobile": _build_snowmobile,
    "utv": _build_utv,
}

# Silhouettes that share one drawing. The families differ in how a machine is built, not
# in what it is called: a tanker and a lowboy are both a frame, axles and a cab, and the
# body on the back is the only thing that changes.
SILHOUETTE_FAMILY = {
    "groomer": "groomer", "carrier": "groomer",
    "truck": "truck", "tanker": "truck", "mixer": "truck", "lowboy": "truck",
    "pickup": "truck", "semi": "truck",
    "tractor": "tractor",
    "loader": "loader", "telehandler": "loader",
    "grader": "grader",
    "excavator": "excavator", "drill": "excavator",
    "crane": "crane",
    "blower": "blower",
    "walkbehind": "walkbehind",
    "gun": "gun",
    "station": "station", "tower": "gun",
    "snowmobile": "snowmobile",
    "utv": "utv", "atv": "utv",
}


def machine_sketch(rec):
    """One machine's silhouette, its accent and how much of the box it fills."""
    vis = datasrc.visual(rec)
    tier = datasrc.tier_of(rec)
    family = SILHOUETTE_FAMILY.get(vis["Silhouette"], "truck")
    sk = Sketch()
    MACHINE_FAMILIES[family](sk, rec, vis, tier)
    return sk, accent_for_tier(tier), size_fill(float(rec.get("MassKg") or 500), 150.0, 40000.0)


# --------------------------------------------------------------------------- lifts
# The tower stands on the left and the carrier hangs on the right, in metres. The tower is
# drawn as a stub rather than the 12 m it really is: a player picking between a t-bar and
# an eight pack is choosing the carrier, and a truthful tower height would leave the
# carrier four pixels tall.
PYLON_X = -3.4
PYLON_H = 4.0
ROPE_Y = 3.8
CARRIER_X = 1.1


def _pylon(sk, x, h, heavy=False):
    """Tapered mast, crossarm and sheave train. A heavy line carries more rope on a wider
    base, which is what separates a tram tower from a chairlift tower at this size."""
    foot = h * (0.13 if heavy else 0.09)
    sk.poly([(x - foot, 0.0), (x + foot, 0.0), (x + foot * 0.45, h), (x - foot * 0.45, h)],
            "shell_dim")
    arm = h * (0.17 if heavy else 0.13)
    sk.rect(x - arm, h, arm * 2.0, h * 0.07, "shell_dim")
    for side in (-1.0, 1.0):
        sk.circle(x + side * arm * 0.66, h - h * 0.015, h * 0.045, "dark")


def _ropes(sk, y, configuration, x0, x1):
    """The haul rope, plus the track ropes a bi, tri, funitel or reversible line adds.

    Drawn as thin bars rather than strokes: a stroke stays one weight however far the
    icon is scaled, and at this scale the rope would come out thicker than the tower.
    """
    offsets = {"Mono": (0.0,), "Bi": (-0.26, 0.26), "Tri": (-0.32, 0.0, 0.32),
               "Funitel": (-0.20, 0.20), "Reversible": (-0.30, 0.24)}.get(
                   configuration, (0.0,))
    for dy in offsets:
        sk.beam((x0, y + dy), (x1, y + dy * 0.7), 0.12, "dark")
    return offsets


def _grip(sk, x, y, detachable):
    """A fixed grip is a clamp. A detachable adds the jaw and the lever that let it open
    in the terminal, which is twice the ironmongery and the reason it costs what it does."""
    sk.rect(x - 0.22, y - 0.20, 0.44, 0.40, "dark")
    if detachable:
        sk.rect(x - 0.34, y - 0.52, 0.68, 0.34, "shell_dim")
        sk.beam((x + 0.22, y - 0.36), (x + 0.58, y + 0.10), 0.14, "dark")


def _chair(sk, rec, x, rope_y):
    seats = max(1, int(rec.get("SeatsOrCabinCapacity") or 1))
    width = clamp(0.62 * seats + 0.55, 1.1, 4.4)
    _grip(sk, x, rope_y, rec.get("Grip") == "Detachable")
    top = rope_y - 0.95
    pan = top - 0.85
    sk.beam((x, rope_y - 0.25), (x, top), 0.18, "shell_dim")
    sk.rect(x - width * 0.5, pan, width, 0.86, "accent")
    sk.rect(x - width * 0.5, pan - 0.26, width * 1.04, 0.28, "shell_dim")
    for i in range(1, seats):
        sx = x - width * 0.5 + width * i / seats
        sk.line([(sx, pan + 0.12), (sx, pan + 0.74)], "ink")
    sk.beam((x - width * 0.5 - 0.15, pan + 1.02), (x + width * 0.5 + 0.15, pan + 1.02),
            0.14, "dark")
    sk.rect(x - width * 0.44, pan - 1.20, width * 0.88, 0.20, "shell_dim")
    if float(rec.get("WeatherExposure") or 1.0) <= 0.5:
        sk.arc(x, pan + 0.55, width * 0.60, 0.0, 180.0, 0.22, "glass")


def _cabin(sk, rec, x, rope_y, width, height, doors=True):
    _grip(sk, x, rope_y, rec.get("Grip") == "Detachable")
    top = rope_y - 0.75
    sk.beam((x, rope_y - 0.25), (x, top + 0.10), 0.20, "shell_dim")
    sk.rect(x - width * 0.5, top - height, width, height, "accent")
    sk.rect(x - width * 0.46, top - height * 0.06, width * 0.92, height * 0.12, "shell_dim")
    sk.rect(x - width * 0.40, top - height * 0.52, width * 0.80, height * 0.34, "glass")
    if doors:
        sk.line([(x, top - height * 0.90), (x, top - height * 0.14)], "ink")
    sk.rect(x - width * 0.5, top - height - 0.14, width, 0.16, "shell_dim")

def _build_chair(sk, rec):
    seats = max(1, int(rec.get("SeatsOrCabinCapacity") or 1))
    _pylon(sk, PYLON_X, PYLON_H)
    _ropes(sk, ROPE_Y, rec.get("RopeConfiguration"), PYLON_X, CARRIER_X + seats * 0.36 + 1.2)
    _chair(sk, rec, CARRIER_X, ROPE_Y)


def _build_gondola(sk, rec):
    capacity = int(rec.get("SeatsOrCabinCapacity") or 8)
    carriers = max(1, int(rec.get("CarriersPerHaulRope") or 1))
    _pylon(sk, PYLON_X, PYLON_H, heavy=capacity >= 15)
    width = clamp(1.50 + 0.060 * capacity, 1.7, 3.2)
    height = clamp(2.00 + 0.020 * capacity, 2.0, 2.9)
    if carriers > 1:
        # A pulse gondola runs its cabins in a train, which is the only thing that tells
        # it from a continuous line at a glance.
        train = min(carriers, 3)
        _ropes(sk, ROPE_Y, rec.get("RopeConfiguration"), PYLON_X,
               CARRIER_X + train * width * 0.72)
        for i in range(train):
            _cabin(sk, rec, CARRIER_X - 0.3 + i * width * 0.80, ROPE_Y,
                   width * 0.70, height * 0.84, doors=False)
    else:
        _ropes(sk, ROPE_Y, rec.get("RopeConfiguration"), PYLON_X, CARRIER_X + width * 0.5 + 1.0)
        _cabin(sk, rec, CARRIER_X, ROPE_Y, width, height)


def _build_aerial(sk, rec):
    capacity = int(rec.get("SeatsOrCabinCapacity") or 40)
    _pylon(sk, PYLON_X, PYLON_H, heavy=True)
    width = clamp(1.7 + 0.016 * capacity, 2.0, 3.6)
    height = clamp(2.1 + 0.005 * capacity, 2.1, 2.9)
    offsets = _ropes(sk, ROPE_Y, rec.get("RopeConfiguration"), PYLON_X,
                     CARRIER_X + width * 0.5 + 1.0)
    x = CARRIER_X
    # A tram or a 3S rides on track ropes through a bogie rather than on a single grip, so
    # the running gear is drawn as the carriage it is.
    sk.rect(x - width * 0.34, ROPE_Y - 0.48, width * 0.68, 0.46, "shell_dim")
    for dy in offsets:
        for side in (-0.22, 0.22):
            sk.circle(x + width * side, ROPE_Y + dy, 0.20, "dark")
    top = ROPE_Y - 1.05
    sk.beam((x, ROPE_Y - 0.40), (x, top + 0.1), 0.24, "shell_dim")
    sk.rect(x - width * 0.5, top - height, width, height, "accent")
    sk.rect(x - width * 0.42, top - height * 0.62, width * 0.84, height * 0.44, "glass")
    sk.line([(x, top - height * 0.94), (x, top - height * 0.10)], "ink")
    sk.rect(x - width * 0.5, top - height - 0.14, width, 0.16, "shell_dim")


def _build_hybrid(sk, rec):
    """A chondola carries chairs and cabins on one rope, so its icon has to as well."""
    _pylon(sk, PYLON_X, PYLON_H)
    _ropes(sk, ROPE_Y, rec.get("RopeConfiguration"), PYLON_X, CARRIER_X + 3.6)
    chair = dict(rec)
    chair["SeatsOrCabinCapacity"] = max(2, int(rec.get("SeatsOrCabinCapacity") or 6) // 2)
    _chair(sk, chair, CARRIER_X - 0.5, ROPE_Y)
    _cabin(sk, rec, CARRIER_X + 2.5, ROPE_Y, 1.5, 2.1)


def _build_rail(sk, rec):
    """A funicular, a cog railway and an inclined elevator are a car on a slope: no tower,
    no rope in the air, and a rack between the rails when the record says Rack."""
    capacity = int(rec.get("SeatsOrCabinCapacity") or 40)
    x0, x1 = -3.6, 3.6
    slope = 0.26

    def rail_y(x):
        return 0.36 + (x - x0) * slope

    sk.beam((x0, rail_y(x0)), (x1, rail_y(x1)), 0.30, "shell_dim")
    for i in range(6):
        x = x0 + (x1 - x0) * i / 5.0
        sk.rect(x - 0.18, rail_y(x) - 0.50, 0.36, 0.50, "dark")
    if rec.get("RopeConfiguration") == "Rack":
        # The rack is the cog railway's whole point: a toothed rail it climbs rather than
        # a rope it hangs from.
        for i in range(9):
            x = x0 + (x1 - x0) * (i + 0.5) / 9.0
            sk.rect(x - 0.12, rail_y(x) + 0.14, 0.24, 0.30, "dark")
    length = clamp(2.4 + 0.013 * capacity, 2.4, 3.8)
    height = clamp(1.70 + 0.006 * capacity, 1.70, 2.40)
    cx = 0.1
    base = rail_y(cx) + 0.40
    lift = length * slope
    # The body follows the slope while the floor inside steps level, and that step is what
    # says funicular rather than tramcar.
    sk.poly([(cx - length * 0.5, base), (cx + length * 0.5, base + lift),
             (cx + length * 0.5, base + lift + height),
             (cx - length * 0.5, base + height)], "accent")
    sk.poly([(cx - length * 0.46, base + height * 0.86), (cx + length * 0.46, base + lift * 0.94 + height * 0.86),
             (cx + length * 0.46, base + lift * 0.94 + height * 1.06),
             (cx - length * 0.46, base + height * 1.06)], "shell_dim")
    for side in (-0.23, 0.23):
        wx = cx + length * side
        wy = base + (wx - (cx - length * 0.5)) * slope
        sk.poly([(wx - length * 0.17, wy + height * 0.34),
                 (wx + length * 0.17, wy + height * 0.34 + length * 0.34 * slope),
                 (wx + length * 0.17, wy + height * 0.74 + length * 0.34 * slope),
                 (wx - length * 0.17, wy + height * 0.74)], "glass")
    for side in (-0.34, 0.34):
        sk.circle(cx + length * side, rail_y(cx + length * side) + 0.24, 0.22, "dark")

def _build_surface(sk, rec):
    """Surface lifts split by how their carriers are spaced and how comfortable they are.

    A carrier every 2.4 m is a continuous belt rather than a line of hangers; two riders
    on one carrier is a T-bar; a carrier a rider holds with bare hands scores 0.2 for
    comfort and is a rope tow; anything else is a platter.
    """
    spacing = float(rec.get("CarrierSpacingM") or 10.0)
    capacity = int(rec.get("SeatsOrCabinCapacity") or 1)
    comfort = float(rec.get("ComfortScore") or 0.3)
    if spacing < 4.0:
        x0, x1 = -3.6, 3.6
        sk.poly([(x0 - 0.65, 0.0), (x0, 0.40), (x1, 0.40), (x1, 0.0)], "shell_dim")
        sk.rect(x0, 0.40, x1 - x0, 0.32, "accent")
        for i in range(6):
            sk.circle(x0 + 0.55 + i * (x1 - x0 - 1.1) / 5.0, 0.28, 0.20, "dark")
        # The entry portal is the carpet's landmark: it is the bit a beginner walks into.
        sk.beam((x1 - 0.25, 0.40), (x1 - 0.25, 2.30), 0.24, "shell_dim")
        sk.beam((x0 + 0.25, 0.40), (x0 + 0.25, 1.95), 0.20, "shell_dim")
        sk.beam((x0 + 0.25, 1.95), (x1 - 0.25, 2.30), 0.20, "shell_dim")
        if float(rec.get("WeatherExposure") or 1.0) < 0.6:
            sk.arc(0.0, 0.72, 1.55, 10.0, 170.0, 0.26, "glass")
        return
    _pylon(sk, PYLON_X, PYLON_H)
    _ropes(sk, ROPE_Y, "Mono", PYLON_X, CARRIER_X + 1.8)
    x = CARRIER_X
    sk.rect(x - 0.22, ROPE_Y - 0.20, 0.44, 0.40, "dark")
    if capacity >= 2:
        sk.beam((x, ROPE_Y - 0.15), (x, 0.95), 0.18, "shell_dim")
        sk.rect(x - 0.95, 0.60, 1.90, 0.36, "accent")
    elif comfort <= 0.25:
        # A rope tow has no carrier at all, only a grip handle hung off the rope.
        sk.beam((x - 0.15, ROPE_Y - 0.1), (x + 0.45, ROPE_Y - 1.35), 0.16, "shell_dim")
        sk.rect(x + 0.25, ROPE_Y - 2.05, 0.55, 0.75, "accent")
    else:
        sk.beam((x, ROPE_Y - 0.15), (x, 1.35), 0.18, "shell_dim")
        sk.circle(x, 0.95, 0.62, "accent")
        sk.circle(x, 0.95, 0.22, "shell_dim")


LIFT_FAMILIES = {
    "Chair": _build_chair,
    "Gondola": _build_gondola,
    "Aerial": _build_aerial,
    "Hybrid": _build_hybrid,
    "Rail": _build_rail,
    "Surface": _build_surface,
}


def lift_sketch(rec):
    """One lift type's icon: its tower, its rope and the carrier that identifies it."""
    sk = Sketch()
    LIFT_FAMILIES.get(rec.get("Family"), _build_chair)(sk, rec)
    return (sk, accent_for_tier(datasrc.tier_of(rec)),
            size_fill(float(rec.get("CapacityPph") or 1000), 500.0, 4000.0))


# --------------------------------------------------------------------------- attachments
# An implement's accent says what it does, because that is what a player is shopping for:
# these six groups are the verbs in the fleet.
ATTACHMENT_ACCENT = {
    "push": "#4aa6e8",
    "till": "#57bd7d",
    "throw": "#57c4c9",
    "lift": "#efb134",
    "treat": "#a97ce0",
    "utility": "#6f7d8d",
}

KIND_GROUP = {
    "Blade": "push", "Blade12Way": "push", "UBlade": "push", "VPlow": "push",
    "PlowWings": "push", "PlowStraight": "push", "BoxPusher": "push", "ParkBlade": "push",
    "Tiller": "till", "TrackSetter": "till", "PipeCutter": "till", "Mulcher": "till",
    "BlowerHead": "throw", "Spreader": "treat", "BrineTank": "treat", "Broom": "treat",
    "SnowBucket": "lift", "LightBucket": "lift", "Forks": "lift", "Grapple": "lift",
    "TowerJib": "lift",
    "Winch": "utility", "Auger": "utility", "Hitch": "utility", "SledHitch": "utility",
    "CableReel": "utility", "LightTower": "utility", "PumpSkid": "utility",
}


def _headstock(sk, top, half=0.42):
    """The A-frame and mount plate every implement hangs off. Drawing it on all of them is
    what makes 33 unrelated shapes read as one catalogue."""
    sk.beam((-half, top - 0.35), (0.0, top + 0.30), 0.16, "shell_dim")
    sk.beam((half, top - 0.35), (0.0, top + 0.30), 0.16, "shell_dim")
    sk.rect(-0.34, top + 0.24, 0.68, 0.26, "dark")


def _mouldboard(sk, x0, x1, base, height, style="accent"):
    """A blade panel with the top rail a mouldboard is rolled over."""
    sk.rect(x0, base, x1 - x0, height, style)
    sk.rect(x0, base + height - 0.10, x1 - x0, 0.22, "shell_dim")

def _att_blade(sk, rec, w):
    kind = rec["Kind"]
    height = 0.80 + w * 0.075
    if kind == "VPlow":
        # A V plough is two mouldboards meeting at a nose, so the icon is that nose.
        sk.poly([(-w * 0.5, 0.18), (0.0, 0.18), (0.0, height * 1.10),
                 (-w * 0.5, height * 0.80)], "accent")
        sk.poly([(w * 0.5, 0.18), (0.0, 0.18), (0.0, height * 1.10),
                 (w * 0.5, height * 0.80)], "accent")
        sk.line([(0.0, 0.30), (0.0, height * 1.02)], "ink")
        sk.rect(-w * 0.5, 0.0, w, 0.22, "dark")
    elif kind == "Blade12Way":
        # Twelve way means the two outer wings hinge, so the hinges are drawn as the gaps
        # the mesh generator puts blade_angle_L and blade_angle_R at.
        wing = w * 0.26
        _mouldboard(sk, -w * 0.5 + wing, w * 0.5 - wing, 0.18, height)
        for side in (-1.0, 1.0):
            sk.poly([(side * w * 0.5, 0.34), (side * (w * 0.5 - wing * 0.86), 0.18),
                     (side * (w * 0.5 - wing * 0.86), height + 0.18),
                     (side * w * 0.5, height + 0.02)], "accent")
        sk.rect(-w * 0.5 + wing, 0.0, w - wing * 2.0, 0.22, "dark")
    elif kind in ("UBlade", "BoxPusher"):
        _mouldboard(sk, -w * 0.42, w * 0.42, 0.18, height)
        for side in (-1.0, 1.0):
            sk.rect(side * w * 0.46 - 0.13, 0.0, 0.26,
                    height * (1.20 if kind == "BoxPusher" else 0.98), "shell_dim")
        sk.rect(-w * 0.42, 0.0, w * 0.84, 0.22, "dark")
    else:
        _mouldboard(sk, -w * 0.5, w * 0.5, 0.18, height)
        if kind == "PlowWings":
            sk.poly([(w * 0.5, 0.24), (w * 0.5 + w * 0.20, 0.42),
                     (w * 0.5 + w * 0.20, height * 0.92), (w * 0.5, height + 0.18)],
                    "shell_dim")
        if kind == "ParkBlade":
            # A park blade is shaped: its cutting edge carries the profile it cuts.
            sk.poly([(-w * 0.5, 0.0), (w * 0.5, 0.0), (w * 0.5, 0.26),
                     (w * 0.18, 0.26), (0.0, 0.04), (-w * 0.18, 0.26),
                     (-w * 0.5, 0.26)], "dark")
        else:
            sk.rect(-w * 0.5, 0.0, w, 0.22, "dark")
    _headstock(sk, height + 0.30)
    sk.beam((-w * 0.24, height * 0.55), (-0.02, height + 0.20), 0.14, "shell_dim")
    sk.beam((w * 0.24, height * 0.55), (0.02, height + 0.20), 0.14, "shell_dim")

def _att_tiller(sk, rec, w):
    kind = rec["Kind"]
    height = 0.62 + w * 0.05
    sk.rect(-w * 0.5, 0.34, w, height, "accent")
    sk.rect(-w * 0.5, 0.34 + height, w, 0.18, "shell_dim")
    if kind == "PipeCutter":
        # A pipe cutter carries the half pipe wall it shapes, not a flat finisher.
        sk.arc(0.0, 0.30, w * 0.42, 20.0, 160.0, 0.26, "shell_dim")
    elif kind == "Mulcher":
        for i in range(6):
            sk.circle(-w * 0.40 + w * 0.80 * i / 5.0, 0.30, 0.14, "dark")
    else:
        teeth = int(clamp(round(w * 1.7), 5, 9))
        for i in range(teeth):
            x = -w * 0.44 + w * 0.88 * i / (teeth - 1)
            sk.rect(x - w * 0.030, 0.0, w * 0.060, 0.34, "dark")
    if kind == "TrackSetter":
        # Track setters press the classic tracks: two boxes outboard of the rotor.
        for side in (-1.0, 1.0):
            sk.rect(side * w * 0.42 - 0.22, 0.0, 0.44, 0.30, "shell_dim")
    _headstock(sk, 0.34 + height + 0.22)

def _att_blower_head(sk, rec, w):
    height = 0.85 + w * 0.10
    sk.rect(-w * 0.5, 0.10, w, height, "accent")
    sk.rect(-w * 0.5, 0.0, w, 0.16, "dark")
    for side in (-1.0, 1.0):
        sk.circle(side * w * 0.24, height * 0.52, height * 0.30, "shell_dim")
        sk.circle(side * w * 0.24, height * 0.52, height * 0.11, "dark")
    sk.rect(-w * 0.5, height + 0.10, w, 0.18, "shell_dim")
    # The chute is what makes a blower a blower: it throws the snow somewhere, and it is
    # offset so it does not stack under the headstock and read as a mast.
    sk.beam((-w * 0.18, height + 0.20), (-w * 0.18, height + 0.80), 0.42, "shell_dim")
    sk.arc(-w * 0.18 - 0.44, height + 0.80, 0.46, -6.0, 84.0, 0.40, "shell_dim")
    sk.rect(-w * 0.18 - 1.08, height + 0.62, 0.36, 0.30, "accent")
    _headstock(sk, height + 1.15, half=0.40)

def _att_bucket(sk, rec, w):
    height = 0.75 + w * 0.09
    sk.poly([(-w * 0.5, height), (w * 0.5, height), (w * 0.44, 0.20),
             (-w * 0.44, 0.20)], "accent")
    sk.rect(-w * 0.44, 0.0, w * 0.88, 0.24, "dark")
    for side in (-1.0, 1.0):
        sk.poly([(side * w * 0.5, height), (side * w * 0.44, 0.20),
                 (side * w * 0.36, 0.20), (side * w * 0.42, height)], "shell_dim")
    if rec["Kind"] == "LightBucket":
        # A light material bucket carries a spill guard over the back.
        sk.rect(-w * 0.5, height, w, 0.20, "shell_dim")
    if rec["Kind"] == "Grapple":
        for side in (-1.0, 1.0):
            sk.beam((side * w * 0.30, height), (side * w * 0.46, height + 0.85), 0.18,
                    "shell_dim")
            sk.beam((side * w * 0.46, height + 0.85), (side * w * 0.10, height + 0.55),
                    0.16, "dark")
    _headstock(sk, height + (1.05 if rec["Kind"] == "Grapple" else 0.30))


def _att_forks(sk, rec, w):
    """Forks read from a three quarter view: a carriage, and two tines with their heels on
    the ground so the pair is visible rather than one hiding behind the other."""
    height = 1.05 + w * 0.30
    sk.rect(-0.52, 0.0, 0.32, height, "shell_dim")
    for y in (height * 0.30, height * 0.66):
        sk.rect(-0.52, y, 1.00, 0.18, "dark")
    sk.rect(-0.24, 0.42, 0.26, height * 0.52, "shell_dim")
    sk.poly([(-0.24, 0.30), (w + 0.10, 0.30), (w + 0.10, 0.48), (-0.24, 0.52)], "accent")
    sk.rect(-0.10, 0.16, 0.26, height * 0.40, "shell_dim")
    sk.poly([(-0.10, 0.0), (w + 0.44, 0.0), (w + 0.44, 0.20), (-0.10, 0.24)], "accent")
    _headstock(sk, height + 0.10)

def _att_broom(sk, rec, w):
    r = 0.46
    sk.rect(-w * 0.5, 0.10, w, r * 2.0, "accent")
    for side in (-1.0, 1.0):
        sk.circle(side * w * 0.5, 0.10 + r, r, "accent")
    for i in range(9):
        x = -w * 0.46 + w * 0.92 * i / 8.0
        sk.line([(x, 0.16), (x, 0.10 + r * 1.9)], "ink")
    sk.rect(-w * 0.5, 0.10 + r * 2.0, w, 0.20, "shell_dim")
    _headstock(sk, 0.10 + r * 2.0 + 0.24)


def _att_auger(sk, rec, w):
    """An auger is a flighting on a shaft, so the icon is the helix seen edge on."""
    height = 2.1
    sk.rect(-w * 0.18, 0.0, w * 0.36, height, "shell_dim")
    for i in range(5):
        y = 0.20 + i * (height - 0.7) / 4.0
        sk.poly([(-w * 0.55, y), (w * 0.55, y + 0.22), (w * 0.55, y + 0.40),
                 (-w * 0.55, y + 0.18)], "accent")
    sk.poly([(-w * 0.22, 0.0), (w * 0.22, 0.0), (0.0, -0.35)], "dark")
    _headstock(sk, height + 0.10)


def _att_spreader(sk, rec, w):
    height = 1.15
    sk.poly([(-w * 0.5, height), (w * 0.5, height), (w * 0.26, 0.46), (-w * 0.26, 0.46)],
            "accent")
    sk.rect(-w * 0.5, height, w, 0.20, "shell_dim")
    sk.rect(-w * 0.16, 0.26, w * 0.32, 0.24, "shell_dim")
    sk.circle(0.0, 0.16, w * 0.15, "dark")
    sk.line([(-w * 0.34, 0.16), (w * 0.34, 0.16)], "ink")
    _headstock(sk, height + 0.26)

def _att_tank(sk, rec, w):
    """A brine tank is a barrel and a spray bar: the bar is what puts the liquid down."""
    height = 1.05
    r = height * 0.5
    sk.circle(-w * 0.5 + r, 0.52 + r, r, "accent")
    sk.circle(w * 0.5 - r, 0.52 + r, r, "accent")
    sk.rect(-w * 0.5 + r, 0.52, w - 2 * r, height, "accent")
    sk.rect(-w * 0.12, 0.52 + height, w * 0.24, 0.22, "shell_dim")
    sk.rect(-w * 0.5, 0.20, w, 0.18, "shell_dim")
    for i in range(5):
        x = -w * 0.4 + w * 0.8 * i / 4.0
        sk.rect(x - 0.07, 0.0, 0.14, 0.22, "dark")
    _headstock(sk, 0.52 + height + 0.20)

def _att_hitch(sk, rec, w):
    plate = max(w, 0.55)
    sk.rect(-plate * 0.5, 0.35, plate, 0.42, "shell_dim")
    sk.rect(-plate * 0.22, 0.0, plate * 0.44, 0.40, "accent")
    if rec["Kind"] == "SledHitch":
        # A sled hitch is a loop for a rescue toboggan, not a drawbar eye.
        sk.arc(0.0, 0.16, 0.34, 190.0, 350.0, 0.16, "dark")
    else:
        sk.circle(0.0, 0.12, 0.24, "dark")
    _headstock(sk, 0.77)


def _att_reel(sk, rec, w):
    r = w * 0.42
    sk.rect(-w * 0.5, 0.0, w, 0.26, "shell_dim")
    for side in (-1.0, 1.0):
        sk.beam((side * w * 0.42, 0.20), (side * w * 0.30, 0.35 + r), 0.20, "shell_dim")
    sk.circle(0.0, 0.35 + r, r, "accent")
    sk.circle(0.0, 0.35 + r, r * 0.38, "shell_dim")
    sk.line([(w * 0.30, 0.35 + r), (w * 0.62, 0.35 + r * 0.4)], "ink")
    _headstock(sk, 0.35 + r * 2.0 + 0.10)


def _att_jib(sk, rec, w):
    mast = 1.9
    sk.rect(-0.26, 0.0, 0.52, mast, "shell_dim")
    sk.beam((0.0, mast), (w + 0.9, mast + 0.75), 0.30, "accent")
    sk.line([(w + 0.85, mast + 0.70), (w + 0.85, mast - 0.35)], "ink")
    sk.arc(w + 0.85, mast - 0.55, 0.22, 200.0, 380.0, 0.16, "dark")
    _headstock(sk, mast + 0.10)


def _att_light_tower(sk, rec, w):
    mast = 2.4
    sk.rect(-w * 0.5, 0.0, w, 0.30, "shell_dim")
    sk.rect(-0.18, 0.20, 0.36, mast, "shell_dim")
    sk.rect(-w * 0.5, mast + 0.20, w, 0.22, "dark")
    for i in range(4):
        x = -w * 0.36 + w * 0.72 * i / 3.0
        sk.rect(x - w * 0.11, mast + 0.42, w * 0.22, 0.34, "accent")
    _headstock(sk, mast + 0.80)


def _att_pump_skid(sk, rec, w):
    height = 1.15
    sk.rect(-w * 0.5, 0.0, w, 0.26, "dark")
    sk.rect(-w * 0.46, 0.22, w * 0.92, height * 0.55, "shell_dim")
    sk.circle(-w * 0.16, 0.22 + height * 0.30, height * 0.28, "accent")
    sk.beam((-w * 0.16, 0.22 + height * 0.30), (-w * 0.16, height + 0.20), 0.24,
            "shell_dim")
    sk.beam((w * 0.10, 0.40), (w * 0.44, 0.40), 0.22, "shell_dim")
    sk.rect(w * 0.06, 0.22 + height * 0.20, w * 0.34, height * 0.30, "shell")
    _headstock(sk, height + 0.30)


def _att_winch(sk, rec, w):
    """A winch is a drum and the rope off it; the rope is the reason the machine can hold
    a fall line at all."""
    width = max(w, 1.6)
    sk.rect(-width * 0.5, 0.0, width, 0.30, "dark")
    for side in (-1.0, 1.0):
        sk.rect(side * width * 0.5 - side * 0.16 - 0.08, 0.24, 0.28, 1.10, "shell_dim")
    sk.rect(-width * 0.36, 0.42, width * 0.72, 0.80, "accent")
    sk.circle(0.0, 0.82, 0.40, "shell_dim")
    sk.beam((0.0, 1.30), (width * 0.42, 2.35), 0.14, "dark")
    _headstock(sk, 1.34)


ATTACHMENT_KINDS = {
    "Blade": _att_blade, "Blade12Way": _att_blade, "UBlade": _att_blade,
    "VPlow": _att_blade, "PlowWings": _att_blade, "PlowStraight": _att_blade,
    "BoxPusher": _att_blade, "ParkBlade": _att_blade,
    "Tiller": _att_tiller, "TrackSetter": _att_tiller, "PipeCutter": _att_tiller,
    "Mulcher": _att_tiller,
    "BlowerHead": _att_blower_head,
    "SnowBucket": _att_bucket, "LightBucket": _att_bucket, "Grapple": _att_bucket,
    "Forks": _att_forks,
    "Broom": _att_broom,
    "Auger": _att_auger,
    "Spreader": _att_spreader,
    "BrineTank": _att_tank,
    "Hitch": _att_hitch, "SledHitch": _att_hitch,
    "CableReel": _att_reel,
    "TowerJib": _att_jib,
    "LightTower": _att_light_tower,
    "PumpSkid": _att_pump_skid,
    "Winch": _att_winch,
}


def attachment_sketch(rec):
    """One implement's icon, keyed on Kind and sized by its own working width."""
    kind = rec.get("Kind", "")
    width = max(float(rec.get("WorkingWidthM") or 0.0), 0.8)
    sk = Sketch()
    builder = ATTACHMENT_KINDS.get(kind)
    if builder is None:
        raise KeyError("attachments.json has Kind '%s' with no icon recipe" % kind)
    builder(sk, rec, width)
    accent = ATTACHMENT_ACCENT[KIND_GROUP.get(kind, "utility")]
    return sk, accent, size_fill(width, 0.6, 6.0)


# --------------------------------------------------------------------------- build
def build_all(out_dir):
    """Every catalogue icon, in file order. One manifest record per rasterised PNG.

    Every record in vehicles.json, lifts.json and attachments.json gets an icon. A missing
    one is not a cosmetic gap: the shop list draws whatever the registry hands it, so a
    hole here is a blank tile next to a machine a player is being asked to pay for.
    """
    records = []
    for rec in datasrc.vehicles():
        sketch, accent, fill = machine_sketch(rec)
        records += rasterise(document(sketch, accent, fill=fill, plinth=True), out_dir,
                             "machine_" + rec["Id"], "machine",
                             extra={"sourceId": rec["Id"],
                                    "displayName": rec.get("DisplayName", ""),
                                    "tier": datasrc.tier_of(rec)})
    for rec in datasrc.lifts():
        sketch, accent, fill = lift_sketch(rec)
        records += rasterise(document(sketch, accent, fill=fill, plinth=True), out_dir,
                             "lift_" + rec["Id"], "lift",
                             extra={"sourceId": rec["Id"],
                                    "displayName": rec.get("DisplayName", ""),
                                    "tier": datasrc.tier_of(rec)})
    for rec in datasrc.attachments():
        sketch, accent, fill = attachment_sketch(rec)
        records += rasterise(document(sketch, accent, fill=fill, plinth=True), out_dir,
                             "attachment_" + rec["Id"], "attachment",
                             extra={"sourceId": rec["Id"],
                                    "displayName": rec.get("DisplayName", ""),
                                    "kindName": rec.get("Kind", "")})
    return records


def audit(records):
    """Every id in the three data files has an icon at every scale, or this raises.

    The build would otherwise stay green with a hole in the UI, which is exactly the kind
    of failure nobody notices until a player opens the shop.
    """
    have = set()
    for r in records:
        have.add((r["group"], r.get("sourceId"), r["scale"]))
    missing = []
    for group, rows in (("machine", datasrc.vehicles()), ("lift", datasrc.lifts()),
                        ("attachment", datasrc.attachments())):
        for rec in rows:
            for scale in config.ICON_SCALES:
                if (group, rec["Id"], scale) not in have:
                    missing.append("%s_%s@%dx" % (group, rec["Id"], scale))
    if missing:
        raise validate.ContractError(
            "icons: %d catalogue records produced no icon: %s"
            % (len(missing), ", ".join(missing[:8])))
    return len(have)


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.ICONS_DIR
    validate.reset()
    written = build_all(target)
    audit(written)
    print("%d icons, %d records" % (len(written) // len(config.ICON_SCALES), len(written)))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
