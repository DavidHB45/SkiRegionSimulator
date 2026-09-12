"""HUD glyphs, map markers, weather symbols and the 9-slice panel and button sheets.

These are the only icons with no record behind them: there is no fuel.json to read, so
the drawings are authored here. What keeps them part of the same set is that they are
built with the same kit as the catalogue icons in ui/icons.py, on the same grid, with the
same stroke weight, corner radius and palette, and rasterised through the same path. An
icon set that agrees about everything except its HUD is a set with a hole in it.

Two things are deliberate. HUD glyphs are authored directly on the 64 unit grid rather
than fitted to it, because a thermometer and a cloud want different optical sizes and
fitting both to the same box would make the thermometer tower over everything. And the
panel sheets are written straight out as SVG rather than through the sketch kit, because
a 9-slice sheet needs flat translucent fills and edge detail that lands inside the border
insets, which is a different problem from drawing a shape.

Naming is docs/ART_CONTRACT.md section 8: hud_<name>@2x.png, marker_<name>@4x.png,
weather_<name>@1x.png, ui_panel_<state>.png, ui_button_<state>.png.

Run standalone from tools/assetgen:  python3 -m ui.hud [out_dir]
"""
import io
import json
import math
import os

import cairosvg
from PIL import Image

import config
from lib import validate
from ui.icons import GRID, RADIUS, STROKE, Sketch, document, rasterise

# Semantic colours, matching Assets/Scripts/Unity/UI/UiFactory.cs so a generated glyph and
# a runtime-tinted label agree about what good, warning and bad look like.
ACCENT = "#5cb8ff"
GOOD = "#73d973"
WARN = "#ffcc4d"
BAD = "#ff6659"
COLD = "#8fd3ff"
GOLD = "#f2c14e"
PALE = "#cfdae6"


# --------------------------------------------------------------------------- shape kit
def _star(sk, cx, cy, r, style="accent", points=5):
    pts = []
    for i in range(points * 2):
        a = math.radians(-90.0 + 180.0 * i / points)
        radius = r if i % 2 == 0 else r * 0.45
        pts.append((cx + math.cos(a) * radius, cy + math.sin(a) * radius))
    sk.poly(pts, style)


def _gear(sk, cx, cy, r, style="shell", teeth=8):
    for i in range(teeth):
        a = math.radians(360.0 * i / teeth)
        sk.rect(cx + math.cos(a) * r - r * 0.16, cy + math.sin(a) * r - r * 0.16,
                r * 0.32, r * 0.32, style)
    sk.circle(cx, cy, r, style)
    sk.circle(cx, cy, r * 0.34, "dark")


def _person(sk, cx, base, height, style="shell"):
    head = height * 0.26
    sk.circle(cx, base + height - head, head, style)
    sk.poly([(cx - height * 0.30, base), (cx + height * 0.30, base),
             (cx + height * 0.22, base + height * 0.62),
             (cx - height * 0.22, base + height * 0.62)], style)


def _cloud(sk, cx, cy, width, style="shell"):
    r = width * 0.30
    sk.circle(cx - width * 0.26, cy, r * 0.86, style)
    sk.circle(cx + width * 0.02, cy + r * 0.42, r, style)
    sk.circle(cx + width * 0.30, cy - r * 0.05, r * 0.78, style)
    sk.rect(cx - width * 0.42, cy - r * 0.86, width * 0.84, r * 0.90, style)


def _flake(sk, cx, cy, r, style="accent"):
    """A diamond, not an asterisk: six hairlines at this size turn into a grey blob."""
    sk.poly([(cx, cy + r), (cx + r * 0.62, cy), (cx, cy - r), (cx - r * 0.62, cy)], style)


def _pin(sk, cx, cy, r, style="accent"):
    """The marker body every map pin shares, so the map reads as one family."""
    sk.poly([(cx - r * 0.74, cy + r * 0.30), (cx + r * 0.74, cy + r * 0.30),
             (cx, cy - r * 1.65)], style)
    sk.circle(cx, cy + r * 0.30, r, style)


