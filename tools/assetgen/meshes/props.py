"""Scenery: everything on the mountain that is neither a machine nor a lift.

Signage, fencing, the queue maze, the lift operator's shack, the lodge shells, trees,
rocks and the small furniture of a base area. Almost none of it has a record in
Assets/StreamingAssets/Data, so where a real number exists this module reads it and
where none does it carries its own measured dimension with the practice it comes from:

    climate.json      how deep the season's pack gets, which is what decides how far a
                      marker pole, a snow stake, a snow fence and an avalanche net have
                      to stand out of the ground to still be there in March
    scenarios.json    the piste grades actually used (one difficulty blade per grade),
                      the longest run name (a name board is sized to the name it
                      carries) and the parking lots at the base
    tuning.json       snow densities for the settled-pack figure, and the guest numbers
                      that size the base lodge
    stations.json     hydrant spacing, reported on the sections the view arrays

The base lodge is the clearest case. Its footprint is not a number somebody liked: the
lots in the scenario hold so many cars, `guests.guestsPerCar` fills them, that many
guests eat with probability `guests.lunchProbPerVisit` inside the lunch window, and the
window fits `lunchMinutes` sittings, so the room needs a known number of seats and a
seat needs a known area. Widen a lot in scenarios.json and the lodge grows on the next
build.

Tileable sections
-----------------
`fence_snow_section`, `net_a_section`, `maze_rail`, `rope_line_stanchion` and
`bamboo_boundary` are SECTIONS, not finished fences. Each is built along the model's +Z
axis, centred on the origin, and carries its LEADING post only: the post sits at
z = -sectionLengthM / 2 and the rails, slats and netting run out to z = +sectionLengthM / 2
where the next instance's post picks them up. So the view places instance k at
`start + forward * sectionLengthM * k`, all with the same rotation, and the run comes out
evenly posted with no doubled posts at the seams. It caps the far end with one extra
instance, or leaves the last span open, which is what a real fence line does anyway. The
step is published as `sectionLengthM` on every one of those manifest records.

Articulation
------------
docs/ART_CONTRACT.md names transforms for machines and lifts and says nothing about
scenery, so the props that move publish three names of their own: `gate_swing` on the
closure gate and on the maze gate (rotates about Y at the hinge), `sock_yaw` on the wind
sock (yaws about Y) and `anemometer` on the weather station (spins about Y). They are
optional in exactly the way the contract means: bound if a view wants them, ignored
otherwise. Nothing else here articulates, and the family publishes no required
transform, which is what `prop` means in validate.REQUIRED_NODES.
"""
import math
import os

from lib import datasrc, export
from lib import meshkit as mk
from lib.meshkit import BODY, GLASS, METAL

# Parts that butt against each other are overlapped by this much instead. meshkit welds
# vertices closer than a hundredth of a millimetre, and two boxes meeting on an exact
# plane would weld into an edge carrying four faces, which is the non-manifold failure
# in validate.py.
LAP = 0.004

# --------------------------------------------------------------------------- catalogue
# Dimensions of the scenery no JSON record describes. Every figure is what a resort
# actually buys, not what happened to look right in the viewport: a sign post is a
# 100 mm galvanised tube because that is the stock size a sign shop welds blades to, a
# queue stanchion is 1.0 m because that is belt height, a portable cabin is 1.2 m square
# because that is what fits on a trailer.
POST_D = 0.100              # trail sign post, 100 mm galvanised tube
POST_OUT = 2.40             # sign post standing height out of the ground
BLADE_T = 0.002             # 2 mm folded sheet; the fold is a texture, not geometry
BOARD_T = 0.030             # aluminium composite board on a folded frame
GATE_SPAN = 3.20            # closure gate clear width, one snowmobile plus margin
MAZE_POST_H = 1.00          # queue belt height
MAZE_BAY = 2.00             # one maze rail section
SHACK_L, SHACK_W = 2.40, 2.00       # lift operator hut, a two-person heated box
SHACK_EAVE = 2.30
PATROL_L, PATROL_W = 6.00, 4.20     # patrol hut: toboggan bay plus a warm room
SEAT_AREA_M2 = 1.60         # dining area per seat, self-service layout
BACK_OF_HOUSE = 2.00        # kitchen, servery, boot room, toilets and circulation
LODGE_ASPECT = 3.00         # a lodge is a long facade to the plaza, not a cube
STALL_AREA_M2 = 48.0        # per car in a resort lot: stall, aisle and snow storage

# What a difficulty blade looks like. The grades come from the piste records, so the
# table is keyed by the sim's own PisteDifficulty names; anything new in the data gets a
# plain rectangular blade, which is what a sign shop makes for a run with no grade yet.
BLADE_SHAPES = {
    "Green": ("disc", 1, 0.40),
    "Blue": ("disc", 1, 0.40),
    "Red": ("disc", 1, 0.40),
    "Black": ("diamond", 1, 0.46),
    "DoubleBlack": ("diamond", 2, 0.34),
    "Nordic": ("plate", 1, 0.42),
    "Park": ("disc", 1, 0.44),
}

# The extreme-terrain blade the art brief asks for. PisteDifficulty has no such grade,
# so no run can carry it yet; the blade is built anyway so the sign family is complete
# the day one does.
EXTRA_GRADES = ("DoubleBlack",)


# --------------------------------------------------------------------------- numbers
def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def _seg(radius):
    """Sides on a revolved part, from how big it reads on screen.

    A 0.5 m lodge chimney flue earns more sides than an 18 mm bamboo pole, and the small
    stuff is where a procedural pipeline throws its budget away.
    """
    return int(_clamp(6.0 + radius * 14.0, 6.0, 16.0))


def _tune(tuning, key):
    """A dotted tuning key, read the way TuningData reads it: a missing key throws."""
    node = tuning
    for part in key.split("."):
        node = node[part]
    return float(node["value"])


def _default_scenario():
    data = datasrc.load("scenarios")
    scenario = datasrc.by_id(data["scenarios"], data["default"])
    if scenario is None:
        raise KeyError("scenarios.json names a default scenario that does not exist: "
                       + str(data["default"]))
    return scenario


def _design_snowpack(climate, tuning):
    """How deep the settled pack gets in a normal season, in metres.

    Every piece of scenery that has to stay visible in March is proportioned from this:
    a piste marker buried to its tip is worse than no marker at all. Precipitation that
    falls with the period mean above freezing runs off instead of lying, and what does
    lie falls at the period's snow-to-liquid ratio and then settles from fresh density
    to settled density.
    """
    periods = climate["Periods"]
    season_days = float(climate["SeasonLengthDays"])
    cold_ratio = float(climate["SnowToLiquidRatioCold"])
    warm_ratio = float(climate["SnowToLiquidRatioWarm"])
    fresh_m = 0.0
    for i, period in enumerate(periods):
        start = float(period["SeasonDayStart"])
        end = float(periods[i + 1]["SeasonDayStart"]) if i + 1 < len(periods) else season_days
        mean_c = float(period["MeanTempC"])
        if mean_c >= 0.0:
            continue
        water_mm = (end - start) * float(period["PrecipDayProbability"]) \
            * float(period["PrecipMmPerDayMean"])
        cold = _clamp(-mean_c / 8.0, 0.0, 1.0)
        fresh_m += water_mm * (warm_ratio + (cold_ratio - warm_ratio) * cold) / 1000.0
    settled = fresh_m * (_tune(tuning, "snow.freshDensityKgM3")
                         / _tune(tuning, "snow.settledLooseDensity"))
    return _clamp(settled, 0.8, 2.5)


def _mean_wind(climate):
    periods = climate["Periods"]
    return sum(float(p["WindMeanKmh"]) for p in periods) / max(1, len(periods))


def _lunch_seats(scenario, tuning):
    """Seats the base lodge has to hold at the peak of the lunch window.

    The chain is the sim's own: the lots hold cars, a car brings `guestsPerCar` guests,
    that fraction of them eats, and the window is long enough for so many sittings.
    """
    area = 0.0
    for zone in scenario["Zones"]:
        if zone.get("Kind") != "Lot":
            continue
        radius = float(zone.get("RadiusM") or 0.0)
        area += math.pi * radius * radius
    cars = area / STALL_AREA_M2
    guests = cars * _tune(tuning, "guests.guestsPerCar")
    window_min = (_tune(tuning, "guests.lunchToHour")
                  - _tune(tuning, "guests.lunchFromHour")) * 60.0
    sittings = max(1.0, window_min / _tune(tuning, "guests.lunchMinutes"))
    return guests * _tune(tuning, "guests.lunchProbPerVisit") / sittings


def _plan_from_seats(seats, storeys, aspect=LODGE_ASPECT, back=BACK_OF_HOUSE):
    """Footprint of a building that has to seat `seats` people on `storeys` floors."""
    floor_area = seats * SEAT_AREA_M2 * back / max(1, storeys)
    length = _clamp(math.sqrt(max(20.0, floor_area) * aspect), 12.0, 44.0)
    return length, length / aspect


def _grades(scenario):
    """Piste grades in use, in file order, plus the blades the brief asks for on top."""
    order = []
    for piste in scenario["Pistes"]:
        grade = str(piste.get("Difficulty") or "Blue")
        if grade not in order:
            order.append(grade)
    for grade in EXTRA_GRADES:
        if grade not in order:
            order.append(grade)
    return order


def _longest_name(scenario):
    return max([len(str(p.get("Name") or "")) for p in scenario["Pistes"]] + [6])


