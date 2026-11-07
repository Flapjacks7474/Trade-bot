# Changelog

All notable changes. Dates are UTC.

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
