# Technical Decisions

Each entry: decision · reason · tradeoffs · rejected alternatives.

## TD-001 — Gameplay rules live in an engine-independent assembly
`FFTS.Simulation` has `noEngineReferences: true`. All rules (combat, economy, missions, memory, saves) are plain C#.
- **Why:** determinism, fast tests outside the editor (the dev environment had no Unity), clean separation of
  gameplay and presentation as the brief requires, and future server-authoritative multiplayer.
- **Tradeoffs:** a bridge layer (`FFTS.Runtime`) is needed. Vectors use `System.Numerics` in simulation and `UnityEngine` in runtime.
- **Rejected:** MonoBehaviour-centric logic (untestable without the editor, encourages monolithic scripts).

## TD-002 — Compile the simulation as netstandard2.1 / C# 9 in CI
`Tests/DotNet/Simulation/FFTS.Simulation.csproj` targets Unity's API surface and language level, with warnings as errors.
- **Why:** catches use of APIs or syntax Unity can't compile (records, file-scoped namespaces, newer BCL) without Unity.
- **Tradeoffs:** the same tests also need Unity's Test Runner to confirm the editor environment.

## TD-003 — Fixed-tick deterministic combat (60 Hz), 1D fight line
- **Why:** frame data (startup/active/recovery) must be exact for a fighting-game feel; determinism allows replays,
  AI regression tests and later netcode.
- **Tradeoffs:** fights happen on a line through the world (Def Jam-style arena feel), not free 3D. Group combat
  (RivalEncounter) will need an N-fighter extension with lane/target selection.
- **Rejected:** physics-driven hit detection (nondeterministic, hard to tune, expensive on mobile).

## TD-004 — Rock-paper-scissors core with explicit sub-states
Strikes beat grapples, grapples beat blocks/counters, blocks/counters beat strikes, dodges beat all during
invulnerability but cost stamina. Added states: HitStun, BlockStun, Dodging, Grappled, FinisherVictim.
- **Parry on PC:** the key list has no counter key, so a tap of Block within the Technique-scaled parry window counters.
  The window is measured from the *press edge*, so holding block never auto-parries (tested).

## TD-005 — Infinite-combo prevention is layered
Finite combo DAG (validator rejects cycles) + damage scaling + hitstun decay + stamina costs + breakaway escape.
Each layer has a test.

## TD-006 — AI perceives through delayed snapshots
The AI never reads opponent input. It sees opponent state `ReactionTicks` late and learns habits through decayed
counts. Normal reaction (14 ticks) is shorter than the haymaker's 16-tick startup, so heavies are reactable while jabs
(≈5) are not. Tuned after a test showed the original 20-tick reaction made all defence luck-based.

## TD-007 — Idempotency keys everywhere money or rewards move
Bank posts require a key; missions grant rewards under `reward.<mission>.<n>` and `branch.<mission>.<branch>.<n>`;
dialogue effects under `dialogue.<id>.<option>.<n>`. The reward ledger is saved. Replays run sandboxed.
- **Tradeoff:** ambient repeatable dialogue can't grant repeated effects. Intended: those need explicit keys per occurrence.

## TD-008 — Content as JSON TextAssets referenced by a ScriptableObject
`ContentLibrary` (ScriptableObject) lists TextAssets so the identical files are validated by the test suite.
- **Addressables:** planned for streaming district content (M11). Not used yet, because one district fits in memory and
  Addressables couldn't be verified without the editor.
- **Rejected for now:** ScriptableObject-per-move authoring (not testable outside Unity, merge-unfriendly).

## TD-009 — Hand-written camera rig instead of Cinemachine (temporary)
Cinemachine 3 is in the manifest, but its API couldn't be compile-checked here. `CameraRig` is small and isolated
behind explore/fight/finisher modes, so swapping in Cinemachine later is local. **Action:** replace in M2 polish once in-editor.

## TD-010 — Input System actions are built in code from engine-independent binding data
`InputBindingMap` holds defaults and overrides (saved with settings later). `InputSystemSource` creates actions at
runtime, and a legacy fallback compiles when `ENABLE_INPUT_SYSTEM` is absent. Kick is bound to **I** (not in the brief's list).

## TD-011 — Placeholder UI is IMGUI
The HUD, dialogue and fight bars use `OnGUI`: zero assets, works immediately, but is slow and not mobile-friendly.
FINESS OS and final HUD will be uGUI/UI Toolkit (KNOWN_LIMITATIONS).

## TD-012 — Graybox world generated at runtime from layout data
`SliceBuilder` builds Southside from `southside_layout.json`. **Why:** no editor to author scenes; data stays
validated and merge-friendly. **Later:** bake to authored scenes or prefabs per streaming cell once art exists.

## TD-013 — Likeness-rights gate in data
`CastingInfo.DisplayName` shows `PlaceholderName` unless `LikenessRightsCleared`. A test asserts all shipped crews default closed.

## TD-014 — Save envelope stores the payload as a string
The checksum then covers exact bytes. Newer-version saves are refused rather than "best effort" loaded.
`TypeNameHandling.None` prevents type-injection from tampered files.

## TD-015 — Story reveal is guarded in code and in content validation
`story.chairman.identity_revealed` requires chapter ≥ 7 **and** the reveal mission active/completed. The mission
validator rejects earlier missions setting it, and the dialogue parser rejects any dialogue setting it.

## TD-016 — Vehicle physics is a deterministic simulation model, collisions come from Unity
`VehicleDynamics` is a hand-written ground-plane model (drift angle instead of a tyre model), stepped at 60 Hz. Unity sweeps a box
toward the simulated position and reports closing speed back as an impact.
- **Why:** testable without the editor, deterministic for AI racers and future replays, consistent across PC/mobile, no
  WheelCollider tuning that can't be verified here.
- **Tradeoffs:** no suspension, ramps or rollovers; car-to-car contact is a stop-and-damage, not a shove. A WheelCollider or
  custom raycast-suspension upgrade can replace the presentation later without changing ownership, racing or save code.

## TD-017 — Racing AI uses the player's physics, no rubber-banding
AI cars run the same `VehicleDynamics` with their definition's stats. Difficulty comes only from skill (line, braking points,
mistakes). A test asserts no AI car ever exceeds its top speed.