def _site():
    """Every number the scenery is proportioned from, read once per build."""
    tuning = datasrc.tuning()
    climate = datasrc.load("climate")
    scenario = _default_scenario()
    seats = _lunch_seats(scenario, tuning)
    lodge_l, lodge_w = _plan_from_seats(seats, 2)
    # A mid-mountain restaurant serves fewer covers than the base and runs almost no
    # back of house: no rental, no ski school, no boot room, one delivery a day.
    mid_l, mid_w = _plan_from_seats(seats * 0.45, 1, aspect=2.4, back=1.5)
    snow = _design_snowpack(climate, tuning)
    return {
        "snow_m": snow,
        "wind_kmh": _mean_wind(climate),
        "grades": _grades(scenario),
        "name_chars": _longest_name(scenario),
        "hydrant_spacing_m": float(datasrc.stations()["HydrantSpacingM"]),
        "seats": seats,
        "lodge": (lodge_l, lodge_w),
        "mid": (mid_l, mid_w),
        # A marker has to be read from up the hill with a full pack underfoot, a stake
        # has to be graduated past the deepest it ever gets, and a fence that drifts
        # over stops catching snow.
        "marker_h": _clamp(snow + 1.2, 2.2, 3.6),
        "stake_h": _clamp(math.ceil((snow + 0.4) * 2.0) / 2.0, 1.5, 3.0),
        "fence_h": _clamp(snow, 1.4, 2.6),
        "net_h": _clamp(snow + 1.2, 3.0, 5.0),
        "bamboo_h": _clamp(snow * 0.6 + 1.4, 2.0, 3.2),
    }


# --------------------------------------------------------------------------- parts
def _post(m, x, z, height, radius, mat=METAL, segments=None, parent=None):
    """A round post standing on the ground at (x, z)."""
    return m.cylinder((x, height * 0.5, z), radius, height, axis=1,
                      segments=segments or _seg(radius), mat=mat, parent=parent)


def _flange(m, x, z, radius, parent=None):
    """The bolted base plate every steel post in a resort actually stands on."""
    m.cylinder((x, 0.012, z), radius, 0.024, axis=1, segments=_seg(radius), mat=METAL,
               parent=parent)
    m.cylinder((x, 0.10, z), radius * 0.62, 0.16, axis=1, segments=_seg(radius),
               mat=METAL, parent=parent)


def _clamp_band(m, y, post_r=POST_D * 0.5, parent=None):
    """The band clamp a blade is actually held to a post with, and its two bolts."""
    m.tube((0.0, y, 0.0), post_r * 1.22, post_r * 1.04, 0.045, axis=1, segments=10,
           mat=METAL, parent=parent)
    for side in (-1.0, 1.0):
        m.box((side * post_r * 1.3, y, 0.0), (post_r * 0.5, 0.035, 0.035), mat=METAL,
              parent=parent, bevel=False)


def _disc_profile(radius, sides=12):
    return [(math.cos(2.0 * math.pi * i / sides) * radius,
             math.sin(2.0 * math.pi * i / sides) * radius) for i in range(sides)]


def _diamond_profile(size):
    h = size * 0.5
    return [(0.0, h), (h, 0.0), (0.0, -h), (-h, 0.0)]


def _rect_profile(width, height):
    hw, hh = width * 0.5, height * 0.5
    return [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)]


def _chamfered_plan(length, width, corner):
    """A rectangle with its corners cut off, as a footprint polygon in (x, z).

    Lodges are not boxes: the corners get cut for the entry, the servery bay or simply
    because a timber frame with a 45 degree corner sheds snow better than a right angle.
    """
    hl, hw, c = length * 0.5, width * 0.5, min(corner, length * 0.25, width * 0.25)
    return [(-hl + c, -hw), (hl - c, -hw), (hl, -hw + c), (hl, hw - c),
            (hl - c, hw), (-hl + c, hw), (-hl, hw - c), (-hl, -hw + c)]


def _scaled_plan(plan, sx, sz, y):
    """A footprint polygon as a loft ring at one height."""
    return [(x * sx, y, z * sz) for x, z in plan]


def _railing(m, x0, x1, z, top_y, base_y, parent=None, balusters=48):
    """Deck railing: newel posts, a top rail, a bottom rail and a baluster field.

    The baluster field is capped rather than spaced to code. A thirty metre deck at code
    spacing is two hundred balusters and two thousand triangles for a building that is
    read from the far side of the valley, so the field thins out as the deck grows and
    the spacing is what is left over.

    Everything repeated is built unchamfered, because meshkit's array copies the faces
    it is handed and a bevel replaces those faces.
    """
    span = abs(x1 - x0)
    if span < 0.2:
        return
    posts = max(2, int(span / 2.2) + 1)
    step = span / max(1, posts - 1)
    newel = m.box((min(x0, x1), base_y + (top_y - base_y) * 0.5, z),
                  (0.09, top_y - base_y, 0.09), mat=METAL, parent=parent, bevel=False)
    m.array(newel, posts, (step, 0.0, 0.0), parent=parent)
    m.box((0.5 * (x0 + x1), top_y, z), (span, 0.075, 0.10), mat=BODY, parent=parent)
    m.box((0.5 * (x0 + x1), base_y + 0.12, z), (span, 0.05, 0.06), mat=METAL,
          parent=parent, bevel=False)
    count = max(2, min(balusters, int(span / 0.14)))
    gap = span / count
    baluster = m.box((min(x0, x1) + gap * 0.5, base_y + (top_y - base_y) * 0.5, z),
                     (0.035, top_y - base_y - 0.14, 0.035), mat=METAL, parent=parent,
                     bevel=False)
    m.array(baluster, count, (gap, 0.0, 0.0), parent=parent)


def _window_band(m, x0, x1, z, sill_y, height, count, parent=None):
    """A glazed band with real frames: a mullion field is what makes a wall read as a
    building rather than a shed with a hole in it."""
    span = abs(x1 - x0)
    if span < 0.4 or count < 1:
        return
    pitch = span / count
    pane_w = pitch * 0.78
    cx = min(x0, x1) + pitch * 0.5
    cy = sill_y + height * 0.5
    pane = m.box((cx, cy, z), (pane_w, height, 0.02), mat=GLASS, parent=parent,
                 bevel=False)
    m.array(pane, count, (pitch, 0.0, 0.0), parent=parent)
    frame = []
    reveal = 0.10                 # how far the surround stands proud of the wall
    frame += m.box((cx, sill_y - 0.05, z), (pane_w + 0.10, 0.10, reveal), mat=BODY,
                   parent=parent, bevel=False)
    frame += m.box((cx, sill_y + height + 0.05, z), (pane_w + 0.10, 0.10, reveal),
                   mat=BODY, parent=parent, bevel=False)
    for side in (-1.0, 1.0):
        frame += m.box((cx + side * (pane_w * 0.5 + 0.05), cy, z),
                       (0.10, height + 0.20, reveal), mat=BODY, parent=parent,
                       bevel=False)
    m.array(frame, count, (pitch, 0.0, 0.0), parent=parent)


# --------------------------------------------------------------------------- signage
def _sign_post(m, x=0.0, height=POST_OUT, radius=POST_D * 0.5):
    """The post every piece of trail signage stands on, with its ground flange."""
    _post(m, x, 0.0, height, radius, segments=10)
    _flange(m, x, 0.0, radius * 2.1)
    # The pressed cap sits over the tube end, overlapped by LAP: a cap butted onto the
    # top ring would weld into an edge carrying four faces.
    m.cylinder((x, height + 0.012 - LAP, 0.0), radius * 1.08, 0.024, axis=1, segments=10,
               mat=METAL)


def _blade(m, grade, y, z=POST_D * 0.5 + BLADE_T):
    """One difficulty blade, shaped by the grade it marks."""
    shape, count, size = BLADE_SHAPES.get(grade, ("plate", 1, 0.42))
    spread = size * 0.58
    for i in range(count):
        x = (i - (count - 1) * 0.5) * (spread * 2.0)
        if shape == "disc":
            profile = _disc_profile(size * 0.5, 12)
        elif shape == "diamond":
            profile = _diamond_profile(size)
        else:
            profile = _rect_profile(size * 1.35, size * 0.72)
        m.prism(profile, (x, y, z), BLADE_T, mat=BODY, axis=2)
    for band in (-1.0, 1.0):
        _clamp_band(m, y + band * size * 0.34)


def _trail_sign(m, site, grade="Blue"):
    """A difficulty blade on a post: the one sign every junction on the hill carries."""
    _sign_post(m)
    shape, count, size = BLADE_SHAPES.get(grade, ("plate", 1, 0.42))
    head = POST_OUT - size * 0.62
    _blade(m, grade, head)
    # Under the blade goes the direction arrow, pointing the way the run leaves.
    arrow_w = 0.62
    arrow = [(-arrow_w * 0.5, -0.10), (arrow_w * 0.30, -0.10), (arrow_w * 0.5, 0.0),
             (arrow_w * 0.30, 0.10), (-arrow_w * 0.5, 0.10)]
    m.prism(arrow, (arrow_w * 0.10, head - size * 0.75, POST_D * 0.5 + BLADE_T),
            BLADE_T, mat=BODY, axis=2)
    return {"grade": grade, "postHeightM": POST_OUT}