def _chairlift(sk, cx, cy, scale, style="shell_dim"):
    """A tower, a rope and a chair, sized to sit inside a pin or beside a status badge."""
    sk.poly([(cx - 1.7 * scale, cy - 6.0 * scale), (cx - 0.3 * scale, cy - 6.0 * scale),
             (cx - 0.6 * scale, cy + 4.2 * scale), (cx - 1.4 * scale, cy + 4.2 * scale)],
            style)
    sk.beam((cx - 4.4 * scale, cy + 3.8 * scale), (cx + 5.6 * scale, cy + 4.6 * scale),
            1.0 * scale, "dark")
    sk.beam((cx + 3.0 * scale, cy + 4.2 * scale), (cx + 3.0 * scale, cy + 1.0 * scale),
            1.0 * scale, style)
    sk.rect(cx + 0.9 * scale, cy - 2.6 * scale, 4.4 * scale, 3.6 * scale, "accent")
    sk.rect(cx + 0.9 * scale, cy - 4.0 * scale, 4.4 * scale, 1.3 * scale, style)

# --------------------------------------------------------------------------- hud glyphs
def _g_fuel(sk):
    """A drop with a level in it. A pump silhouette needs a hose and a nozzle to read, and
    both of those are gone by 16 px."""
    sk.poly([(32, 56), (50, 30), (50, 20), (40, 8), (24, 8), (14, 20), (14, 30)], "shell")
    sk.poly([(19.5, 26), (44.5, 26), (46, 22), (44, 14), (36, 8), (28, 8), (20, 14),
             (18, 22)], "accent")
    sk.rect(17, 27, 30, 5, "dark")

def _g_wear(sk):
    sk.rect(9, 14, 46, 15, "dark")
    for i in range(4):
        sk.rect(12 + i * 11, 27, 8, 17 - i * 4, "shell")
    sk.poly([(46, 50), (56, 50), (51, 40)], "accent")


def _g_condition(sk):
    sk.arc(32, 22, 19, 18.0, 162.0, 7.0, "shell")
    sk.beam((32, 22), (43, 36), 4.5, "accent")
    sk.circle(32, 22, 5, "dark")


def _g_pqi(sk):
    sk.poly([(8, 12), (56, 12), (56, 26), (8, 21)], "shell")
    for i in range(3):
        sk.line([(13 + i * 15, 15), (13 + i * 15, 23)], "ink")
    _star(sk, 32, 42, 13, "accent")


def _g_wind(sk):
    sk.beam((10, 42), (40, 42), 5.0, "shell")
    sk.arc(40, 46, 4.5, -90.0, 150.0, 5.0, "shell")
    sk.beam((10, 30), (46, 30), 5.0, "accent")
    sk.arc(46, 34.5, 5.0, -90.0, 150.0, 5.0, "accent")
    sk.beam((10, 18), (34, 18), 5.0, "shell")


def _g_temperature(sk):
    sk.rect(26, 20, 12, 34, "shell")
    sk.circle(32, 18, 11, "shell")
    sk.rect(29, 20, 6, 24, "accent")
    sk.circle(32, 18, 7, "accent")


def _g_wetbulb(sk):
    sk.rect(28, 22, 11, 32, "shell")
    sk.circle(33, 20, 10, "shell")
    sk.rect(31, 22, 5, 22, "accent")
    sk.circle(33, 20, 6.5, "accent")
    # The wet bulb is a thermometer with a wet sock on it, so the drop is the glyph.
    sk.poly([(14, 34), (20, 24), (14, 14), (8, 24)], "glass")


def _g_queue(sk):
    for i in range(3):
        _person(sk, 14 + i * 18, 12, 30, "accent" if i == 2 else "shell")


def _g_alert(sk):
    sk.poly([(32, 54), (57, 10), (7, 10)], "accent")
    sk.rect(29, 24, 6, 18, "dark")
    sk.rect(29, 15, 6, 6, "dark")


def _g_engine(sk):
    """A block, a head and a pulley. The stepped top is what says engine rather than box."""
    sk.rect(8, 10, 34, 22, "shell")
    sk.rect(14, 30, 22, 12, "shell")
    sk.rect(20, 40, 10, 10, "dark")
    sk.rect(36, 16, 18, 8, "shell_dim")
    sk.circle(48, 20, 10, "shell")
    sk.circle(48, 20, 4, "accent")
    sk.rect(11, 32, 6, 6, "accent")

