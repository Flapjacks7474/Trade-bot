# Third-party assets in the browser build

Every file here is public domain (CC0 1.0). No attribution is required; credits are given anyway.

| File | Source | Author | License | Notes |
|---|---|---|---|---|
| `characters/Mannequin_Medium.glb` | KayKit Character Animations 1.1 (neutral mannequin, `Rig_Medium`), obtained via github.com/NoLimitNexus/game-models `tools/reference/` | Kay Lousberg (kaylousberg.com) | CC0 1.0 (`characters/LICENSE-KayKit.txt`) | Only KayKit's mannequin is used; that repo's own body/parts (no license file) are **not** used |
| `animations/Rig_Medium_CombatMelee.glb`, `_General.glb`, `_MovementBasic.glb`, `_MovementAdvanced.glb` | KayKit Character Animations 1.1, via the same repo `library/animations/` | Kay Lousberg | CC0 1.0 | `Rig_Medium_Ours.glb` (that repo's own clips) is **not** used |
| `env/city.exr` | Poly Haven HDRI, resized by @pmndrs/assets 1.7.0 (`hdri/city.exr`) | Poly Haven | CC0 1.0 (`env/LICENSE-CC0.txt`) | Used for image-based lighting and reflections |

Everything else visible in the game (asphalt, sidewalks, brick, siding, windows, signs, grass, trees, lamps, cars) is
generated in code at runtime (`src/game/textures.js`, `src/game/world3d.js`), so it has no third-party license.