def _run_name_board(m, site):
    """A name board, sized to the longest run name in the data plus its margins.

    A sign shop sets 80 mm letters at about 55 mm of advance each and leaves 120 mm of
    margin at both ends, so the board is as long as the name makes it.
    """
    width = _clamp(site["name_chars"] * 0.055 + 0.24, 0.80, 1.80)
    height = 0.34
    top = POST_OUT - 0.10
    _sign_post(m)
    face = POST_D * 0.5 + BOARD_T * 0.5
    m.box((0.0, top - height * 0.5, face), (width, height, BOARD_T), mat=BODY)
    m.box((0.0, top - height * 0.5, face - BOARD_T), (width + 0.04, height + 0.04, 0.012),
          mat=METAL, bevel=False)
    # The grade disc sits at the left end of the board, the way a piste map keys it.
    m.prism(_disc_profile(height * 0.36, 12),
            (-width * 0.5 + height * 0.42, top - height * 0.5, face + BOARD_T * 0.5 + BLADE_T),
            BLADE_T, mat=BODY, axis=2)
    for band in (-1.0, 1.0):
        _clamp_band(m, top - height * 0.5 + band * height * 0.32)
    return {"boardWidthM": round(width, 3)}


def _slow_zone_sign(m, site):
    """A slow-zone diamond: the loudest sign on the hill, so it is the biggest blade."""
    size = 0.70
    head = POST_OUT - size * 0.55
    _sign_post(m)
    face = POST_D * 0.5 + BLADE_T
    m.prism(_diamond_profile(size), (0.0, head, face), BLADE_T, mat=BODY, axis=2)
    m.prism(_diamond_profile(size * 0.86), (0.0, head, face + BLADE_T), BLADE_T,
            mat=METAL, axis=2)
    # A pennant under the diamond: it moves in the wind and catches the eye first.
    m.prism([(-0.02, 0.0), (0.44, -0.09), (0.44, 0.09)],
            (0.02, head - size * 0.62, face), BLADE_T, mat=BODY, axis=2)
    for band in (-1.0, 1.0):
        _clamp_band(m, head + band * size * 0.22)
    return {}


def _closure_gate(m, site):
    """A swinging closure gate across a run entrance, with its CLOSED panel.

    `gate_swing` pivots about Y at the hinge post, so a view can stand it open when the
    run is open and swing it across when patrol closes the run.
    """
    half = GATE_SPAN * 0.5
    post_r = 0.060
    height = 1.70
    for side in (-1.0, 1.0):
        _post(m, side * half, 0.0, height, post_r, segments=10)
        _flange(m, side * half, 0.0, post_r * 2.0)
        m.cylinder((side * half, height + 0.015 - LAP, 0.0), post_r * 1.10, 0.03,
                   axis=1, segments=10, mat=METAL)
    m.node("gate_swing", pivot=(-half, 0.0, 0.0))
    arm_y = 1.05
    arm_len = GATE_SPAN - post_r * 2.0
    m.cylinder((-half + arm_len * 0.5 + post_r, arm_y, 0.0), 0.035, arm_len, axis=0,
               segments=8, mat=METAL, parent="gate_swing")
    m.cylinder((-half + arm_len * 0.5 + post_r, arm_y - 0.42, 0.0), 0.028, arm_len,
               axis=0, segments=8, mat=METAL, parent="gate_swing")
    panel_w = GATE_SPAN * 0.42
    m.box((0.0, arm_y - 0.20, 0.02), (panel_w, 0.46, BOARD_T), mat=BODY,
          parent="gate_swing")
    m.box((0.0, arm_y - 0.20, 0.02 - BOARD_T), (panel_w + 0.04, 0.50, 0.012), mat=METAL,
          parent="gate_swing", bevel=False)
    # Hinge barrel and latch keeper, the two details that say the gate really swings.
    m.cylinder((-half + post_r + 0.04, arm_y, 0.0), 0.05, 0.22, axis=1, segments=8,
               mat=METAL, parent="gate_swing")
    m.box((half - post_r - 0.06, arm_y, 0.0), (0.14, 0.10, 0.10), mat=METAL)
    return {"clearWidthM": GATE_SPAN}


def _map_board(m, site):
    """A piste map on two posts under a small snow roof, tilted back to kill glare."""
    board_w, board_h = 2.20, 1.40
    bottom = 1.00
    tilt = mk.unity_euler(-14.0, 0.0, 0.0)
    spacing = board_w * 0.62
    for side in (-1.0, 1.0):
        _post(m, side * spacing * 0.5, 0.0, bottom + board_h * 0.96, 0.055, segments=10)
        _flange(m, side * spacing * 0.5, 0.0, 0.12)
    mid = bottom + board_h * 0.5
    m.box((0.0, mid, 0.10), (board_w, board_h, BOARD_T), mat=BODY, rot=tilt)
    m.box((0.0, mid, 0.10 - BOARD_T), (board_w + 0.06, board_h + 0.06, 0.014),
          mat=METAL, rot=tilt, bevel=False)
    # Snow roof with a real overhang, or the map is unreadable by January.
    roof_y = bottom + board_h + 0.14
    m.wedge((0.0, roof_y, 0.18), (board_w + 0.24, 0.16, 0.70), mat=METAL, taper=0.35)
    for side in (-1.0, 1.0):
        m.beam((side * spacing * 0.5, roof_y - 0.34, -0.02),
               (side * spacing * 0.5, roof_y - 0.02, 0.34), 0.05, mat=METAL)
    return {"boardWidthM": board_w}


def _lift_status_sign(m, site):
    """The board at the bottom of a lift: header, status lamps, a wait-time slot."""
    board_w, board_h = 0.95, 1.30
    bottom = 0.95
    spacing = board_w * 0.6
    for side in (-1.0, 1.0):
        _post(m, side * spacing * 0.5, 0.0, bottom + board_h * 0.92, 0.05, segments=10)
        _flange(m, side * spacing * 0.5, 0.0, 0.11)
    mid = bottom + board_h * 0.5
    m.box((0.0, mid, 0.08), (board_w, board_h, BOARD_T), mat=BODY)
    m.box((0.0, bottom + board_h - 0.16, 0.08 + BOARD_T * 0.6), (board_w * 0.94, 0.24, 0.012),
          mat=METAL, bevel=False)
    # Three lamps down the side: running, on hold, closed. The lamp that is lit is a
    # material property at runtime, so all three are modelled.
    for i in range(3):
        y = mid + 0.22 - i * 0.22
        m.cylinder((-board_w * 0.5 + 0.14, y, 0.08 + BOARD_T * 0.5 + 0.02), 0.045, 0.05,
                   axis=2, segments=10, mat=GLASS)
        # The bezel clears the lens by a hair; sharing its radius would weld the two
        # into one non-manifold shell.
        m.tube((-board_w * 0.5 + 0.14, y, 0.08 + BOARD_T * 0.5 + 0.015), 0.058, 0.048,
               0.04, axis=2, segments=10, mat=METAL)
    m.box((0.06, mid - 0.30, 0.08 + BOARD_T * 0.6), (board_w * 0.62, 0.30, 0.014),
          mat=METAL, bevel=False)
    return {"boardWidthM": board_w}


# --------------------------------------------------------------------------- fencing
def _snow_fence(m, site):
    """One 4 m bay of drift fence: a leading post, a brace and a slat field.

    A drift fence works by porosity. Solid, it scours its own base; too open, it catches
    nothing. Around half open is the figure that has held since the Wyoming trials, and
    a windier site is built a little tighter, so the number of boards comes out of the
    season's mean wind rather than a modelling decision.
    """
    length = 4.00
    height = site["fence_h"]
    porosity = _clamp(0.55 - site["wind_kmh"] / 200.0, 0.40, 0.55)
    slat_w = 0.15
    bottom = height * 0.14              # the gap that keeps the fence from burying itself
    z0 = -length * 0.5
    m.box((0.0, height * 0.5, z0), (0.10, height, 0.10), mat=BODY)
    m.box((0.0, height + 0.03, z0), (0.14, 0.06, 0.14), mat=METAL, bevel=False)
    # Braced both ways, which is what a free-standing panel needs: the wind that builds
    # the drift comes from one side and the drift itself pushes back from the other.
    for side in (-1.0, 1.0):
        m.beam((0.0, height * 0.86, z0), (side * height * 0.55, 0.02,
                                          z0 + height * 0.30), 0.08, mat=BODY)
        m.box((side * height * 0.55, 0.05, z0 + height * 0.30), (0.30, 0.10, 0.30),
              mat=METAL)
    pitch = slat_w / max(0.15, 1.0 - porosity)
    count = max(3, int((height - bottom) / pitch))
    slat = m.box((0.0, bottom + slat_w * 0.5, 0.0), (0.025, slat_w, length), mat=BODY,
                 bevel=False)
    m.array(slat, count, (0.0, pitch, 0.0))
    return {"sectionLengthM": length, "heightM": round(height, 2),
            "porosity": round(porosity, 3), "slats": count}