def _g_hydraulics(sk):
    sk.rect(8, 24, 28, 16, "shell")
    sk.rect(36, 29, 16, 6, "accent")
    sk.circle(53, 32, 5, "dark")
    sk.rect(12, 40, 8, 8, "dark")
    sk.line([(16, 48), (16, 54)], "ink")


def _g_drivetrain(sk):
    _gear(sk, 23, 26, 15, "shell")
    _gear(sk, 44, 42, 10, "accent", teeth=7)


def _g_tracks(sk):
    sk.poly([(12, 16), (52, 16), (52, 32), (12, 32)], "dark")
    sk.circle(16, 24, 10, "dark")
    sk.circle(48, 24, 10, "dark")
    sk.circle(16, 24, 4.5, "shell_dim")
    sk.circle(48, 24, 4.5, "shell_dim")
    for i in range(5):
        sk.rect(13 + i * 9.5, 10, 6, 5, "accent")


def _g_snow_depth(sk):
    sk.rect(6, 10, 52, 6, "shell_dim")
    sk.arc(32, 16, 20, 12.0, 168.0, 9.0, "snow")
    sk.rect(29, 18, 6, 30, "accent")
    sk.poly([(32, 54), (40, 44), (24, 44)], "accent")


def _g_density(sk):
    sk.rect(8, 10, 48, 44, "shell")
    for row in range(3):
        for col in range(3):
            r = 2.2 + row * 1.6
            sk.circle(17 + col * 15, 18 + row * 14, r, "accent")


def _g_staff(sk):
    _person(sk, 32, 10, 36, "shell")
    sk.arc(32, 37, 12, 10.0, 170.0, 5.5, "accent")
    sk.rect(18, 35, 28, 5, "accent")


def _g_money(sk):
    sk.rect(6, 16, 52, 32, "shell")
    sk.circle(32, 32, 9, "accent")
    for x, y in ((12, 22), (48, 22), (12, 40), (48, 40)):
        sk.circle(x, y, 3.0, "shell_dim")


def _g_time(sk):
    sk.circle(32, 32, 22, "shell")
    sk.circle(32, 32, 16, "glass")
    sk.beam((32, 32), (32, 45), 4.0, "dark")
    sk.beam((32, 32), (42, 28), 4.0, "accent")
    sk.circle(32, 32, 3.0, "dark")


def _g_lift_open(sk):
    _chairlift(sk, 24, 34, 3.4, "shell")
    sk.circle(47, 14, 13, "accent")
    sk.poly([(47, 22), (55, 10), (39, 10)], "dark")

def _g_lift_hold(sk):
    _chairlift(sk, 24, 34, 3.4, "shell")
    sk.circle(47, 14, 13, "accent")
    sk.rect(41, 8, 5, 13, "dark")
    sk.rect(49, 8, 5, 13, "dark")

def _g_lift_closed(sk):
    _chairlift(sk, 24, 34, 3.4, "shell")
    sk.circle(47, 14, 13, "accent")
    sk.beam((41, 8), (53, 20), 4.5, "dark")
    sk.beam((41, 20), (53, 8), 4.5, "dark")

HUD_GLYPHS = (
    ("fuel", _g_fuel, ACCENT),
    ("wear", _g_wear, WARN),
    ("condition", _g_condition, GOOD),
    ("pqi", _g_pqi, GOLD),
    ("wind", _g_wind, ACCENT),
    ("temperature", _g_temperature, BAD),
    ("wetbulb", _g_wetbulb, COLD),
    ("queue", _g_queue, ACCENT),
    ("alert", _g_alert, WARN),
    ("engine", _g_engine, ACCENT),
    ("hydraulics", _g_hydraulics, ACCENT),
    ("drivetrain", _g_drivetrain, ACCENT),
    ("tracks", _g_tracks, ACCENT),
    ("snow_depth", _g_snow_depth, ACCENT),
    ("density", _g_density, ACCENT),
    ("staff", _g_staff, GOLD),
    ("money", _g_money, GOOD),
    ("time", _g_time, ACCENT),
    ("lift_open", _g_lift_open, GOOD),
    ("lift_hold", _g_lift_hold, WARN),
    ("lift_closed", _g_lift_closed, BAD),
)


