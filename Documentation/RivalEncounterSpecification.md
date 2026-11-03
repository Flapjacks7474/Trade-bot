# Rival Encounter & Crew Specification — status: NOT IMPLEMENTED (Milestone 7)

**Finess City Conflict System.** Encounters spawn anywhere they are logically justified: the rival's schedule puts them
there, they heard where you'd be (via rumor memory), or a crew is contesting that territory. No teleporting.

**Player options:** walk away, talk, negotiate, accept or reject a challenge, fight one or both sides, temporarily join a
side, protect someone, stop the conflict, ally if earned.

**Temporary alliance ladder:** temporary cooperation → mutual respect → favor owed → growing trust → friendship →
strategic partnership (or betrayal). Cooperation is not friendship.

**Outcome tracking:** participants, winner, escaped, witnesses, who was helped, who was betrayed. Outcomes feed Face Card,
Public Image, relationships, crew loyalty, territory, businesses, police attention, rumors and future missions.
Built on `SocialManager.Record` and `FightConsequences`.

**Modules:** RivalEncounterDirector, EncounterDefinition, EncounterInstance, EncounterStateMachine,
EncounterSpawnEvaluator, RivalBehaviorController, RivalRecognitionSystem, RivalMemoryManager, RivalProvocationSystem,
RivalAllianceEvaluator, ConflictManager, GroupCombatCoordinator (requires the N-fighter combat extension, TD-003),
MultiFactionConflict, TemporaryAllianceManager, ConflictOutcomeResolver, RivalReputationManager, ConflictRumorManager.
Mobile caps on active combatants. Cooldowns prevent repetitive encounters.

## Crews
Data exists (`Data/Crews/crews.json`: five crews, territory, interests, leaders behind the rights gate). `CrewRank` enum
defined. Planned: member attributes (Trust, Fear, Respect, Loyalty, Ambition, Satisfaction, Rivalry), non-obedient
members, join/leave/expel/challenge/found a crew, and dynamic territory where defeating a crew does not transfer its holdings.