def _a_net(m, site):
    """One bay of avalanche snow net: a raked pole, a net panel and its anchors.

    Net height follows the pack, because a net is only a defence for the snow it stands
    above: the sim's own climate record says how deep that is.
    """
    length = 3.50
    height = site["net_h"]
    rake = 18.0                          # the pole leans downhill, so the net faces the slip
    z0 = -length * 0.5
    # The fence line runs along +Z and the slope falls along +X, so the rake and the
    # guys are perpendicular to the line the sections are arrayed on.
    lean = math.tan(math.radians(rake)) * height
    # The section straddles its own footprint: the anchor reaches as far uphill as the
    # rake reaches downhill, so a row of them arrays on the fence line, not beside it.
    foot_x = (height * 0.55 - lean) * 0.5
    top = (foot_x + lean, height, z0)
    m.beam((foot_x, 0.0, z0), top, 0.14, mat=METAL, square=False, segments=10)
    # The pole is padded where a skier can hit it, which is the bottom two metres.
    m.cylinder((foot_x + lean * 0.30, 0.90, z0), 0.13, 1.8, axis=2, segments=10,
               mat=BODY, rot=mk.look_rotation((lean, height, 0.0)))
    m.box((foot_x, 0.06, z0), (0.44, 0.12, 0.44), mat=METAL, bevel=False)
    # Uphill guy into a ground anchor. The real anchor is eight metres of rope grouted
    # into rock; what is modelled is the plate it comes out of.
    anchor = (foot_x - height * 0.55, 0.05, z0)
    m.beam(top, anchor, 0.035, mat=METAL, square=False, segments=6)
    m.box(anchor, (0.30, 0.10, 0.30), mat=METAL)
    # Net: a rhombic field between the top cable and the ground cable. Four strands each
    # way is enough to read as netting once the texture is on it.
    top_x, bot_x = top[0], foot_x
    m.beam((top_x, height, z0), (top_x, height, z0 + length), 0.045, mat=METAL,
           square=False, segments=6)
    m.beam((bot_x, 0.10, z0), (bot_x, 0.10, z0 + length), 0.045, mat=METAL,
           square=False, segments=6)
    strands = 4
    for i in range(strands):
        t = (i + 0.5) / strands
        a = (top_x, height, z0 + length * t)
        for other in (min(1.0, t + 0.5), max(0.0, t - 0.5)):
            m.beam(a, (bot_x, 0.10, z0 + length * other), 0.022, mat=METAL,
                   square=False, segments=4)
    return {"sectionLengthM": length, "heightM": round(height, 2)}


def _rope_stanchion(m, site):
    """A rope-line stanchion: the pole a boundary rope is clipped to, one per span."""
    span = 4.00
    height = 1.15
    prof = [(0.16, 0.0), (0.16, 0.03), (0.055, 0.10), (0.045, height * 0.55),
            (0.042, height - 0.14), (0.055, height - 0.10), (0.048, height - 0.02),
            (0.0, height)]
    m.lathe(prof, (0.0, 0.0, -span * 0.5), segments=12, mat=METAL)
    # The rope eye, and the hi-vis sleeve that makes the pole visible against snow.
    m.tube((0.0, height - 0.18, -span * 0.5), 0.075, 0.048, 0.035, axis=2, segments=10,
           mat=METAL)
    m.cylinder((0.0, height * 0.62, -span * 0.5), 0.052, 0.30, axis=1, segments=12,
               mat=BODY)
    return {"sectionLengthM": span, "heightM": height}


def _bamboo(m, site):
    """A boundary bamboo with its flag: the cheapest marker on the hill and the most
    used one, planted every few metres along a closure or a hazard."""
    span = 3.00
    height = site["bamboo_h"]
    nodes = max(5, int(height / 0.38))
    prof = [(0.0, 0.0)]
    for i in range(nodes + 1):
        y = height * i / nodes
        prof.append((0.019, y - 0.02))
        prof.append((0.024, y))
        prof.append((0.019, y + 0.02))
    prof.append((0.0, height + 0.01))
    m.lathe(prof, (0.0, 0.0, -span * 0.5), segments=8, mat=METAL)
    flag_h = 0.26
    m.prism([(0.0, 0.0), (0.34, -0.06), (0.34, 0.10), (0.0, flag_h)],
            (0.02, height - flag_h - 0.05, -span * 0.5), 0.002, mat=BODY, axis=2)
    return {"sectionLengthM": span, "heightM": round(height, 2)}


# --------------------------------------------------------------------------- maze
def _maze_stanchion(m, site):
    """A retractable-belt stanchion: weighted base, chrome post, belt cassette."""
    height = MAZE_POST_H
    prof = [(0.0, 0.0), (0.175, 0.0), (0.175, 0.035), (0.150, 0.055), (0.045, 0.075),
            (0.038, height - 0.16), (0.052, height - 0.14), (0.052, height - 0.02),
            (0.0, height)]
    m.lathe(prof, (0.0, 0.0, 0.0), segments=14, mat=METAL)
    m.tube((0.0, height - 0.09, 0.0), 0.066, 0.052, 0.13, axis=1, segments=12, mat=BODY)
    for side in (-1.0, 1.0):
        m.box((side * 0.058, height - 0.09, 0.0), (0.016, 0.075, 0.045), mat=METAL,
              bevel=False)
    return {"heightM": height}


def _maze_rail(m, site):
    """One 2 m bay of fixed maze rail: a leading upright and two rails running on."""
    length = MAZE_BAY
    height = 1.05
    z0 = -length * 0.5
    _post(m, 0.0, z0, height, 0.032, segments=10)
    m.cylinder((0.0, 0.014, z0), 0.13, 0.028, axis=1, segments=12, mat=METAL)
    m.cylinder((0.0, height + 0.014, z0), 0.038, 0.028, axis=1, segments=10, mat=METAL)
    for y in (height - 0.05, height * 0.52):
        m.cylinder((0.0, y, 0.0), 0.028, length, axis=2, segments=10, mat=METAL)
    # A padded sleeve at knee height: a maze rail is something skiers walk into.
    m.cylinder((0.0, height * 0.52, 0.0), 0.040, length * 0.92, axis=2, segments=10,
               mat=BODY)
    return {"sectionLengthM": length, "heightM": height}


def _maze_gate(m, site):
    """The singles-line gate at the head of a maze; `gate_swing` opens it about Y."""
    width = 1.20
    height = 1.05
    half = width * 0.5
    for side in (-1.0, 1.0):
        _post(m, side * half, 0.0, height, 0.036, segments=10)
        m.cylinder((side * half, 0.014, 0.0), 0.14, 0.028, axis=1, segments=12,
                   mat=METAL)
    m.node("gate_swing", pivot=(-half, 0.0, 0.0))
    leaf_w = width - 0.12
    cx = -half + 0.06 + leaf_w * 0.5
    for y in (height - 0.06, height * 0.5):
        m.cylinder((cx, y, 0.0), 0.026, leaf_w, axis=0, segments=8, mat=METAL,
                   parent="gate_swing")
    for i in range(4):
        x = -half + 0.06 + leaf_w * (i + 0.5) / 4.0
        m.cylinder((x, height * 0.75, 0.0), 0.016, height * 0.52, axis=1, segments=6,
                   mat=METAL, parent="gate_swing")
    m.box((cx, height * 0.5, 0.0), (leaf_w * 0.5, 0.22, 0.02), mat=BODY,
          parent="gate_swing", bevel=False)
    m.box((half - 0.05, height * 0.72, 0.0), (0.10, 0.09, 0.09), mat=METAL)
    return {"clearWidthM": round(width, 2)}


# --------------------------------------------------------------------------- buildings
def _pitched_roof(m, length, width, eave_y, pitch_deg, overhang, thickness,
                  gable_overhang, mat=METAL, parent=None):
    """A gable roof with real eaves: a slab of constant thickness, not a folded plane.

    The profile is drawn in (y, z) and extruded along X, so the ridge runs the length of
    the building and the eaves overhang both long walls the way they must to keep a
    metre of snow off the wall head.
    """
    half_w = width * 0.5 + overhang
    rise = math.tan(math.radians(pitch_deg)) * (width * 0.5)
    ridge = eave_y + rise
    drop = thickness / math.cos(math.radians(pitch_deg))
    profile = [(eave_y, -half_w), (ridge, 0.0), (eave_y, half_w),
               (eave_y - drop, half_w), (ridge - drop, 0.0), (eave_y - drop, -half_w)]
    m.prism(profile, (0.0, 0.0, 0.0), length + gable_overhang * 2.0, mat=mat,
            parent=parent, axis=0)
    return ridge


def _eave_brackets(m, length, width, eave_y, count, parent=None):
    """Timber brackets under the eaves. Repeated, so built plain."""
    span = length * 0.92
    step = span / max(1, count - 1)
    for side in (-1.0, 1.0):
        z = side * width * 0.5
        bracket = m.box((-span * 0.5, eave_y - 0.22, z + side * 0.22),
                        (0.12, 0.36, 0.55), mat=BODY, parent=parent, bevel=False)
        m.array(bracket, count, (step, 0.0, 0.0), parent=parent)


def _snow_guards(m, length, width, eave_y, pitch_deg, parent=None):
    """Snow stops along the eaves: an Alpine roof that sheds onto a deck kills somebody."""
    rise_at = math.tan(math.radians(pitch_deg))
    count = _clamp(int(length / 1.4), 4, 14)
    span = length * 0.9
    step = span / max(1, count - 1)
    for side in (-1.0, 1.0):
        z = side * (width * 0.5 - 0.55)
        y = eave_y + (width * 0.5 - abs(z)) * rise_at + 0.06
        guard = m.box((-span * 0.5, y + 0.07, z), (0.05, 0.14, 0.04), mat=METAL,
                      parent=parent, bevel=False)
        m.array(guard, count, (step, 0.0, 0.0), parent=parent)