# --------------------------------------------------------------------------- markers
def _m_lift(sk):
    _pin(sk, 32, 26, 21)
    _chairlift(sk, 29, 27, 2.3, "shell")

def _m_run(sk):
    _pin(sk, 32, 26, 21)
    sk.poly([(17, 20), (45, 20), (45, 26), (17, 24)], "shell")
    sk.beam((22, 26), (22, 40), 3.2, "dark")
    sk.poly([(22, 40), (34, 36), (22, 32)], "dark")


def _m_machine(sk):
    _pin(sk, 32, 26, 21)
    sk.rect(20, 20, 22, 9, "shell")
    sk.circle(23, 24, 5, "shell")
    sk.circle(39, 24, 5, "shell")
    sk.rect(22, 29, 13, 7, "shell")
    sk.poly([(44, 18), (48, 18), (48, 34), (44, 32)], "shell")


def _m_guest(sk):
    _pin(sk, 32, 26, 21)
    _person(sk, 32, 15, 24, "shell")


def _m_hydrant(sk):
    _pin(sk, 32, 26, 21)
    sk.rect(27, 14, 10, 22, "shell")
    sk.arc(32, 34, 6, 10.0, 170.0, 4.0, "shell")
    sk.rect(19, 22, 6, 7, "shell")
    sk.rect(39, 22, 6, 7, "shell")


def _m_depot(sk):
    _pin(sk, 32, 26, 21)
    sk.rect(20, 14, 24, 16, "shell")
    sk.poly([(17, 30), (47, 30), (32, 40)], "shell")
    sk.rect(27, 14, 10, 10, "dark")


def _m_workshop(sk):
    _pin(sk, 32, 26, 21)
    sk.beam((22, 16), (42, 36), 7.0, "shell")
    sk.circle(20, 14, 6.5, "shell")
    sk.circle(20, 14, 2.8, "accent")
    sk.rect(38, 32, 8, 8, "shell")


def _m_incident(sk):
    _pin(sk, 32, 26, 21)
    sk.rect(29, 22, 6, 16, "dark")
    sk.rect(29, 14, 6, 6, "dark")


MARKERS = (
    ("lift", _m_lift, ACCENT),
    ("run", _m_run, GOOD),
    ("machine", _m_machine, GOLD),
    ("guest", _m_guest, "#4fb3d9"),
    ("hydrant", _m_hydrant, ACCENT),
    ("depot", _m_depot, "#8494ab"),
    ("workshop", _m_workshop, "#a97ce0"),
    ("incident", _m_incident, BAD),
)


# --------------------------------------------------------------------------- weather
def _sun(sk, cx, cy, r, rays=8):
    for i in range(rays):
        a = math.radians(360.0 * i / rays)
        sk.beam((cx + math.cos(a) * r * 1.25, cy + math.sin(a) * r * 1.25),
                (cx + math.cos(a) * r * 1.75, cy + math.sin(a) * r * 1.75), 4.0, "accent")
    sk.circle(cx, cy, r, "accent")


def _moon(sk, cx, cy, r):
    """A crescent as a thick arc: an outline circle minus a circle needs a boolean this
    kit does not have, and a thick C reads as a moon at every size."""
    sk.arc(cx, cy, r, 40.0, 320.0, r * 0.62, "accent")


def _fall(sk, count, maker, rows=2):
    """Precipitation under a cloud, laid out on a fixed lattice so light, moderate and
    heavy read as the same symbol with more of it rather than three different symbols."""
    slots = [(15, 16), (32, 13), (49, 16), (23, 6), (41, 6), (32, 22)]
    for i in range(min(count, len(slots))):
        maker(slots[i])


def _w_clear(sk):
    _sun(sk, 32, 32, 13)


def _w_partly_cloudy(sk):
    _sun(sk, 42, 42, 10)
    _cloud(sk, 28, 26, 42, "shell")


def _w_overcast(sk):
    _cloud(sk, 36, 36, 38, "shell_dim")
    _cloud(sk, 26, 24, 44, "shell")


def _w_snow_light(sk):
    _cloud(sk, 32, 36, 44, "shell")
    _fall(sk, 2, lambda p: _flake(sk, p[0], p[1], 5.5))


