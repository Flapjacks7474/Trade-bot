# Technical Architecture

## Layers

```
┌───────────────────────── FFTS.Editor (Editor only) ─────────────────────────┐
│ SliceSceneBuilder · URP setup · Validate Content · BuildScripts (batch)     │
└───────────────────────────────┬─────────────────────────────────────────────┘
┌───────────────────────── FFTS.Runtime (UnityEngine) ────────────────────────┐
│ CoreGameManager ─ owns GameSession, input sampling, clock, save/load,      │
│                   NPC conversation routing, control locks                   │
│ PlayerInputSource (Input System / legacy) · PlayerMotor · CameraRig         │
│ WorldInteractable + InteractionManager · SliceBuilder + LocationTracker     │
│ FightDirector (60 Hz fixed tick) + FighterView · DialoguePanel · GameHud    │
│ DrivingDirector + VehicleView · RaceDirector · GaragePanel                  │
└───────────────────────────────┬─────────────────────────────────────────────┘
                                │ reads state, sends commands, listens to events
┌──────────────────── FFTS.Simulation (noEngineReferences) ───────────────────┐
│ Core: EventBus · Ids · GameClock · DeterministicRandom                      │
│ SaveSystem: SaveEnvelopeCodec · SaveSlotManager<T> · ISaveStorage           │
│ Input: GameAction · InputFrame · InputBindingMap                            │
│ Interaction: InteractionSelector                                            │
│ Combat: CombatDatabase · Moveset · CombatSimulation · FighterAI             │
│ Economy: FinessBank   Reputation: ReputationManager                         │
│ Relationships: SocialManager (relationships, personality, memory)           │
│ Story: StoryManager   Missions: MissionManager   Dialogue: DialogueRunner   │
│ World: DistrictLayout   Crews: CrewFile                                     │
│ Vehicles: VehicleDatabase · VehicleDynamics · GarageManager                 │
│ Racing: RaceDatabase · RaceSession · RaceDriverAI · RaceManager             │
│ Session: GameSession · GameContentLoader · NpcConversationRules ·           │
│          FightConsequences · GameSaveData                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

Dependencies point downward only. Simulation never references Runtime. Within Simulation, `Session` is the only
namespace that knows every other system. `StoryManager` asks "has mission X been reached?" through an injected
delegate, so Story does not depend on Missions (that would be a cycle).

## Ownership of state

| State | Owner | Mutated through |
|---|---|---|
| Money | `FinessBank` | `Post` / `Pay` with idempotency keys |
| Face Card / Public Image | `ReputationManager` | `Apply(ReputationChange, source)` |
| Relationships, memories, world events | `SocialManager` | `Modify`, `Record`, `ShareRumor`, `Resolve` |
| Chapter, story flags | `StoryManager` | `TrySet` (guarded), `AdvanceChapter` |
| Mission progress, reward ledger | `MissionManager` | `Start`, `ReportTrigger`, `ChooseBranch`, `Fail` |
| Fight state | `CombatSimulation` | `Step(inputA, inputB)` only |
| Player identity, contacts, inventory, position | `GameSession` | reward grants, runtime position capture |

## Events (implemented)
`TimeSegmentChanged`, `DayChanged`, `TransactionPosted`, `ReputationChanged`, `RelationshipChanged`,
`StoryFlagChanged`, `MissionStateChanged`, `MissionCompleted`, `ObjectiveCompleted`, `WorldEventRecorded`,
`FightCompleted`, `GameSaved`, `GameLoaded`, `VehiclePurchased`, `VehicleSold`, `RaceCompleted`. Planned: `BusinessPurchased`,
`TerritoryChanged`, `RivalEncounterResolved`, `IncidentReported`, `SongReleased`, `CrewRankChanged`.

## Triggers
Missions advance through string triggers: `location.*` (LocationTracker), `interact.*` (interaction markers),
`dialogue.*` (dialogue options/end), `fight.won.<fighter>` / `fight.lost.<fighter>` (GameSession), `combat.tutorial.*`
(FightDirector). `GameContentLoader.CrossCheck` proves every objective trigger can actually be produced.

## Save format
`{ format: "FFTS_SAVE", schemaVersion, savedUtc, checksum: sha256(payload), payload: "<GameSaveData JSON>" }`.
Writes go to `.tmp`, are flushed, then `File.Replace` swaps them in and keeps a `.bak`. On load the primary is tried
first, then the backup. Migrations (`ISaveMigration`) run on raw JSON before typed deserialization. Slots:
`manual_N`, `autosave_0..2` (rotating). Location: `Application.persistentDataPath/saves`.

## Requested module names → implementation
| Brief | Implementation |
|---|---|
| CoreGameManager | `FFTS.Runtime.CoreGameManager` + `FFTS.Session.GameSession` |
| InputManager | `PlayerInputSource` + `InputBindingMap` |
| CameraManager | `CameraRig` |
| InteractionManager | `InteractionManager` + `InteractionSelector` |
| CombatManager / FighterAI | `FightDirector` + `CombatSimulation` / `FighterAI` |
| RelationshipManager / NPC memory | `SocialManager` |
| DialogueManager | `DialogueRunner` + `DialoguePanel` + `NpcConversationRules` |
| MissionManager / StoryManager | same names |
| BankManager / EconomyManager | `FinessBank` |
| ReputationManager | same name |
| SaveManager | `SaveSlotManager<GameSaveData>` + `GameSession.Capture/Restore` |
| UIManager | `GameHud`, `DialoguePanel` (placeholder) |
| WorldStreamingManager | not yet; `SliceBuilder` builds one district |
| VehicleManager / GarageManager | `GarageManager` |
| DrivingController | `VehicleDynamics` + `DrivingDirector` + `VehicleView` |
| RacingManager | `RaceManager` + `RaceSession` + `RaceDirector` |
| Others (Police, Music, Business, Property, Phone, Platform…) | not started |

## Performance notes
Combat simulation allocates only the per-step event list (reused). AI enqueues one struct per tick. The full test
suite, including thousands of AI-vs-AI fight ticks, runs in about 1 s. Runtime: the `WorldInteractable.All` scan is
O(n) per frame (fine for the slice; needs spatial partitioning in M11). Distant-NPC simulation tiers are not built yet.