def _end_windows(m, length, width, storeys, storey_h, parent=None):
    """Two punched windows per floor in each gable end.

    A building glazed only on its long face reads as a facade flat. The ends are what
    you see first coming up the road, so they get real openings with a surround.
    """
    for side in (-1.0, 1.0):
        x = side * (length * 0.5 + LAP)
        for floor in range(storeys):
            cy = storey_h * floor + 1.72
            for k in (-1.0, 1.0):
                z = k * width * 0.24
                m.box((x, cy, z), (0.02, 1.25, 0.95), mat=GLASS, parent=parent,
                      bevel=False)
                for dy, dz, sy, sz in ((0.72, 0.0, 0.12, 1.15), (-0.72, 0.0, 0.12, 1.15),
                                       (0.0, 0.53, 1.32, 0.12), (0.0, -0.53, 1.32, 0.12)):
                    m.box((x - side * 0.04, cy + dy, z + dz), (0.09, sy, sz), mat=BODY,
                          parent=parent, bevel=False)


def _chimney(m, x, z, base_y, top_y, size=0.70, parent=None):
    plan = _rect_profile(size, size * 0.72)
    m.prism(plan, (x, (base_y + top_y) * 0.5, z), top_y - base_y, mat=METAL,
            parent=parent, axis=1)
    m.box((x, top_y + 0.06, z), (size + 0.16, 0.12, size * 0.72 + 0.16), mat=METAL,
          parent=parent)
    m.cylinder((x, top_y + 0.28, z), size * 0.16, 0.40, axis=1, segments=8, mat=METAL,
               parent=parent)


def _lodge(m, site, length, width, storeys, deck_depth, chimneys, name):
    """A backdrop building: walls off a lofted footprint, a pitched roof, a deck.

    The massing is deliberately simple, because these are read at two hundred metres
    across a valley. What has to be right is the scale and the roof: a building with a
    thin roof plane and no eave reads as cardboard however much detail is on the walls.
    """
    storey_h = 3.10
    eave_y = storey_h * storeys
    plan = _chamfered_plan(length, width, min(length, width) * 0.18)
    # A stone plinth that batters out at the bottom: the wall of a mountain building
    # meets the ground thicker than it ends.
    rings = [_scaled_plan(plan, 1.045, 1.060, 0.0),
             _scaled_plan(plan, 1.010, 1.014, 0.80),
             _scaled_plan(plan, 1.0, 1.0, 0.82 + LAP),
             _scaled_plan(plan, 1.0, 1.0, eave_y)]
    m.loft(rings, mat=BODY)
    ridge = _pitched_roof(m, length, width, eave_y, 26.0, 0.90, 0.26, 0.60)
    m.box((0.0, ridge + 0.03, 0.0), (length + 1.20, 0.10, 0.36), mat=METAL)
    _eave_brackets(m, length, width, eave_y, int(_clamp(int(length / 3.2), 4, 12)))
    _snow_guards(m, length, width, eave_y, 26.0)
    for i in range(chimneys):
        x = (i - (chimneys - 1) * 0.5) * length * 0.42
        _chimney(m, x, -width * 0.12, eave_y - 1.2, ridge + 1.10,
                 size=_clamp(width * 0.09, 0.45, 0.95))

    # Glazing: a band to the plaza on every floor, and a smaller one on the gable ends.
    face_z = width * 0.5 + LAP
    panes = int(_clamp(int(length / 2.6), 3, 10))
    for floor in range(storeys):
        sill = storey_h * floor + 1.00
        _window_band(m, -length * 0.42, length * 0.42, face_z, sill, 1.55, panes)
    m.prism(_rect_profile(1.90, 2.35), (0.0, 1.175, face_z + 0.02), 0.10, mat=METAL,
            axis=2)
    m.box((0.0, 1.10, face_z + 0.09), (1.70, 2.20, 0.06), mat=GLASS, bevel=False)
    _end_windows(m, length, width, storeys, storey_h)

    if deck_depth > 0.2:
        deck_y = 0.82
        z0 = width * 0.5 - 0.10
        z1 = z0 + deck_depth
        m.box((0.0, deck_y - 0.09, (z0 + z1) * 0.5), (length * 0.92, 0.18, deck_depth),
              mat=BODY)
        posts = int(_clamp(int(length / 3.4), 2, 12))
        step = length * 0.88 / max(1, posts - 1)
        leg = m.box((-length * 0.44, deck_y * 0.5 - 0.09, z1 - 0.25),
                    (0.16, deck_y - 0.18, 0.16), mat=BODY, bevel=False)
        m.array(leg, posts, (step, 0.0, 0.0))
        _railing(m, -length * 0.46, length * 0.46, z1 - 0.06, deck_y + 0.95, deck_y,
                 balusters=64)
        for side in (-1.0, 1.0):
            m.box((side * length * 0.46, deck_y + 0.48, (z0 + z1) * 0.5),
                  (0.09, 0.95, deck_depth), mat=METAL, bevel=False)
        # Steps off the deck, one flight, real riser height.
        risers = max(2, int(deck_y / 0.18))
        tread = m.box((0.0, deck_y - 0.09, z1 + 0.16), (2.20, 0.21, 0.36), mat=METAL,
                      bevel=False)
        m.array(tread, risers, (0.0, -0.18, 0.32))

    hull = (0.0, eave_y * 0.5, 0.0)
    return {"colliders": [(name + "_col", hull, (length, eave_y, width))],
            "footprintM": [round(length, 2), round(width, 2)],
            "storeys": storeys, "ridgeHeightM": round(ridge, 2)}


def _lodge_base(m, site):
    length, width = site["lodge"]
    out = _lodge(m, site, length, width, 2, 5.0, 2, "lodge")
    out["lunchSeats"] = int(round(site["seats"]))
    return out


def _lodge_mid(m, site):
    length, width = site["mid"]
    out = _lodge(m, site, length, width, 1, 4.0, 1, "lodge")
    out["lunchSeats"] = int(round(site["seats"] * 0.45))
    return out


def _patrol_hut(m, site):
    """Patrol at the top of the hill: a toboggan bay, a warm room and a radio mast."""
    out = _lodge(m, site, PATROL_L, PATROL_W, 1, 1.8, 1, "hut")
    mast_h = 4.20
    x = PATROL_L * 0.5 - 0.35
    m.cylinder((x, 3.10 + mast_h * 0.5, -PATROL_W * 0.3), 0.045, mast_h, axis=1,
               segments=8, mat=METAL, radius_end=0.022)
    for i in range(3):
        y = 3.10 + mast_h * (0.55 + i * 0.16)
        m.cylinder((x, y, -PATROL_W * 0.3), 0.16, 0.016, axis=1, segments=8, mat=METAL)
    # A rescue toboggan parks nose-up against the gable wall; the cross is a texture.
    m.box((-PATROL_L * 0.5 - 0.10, 0.55, PATROL_W * 0.18), (0.06, 1.10, 0.52),
          mat=BODY, rot=mk.unity_euler(0.0, 0.0, 12.0))
    return out


def _lift_shack(m, site):
    """The operator's hut at a terminal: door, window, stove pipe and a step.

    Two and a half metres square, because that is the box a resort buys on a skid and
    drags into place with a snowcat.
    """
    plan = _chamfered_plan(SHACK_L, SHACK_W, 0.18)
    m.loft([_scaled_plan(plan, 1.03, 1.03, 0.0),
            _scaled_plan(plan, 1.0, 1.0, 0.16),
            _scaled_plan(plan, 1.0, 1.0, SHACK_EAVE)], mat=BODY)
    # A mono-pitch roof draining away from the door, with an overhang over the step.
    ridge_drop = 0.34
    m.prism([(SHACK_EAVE + ridge_drop, -SHACK_W * 0.5 - 0.30),
             (SHACK_EAVE, SHACK_W * 0.5 + 0.46),
             (SHACK_EAVE - 0.14, SHACK_W * 0.5 + 0.46),
             (SHACK_EAVE + ridge_drop - 0.14, -SHACK_W * 0.5 - 0.30)],
            (0.0, 0.0, 0.0), SHACK_L + 0.44, mat=METAL, axis=0)
    face = SHACK_W * 0.5 + LAP
    door_w, door_h = 0.80, 1.95
    m.prism(_rect_profile(door_w + 0.12, door_h + 0.08), (-0.55, door_h * 0.5, face),
            0.09, mat=METAL, axis=2)
    m.box((-0.55, door_h * 0.5, face + 0.05), (door_w, door_h, 0.05), mat=BODY)
    m.box((-0.55 + door_w * 0.36, 1.02, face + 0.10), (0.04, 0.14, 0.05), mat=METAL,
          bevel=False)
    m.box((-0.55, door_h - 0.38, face + 0.09), (0.42, 0.40, 0.02), mat=GLASS,
          bevel=False)
    # The window the operator actually works through, facing the load zone.
    _window_band(m, 0.18, 1.02, face, 1.15, 0.75, 1)
    m.box((0.60, 1.08, face + 0.06), (1.00, 0.10, 0.22), mat=BODY, bevel=False)
    # Stove pipe with its rain cap, through the high side of the roof.
    pipe_x, pipe_z = SHACK_L * 0.28, -SHACK_W * 0.22
    m.cylinder((pipe_x, SHACK_EAVE + ridge_drop * 0.7 + 0.55, pipe_z), 0.065, 1.20,
               axis=1, segments=10, mat=METAL)
    m.cylinder((pipe_x, SHACK_EAVE + ridge_drop * 0.7 + 1.18, pipe_z), 0.115, 0.10,
               axis=1, segments=10, mat=METAL)
    # The step, and the grab rail beside it.
    m.box((-0.55, 0.09, face + 0.40), (1.10, 0.18, 0.55), mat=METAL)
    m.beam((-0.55 - 0.60, 0.18, face + 0.62), (-0.55 - 0.60, 1.05, face + 0.18), 0.045,
           mat=METAL, square=False, segments=8)
    return {"colliders": [("shack_col", (0.0, SHACK_EAVE * 0.5, 0.0),
                           (SHACK_L, SHACK_EAVE, SHACK_W))],
            "footprintM": [SHACK_L, SHACK_W]}