def _w_snow_moderate(sk):
    _cloud(sk, 32, 36, 44, "shell")
    _fall(sk, 4, lambda p: _flake(sk, p[0], p[1], 5.5))


def _w_snow_heavy(sk):
    _cloud(sk, 32, 38, 46, "shell")
    _fall(sk, 6, lambda p: _flake(sk, p[0], p[1], 5.5))


def _w_rain(sk):
    _cloud(sk, 32, 36, 44, "shell")
    for x in (18, 32, 46):
        sk.beam((x + 4, 22), (x - 3, 6), 4.5, "accent")


def _w_fog(sk):
    _cloud(sk, 32, 40, 42, "shell")
    for i, y in enumerate((22, 14, 6)):
        sk.beam((10 + i * 3, y), (54 - i * 3, y), 5.0, "accent")


def _w_wind(sk):
    _g_wind(sk)


def _w_blowing_snow(sk):
    sk.beam((8, 42), (40, 42), 5.0, "shell")
    sk.arc(40, 46, 4.5, -90.0, 150.0, 5.0, "shell")
    sk.beam((8, 28), (44, 28), 5.0, "shell")
    for x, y in ((18, 16), (34, 12), (48, 18)):
        _flake(sk, x, y, 5.5)


def _w_night_clear(sk):
    _moon(sk, 34, 32, 16)
    _star(sk, 14, 48, 5, "accent")


def _w_night_snow(sk):
    _moon(sk, 42, 46, 11)
    _cloud(sk, 30, 32, 40, "shell")
    _fall(sk, 3, lambda p: _flake(sk, p[0], p[1], 5.0))


WEATHER = (
    ("clear", _w_clear, GOLD),
    ("partly_cloudy", _w_partly_cloudy, GOLD),
    ("overcast", _w_overcast, PALE),
    ("snow_light", _w_snow_light, COLD),
    ("snow_moderate", _w_snow_moderate, COLD),
    ("snow_heavy", _w_snow_heavy, COLD),
    ("rain", _w_rain, ACCENT),
    ("fog", _w_fog, PALE),
    ("wind", _w_wind, ACCENT),
    ("blowing_snow", _w_blowing_snow, COLD),
    ("night_clear", _w_night_clear, PALE),
    ("night_snow", _w_night_snow, PALE),
)


# --------------------------------------------------------------------------- ui sheets
# Border insets for the 9-slice sheets, in pixels of the sheet itself. A sheet is written
# at 1x so one sheet pixel is one reference pixel in the canvas scaler UiFactory sets up,
# and the insets clear the corner radius and the frame stroke with room to spare. The
# button inset stays under half of UiFactory's 26 px button height, so a button never has
# to squash its own borders.
PANEL_INSET = 16
BUTTON_INSET = 12

# Fills from Assets/Scripts/Unity/UI/UiFactory.cs, so a generated sheet dropped behind a
# panel matches the flat colour the same panel has today.
UI_SHEETS = (
    ("ui_panel_normal", "#12171f", 0.88, "#2b3543", PANEL_INSET, None),
    ("ui_panel_raised", "#242b38", 0.95, "#3c4859", PANEL_INSET, ("top", "#5a6a80")),
    ("ui_panel_sunken", "#0b0f15", 0.90, "#242c38", PANEL_INSET, ("bottom", "#39455a")),
    ("ui_button_normal", "#33475f", 1.00, "#4d6280", BUTTON_INSET, ("top", "#5f7a9c")),
    ("ui_button_hover", "#3d546e", 1.00, "#6a88ad", BUTTON_INSET, ("top", "#87a5ca")),
    ("ui_button_pressed", "#29394c", 1.00, "#1d2836", BUTTON_INSET, ("bottom", "#4a5f7b")),
    ("ui_button_disabled", "#3a424e", 0.55, "#4a5460", BUTTON_INSET, None),
)


