# Handoff — FIGHT FOR THE SOUTH

You are taking over an in-progress Unity 6.3 LTS open-world action RPG. Read this file first, then
`Documentation/PROJECT_STATUS.md`. The original full design brief is condensed in `Documentation/MasterGameDesignDocument.md`
and the per-system specs in `Documentation/`.

## State at handoff (2026-11-11, second session)
- **Everything is on GitHub:** repo `Flapjacks7474/Trade-bot`, branch `claude/fight-for-south-foundation-mgjm04` (push works now).
- **Unity project:** milestones 0, 1, 3, 4, 5 built, plus this session's styles, hybrids and customisation rules. Never run in Unity.
- **⚠️ First thing:** this session had **no .NET SDK**, so the C# changes (stances, hybrids, `StyleLoadout`, `Characters/*`,
  17 new tests, `FighterAnimator`) are **uncompiled**. Run `Tools/run-tests.sh` and fix anything before new work. The same
  rules are ported to JS and pass: `cd WebPrototype && node --test tests/*.test.mjs` (28/28).
- **Fighting:** 10 base styles (Street, Boxing, Kickboxing, Muay Thai, Karate, Taekwondo, Kung Fu, Wrestling, Judo & Jujutsu,
  MMA) and 6 hybrids (`Data/Moves/`). Stances change the rules (see `CombatSpecification.md`). The AI round robin keeps every style at 25-75% win rate.
- **Customisation:** `Simulation/Characters/` + `Data/Appearance/appearance_catalog.json` (7 original brands, 45 items,
  16 hairstyles, 11 authored looks; the player default follows the owner's reference). See `CustomizationSpecification.md`.
- **Art pipeline (owner's PC, Windows, Blender 5.2.3 + Storyboard Pro 27.1 trial):** work folder `Documents\FightForTheSouth_Art`.
  Blender is driven by scripts in `Tools/Blender/` (paste a bootstrap into the Text Editor; it pulls the scripts and move data
  from GitHub raw by commit SHA). Storyboard Pro is driven by `Tools/StoryboardPro/` (Script Editor → Sandbox → `include(...)`).
  Read `Documentation/CharacterAndAnimationPipeline.md`.
- **Assets made:** `Assets/_Project/Art/Characters/FFTS_FightRig.fbx` (22-bone Humanoid-named rig, mannequin, 252 clips),
  `WebPrototype/assets/animations/FFTS_FightRig.glb`, `ArtSource/Blender/10_fight_rig.blend`. Storyboard project
  `storyboard/FFTS_FightChoreography` lives on the owner's PC (191 scenes, 16 sequences).
- **Hero model finding:** `player.fbx` has the **hands fused inside the pockets**, no UVs, no textures and short hair. It can't be
  rigged for fighting. The owner needs to regenerate it in Meshy in a T-pose with textures; then fit and skin it to FFTS_Rig.
- **Browser build** not rebuilt (no three.js offline). Published link is still the older build.

## What's next (owner's requests, in order)
1. Compile and run the C# tests; fix.
2. Hero: when the T-pose Meshy export arrives, write `ffts_bind_character.py` (fit joints to mesh landmarks, auto weights,
   export) and swap the mannequin out. Then do the same for the 10 named NPC looks.
3. Animation polish: clips are blocking quality. Mocap or hand polish against `ArtSource/Reports/fight_rig_report.txt`; keep the impact frames.
4. Unity: import FBX as Humanoid, make `FightAnimationLibrary`, add `FighterAnimator` to player/opponent prefabs, play-test.
5. UI for the creator, stores, barber and gyms (learn/equip styles). Give Dre, Rell and Brick their styles (Brick → Wrestling, Rell → Boxing) once balanced in-engine.
6. Environmental combat beyond the wall slam; interiors; then M6 (FINESS OS phone).

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