# --------------------------------------------------------------------------- vegetation
def _conifer(m, site, height, whorls, needle_segments=8):
    """A spruce: a tapered trunk and a stack of drooping branch whorls.

    One whorl is a revolved skirt rather than a cone, because a cone reads as a party
    hat and a skirt with a drooped outer edge reads as a tree. The whorls shrink and
    tighten toward the leader, and the lower ones are cut away on the mature tree the
    way a closed canopy does it.
    """
    rng = m.rng
    trunk_r = _clamp(height * 0.014, 0.02, 0.26)
    m.cylinder((0.0, height * 0.48, 0.0), trunk_r, height * 0.96, axis=1,
               segments=max(6, _seg(trunk_r) - 2), mat=METAL, radius_end=trunk_r * 0.18)
    # A root flare, so the trunk does not look pushed into the ground like a dowel.
    m.cylinder((0.0, trunk_r * 0.9, 0.0), trunk_r * 1.8, trunk_r * 1.8, axis=1,
               segments=8, mat=METAL, radius_end=trunk_r)
    spread = height * 0.24
    base_t = 0.20 if height > 5.0 else 0.08
    for i in range(whorls):
        t = base_t + (0.97 - base_t) * (i / max(1, whorls - 1.0))
        y = height * t
        taper = (1.0 - t) ** 0.72
        r = spread * taper * float(1.0 + 0.12 * (rng.random() - 0.5))
        if r < 0.06:
            continue
        droop = r * 0.42
        profile = [(trunk_r * 0.9, droop * 0.55), (r * 0.45, 0.0), (r * 0.82, -droop * 0.5),
                   (r, -droop)]
        seg = max(6, int(needle_segments * _clamp(taper + 0.45, 0.6, 1.0)))
        m.lathe(profile, (0.0, y, 0.0), segments=seg, mat=BODY,
                rot=mk.unity_euler(0.0, float(rng.random()) * 60.0, 0.0))
    # A leader, and on a grown tree the bare lower branches that catch the light.
    m.cylinder((0.0, height * 0.985, 0.0), spread * 0.10, height * 0.10, axis=1,
               segments=6, mat=BODY, radius_end=0.01)
    if height > 5.0:
        for i in range(4):
            angle = 2.0 * math.pi * (i + float(rng.random()) * 0.4) / 4.0
            y = height * (0.16 + 0.05 * i)
            reach = spread * (0.95 - 0.07 * i)
            m.beam((0.0, y, 0.0),
                   (math.cos(angle) * reach, y - reach * 0.32, math.sin(angle) * reach),
                   trunk_r * 0.32, mat=METAL, square=False, segments=5)
    return {"heightM": round(height, 2), "whorls": whorls}


def _deciduous_bare(m, site, height=9.0):
    """A bare larch or birch: a trunk that forks, and forks again, and once more.

    Winter deciduous trees are all silhouette, so the budget goes into branching depth
    rather than into anything on the branches.
    """
    rng = m.rng
    trunk_r = height * 0.018
    fork_y = height * 0.34
    m.cylinder((0.0, fork_y * 0.5, 0.0), trunk_r, fork_y, axis=1, segments=8,
               mat=METAL, radius_end=trunk_r * 0.72)
    m.cylinder((0.0, trunk_r * 1.1, 0.0), trunk_r * 2.0, trunk_r * 2.2, axis=1,
               segments=8, mat=METAL, radius_end=trunk_r)

    def branch(origin, direction, length, radius, depth):
        end = (origin[0] + direction[0] * length,
               origin[1] + direction[1] * length,
               origin[2] + direction[2] * length)
        m.beam(origin, end, radius * 2.0, mat=METAL, square=False,
               segments=6 if depth > 1 else 4)
        if depth <= 0 or length < height * 0.05:
            return
        children = 3
        for k in range(children):
            spin = 2.0 * math.pi * ((k + float(rng.random()) * 0.5) / children)
            lean = 0.42 + 0.30 * float(rng.random())
            nd = (direction[0] + math.cos(spin) * lean,
                  direction[1] * 0.86 + 0.12,
                  direction[2] + math.sin(spin) * lean)
            norm = math.sqrt(nd[0] ** 2 + nd[1] ** 2 + nd[2] ** 2) or 1.0
            branch(end, (nd[0] / norm, nd[1] / norm, nd[2] / norm),
                   length * (0.62 + 0.10 * float(rng.random())), radius * 0.62,
                   depth - 1)

    limbs = 4
    for k in range(limbs):
        spin = 2.0 * math.pi * (k + float(rng.random()) * 0.4) / limbs
        lean = 0.36 + 0.22 * float(rng.random())
        d = (math.cos(spin) * lean, 1.0, math.sin(spin) * lean)
        norm = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
        branch((0.0, fork_y - 0.05, 0.0), (d[0] / norm, d[1] / norm, d[2] / norm),
               height * 0.30, trunk_r * 0.62, 2)
    return {"heightM": round(height, 2)}


def _boulder(m, site, radius, aspect, segments, rings, lumps=5):
    """A boulder: an icosphere pushed about by seeded noise, faceted, sitting on y = 0.

    The displacement is a pure function of the vertex position, which matters more than
    it looks: the sphere's pole vertices are built coincident and welded afterwards, so
    anything that moved them differently would tear the mesh open. A rock has to stay
    watertight, because half of them are half buried and the terrain has to be able to
    cut them.
    """
    rng = m.rng
    waves = []
    for k in range(lumps):
        freq = 1.4 + 1.5 * k + float(rng.random()) * 1.2
        amp = 0.20 / (1.0 + 0.9 * k)
        phase = float(rng.random()) * 6.283
        axis = (float(rng.random()) * 2.0 - 1.0, float(rng.random()) * 2.0 - 1.0,
                float(rng.random()) * 2.0 - 1.0)
        norm = math.sqrt(sum(c * c for c in axis)) or 1.0
        waves.append((freq, amp, phase, (axis[0] / norm, axis[1] / norm, axis[2] / norm)))

    faces = m.sphere((0.0, radius, 0.0), radius, segments=segments, rings=rings,
                     mat=METAL)
    centre = mk.to_blender((0.0, radius, 0.0))
    verts = list({v for f in faces for v in f.verts})
    lowest = None
    for v in verts:
        d = v.co - centre
        if d.length < 1e-9:
            continue
        n = d.normalized()
        scale = 1.0
        for freq, amp, phase, axis in waves:
            scale += amp * math.sin(freq * (n.x * axis[0] + n.y * axis[1]
                                            + n.z * axis[2]) * 3.1 + phase)
        d = n * (radius * _clamp(scale, 0.55, 1.45))
        # Blender stores Unity (x, y, z) as (x, -z, y), so a Unity-space squash on
        # (x, y, z) is a Blender-space squash on (x, z, y).
        d.x *= aspect[0]
        d.y *= aspect[2]
        d.z *= aspect[1]
        v.co = centre + d
        lowest = v.co.z if lowest is None else min(lowest, v.co.z)
    if lowest is not None:
        for v in verts:
            v.co.z -= lowest
    for f in faces:
        f.smooth = False          # a boulder is facets; smooth shading reads as a balloon
    return {"radiusM": round(radius, 2)}


# --------------------------------------------------------------------------- misc
def _snow_stake(m, site):
    """A graduated snow stake, marked past the deepest the season gets.

    The height is the whole point of the prop: read it against the pack and you know the
    depth, so it is the one piece of scenery that has to agree with the climate record.
    """
    height = site["stake_h"]
    m.box((0.0, height * 0.5, 0.0), (0.06, height, 0.06), mat=BODY)
    m.box((0.0, height + 0.03, 0.0), (0.09, 0.06, 0.09), mat=METAL, bevel=False)
    band_pitch = 0.20
    bands = max(3, int((height - 0.10) / band_pitch))
    band = m.box((0.0, 0.10 + band_pitch * 0.5, 0.0), (0.065, band_pitch * 0.5, 0.065),
                 mat=METAL, bevel=False)
    m.array(band, bands, (0.0, band_pitch, 0.0))
    # The foot plate that keeps it from sinking, and the driven spike under it.
    m.box((0.0, 0.02, 0.0), (0.30, 0.04, 0.30), mat=METAL, bevel=False)
    m.cylinder((0.0, 0.12, 0.0), 0.035, 0.24, axis=1, segments=6, mat=METAL)
    return {"heightM": round(height, 2), "bandSpacingM": band_pitch}


