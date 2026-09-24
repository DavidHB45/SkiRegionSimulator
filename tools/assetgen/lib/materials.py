"""Material slots and trim-sheet UV mapping.

A machine gets three material slots and no more: body (tinted per livery), dark metal,
glass. Everything that reads as a panel line, a bolt row, a grip plate or a weld bead
comes from a shared 2048 industrial trim sheet, so a whole category renders with one
texture set and the fleet looks like it came out of one factory.
"""
import bmesh

from config import TRIM_SHEET_SIZE
from lib.meshkit import BODY, GLASS, METAL

# Cells on the shared trim sheet, in UV space (u0, v0, u1, v1). textures/trimsheets.py
# paints exactly these regions; anything that maps here gets that detail for free.
TRIM_CELLS = {
    "panel_large": (0.00, 0.00, 0.50, 0.25),
    "panel_small": (0.50, 0.00, 0.75, 0.25),
    "bolt_row": (0.75, 0.00, 1.00, 0.125),
    "grip_plate": (0.75, 0.125, 1.00, 0.25),
    "vent": (0.00, 0.25, 0.25, 0.375),
    "louvre": (0.25, 0.25, 0.50, 0.375),
    "hydraulic": (0.50, 0.25, 0.75, 0.3125),
    "weld_bead": (0.50, 0.3125, 0.75, 0.375),
    "tread_plate": (0.75, 0.25, 1.00, 0.50),
    "rubber_track": (0.00, 0.375, 0.50, 0.50),
    "glass_clean": (0.50, 0.375, 0.75, 0.50),
    "steel_plain": (0.00, 0.50, 0.50, 1.00),
    "paint_plain": (0.50, 0.50, 1.00, 1.00),
}

# Which cell each material slot falls back to when a generator does not say.
DEFAULT_CELL = {BODY: "paint_plain", METAL: "steel_plain", GLASS: "glass_clean"}


def cell_uv(cell, u, v):
    """Map a 0..1 coordinate into a named trim cell."""
    u0, v0, u1, v1 = TRIM_CELLS[cell]
    return (u0 + (u1 - u0) * (u % 1.0), v0 + (v1 - v0) * (v % 1.0))


def map_to_trim(model, node=None, cell=None, tile=1.0):
    """Re-map a node's UVs into a trim-sheet cell, tiling `tile` times across it."""
    names = [node] if node else list(model.order)
    for name in names:
        bm = model.nodes[name].bm
        uv_layer = bm.loops.layers.uv.verify()
        for face in bm.faces:
            target = cell or DEFAULT_CELL.get(face.material_index, "paint_plain")
            for loop in face.loops:
                u, v = loop[uv_layer].uv
                loop[uv_layer].uv = cell_uv(target, u * tile, v * tile)


def slot_for(kind):
    """Map a human word used by generators to a material slot index."""
    k = (kind or "body").lower()
    if k in ("glass", "window", "windscreen", "bubble", "canopy"):
        return GLASS
    if k in ("metal", "steel", "track", "rubber", "chrome", "dark", "frame"):
        return METAL
    return BODY


def texture_set_for(record_kind):
    """Which generated texture set a model asks ModelRegistry for."""
    return {
        "machine": "machine",
        "attachment": "machine",
        "lift": "lift",
        "prop": "prop",
        "tree": "vegetation",
        "rock": "terrain",
    }.get(record_kind, "machine")


TRIM_SIZE = TRIM_SHEET_SIZE
