# Handoff — FIGHT FOR THE SOUTH

You are taking over an in-progress Unity 6.3 LTS open-world action RPG. Read this file first, then
`Documentation/PROJECT_STATUS.md`. The original full design brief is condensed in `Documentation/MasterGameDesignDocument.md`
and the per-system specs in `Documentation/`.

## State at handoff (2026-11-07)
- Milestones done (simulation tested; Unity layer compile-checked but **never run in an editor**):
  0 audit · 1 core foundation · 3 combat · 4 driving/racing · 5 characters/missions (schedules, travel, Chapter 1).
- **134 automated tests, all passing**, via `Tools/run-tests.sh` (.NET 8 SDK, no Unity needed).
- Git: branch `claude/fight-for-south-foundation-mgjm04`, 4 commits on top of `main`. **Not pushed**: the previous agent had
  no GitHub write access (HTTP 403). If you received this as a zip, `git init` it or upload it to a new repository.

## Ground rules that keep the project healthy
1. **Gameplay rules go in `Assets/_Project/Scripts/Simulation/`** (assembly `FFTS.Simulation`, `noEngineReferences`). No
   `UnityEngine` there. It must compile as netstandard2.1 / C# 9 (no records, no file-scoped namespaces, no `init`).
2. **Unity code goes in `Assets/_Project/Scripts/Runtime/`** and only presents and drives the simulation.
3. **Every money or reward movement uses an idempotency key** (`FinessBank.Post`/`Pay`, mission reward ledger). Never pay twice.
4. **Content is JSON** in `Assets/_Project/Data/`, validated by `GameContentLoader` and cross-checked by tests. Add new content
   files to `ContentSources`, `ContentLibrary` and `SliceSceneBuilder.BuildContentLibrary`.
5. **Add tests with every feature** (`Assets/_Project/Tests/EditMode/`). Run `Tools/run-tests.sh` before committing.
6. **Never claim something ran in Unity unless it did.** Update `Documentation/TEST_RESULTS.md`, `PROJECT_STATUS.md`,
   `BUG_TRACKER.md`, `KNOWN_LIMITATIONS.md` and `CHANGELOG.md` honestly.
7. The Chairman reveal flag `story.chairman.identity_revealed` is code-guarded to Chapter 7. Don't work around it.
8. Real artists' names stay behind `CastingInfo.LikenessRightsCleared` (default false). No copyrighted music or lyrics.

## First thing to do (needs a machine with Unity 6.3 LTS)
1. Open the folder in Unity. Fix any package versions Package Manager rejects (`Packages/manifest.json` versions are guesses).
2. Project Settings → Player → Active Input Handling → *Input System Package (New)*.
3. Menu **FFTS > Build Southside Slice Scene**, then Play. Run **Test Runner > EditMode**.
4. Play Chapter 1, buy the Magnolia Cruiser at Southside Motors, run the Pecan Street Sprint, and log issues in `BUG_TRACKER.md`.
5. Commit the generated `.meta` files.

## Next milestones (in order; see `Documentation/DevelopmentRoadmap.md`)
- **M6 FINESS OS phone:** Map, Contacts, Messages, Bank, mission tracking, Settings (pause/slow on open). Back each app with
  existing systems (`FinessBank.Ledger`, `PlayerIdentity.Contacts`, `MissionManager`, `LivingWorldManager`). Replace IMGUI with uGUI or UI Toolkit.
- **M7 living world:** rival recognition, dynamic encounters, temporary alliances, crew behaviour, world events (build on `SocialManager`).
- **M8 music career · M9 businesses/property · M10 chapters 2–8 (`Documentation/StoryBible.md`) · M11 full city · M12 release prep.**

## Where things are
| Need | Look at |
|---|---|
| Everything one session owns | `Simulation/Session/GameSession.cs` |
| Fight rules / AI | `Simulation/Combat/CombatSimulation.cs`, `FighterAI.cs`, `Data/Moves/street_fighting.json` |
| Missions / dialogue / who-says-what | `Simulation/Missions/`, `Simulation/Dialogue/`, `Data/Missions/`, `Data/Dialogue/` |
| Cars / races | `Simulation/Vehicles/`, `Simulation/Racing/`, `Data/Vehicles/`, `Data/Racing/` |
| NPC schedules | `Simulation/LivingWorld/`, `Data/Characters/npc_profiles.json` |
| Saves | `Simulation/SaveSystem/`, `GameSession.Capture/Restore` |
| Unity entry point | `Runtime/Core/CoreGameManager.cs`, `Runtime/World/SliceBuilder.cs` |
| Why things are the way they are | `Documentation/TECHNICAL_DECISIONS.md` |