def _piste_marker(m, site):
    """A piste marker pole: tall enough to still show with a full pack under it."""
    height = site["marker_h"]
    r = 0.028
    m.cylinder((0.0, height * 0.5, 0.0), r, height, axis=1, segments=10, mat=BODY)
    m.cylinder((0.0, height + 0.02, 0.0), r * 1.25, 0.04, axis=1, segments=10,
               mat=METAL)
    # Reflective bands down the top metre, which is the part that is above the pack.
    pitch = 0.22
    bands = max(3, int(1.10 / pitch))
    band = m.cylinder((0.0, height - 0.18, 0.0), r * 1.12, 0.10, axis=1, segments=10,
                      mat=METAL)
    m.array(band, bands, (0.0, -pitch, 0.0))
    # The ground sleeve the pole drops into, so it can be pulled for summer.
    m.cylinder((0.0, 0.09, 0.0), r * 2.2, 0.18, axis=1, segments=10, mat=METAL)
    m.cylinder((0.0, 0.015, 0.0), r * 4.5, 0.03, axis=1, segments=10, mat=METAL)
    return {"heightM": round(height, 2),
            "hydrantSpacingM": site["hydrant_spacing_m"]}


def _wind_sock(m, site):
    """A wind sock on a mast. `sock_yaw` yaws the whole head about Y into the wind."""
    mast_h = 5.00
    m.cylinder((0.0, mast_h * 0.5, 0.0), 0.055, mast_h, axis=1, segments=10, mat=METAL,
               radius_end=0.032)
    m.cylinder((0.0, 0.05, 0.0), 0.24, 0.10, axis=1, segments=12, mat=METAL)
    for side in (-1.0, 1.0):
        m.beam((side * 0.55, 0.05, 0.0), (0.0, mast_h * 0.42, 0.0), 0.022, mat=METAL,
               square=False, segments=5)
    m.node("sock_yaw", pivot=(0.0, mast_h, 0.0))
    hoop_r = 0.30
    m.tube((0.0, mast_h, hoop_r * 0.55), hoop_r, hoop_r - 0.025, 0.05, axis=2,
           segments=12, mat=METAL, parent="sock_yaw")
    m.cylinder((0.0, mast_h, 0.0), 0.045, 0.34, axis=2, segments=8, mat=METAL,
               parent="sock_yaw")
    rings = []
    length = 1.30
    for k in range(4):
        t = k / 3.0
        r = hoop_r * (1.0 - 0.55 * t)
        z = hoop_r * 0.55 + length * t
        rings.append([(math.cos(2.0 * math.pi * i / 8) * r, mast_h
                       + math.sin(2.0 * math.pi * i / 8) * r, z) for i in range(8)])
    m.loft(rings, mat=BODY, parent="sock_yaw")
    return {"mastHeightM": mast_h}


def _weather_station(m, site):
    """A slope weather station: tripod, anemometer, vane, radiation shield, solar panel.

    The station is what the climate model is pretending to read from, so it carries the
    instruments the sim actually samples: wind, temperature and humidity.
    """
    mast_h = 3.00
    m.cylinder((0.0, mast_h * 0.5, 0.0), 0.032, mast_h, axis=1, segments=8, mat=METAL)
    for k in range(3):
        # One leg straight back and two forward, so the tripod straddles the centreline
        # instead of sitting to one side of it.
        angle = math.pi * 0.5 + 2.0 * math.pi * k / 3.0
        foot = (math.cos(angle) * 1.05, 0.0, math.sin(angle) * 1.05)
        m.beam((math.cos(angle) * 0.05, mast_h * 0.62, math.sin(angle) * 0.05), foot,
               0.030, mat=METAL, square=False, segments=6)
        m.box(foot, (0.22, 0.05, 0.22), mat=METAL, bevel=False)
    arm_y = mast_h - 0.18
    m.cylinder((0.30, arm_y, 0.0), 0.022, 0.72, axis=0, segments=6, mat=METAL)
    m.node("anemometer", pivot=(0.62, arm_y + 0.14, 0.0))
    m.cylinder((0.62, arm_y + 0.10, 0.0), 0.030, 0.14, axis=1, segments=8, mat=METAL,
               parent="anemometer")
    for k in range(3):
        angle = 2.0 * math.pi * k / 3.0
        cup = (0.62 + math.cos(angle) * 0.16, arm_y + 0.16, math.sin(angle) * 0.16)
        m.beam((0.62, arm_y + 0.16, 0.0), cup, 0.014, mat=METAL, square=False,
               segments=4, parent="anemometer")
        m.sphere(cup, 0.045, segments=8, rings=5, mat=BODY, parent="anemometer")
    # Wind vane on the other arm, a shielded thermometer below it, and the panel and
    # battery box that keep the thing alive through a February.
    m.prism([(0.0, -0.03), (0.34, -0.16), (0.34, 0.16), (0.0, 0.03)],
            (-0.52, arm_y + 0.12, 0.0), 0.004, mat=BODY, axis=2)
    m.cylinder((-0.30, arm_y + 0.12, 0.0), 0.018, 0.30, axis=0, segments=6, mat=METAL)
    shield = [(0.0, 0.0), (0.11, 0.0), (0.11, 0.018), (0.0, 0.022)]
    plate = m.lathe(shield, (0.24, mast_h * 0.62, 0.0), segments=10, mat=BODY)
    m.array(plate, 6, (0.0, -0.045, 0.0))
    m.cylinder((0.24, mast_h * 0.62 - 0.14, 0.0), 0.016, 0.30, axis=1, segments=6,
               mat=METAL)
    m.box((-0.30, mast_h * 0.52, 0.0), (0.55, 0.04, 0.42), mat=GLASS,
          rot=mk.unity_euler(38.0, 0.0, 0.0))
    m.box((0.0, mast_h * 0.30, 0.14), (0.30, 0.40, 0.22), mat=BODY)
    return {"mastHeightM": mast_h}


def _trash_bin(m, site):
    """A base-area bin: a lidded drum on a stand, with an ash tray ring at the top."""
    height = 0.98
    r = 0.30
    prof = [(0.0, 0.0), (r * 0.62, 0.0), (r * 0.62, 0.06), (r, 0.20), (r, height - 0.10),
            (r * 1.06, height - 0.06), (r * 1.06, height), (r * 0.30, height + 0.10),
            (0.0, height + 0.12)]
    m.lathe(prof, (0.0, 0.0, 0.0), segments=14, mat=BODY)
    m.tube((0.0, height + 0.02, 0.0), r * 1.10, r * 0.94, 0.06, axis=1, segments=14,
           mat=METAL)
    # The opening, cut as a recessed flap rather than a hole: a hole in a lathe is a
    # non-manifold argument waiting to happen.
    m.box((0.0, height - 0.26, r * 0.86), (0.36, 0.26, 0.06), mat=METAL)
    for band in (0.30, 0.62):
        m.tube((0.0, height * band, 0.0), r * 1.02, r * 0.98, 0.045, axis=1,
               segments=14, mat=METAL)
    return {"heightM": height}


def _bench(m, site):
    """A slatted bench with cast ends. Its seats are published as sockets."""
    length = 1.80
    seat_y = 0.45
    for side in (-1.0, 1.0):
        x = side * (length * 0.5 - 0.14)
        # The cast end, drawn in (y, z) because an X extrusion is what an end is: a
        # foot, a pedestal, the seat shelf and the rake of the back, in one profile.
        m.prism([(0.0, -0.26), (0.0, 0.26), (0.06, 0.22), (0.10, 0.09),
                 (seat_y, 0.09), (seat_y, 0.26), (seat_y + 0.10, 0.26),
                 (seat_y + 0.10, -0.14), (seat_y + 0.56, -0.30),
                 (seat_y + 0.52, -0.40), (0.08, -0.22)],
                (x, 0.0, 0.0), 0.07, mat=METAL, axis=0)
    slat = m.box((0.0, seat_y + 0.03, -0.18), (length, 0.045, 0.11), mat=BODY,
                 bevel=False)
    m.array(slat, 4, (0.0, 0.0, 0.13))
    back = m.box((0.0, seat_y + 0.34, -0.30), (length, 0.11, 0.04), mat=BODY,
                 bevel=False)
    m.array(back, 3, (0.0, 0.15, -0.045))
    for i in range(3):
        m.socket("seat_%02d" % (i + 1), ((i - 1) * length * 0.30, seat_y + 0.06, 0.0))
    return {"lengthM": length, "seats": 3}


def _picnic_table(m, site):
    """A picnic table on the deck: A-frame legs, a plank top, benches either side."""
    length = 1.90
    top_y = 0.75
    seat_y = 0.46
    reach = 0.78
    for side in (-1.0, 1.0):
        x = side * (length * 0.5 - 0.30)
        for lean in (-1.0, 1.0):
            m.beam((x, 0.02, lean * reach), (x, top_y - 0.05, -lean * 0.08), 0.085,
                   mat=METAL)
        m.box((x, seat_y - 0.06, 0.0), (0.09, 0.08, reach * 2.0), mat=METAL,
              bevel=False)
    plank = m.box((0.0, top_y, -0.34), (length, 0.05, 0.15), mat=BODY, bevel=False)
    m.array(plank, 5, (0.0, 0.0, 0.17))
    for side in (-1.0, 1.0):
        seat = m.box((0.0, seat_y, side * (reach - 0.16)), (length, 0.05, 0.15),
                     mat=BODY, bevel=False)
        m.array(seat, 2, (0.0, 0.0, side * 0.17))
    made = 0
    for side in (-1.0, 1.0):
        for i in range(3):
            made += 1
            m.socket("seat_%02d" % made,
                     ((i - 1) * length * 0.30, seat_y + 0.04, side * (reach - 0.08)))
    return {"lengthM": length, "seats": 6}


