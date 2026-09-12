"""Paths, budgets and export settings for the asset generation pipeline.

Nothing here is a balance number: those live in Assets/StreamingAssets/Data/*.json and
are read through lib.datasrc. What lives here is *build* configuration - where output
goes, how many triangles a class of mesh may have, what size a texture is exported at.
"""
import os

# --------------------------------------------------------------------------- paths
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # repository root
DATA_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Data")

# Everything the pipeline writes lives under this tree. It is gitignored: generated
# assets are build output, never repository content.
ART_ROOT = os.path.join(ROOT, "Assets", "Art", "Generated")

# Unity can only Resources.Load from a folder literally called "Resources", and the
# runtime ModelRegistry resolves every generated asset by Resources path.
RES_ROOT = os.path.join(ART_ROOT, "Resources")
RES_PREFIX = "AlpineSim"                               # Resources.Load("AlpineSim/Models/...")

MODELS_DIR = os.path.join(RES_ROOT, RES_PREFIX, "Models")
TEXTURES_DIR = os.path.join(RES_ROOT, RES_PREFIX, "Textures")
ICONS_DIR = os.path.join(RES_ROOT, RES_PREFIX, "Icons")
AUDIO_DIR = os.path.join(RES_ROOT, RES_PREFIX, "Audio")

MACHINES_DIR = os.path.join(MODELS_DIR, "Machines")
ATTACHMENTS_DIR = os.path.join(MODELS_DIR, "Attachments")
LIFTS_DIR = os.path.join(MODELS_DIR, "Lifts")
PROPS_DIR = os.path.join(MODELS_DIR, "Props")

MANIFEST_PATH = os.path.join(ART_ROOT, "manifest.json")
REPORT_PATH = os.path.join(ART_ROOT, "validation.json")

# --------------------------------------------------------------------------- determinism
# Every random choice in the pipeline (noise fields, wear splatter, grouser jitter) is
# seeded from this plus a per-asset string, so two runs agree bit for bit.
MASTER_SEED = 20260909

# --------------------------------------------------------------------------- geometry
# Triangle budgets per LOD0 mesh, by role. The build fails when a mesh overruns its
# ceiling; it warns when a hero machine comes in under its floor (that means the
# generator silently degenerated rather than produced a machine).
TRI_BUDGETS = {
    "machine_hero": (8000, 15000),     # groomers, loaders, trucks, big construction
    "machine_small": (1200, 8000),     # snowmobiles, UTVs, walk-behinds, guns, skids
    "attachment": (600, 6000),
    "lift_tower": (900, 6000),
    "lift_terminal": (2500, 12000),
    "lift_carrier": (700, 9000),
    "prop": (200, 6000),
    "tree": (120, 1500),
    "rock": (120, 1500),
}

# LOD ratios: LOD1 keeps ~40 % of LOD0's triangles, LOD2 ~15 %.
LOD_RATIOS = (1.0, 0.40, 0.15)
LOD_COUNT = len(LOD_RATIOS)

# Screen-relative heights handed to the runtime LODGroup (fraction of screen height).
LOD_SCREEN_HEIGHTS = (0.35, 0.12, 0.02)

# Bevel width in metres. Razor edges read as noise under snow lighting; every hard
# surface gets a small chamfer so it catches a highlight.
BEVEL_WIDTH_M = 0.018
BEVEL_SEGMENTS = 1

# Anything smaller than this is not worth a separate object.
MIN_FEATURE_M = 0.02

# --------------------------------------------------------------------------- export
FBX_AXIS_FORWARD = "-Z"
FBX_AXIS_UP = "Y"
FBX_GLOBAL_SCALE = 1.0
EXPORT_GLTF_SIDECAR = False        # FBX is what Unity imports; glTF is opt-in for DCC hand-off

# --------------------------------------------------------------------------- textures
TEX_SIZE_HERO = 2048
TEX_SIZE_PROP = 1024
TEX_SIZE_SMALL = 512
TRIM_SHEET_SIZE = 2048

# Channel packing, published in docs/ART_CONTRACT.md and read by the Unity shader:
#   _MainTex     albedo (RGB), alpha unused
#   _BumpMap     tangent-space normal (RGB)
#   _ORM         R = ambient occlusion, G = roughness, B = metallic
#   _WearMask    R = edge wear, G = rust, B = salt/dirt, A = paint chipping
ORM_CHANNELS = ("occlusion", "roughness", "metallic")
WEAR_CHANNELS = ("edge", "rust", "salt", "chip")

# --------------------------------------------------------------------------- ui
ICON_GRID = 64                     # SVG author grid; every icon is drawn in a 64x64 box
ICON_SCALES = (1, 2, 4)            # rasterised at 64, 128 and 256 px
ICON_STROKE = 4.0                  # stroke weight on the 64 px grid
ICON_RADIUS = 6.0                  # corner radius on the 64 px grid

# --------------------------------------------------------------------------- audio
SAMPLE_RATE = 48000
AUDIO_BITS = 16
ENGINE_BANDS = ("idle", "low", "mid", "high")
LOOP_SECONDS = 2.0                 # engine and machinery loops
AMBIENCE_SECONDS = 6.0


def rel_to_root(path):
    """Repository-relative, forward-slash path for manifests and log lines."""
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def resource_path(path):
    """Resources.Load key for an asset under RES_ROOT (no extension, forward slashes)."""
    rel = os.path.relpath(path, RES_ROOT).replace(os.sep, "/")
    return os.path.splitext(rel)[0]


def ensure_dirs():
    for d in (ART_ROOT, RES_ROOT, MODELS_DIR, TEXTURES_DIR, ICONS_DIR, AUDIO_DIR,
              MACHINES_DIR, ATTACHMENTS_DIR, LIFTS_DIR, PROPS_DIR):
        os.makedirs(d, exist_ok=True)
