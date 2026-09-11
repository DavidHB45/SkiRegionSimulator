# Assets/Art

Two trees live here, and only one of them is in the repository.

- **`Generated/`** — build output of `tools/assetgen`, produced by `make assets` from
  `Assets/StreamingAssets/Data/*.json`. Gitignored. Delete it at any time: the game
  falls back to procedural primitives and stays fully playable.
- **`Authored/`** — hand-made or purchased models, if any are ever commissioned. A
  record in `vehicles.json`, `attachments.json` or `lifts.json` points at one with
  `"modelOverride": "Art/Authored/<path>"`, which `ModelRegistry` resolves ahead of the
  generated asset. Nothing here is generated and nothing in code needs to change to
  adopt one.

`docs/ART_CONTRACT.md` is the interface both tiers must satisfy;
`docs/ASSET_PIPELINE.md` explains how to run and extend the generator.
