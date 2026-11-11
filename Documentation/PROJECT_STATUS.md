# Project Status

_Last updated: 2026-11-11_

## Current milestone

**Milestone 1 (Core Foundation): complete at the simulation level, unverified in Unity.**
**Milestone 3 (Combat) and parts of 5 and 6:** simulation done and tested. The Unity presentation is written but
has not run in an editor.
**Milestone 4 (Driving):** simulation complete and tested (vehicles, driving model, garages, one race with AI).
Unity driving/racing layer written and compile-checked, not run.
**Milestone 5 (Characters & missions):** complete at the simulation level: NPC schedules, travel along roads, update
tiers, authored personalities and time-aware dialogue join the existing missions, branches, dialogue and relationships.

**Playable now:** a browser version of Chapter 1 (`WebPrototype/`) runs on phones and desktops from a private link. It's built
from the same JSON content, with a line-by-line JavaScript port of the game rules (9 node tests mirror the C# tests, plus a
headless-browser gameplay smoke test). It's the way to play while the Unity build is pending.

**GitHub push is blocked** (403: the Claude GitHub App has no write access to `Flapjacks7474/Trade-bot`). Work is
committed locally and delivered as zip files; push once access is granted.

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
| Vehicle data + validation (3 vehicles, 4 upgrades, 2 garages, fictional makes) | ✅ Tested | VehicleDataTests |
| Driving model (throttle/brake/reverse, steering, handbrake drift, surface grip, nitrous, damage, presets) | ✅ Tested | VehicleDynamicsTests |
| Garages & ownership (buy, sell, store, retrieve, repair, tow/recover, upgrades, garage space) | ✅ Tested | GarageTests |
| Racing (ordered checkpoints, countdown, honest AI, DNF, purses, first-win bonus, ranks) | ✅ Tested | RacingTests |
| Vehicles + racing in saves and mission triggers | ✅ Tested | DrivingSessionTests |
| NPC schedules (10 Southside NPCs, weekly routines, story pins) | ✅ Tested | LivingWorldTests |
| NPC travel along the road graph, no teleporting, Full/Simplified/Distant update tiers, hold while talking | ✅ Tested | LivingWorldTests |
| Authored personalities loaded into relationships | ✅ Tested | LivingWorldSessionTests |
| Time-of-day dialogue variants and activity-aware conversation rules (e.g. Grandma asleep) | ✅ Tested | LivingWorldSessionTests |
| NPC state in saves (mid-trip positions) | ✅ Tested | LivingWorldSessionTests |
| Unity NPC agents (moving capsules / cars, activity labels, talk while walking) | ⚠️ Written, compile-checked, **never run** | — |
| Unity driving (enter/exit, collisions, chase/hood/cockpit cameras, garage panel, race director, HUD) | ⚠️ Written, compile-checked, **never run** | — |
| 10 fighting styles + 6 hybrids (stances change rules; own moves, combos, counters, finishers) | ✅ JS port tested (11 tests incl. AI balance); ⚠️ C# written, **not compiled** | styles.test.mjs, StyleTests |
| Style learning/equipping via FINESS BANK (idempotent), saved | ✅ JS tested; ⚠️ C# not compiled | styles.test.mjs, StyleTests |
| Character appearance + wardrobe + barber + NPC look generator; 45 items, 7 original brands, 11 authored looks | ✅ JS tested; ⚠️ C# not compiled | appearance.test.mjs, AppearanceTests |
| Blender fight rig, mannequin, 252 clips (60 fps, impact on Startup), GLB/FBX export | ✅ Ran in Blender 5.2 on owner's PC; GLB verified | ArtSource/Reports |
| Storyboard Pro fight choreography board (191 scenes, 16 sequences) | ✅ Built in Storyboard Pro 27.1 on owner's PC | TEST_RESULTS |
| Unity `FighterAnimator` (Playables, tick-synced) | ⚠️ Written, **not compiled, never run** | — |

## Not started (by roadmap order)

Traffic, police, other race types (M4 leftovers) · background crowds (M5 leftover) · FINESS OS phone (M6) · rival encounters, crew
behaviour, world events (M7) · music career beyond the first-demo flag (M8) · businesses/property (M9) ·
chapters 2–8 (M10) · other districts (M11) · release prep (M12) · police/law · creator/store/style UI · final character art.

## Blocked

- **Runtime verification in Unity**: needs a machine with Unity 6.3 LTS. First actions are in README "Opening in Unity".
- **Final art, animation, audio, voice**: no assets exist; placeholders are documented in ASSET_REGISTER.md.
- **Real-artist likeness/name use**: blocked on rights; the data gate defaults closed.

## Next implementation task

1. Open in Unity 6.3, resolve packages, run **FFTS > Build Southside Slice Scene**, run the EditMode tests in the
   Test Runner, play the slice through Chapter 1, and log every issue in BUG_TRACKER.md.
2. Drive the Magnolia Cruiser around Southside and run the Pecan Street Sprint in-editor; tune feel.
3. Watch the NPCs go about their day in-editor; check pacing (walkers at 1.6 m/s can take hours of game time to cross the district).
4. Then Milestone 6: FINESS OS phone (Map, Contacts, Messages, Bank, mission tracking) on top of the existing systems.
