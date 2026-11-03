# Project Status

_Last updated: 2026-11-03_

## Current milestone

**Milestone 1 (Core Foundation): complete at the simulation level, unverified in Unity.**
**Milestone 3 (Combat) and parts of 5 and 6:** simulation done and tested. The Unity presentation is written but
has not run in an editor.

The overriding fact: **no Unity editor was available in the development environment.** Everything marked
"verified" below was verified by compiling and running automated tests with .NET 8 against the same source
files. Unity-side scripts were only compile-checked against UnityEngine 2021.3 reference assemblies (see
TEST_RESULTS.md). Nothing has been play-tested.

## Milestone 0 audit (repository as found)

| Question | Finding |
|---|---|
| Files present | `README.md` (6-line trading-bot template snippet), `LICENSE` (GPL-3.0). Nothing else. |
| Valid Unity project | **No.** No `Assets/`, `ProjectSettings/` or `Packages/`. |
| Unity version | None configured. |
| Systems implemented | None. |
| Assets present | None. |
| Dependencies | No Unity editor or .NET SDK in the environment; Unity package registry unreachable. .NET 8 SDK was installed from Ubuntu's archive for testing. |
| Existing errors / risks | Repository is named `Trade-bot`; GPL-3.0 license; no prior work to preserve beyond README text (kept in README). |

## Systems

| System | State | Evidence |
|---|---|---|
| Event bus, IDs, clock, RNG | ✅ Implemented + tested | CoreTests |
| Save system (versioning, migration, checksum, atomic writes, backup, autosave rotation) | ✅ Implemented + tested | SaveSystemTests, CampaignTests |
| Input abstraction + rebinding data | ✅ Simulation tested; ⚠️ Input System adapter not compiled (needs package) | CoreTests |
| Interaction selection | ✅ Tested; ⚠️ Unity component compile-checked only | CoreTests |
| Combat simulation (one complete style, all required mechanics, one finisher) | ✅ Tested | CombatTests (27) |
| Fighter AI (two distinct opponents: Rell/Technical, Brick/Wrestler, plus Dre/Showman tutorial) | ✅ Tested | FighterAITests (8 cases) |
| FINESS BANK (accounts, ledger, idempotency, business separation) | ✅ Tested; no loans yet | BankTests |
| Face Card / Public Image | ✅ Tested | ReputationTests |
| Relationships, personality, memory, rumors | ✅ Tested | SocialTests |
| Story flags + reveal guard | ✅ Tested | StoryGuardTests |
| Missions (prereqs, branches, recovery, replay, reward ledger) | ✅ Tested | CampaignTests |
| Dialogue + NPC conversation rules | ✅ Tested | CampaignTests, ConversationRuleTests |
| Chapter 1 content (6 missions + 1 side) | ✅ Full playthrough test passes via the API | CampaignTests.FullChapterOnePlaythroughReachesChapterTwo |
| Unity slice (graybox Southside, player, camera, NPCs, fights, dialogue, HUD, save/load keys) | ⚠️ Written, compile-checked, **never run** | — |
| Editor tools (scene builder, URP setup, validation, batch build) | ⚠️ Written, **not compiled** (no UnityEditor.dll) | — |

## Not started (by roadmap order)

Vehicles/driving/racing (M4) · NPC schedules and travel (M5) · FINESS OS phone (M6) · rival encounters, crew
behaviour, world events (M7) · music career beyond the first-demo flag (M8) · businesses/property (M9) ·
chapters 2–8 (M10) · other districts (M11) · release prep (M12) · police/law · customization.

## Blocked

- **Runtime verification in Unity**: needs a machine with Unity 6.3 LTS. First actions are in README "Opening in Unity".
- **Final art, animation, audio, voice**: no assets exist; placeholders are documented in ASSET_REGISTER.md.
- **Real-artist likeness/name use**: blocked on rights; the data gate defaults closed.

## Next implementation task

1. Open in Unity 6.3, resolve packages, run **FFTS > Build Southside Slice Scene**, run the EditMode tests in the
   Test Runner, play the slice through Chapter 1, and log every issue in BUG_TRACKER.md.
2. Then Milestone 4 (Driving): `VehicleDefinition`/`VehicleInstance` data plus validation in Simulation (tested), then a
   WheelCollider-based `DrivingController`, enter/exit, garage store/retrieve, and one Southside sprint race.
