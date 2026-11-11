# Asset Register

No third-party or final assets exist in the repository. Everything visible at runtime is generated placeholder content.

| Asset | Type | Source | License | Import status | Platforms | Notes |
|---|---|---|---|---|---|---|
| Graybox buildings, roads, ground | Runtime-generated primitives | Unity built-in Cube | Unity built-in | Generated at Play by `SliceBuilder` | All | **Placeholder** |
| Characters (player, NPCs, opponents) | Runtime-generated capsules | Unity built-in Capsule | Unity built-in | Generated at Play | All | **Placeholder.** `FighterDefinition.PlaceholderModel` ids reserve slots |
| Vehicles (3) | Runtime-generated boxes | Unity built-in Cube | Unity built-in | Generated at Play | All | **Placeholder.** Makes (Delmont, Hoshiro, Crestmore) are fictional; prefab keys reserved |
| World labels | TextMesh + built-in font | `LegacyRuntime.ttf` / `Arial.ttf` | Unity built-in | Runtime | All | **Placeholder** |
| HUD / dialogue / fight bars | IMGUI | Code | Project | Runtime | PC | **Placeholder** (TD-011) |
| Content data (missions, dialogue, moves, fighters, world, crews, districts) | JSON | Original, authored in this repo | Project license (see README) | In repo | All | Original writing |
| Music, SFX, voice | — | — | — | **None present** | — | Must be original or licensed |
| Animations (178-slot target) | — | — | — | **None present** | — | See AssetRequirements.md |

## Likeness and name rights

| Casting reference | In-game placeholder | Rights status |
|---|---|---|
| Future | Aurelius "Goldie" Crane | Not cleared |
| Young Thug | Jaylen "Verde" Moss | Not cleared |
| 21 Savage | Silas "Blade" Mercer | Not cleared |
| Lil Baby | Darius "Blueprint" Hale | Not cleared |
| 2 Chainz | Cornelius "Two Rings" Bishop | Not cleared |

Supporting-roster casting references (CharacterDatabase.md) are design notes only and are not in shipped data.

## Added 2026-11-11 (original, made for this project)
| Asset | Path | Source | Licence | Notes |
|---|---|---|---|---|
| FFTS fight rig + mannequin + 252 clips | `Assets/_Project/Art/Characters/FFTS_FightRig.fbx`, `WebPrototype/assets/animations/FFTS_FightRig.glb` | Generated in Blender 5.2 by `Tools/Blender/ffts_fight_rig.py` | Project-owned | Blocking-quality previs; replace the mannequin with final character meshes |
| Blender source | `ArtSource/Blender/10_fight_rig.blend` | same | Project-owned | Rebuildable from the scripts |
| Fight choreography storyboard | owner's PC: `Documents\FightForTheSouth_Art\storyboard\FFTS_FightChoreography` | Storyboard Pro 27.1 + Blender renders | Project-owned | 191 scenes / 16 sequences |
