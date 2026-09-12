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
        rx = min(RADIUS, gw * 0.5, gh * 0.5)
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
    """Cab shell and glazing. A tier 1 cab is a boxy upright; a tier 5 rakes its
    windscreen forward into a wraparound, which is the same progression the mesh
    generators build in three dimensions."""
    cl = max(cl, 0.6)
    ch = max(ch, 0.5)
    rake = 0.10 + 0.05 * tier
    back, front = cx - cl * 0.5, cx + cl * 0.5
    sk.poly([(back, base), (front, base), (front - cl * rake, base + ch),
             (back + cl * 0.06, base + ch)], "shell")
    if glazing:
        inset = min(0.18, cl * 0.14)
        sk.poly([(back + cl * 0.20, base + ch * 0.36), (front - inset * 0.7, base + ch * 0.30),
                 (front - cl * rake - inset * 0.5, base + ch - inset),
                 (back + cl * 0.20, base + ch - inset)], "glass")


def _blade(sk, x, h, clearance):
    """A mouldboard from the side: concave forward, cutting edge on the ground."""
    sk.beam((x + 0.20, h * 0.40), (x - 0.95, clearance * 0.75), 0.16, "dark")
    sk.poly([(x + 0.46, 0.02), (x + 0.18, 0.0), (x - 0.02, h * 0.45),
             (x + 0.16, h), (x + 0.50, h), (x + 0.32, h * 0.50)], "accent")


def _tiller(sk, x, w, h, clearance):
    """Rear tiller or track setter: rotor housing, finisher flap, lift arm."""
    sk.beam((x + 0.5, clearance + 0.25), (x - w * 0.45, h * 0.9), 0.16, "shell_dim")
    sk.rect(x - w, 0.16, w, h, "accent")
    sk.poly([(x - w - 0.45, 0.02), (x - w * 0.35, 0.05), (x - w * 0.35, 0.22),
             (x - w - 0.45, 0.26)], "shell_dim")


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
    """Intake housing and chute. The auger mouth is the whole point of the silhouette."""
    sk.rect(min(x, x + direction * w), 0.05, w, h, "accent")
    sk.circle(x + direction * w * 0.5, h * 0.5, h * 0.36, "shell_dim")
    sk.arc(x + direction * w * 0.5, h, h * 0.85, 90.0, 180.0 if direction > 0 else 0.0,
           0.42, "shell_dim")


def _winch(sk, cx, cy):
    """Roof winch: a drum and the rope leaving it uphill, which is how a winch cat is
    told from the same machine without one at a glance."""
    sk.rect(cx - 0.55, cy, 1.1, 0.5, "shell_dim")
    sk.circle(cx, cy + 0.25, 0.34, "accent")
    sk.line([(cx, cy + 0.25), (cx + 2.6, cy + 1.7)], "ink")


def _front_implement(sk, rec, nose, clearance):
    att = default_attachment(rec, "Front")
    if att is None:
        return
    kind = att.get("Kind")
    width = float(att.get("WorkingWidthM") or 1.0)
    if kind in BLADE_KINDS:
        _blade(sk, nose + 0.10, 0.62 + width * 0.11, clearance)
    elif kind == "BlowerHead":
        _blower_head(sk, nose + 0.05, 0.85, 0.55 + width * 0.20)
    elif kind == "Broom":
        sk.circle(nose + 0.55, 0.42, 0.42, "accent")
        sk.beam((nose + 0.55, 0.42), (nose - 0.6, clearance * 0.8), 0.16, "dark")


def _rear_implement(sk, rec, tail, clearance):
    att = default_attachment(rec, "Rear")
    if att is None:
        return
    kind = att.get("Kind")
    width = float(att.get("WorkingWidthM") or 1.0)
    if kind in TILL_KINDS:
        _tiller(sk, tail - 0.05, 1.05 + width * 0.10, 0.48 + width * 0.05, clearance)
    elif kind == "Spreader":
        _hopper(sk, tail - 0.1, 1.5, 0.9, clearance)
    elif kind == "BrineTank":
        _tank(sk, tail - 0.1, 2.4, 1.1, clearance + 0.2)
    elif kind == "BlowerHead":
        _blower_head(sk, tail - 0.05, 0.85, 0.55 + width * 0.20, direction=-1.0)


def _roof_implement(sk, rec, roof_y, cx):
    att = default_attachment(rec, "Roof")
    if att is not None and att.get("Kind") == "Winch":
        _winch(sk, cx, roof_y)