def _sheet_svg(fill, opacity, border, inset, highlight):
    """One 9-slice sheet: a rounded frame whose detail all lands inside the border insets,
    so stretching the middle never stretches anything that has to keep its shape."""
    pad = STROKE * 0.5
    span = GRID - STROKE
    parts = ['<rect x="%.2f" y="%.2f" width="%.2f" height="%.2f" rx="%.2f" fill="%s" '
             'fill-opacity="%.2f" stroke="%s" stroke-width="%.2f"/>'
             % (pad, pad, span, span, RADIUS, fill, opacity, border, STROKE)]
    if highlight:
        edge, colour = highlight
        y = pad + STROKE * 0.9 if edge == "top" else GRID - pad - STROKE * 1.9
        parts.append('<rect x="%.2f" y="%.2f" width="%.2f" height="%.2f" fill="%s" '
                     'fill-opacity="0.55"/>'
                     % (RADIUS + STROKE, y, GRID - (RADIUS + STROKE) * 2.0, STROKE * 0.5,
                        colour))
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" '
            'viewBox="0 0 %d %d">\n%s\n</svg>\n'
            % (GRID, GRID, GRID, GRID, "\n".join(parts)))


def _write_png(svg, out_dir, name, px):
    data = cairosvg.svg2png(bytestring=svg.encode("utf-8"), output_width=px,
                            output_height=px)
    image = Image.open(io.BytesIO(data)).convert("RGBA")
    validate.check_texture(name, image, expect_size=px, expect_channels=4)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".png")
    image.save(path, format="PNG", optimize=True)
    return path


def build_sheets(out_dir):
    """The panel and button sheets, plus the sidecar that tells uGUI where to slice them.

    The sidecar exists because Sprite.border is set in code and nothing in a PNG carries
    it: without the file the UI side would be guessing insets that have to match the
    corner radius exactly or every panel corner smears.
    """
    records = []
    slices = {}
    for name, fill, opacity, border, inset, highlight in UI_SHEETS:
        path = _write_png(_sheet_svg(fill, opacity, border, inset, highlight), out_dir,
                          name, GRID)
        slices[name] = {"left": inset, "bottom": inset, "right": inset, "top": inset}
        records.append({
            "id": name,
            "path": config.rel_to_root(path),
            "resource": config.resource_path(path),
            "kind": "icon",
            "group": "ui",
            "size": GRID,
            "scale": 1,
            "border": slices[name],
        })
    sidecar = os.path.join(out_dir, "ui_slices.json")
    with open(sidecar, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"pixelsPerUnit": 100, "grid": GRID, "sheets": slices}, f, indent=1,
                  sort_keys=True)
        f.write("\n")
    records.append({
        "id": "ui_slices",
        "path": config.rel_to_root(sidecar),
        "resource": config.resource_path(sidecar),
        "kind": "data",
        "group": "ui",
    })
    return records


# --------------------------------------------------------------------------- build
def glyphs():
    """Every authored glyph as (asset name, sketch, accent, group), in draw order."""
    out = []
    for prefix, table in (("hud", HUD_GLYPHS), ("marker", MARKERS), ("weather", WEATHER)):
        for name, draw, accent in table:
            sketch = Sketch()
            draw(sketch)
            out.append(("%s_%s" % (prefix, name), sketch, accent, prefix))
    return out


def build_all(out_dir):
    """Every HUD glyph, marker, weather symbol and UI sheet, in one deterministic order."""
    records = []
    for name, sketch, accent, group in glyphs():
        records += rasterise(document(sketch, accent, fit=False), out_dir, name, group)
    records += build_sheets(out_dir)
    audit(records)
    return records


def audit(records):
    """Every glyph this module declares reached disk at every scale."""
    have = set((r["id"]) for r in records)
    missing = []
    for name, _sketch, _accent, _group in glyphs():
        for scale in config.ICON_SCALES:
            if "%s@%dx" % (name, scale) not in have:
                missing.append("%s@%dx" % (name, scale))
    for sheet in UI_SHEETS:
        if sheet[0] not in have:
            missing.append(sheet[0])
    if missing:
        raise validate.ContractError("hud: %d glyphs produced no image: %s"
                                     % (len(missing), ", ".join(missing[:8])))
    return len(have)


if __name__ == "__main__":
    import sys

    target = sys.argv[1] if len(sys.argv) > 1 else config.ICONS_DIR
    validate.reset()
    written = build_all(target)
    audit(written)
    print("%d images, %d records" % (len(written), len(written)))
    for issue in validate.issues():
        print("warning [%s] %s: %s" % (issue["kind"], issue["asset"], issue["message"]))