def _toilet_block(m, site):
    """Two portable cabins in a block, which is how they arrive and how they stand."""
    unit_w, unit_d, unit_h = 1.20, 1.20, 2.30
    block_w = unit_w * 2.0 + 0.06
    for i in range(2):
        x = (i - 0.5) * (unit_w + 0.06)
        # Loft rings are model-space points, so the second cabin is built by shifting
        # its footprint rather than by moving anything afterwards.
        plan = [(px + x, pz) for px, pz in _chamfered_plan(unit_w, unit_d, 0.10)]
        m.loft([[(px, 0.12, pz) for px, pz in plan],
                [(px, unit_h, pz) for px, pz in plan]], mat=BODY)
        m.box((x, unit_h + 0.05, 0.0), (unit_w + 0.08, 0.10, unit_d + 0.08), mat=METAL)
        face = unit_d * 0.5 + LAP
        m.box((x, 1.05, face + 0.02), (unit_w * 0.70, 1.90, 0.05), mat=METAL)
        m.box((x + unit_w * 0.24, 1.05, face + 0.05), (0.05, 0.12, 0.04), mat=METAL,
              bevel=False)
        louvre = m.box((x, 1.86, face + 0.05), (unit_w * 0.44, 0.035, 0.03), mat=METAL,
                       bevel=False)
        m.array(louvre, 4, (0.0, 0.055, 0.0))
        m.cylinder((x - unit_w * 0.30, unit_h + 0.45, -unit_d * 0.28), 0.05, 0.80,
                   axis=1, segments=8, mat=METAL)
    m.box((0.0, 0.06, 0.0), (block_w + 0.10, 0.12, unit_d + 0.10), mat=METAL)
    return {"colliders": [("toilet_col", (0.0, unit_h * 0.5, 0.0),
                           (block_w, unit_h, unit_d))],
            "units": 2}


def _light_pole(m, site):
    """A night-grooming light pole: mast, head and base.

    Twelve metres, because that is the mounting height that lights a piste without
    throwing a shadow off every roller, and the heads are aimed down the fall line.
    """
    mast_h = 12.0
    base_r = 0.20
    m.cylinder((0.0, mast_h * 0.5, 0.0), base_r, mast_h, axis=1, segments=12,
               mat=METAL, radius_end=base_r * 0.55)
    # A poured plinth and a bolted flange: this pole stands in a snow farm all winter.
    m.prism(_rect_profile(0.90, 0.90), (0.0, 0.22, 0.0), 0.44, mat=METAL, axis=1)
    m.cylinder((0.0, 0.46, 0.0), base_r * 1.9, 0.05, axis=1, segments=12, mat=METAL)
    for k in range(4):
        angle = math.pi * 0.25 + math.pi * 0.5 * k
        m.box((math.cos(angle) * base_r * 1.5, 0.52, math.sin(angle) * base_r * 1.5),
              (0.05, 0.10, 0.05), mat=METAL, bevel=False)
    m.box((0.0, 1.05, base_r + 0.06), (0.24, 0.50, 0.14), mat=METAL)
    arm_y = mast_h - 0.25
    m.cylinder((0.0, arm_y, 0.42), 0.055, 0.90, axis=2, segments=8, mat=METAL)
    for side in (-1.0, 1.0):
        x = side * 0.62
        m.cylinder((x * 0.5, arm_y + 0.06, 0.86), 0.045, 0.64, axis=0, segments=8,
                   mat=METAL, rot=None)
        head = mk.unity_euler(28.0, 0.0, 0.0)
        m.box((x, arm_y + 0.10, 0.92), (0.62, 0.16, 0.42), mat=BODY, rot=head)
        m.box((x, arm_y - 0.02, 0.98), (0.56, 0.03, 0.36), mat=GLASS, rot=head)
        m.socket("light_work_%02d" % (1 if side < 0 else 2), (x, arm_y - 0.04, 1.02))
    return {"mastHeightM": mast_h}


def _bollard(m, site):
    """A parking bollard: the thing that survives being backed into all season."""
    height = 0.90
    r = 0.070
    prof = [(0.0, 0.0), (0.17, 0.0), (0.17, 0.035), (r * 1.25, 0.075),
            (r, 0.12), (r, height - 0.09), (r * 0.92, height - 0.03),
            (r * 0.55, height), (0.0, height + 0.015)]
    m.lathe(prof, (0.0, 0.0, 0.0), segments=14, mat=BODY)
    for y in (height * 0.62, height * 0.78):
        m.tube((0.0, y, 0.0), r * 1.06, r * 0.96, 0.07, axis=1, segments=14, mat=METAL)
    return {"heightM": height}


# --------------------------------------------------------------------------- catalogue
def _catalogue(site):
    """Every prop this module owns, in build order.

    The difficulty blades are generated from the grades the piste data actually uses, so
    a scenario that adds a grade gets its sign without anyone touching this file.
    """
    items = []
    for grade in site["grades"]:
        items.append(("sign_trail_" + grade.lower(), "%s run marker" % grade,
                      "prop", "prop", _trail_sign, {"grade": grade}))
    items += [
        ("sign_run_name", "Run name board", "prop", "prop", _run_name_board, {}),
        ("sign_slow_zone", "Slow zone sign", "prop", "prop", _slow_zone_sign, {}),
        ("gate_closure", "Run closure gate", "prop", "prop", _closure_gate, {}),
        ("board_map", "Piste map board", "prop", "prop", _map_board, {}),
        ("sign_lift_status", "Lift status board", "prop", "prop", _lift_status_sign, {}),

        ("fence_snow_section", "Snow fence bay", "prop", "prop", _snow_fence, {}),
        ("net_a_section", "Avalanche net bay", "prop", "prop", _a_net, {}),
        ("rope_line_stanchion", "Rope line stanchion", "prop", "prop", _rope_stanchion, {}),
        ("bamboo_boundary", "Boundary bamboo", "prop", "prop", _bamboo, {}),

        ("maze_stanchion", "Queue maze stanchion", "prop", "prop", _maze_stanchion, {}),
        ("maze_rail", "Queue maze rail", "prop", "prop", _maze_rail, {}),
        ("maze_gate", "Queue maze gate", "prop", "prop", _maze_gate, {}),

        ("lift_shack", "Lift operator hut", "prop", "prop", _lift_shack, {}),
        ("lodge_base", "Base lodge", "prop", "prop", _lodge_base, {}),
        ("lodge_mid", "Mid-mountain restaurant", "prop", "prop", _lodge_mid, {}),
        ("hut_patrol", "Patrol hut", "prop", "prop", _patrol_hut, {}),

        ("tree_conifer_sapling", "Conifer sapling", "tree", "tree", _conifer,
         {"height": 2.1, "whorls": 8, "needle_segments": 8}),
        ("tree_conifer_mid", "Conifer, mid growth", "tree", "tree", _conifer,
         {"height": 8.0, "whorls": 11, "needle_segments": 8}),
        ("tree_conifer_mature", "Mature conifer", "tree", "tree", _conifer,
         {"height": 16.0, "whorls": 14, "needle_segments": 9}),
        ("tree_deciduous_bare", "Bare deciduous", "tree", "tree", _deciduous_bare,
         {"height": 9.0}),

        ("rock_boulder_a", "Boulder, rounded", "rock", "rock", _boulder,
         {"radius": 1.10, "aspect": (1.25, 0.78, 1.00), "segments": 14, "rings": 9}),
        ("rock_boulder_b", "Boulder, blocky", "rock", "rock", _boulder,
         {"radius": 0.70, "aspect": (1.00, 0.92, 1.35), "segments": 16, "rings": 11,
          "lumps": 6}),
        ("rock_boulder_c", "Boulder, slab", "rock", "rock", _boulder,
         {"radius": 1.50, "aspect": (1.30, 0.52, 1.10), "segments": 20, "rings": 13,
          "lumps": 4}),

        ("snow_stake", "Snow depth stake", "prop", "prop", _snow_stake, {}),
        ("piste_marker", "Piste marker pole", "prop", "prop", _piste_marker, {}),
        ("wind_sock", "Wind sock", "prop", "prop", _wind_sock, {}),
        ("weather_station", "Slope weather station", "prop", "prop", _weather_station, {}),
        ("bin_trash", "Trash bin", "prop", "prop", _trash_bin, {}),
        ("bench", "Bench", "prop", "prop", _bench, {}),
        ("picnic_table", "Picnic table", "prop", "prop", _picnic_table, {}),
        ("toilet_block", "Portable toilet block", "prop", "prop", _toilet_block, {}),
        ("light_pole_grooming", "Night grooming light pole", "prop", "prop",
         _light_pole, {}),
        ("bollard_parking", "Parking bollard", "prop", "prop", _bollard, {}),
    ]
    return tuple(items)


def build_all(out_dir):
    """Build every prop, tree and rock. Returns one manifest record per FBX."""
    site = _site()
    records = []
    for index, (name, display, kind, budget, builder, kwargs) in enumerate(_catalogue(site)):
        model = mk.Model(name, budget_key=budget,
                         seed=datasrc.seed_for("prop", name, index))
        extra = dict(builder(model, site, **kwargs) or {})
        colliders = extra.pop("colliders", ())
        extra.update({"family": "prop", "kind": kind, "displayName": display})
        records.append(export.emit(model, os.path.join(out_dir, name + ".fbx"),
                                   box_colliders=colliders, extra=extra))
    return records
