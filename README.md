# FIGHT FOR THE SOUTH

> *Nobody owns the South. Until somebody earns it.*

Open-world Southern hip-hop action RPG set in **Finess City**, combining street fighting, a music career,
racing, business ownership, crew politics and a branching family story. Target engine: **Unity 6.3 LTS (URP)**, PC first.

**Current state:** foundation milestone. The gameplay rules ship in an engine-independent C# assembly with
an automated test suite (134 tests, all passing — see `Documentation/TEST_RESULTS.md`). A Unity
vertical-slice layer builds a graybox Southside from data. **The Unity project has not yet been opened in a Unity
editor**, so nothing here has been verified at runtime inside Unity. Read `Documentation/PROJECT_STATUS.md` first.

## Repository layout

| Path | What it is |
|---|---|
| `Assets/_Project/Scripts/Simulation/` | `FFTS.Simulation` — all gameplay rules, no `UnityEngine` references (`noEngineReferences`) |
| `Assets/_Project/Scripts/Runtime/` | `FFTS.Runtime` — MonoBehaviours: input, player, camera, interaction, fight presentation, HUD, slice builder |
| `Assets/_Project/Scripts/Editor/` | `FFTS.Editor` — scene builder, URP setup, content validation, batch build |
| `Assets/_Project/Data/` | JSON content: missions, dialogue, moves, fighters, world layout, districts, crews |
| `Assets/_Project/Tests/EditMode/` | NUnit tests (run by Unity Test Runner **and** by `dotnet test`) |
| `Tests/DotNet/` | .NET projects that compile the simulation as netstandard2.1 / C# 9 and run the tests outside Unity |
| `Tools/` | `run-tests.sh`, Unity runtime compile-check project |
| `Documentation/` | Design, architecture, status, test results, decisions, limitations |

## Running the tests (no Unity needed)

```bash
# requires the .NET 8 SDK
Tools/run-tests.sh
```

## Opening in Unity (first time)

1. Open the folder with **Unity 6.3 LTS**. Let Package Manager resolve `Packages/manifest.json`. The package
   versions are unverified, so fix any it rejects (see `KNOWN_LIMITATIONS.md`).
2. **Edit > Project Settings > Player > Active Input Handling** → *Input System Package (New)*.
3. Menu **FFTS > Build Southside Slice Scene** (this also creates and assigns the URP asset).
4. Open `Assets/_Project/Scenes/Southside_Slice.unity` and press **Play**.
5. Run **Window > General > Test Runner > EditMode** to execute the same test suite inside Unity.
6. Commit the generated `.meta` files.

Batch Windows build: `Unity -batchmode -quit -projectPath . -executeMethod FFTS.EditorTools.BuildScripts.BuildWindows`

## Controls (PC)

WASD move · Mouse camera · Shift sprint/dodge · E interact · J light · K heavy · I kick · L grapple ·
Space block (tap just before impact = parry-counter) · O counter stance · U finisher · T taunt · F5/F9 quicksave/quickload · F1 help.

Driving: W/S throttle/brake (S reverses when stopped) · A/D steer · Space handbrake · Shift nitrous (with kit) · C camera · E get out / park / start race.

## Licensing and likeness notice

Crew leaders are cast in the design documents as fictionalized versions of real artists. **No participation or
approval by any real person is implied or has been obtained.** In-game data shows original placeholder names
unless `LikenessRightsCleared` is set, and that must only happen with a signed agreement on file.
All music, art and audio must be original or properly licensed (`Documentation/ASSET_REGISTER.md`).

The repository currently carries a **GPL-3.0 `LICENSE`** inherited from its initial commit. That is likely
incompatible with commercial console/mobile distribution and with licensed third-party assets. The project
owner should decide on the license before external contributions or release.

---

### Previous README content (preserved)

This repository started as `Trade-bot`. Its original README contained only:

```
# Clone the template
git clone https://github.com/ai-trader-prod/final-deployment.git
cd final-deployment

# Create your new repo
gh repo create ai-trading-bot-prod --private --push --source .
```
