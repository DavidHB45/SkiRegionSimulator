# Alpine Resort Simulator.
#
# Two things live behind this file: the simulation, which is C# and is tested with
# `dotnet test`, and the art pipeline, which is Python and turns the same
# Assets/StreamingAssets/Data/*.json the simulation reads into every mesh, texture, icon
# and sound under Assets/Art/Generated.
#
# The asset targets need Python 3.11 and the pinned toolchain in
# tools/assetgen/requirements.txt (`make assets-deps`). Nothing here needs Unity, and
# nothing here needs a Unity licence: `make check` is the same gate CI runs in
# .github/workflows/ci.yml, and `make assets-validate` is the gate in
# .github/workflows/assets.yml.

PYTHON ?= python3
DOTNET ?= dotnet
CONFIGURATION ?= Release

TESTS_PROJECT := sim/AlpineSim.Core.Tests.csproj
CORE_PROJECT := sim/AlpineSim.Core.csproj
COMPILE_CHECK_PROJECT := sim/AlpineSim.Unity.CompileCheck.csproj

# tools/assetgen/config.py owns where generated art goes, so ask it rather than keep a
# second copy of the path here. The fallback covers a machine with no python3 on PATH,
# where every asset target is going to fail with a clearer message anyway.
ART_DIR := $(shell $(PYTHON) -c "import sys; sys.path.insert(0, 'tools/assetgen'); import config; print(config.rel_to_root(config.ART_ROOT))" 2>/dev/null || echo Assets/Art/Generated)

.DEFAULT_GOAL := help

.PHONY: help assets assets-meshes assets-textures assets-icons assets-audio \
        assets-clean assets-deps assets-validate assets-selftest \
        test build compile-check meta tuning-doc check

# --------------------------------------------------------------------------- assets
assets: ## Build every generated asset from Data/*.json into Assets/Art/Generated
	$(PYTHON) tools/assetgen/build_all.py

assets-meshes: ## Build models only (machines, attachments, lifts, props)
	$(PYTHON) tools/assetgen/build_all.py --only meshes

assets-textures: ## Build the trim sheet and every material set
	$(PYTHON) tools/assetgen/build_all.py --only textures

assets-icons: ## Build UI icons, HUD glyphs and map markers
	$(PYTHON) tools/assetgen/build_all.py --only icons

assets-audio: ## Build engine, machinery and ambience loops
	$(PYTHON) tools/assetgen/build_all.py --only audio

assets-deps: ## Install the pinned asset toolchain (bpy is a ~500 MB wheel)
	$(PYTHON) -m pip install -r tools/assetgen/requirements.txt

assets-selftest: ## Check the pipeline's own guarantees: axes, pivots, LODs, budgets
	$(PYTHON) tools/assetgen/selftest.py

# Deleting the tree rather than rebuilding over it, because a generator that stops
# emitting an asset would otherwise leave last week's copy behind and nobody would notice
# until it shipped.
assets-clean: ## Delete Assets/Art/Generated
	@test -n "$(ART_DIR)" || { echo "cannot locate the output tree; is $(PYTHON) on PATH?"; exit 1; }
	rm -rf "$(ART_DIR)"
	@echo "removed $(ART_DIR)"

# What CI runs on a pull request that touches the data or the generators: a full build
# from nothing, which fails on any contract violation, so a JSON edit that breaks a
# generator is caught on the pull request that makes it rather than a week later.
assets-validate: ## Self-test, then rebuild everything from clean and report warnings
	$(MAKE) assets-selftest
	$(MAKE) assets-clean
	$(MAKE) assets
	@$(PYTHON) -c "import json, sys; \
	  r = json.load(open('$(ART_DIR)/validation.json')); \
	  w = r.get('warnings', []); \
	  kinds = sorted({p['kind'] for p in w}); \
	  print('%d warnings%s' % (len(w), (' (' + ', '.join(kinds) + ')') if kinds else ''))"

# --------------------------------------------------------------------------- sim
test: ## Run the simulation test suite (no Unity needed; takes about ten minutes)
	$(DOTNET) test $(TESTS_PROJECT) --configuration $(CONFIGURATION)

build: ## Build the simulation core through the /sim mirror csproj
	$(DOTNET) build $(CORE_PROJECT) --configuration $(CONFIGURATION)

compile-check: ## Compile the Unity layer against the API stubs in sim/UnityStubs
	$(DOTNET) build $(COMPILE_CHECK_PROJECT) --configuration $(CONFIGURATION)

meta: ## Write any missing .meta files under Assets/
	$(PYTHON) tools/gen_meta.py

tuning-doc: ## Regenerate docs/TUNING.md from tuning.json
	$(PYTHON) tools/gen_tuning_doc.py

# The same steps, in the same order, as the sim-test job in .github/workflows/ci.yml.
# If this passes locally the pull request is green.
check: ## Everything CI checks: metas, tuning doc, engine-free Core, tests, compile check
	$(PYTHON) tools/gen_meta.py --check
	$(PYTHON) tools/gen_tuning_doc.py --check
	@if grep -rn --include=*.cs -E "UnityEngine|UnityEditor" Assets/Scripts/Core; then \
	   echo "AlpineSim.Core references UnityEngine/UnityEditor"; exit 1; fi
	@echo "Core is engine-free"
	$(MAKE) test
	$(MAKE) compile-check

# --------------------------------------------------------------------------- help
help:
	@echo "Alpine Resort Simulator"
	@echo ""
	@grep -E '^[a-zA-Z0-9_-]+:.*## ' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*## "}; {printf "  %-18s %s\n", $$1, $$2}'
	@echo ""
	@echo "  docs/ASSET_PIPELINE.md  how the asset build works"
	@echo "  docs/ART_CONTRACT.md    what it is allowed to emit"
