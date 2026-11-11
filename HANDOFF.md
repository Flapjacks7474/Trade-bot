# Handoff — FIGHT FOR THE SOUTH

You are taking over an in-progress Unity 6.3 LTS open-world action RPG. Read this file first, then
`Documentation/PROJECT_STATUS.md`. The original full design brief is condensed in `Documentation/MasterGameDesignDocument.md`
and the per-system specs in `Documentation/`.

## State at handoff (2026-11-11)
- **Everything is on GitHub:** repo `Flapjacks7474/Trade-bot`, branch `claude/fight-for-south-foundation-mgjm04`.
- **Unity project (the real game):** milestones 0, 1, 3, 4, 5 built. Game rules pass **134 automated tests** (`Tools/run-tests.sh`,
  .NET 8). The Unity layer compiles against Unity reference libraries but has **never been run in a Unity editor**.
- **Playable browser version** (`WebPrototype/`): Chapter 1 in Southside with fights, cars, the Pecan Street Sprint, NPC schedules
  and saves. Published link (older build, before the graphics upgrade): https://claude.ai/artifact/CV2ZgWjGkitz2gnQwXVdEj
- **Graphics upgrade (committed, not yet republished):** generated PBR textures, sidewalks, detailed buildings, trees, lamps,
  parked cars, sky with time of day, bloom, quality presets, CC0 KayKit animated characters (`WebPrototype/assets/LICENSES.md`).
- **Owner's hero model:** `WebPrototype/assets/characters/player.fbx` (Meshy export). One static mesh, ~282k vertices,
  **no skeleton, no textures, no animations**; shown on the title screen, coloured by body region in code. It cannot animate
  until it is rigged.

## What the owner asked for next (not started)
1. **Look:** people currently read as "Roblox" (KayKit chibi proportions). Wanted: Saints Row / Watch Dogs realism with the style
   of the owner's reference character (stylised streetwear, dreads, beanie, chains, cargo pants). Use original branding: the
   reference image shows third-party logos (XO, Nike-style shoes) that must not ship.
2. **Fighting:** 10+ distinct styles (Street Brawling, Boxing, Kickboxing, Muay Thai, Karate, Taekwondo, Kung Fu, Wrestling,
   Judo/Jujutsu, MMA), each with its own stance, moves, combos, defence and finishers, plus two-style hybrids with their own
   movesets. The data framework already supports styles (`Moveset.Build` merges up to three); only Street Fighting is authored.
   Owner prefers fewer styles with genuinely unique animation over ten that look alike.
3. **Characters:** every character distinct (face, build, height, hair, clothes); modular player customisation.
4. **Environmental combat** beyond the existing wall slam; interiors; NPC variety.
5. Tools the owner wants used: Unity, Blender, Storyboard Pro (none were available in the cloud session; Blender install was interrupted).

## Honest blockers
- Realistic rigged characters and per-style fight animations need real assets: mocap or hand animation, or CC0 libraries
  (e.g. Quaternius Universal Animation Library), or Meshy's own rig-and-animate export. Code alone cannot produce AAA animation.
- Best path for the hero: regenerate in Meshy in a T-pose **with textures**, use Meshy auto-rigging, export GLB.

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
