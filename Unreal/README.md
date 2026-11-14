# FIGHT FOR THE SOUTH — Unreal Engine 5.8 build

The main build going forward. The Unity project (`Assets/`) and the browser version (`WebPrototype/`) stay as references.

## Layout
| Path | What |
|---|---|
| `FightForTheSouth/FightForTheSouth.uproject` | UE 5.8 C++ project |
| `Source/FFTSCore/` | **Portable C++17 simulation** (combat, styles, stances, hybrids, AI, loadout, JSON). No engine types; port of the C# `FFTS.Simulation` and `WebPrototype/src/sim`. |
| `Source/FightForTheSouth/` | Unreal game module: `UFFTSGameSubsystem` (content + progression), `AFFTSFightDirector` (60 Hz fixed-step fight, camera, clip selection), `AFFTSFighterCharacter` (plays clips by AnimationKey), `AFFTSPlayerController` (keyboard + gamepad), `AFFTSHUD`, `AFFTSGameMode` (builds a lit arena in any empty level). |
| `Content/FFTSData/` | Content JSON, **copied** from `Assets/_Project/Data` by `Tools/sync-unreal-data.sh` (single source of truth stays in `Assets/_Project/Data`). Staged as raw files. |
| `Content/Python/ffts_import_fight_rig.py` | Editor script: imports the Blender fight rig + 252 clips at 60 fps into `/Game/FFTS`, creates the Dojo map. |
| `CoreTests/` | `run.sh` builds and runs the core tests with g++/clang — no engine needed (13 tests). |

## First run on a PC
1. Install **Visual Studio 2022** with the *Game development with C++* workload (Unreal 5.8 needs MSVC to build C++ projects).
2. Open `FightForTheSouth.uproject` → Yes to build the modules.
3. In the editor: Tools → Execute Python Script → `Content/Python/ffts_import_fight_rig.py`.
4. Play. Dojo controls are on the HUD (Tab/Q change your styles, V opponent style, N next opponent).

## Status (2026-11-11)
- FFTSCore: **13/13 tests pass** with g++ 13 and clang (warnings as errors). Balance round robin matches the JS port within ±0.03.
- Unreal module: written against UE 5.x APIs; **not yet compiled** — the PC had no Visual Studio. Expect a first round of compile fixes.

## Manny (realistic body)

After `ffts_import_fight_rig.py`, run `Content/Python/ffts_retarget_manny.py` (console: `py "<path>"`). It copies the UE5
Mannequin from the Game Animation Sample project on the PC (`Documents/Unreal Projects/GameAnimationSample`), builds
`IK_FightRig`, `IK_FFTS_Manny` and `RTG_FightRig_to_Manny` in `/Game/FFTS/Retarget`, and retargets all clips into
`/Game/FFTS/AnimManny`. `AFFTSFighterCharacter` uses Manny automatically when those clips exist (`bPreferManny`).

Known limit: the Blender rig stores bone translations in metres under a 100x armature scale, so the retargeter keeps
rotations only (pelvis translation, IK and root-motion ops are off). Hip height changes in crouches/kicks are lost until
the rig is re-exported at 1.0 scale in centimetres. `ffts_retarget_inspect.py` dumps the chain mapping and bone poses.
