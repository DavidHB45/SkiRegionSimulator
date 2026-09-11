"""Deterministic Unity .meta files for generated assets.

Generated assets live in a gitignored folder, but they still need .meta files: the
importer settings decide whether a normal map is treated as a normal map, whether an
ORM pack is sampled linearly, and whether the FBX drags in materials we do not want.
Leaving that to Unity's defaults would quietly break the look.

GUIDs come from the same UUIDv5 derivation tools/gen_meta.py uses, so a generated asset
has the same GUID on every machine and a project that has been built once can be moved
without breaking references.
"""
import importlib.util
import os

from config import ROOT

_spec = importlib.util.spec_from_file_location(
    "alpine_gen_meta", os.path.join(ROOT, "tools", "gen_meta.py"))
_gen_meta = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_gen_meta)

guid_for = _gen_meta.guid_for

TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def _head(rel_path):
    return "fileFormatVersion: 2\nguid: %s\n" % guid_for(rel_path)


def model_meta(rel_path):
    """FBX importer settings.

    - useFileScale/globalScale 1: the exporter already writes metres.
    - materialImportMode 0 (None): ModelRegistry assigns the three runtime materials.
    - importNormals 0 (Import): normals are authored, not recomputed from an angle.
    - importAnimation/Cameras/Lights 0: geometry and transforms only.
    - isReadable 0: nothing reads mesh data on the CPU at runtime.
    """
    return _head(rel_path) + """ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable: []
  externalObjects: {}
  materials:
    materialImportMode: 0
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName: 
    animationImportErrors: 
    animationImportWarnings: 
    animationRetargetingWarnings: 
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalMaterials: 0
    importVisibility: 1
    importBlendShapes: 0
    importCameras: 0
    importLights: 0
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 1
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
    strictVertexDataChecks: 0
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 0
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName: 
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {instanceID: 0}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 0
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
""" + TAIL


def _texture_meta(rel_path, texture_type, srgb, compression_quality=50):
    return _head(rel_path) + """TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: %d
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 4
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: %d
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: %d
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: %d
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag: 
  pSDRemoveMatte: 0
""" % (1 if srgb else 0, compression_quality, texture_type, compression_quality) + TAIL


def albedo_meta(rel_path):
    """Colour map: sRGB, default texture type."""
    return _texture_meta(rel_path, texture_type=0, srgb=True)


def normal_meta(rel_path):
    """Tangent-space normal map: linear, normal-map texture type."""
    return _texture_meta(rel_path, texture_type=1, srgb=False)


def linear_meta(rel_path):
    """Data map (ORM pack, wear masks, detail heights): linear, never colour-managed."""
    return _texture_meta(rel_path, texture_type=0, srgb=False)


def sprite_meta(rel_path):
    """UI icon: sRGB sprite, no mips, point-free bilinear filtering."""
    return _texture_meta(rel_path, texture_type=8, srgb=True)


def audio_meta(rel_path):
    """Looping WAV: decompress on load for short clips, no forced mono conversion."""
    return _head(rel_path) + """AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 1
  preloadAudioData: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
""" + TAIL


def folder_meta(rel_path):
    return (_head(rel_path) + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
            + TAIL)


# --------------------------------------------------------------------------- writing
_SUFFIX_RULES = (
    ("_normal.png", normal_meta),
    ("_orm.png", linear_meta),
    ("_wear.png", linear_meta),
    ("_height.png", linear_meta),
    ("_mask.png", linear_meta),
    ("_detail.png", linear_meta),
)


def meta_for(abs_path):
    """Pick the importer block for a generated file from its path and suffix."""
    rel = os.path.relpath(abs_path, ROOT).replace(os.sep, "/")
    if os.path.isdir(abs_path):
        return folder_meta(rel)
    lower = rel.lower()
    ext = os.path.splitext(lower)[1]
    if ext == ".fbx":
        return model_meta(rel)
    if ext == ".wav":
        return audio_meta(rel)
    if ext == ".png":
        for suffix, fn in _SUFFIX_RULES:
            if lower.endswith(suffix):
                return fn(rel)
        if "/icons/" in lower:
            return sprite_meta(rel)
        return albedo_meta(rel)
    return _head(rel) + "DefaultImporter:\n  externalObjects: {}\n" + TAIL


def write(abs_path):
    """Write <abs_path>.meta next to a generated asset."""
    text = meta_for(abs_path)
    with open(abs_path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def write_tree(root):
    """Write .meta for every folder and file under `root`, including `root` itself."""
    count = 0
    if os.path.isdir(root):
        write(root)
        count += 1
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames.sort()
        for d in dirnames:
            write(os.path.join(dirpath, d))
            count += 1
        for f in sorted(filenames):
            if f.endswith(".meta"):
                continue
            write(os.path.join(dirpath, f))
            count += 1
    return count
