# Changelog

All notable changes. Dates are UTC.

## [0.5.0-styles-characters] — 2026-11-11

### Added
- **Nine new fighting styles** (Boxing, Kickboxing, Muay Thai, Karate, Taekwondo, Kung Fu, Wrestling, Judo & Jujutsu, MMA),
  each with a stance that changes the rules (damage by kind, parry and counter windows, chip, guard drain, dodge
  distance and cost, escapes, clinch strikes), its own moves, combos, counter, reversal and finisher.
- **Six hybrids** with exclusive moves, cross-style combo links, their own stance and finisher; they apply when both parents are equipped and learned.
- `StyleLoadout`: learn styles through FINESS BANK (`style.learn.<id>`, never charged twice), equip up to two, saved in games.
- **Character customisation rules:** `CharacterAppearance` (body, face sliders, skin, eyes, hair, facial hair, 8 outfit slots),
  catalog validation, `WardrobeManager` (creator, stores, barber, idempotent purchases, saves), deterministic NPC look generator
  that rejects look-alikes. Content: 7 original brands, 45 items, 16 hairstyles, 6 stores, 11 authored looks (player
  default after the owner's reference character).
- **Blender pipeline** (`Tools/Blender/`): FFTS_Rig (Humanoid-named skeleton), fight mannequin, 252 animation clips generated from
  the move data at 60 fps (impact on frame `Startup`), GLB and FBX export, storyboard frame renders.
- **Storyboard Pro pipeline** (`Tools/StoryboardPro/`): generator for a board script that builds one sequence per style, an
  intro scene per style and a 3-panel scene per move with action, camera and timing captions.
- Unity: `FighterAnimator` + `FightAnimationLibrary` (Playables, tick-synced clips); `FightDirector` uses the player's style loadout.
- JS port of styles, stances, hybrids, loadout and appearance; 19 new node tests including an AI round-robin balance check.
- 17 new C# EditMode tests (`StyleTests`, `AppearanceTests`). **Not compiled or run:** no .NET SDK in this session.

### Changed
- Balance from the round robin: Karate and Taekwondo ranges trimmed; Wrestling and Judo strikes and throws buffed.
- `GameSaveData` gains `Styles` and `Wardrobe` (older saves load with the starter style and default look).

## [0.4.0-playable-web] — 2026-11-08

### Added
- Browser-playable version of Chapter 1 (`WebPrototype/`): 3D Southside from the layout data, touch and keyboard controls,
  missions and dialogue, fights against Dre, Rell and Brick, NPC schedules, garage, driving and the Pecan Street Sprint, device saves.
- JavaScript port of the simulation (combat, AI, story, missions, vehicles, racing, living world) with node tests mirroring the C# suite.
- Headless-browser smoke test that plays through walking, talking, fighting, buying, driving, racing, save/reload and the phone layout.

### Fixed (found by the smoke test)
- Hidden screens could still block taps (now `[hidden]` always wins).
- An autosave triggered right as a fight began was skipped; autosaves now retry when nothing blocks them.
- Name signs covered the view up close; signs fade with distance, sleeping NPCs stay indoors, phone HUD no longer overlaps.

## [0.3.0-living-world] — 2026-11-07

### Added (Milestone 5 — Characters & missions)
- Road graph from district layout data (intersections, Dijkstra routing).
- NPC profiles for 10 Southside characters: authored personalities, homes, walk/drive, weekly routines, story pins.
- `LivingWorldManager`: schedule evaluation with story pins first, continuous travel along roads, Full/Simplified/Distant
  update tiers, hold-while-talking, arrival/departure events, save/restore mid-trip.
- Time-of-day dialogue conditions and activity-aware conversation rules (Grandma asleep at night, still available for story).
- Personalities now drive relationship scaling for every scheduled NPC.
- Unity: `NpcAgent` views (smoothed movement, car while driving, activity labels); fight witnesses from nearby NPCs.
- 14 new tests (134 total, all passing).

### Changed
- Dre's courts → gym move now comes from his schedule pins; the placement-visibility workaround was removed from the data.

## [0.2.0-driving] — 2026-11-07

### Added (Milestone 4 — Driving)
- Vehicle data model with validation; 3 vehicles (fictional makes), 4 upgrades, 2 garages.
- Deterministic driving model: traction-limited launch, braking/reverse, speed-scaled steering, handbrake drift, surface grip
  (road vs off-road from layout data), nitrous kit, impact damage and wrecks, Arcade/Street/Expert handling presets.
- Garage manager: buy (idempotent, persisted), sell, store, retrieve, repair, tow, upgrades, extra garage space.
- Racing: Pecan Street Sprint with countdown, ordered checkpoints, honest AI rivals (Rell, Deuce), purses, first-win bonus,
  racing rank, best times, reputation/relationship consequences, mission triggers.
- Vehicles and racing persisted in saves; content cross-check covers race and garage locations.
- Unity layer: vehicle views with box-swept collisions, driving director (enter/exit, park, wreck handling), chase/hood/cockpit
  cameras, race director with checkpoint marker and HUD, IMGUI garage panel; new CameraView action (C / right-stick click).
- 38 new tests (120 total, all passing).

### Fixed
- BUG-008 … BUG-011 (see Documentation/BUG_TRACKER.md).

## [0.1.0-foundation] — 2026-11-03

### Added
- Unity 6.3 LTS project skeleton: `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` (URP, Input System,
  Cinemachine, Addressables, Test Framework, Newtonsoft JSON, uGUI, AI Navigation), assembly definitions.
- `FFTS.Simulation` engine-independent assembly:
  - Core: typed event bus with failure isolation and loop detection, stable ID rules and registry, game clock with
    time segments, deterministic serializable RNG.
  - Save system: checksummed versioned envelope, migration chain, atomic file writes with backup, corruption
    fallback, refusal of newer-version saves, rotating autosaves, path-safe slot keys.
  - Input abstraction: device-independent actions, default PC + gamepad bindings, rebinding, conflict detection.
  - Interaction selection (range, facing, priority).
  - Combat: deterministic 60 Hz 1v1 simulation (all spec states plus HitStun/BlockStun/Dodging/Grappled/FinisherVictim),
    Street Fighting style (17 moves, 7 combo links), blocking, parry, counter stance, grapples, clinch strikes, throws,
    wall throws, reversals, clinch escape, knockdowns, quick rise, ground attacks, breakaway, finisher rules,
    stamina, momentum, damage and hitstun scaling, round timer and decisions.
  - Fighter AI: five styles, five difficulties, delayed perception (no input reading), habit-learning opponent model.
  - FINESS BANK with idempotent ledger, personal/business separation; Face Card and Public Image.
  - Relationships, personality, NPC memory with witness/rumor knowledge and attitude evaluation.
  - Story flags with the Chairman-reveal guard; mission manager with prerequisites, branches, checkpoint recovery,
    replay sandboxing and a persistent one-time reward ledger; dialogue runner; data-driven NPC conversation rules.
  - `GameSession` wiring all systems plus full save capture/restore.
- Content: Chapter 1 (six main missions plus *Grandma's Errand*), 10 dialogues, Southside layout (13 locations, 10 NPC
  placements), 8 districts, 5 crews with likeness-rights gate, 4 fighters.
- `FFTS.Runtime` Unity layer: core manager, Input System source with legacy fallback, player motor, orbit/fight camera,
  interaction, data-driven graybox slice builder, fight director, fighter views, dialogue panel, HUD.
- Editor tools: scene builder, URP setup, content validator, batch Windows build.
- 82 automated tests; documentation set under `Documentation/`.
