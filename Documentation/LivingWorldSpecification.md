# Living World Specification

## Implemented (`SocialManager`)
- **Personality:** Confidence, Temper, Loyalty, Ambition, Trust, Patience, Intelligence, Sociability, Courage, Composure.
- **Directional relationships:** Trust, Respect, Loyalty, Affection, Fear, Resentment, Rivalry, Friendship (−100..100).
  Temper and patience scale resentment; courage scales fear.
- **World events** with witness lists. Each witness gets a `Witnessed` memory (reliability 1).
- **Rumors:** only someone who knows can tell. Reliability ×0.7 per retelling and stops below 0.15. Source recorded.
- **Memories** can be resolved, and unresolved negative memories about the player drag attitude down.
- **Attitude:** Hostile / Intimidated / Wary / Neutral / Warm / Friendly from values + memories + personality
  (a brave NPC who fears you turns hostile; a timid one becomes intimidated).

## Implemented in Milestone 5 (`LivingWorldManager`, `RoadGraph`, `Data/Characters/npc_profiles.json`)
- **Profiles** for Grandma, Lou, Dre, Rell, Tone, Big Reece, Brick, Nia, Marlo, Deuce: personality, home, walk or drive, and a
  weekly schedule (hour ranges, weekdays, location, activity, priority). Validation requires routine coverage of every hour of the week.
- **Story pins** (mission active / objective done or not done / flag) outrank routines, so story beats are always reachable:
  Dre waits at the courts and then walks to the gym, Rell waits at the studio during *First Recording*, Reece and Brick are at the
  venue during *Prove Yourself*, Nia at the corner store during *The Invitation*, and Grandma stays home and awake for missions 1 and 6.
  A test confirms the pinned NPC arrives from any starting hour.
- **Travel:** routes follow road centrelines (graph with intersections, Dijkstra). Walk 1.6 m/s, drive 11 m/s. Arrival and departure events.
- **Update tiers by distance from the player:** Full (≤60 m, every frame), Simplified (≤250 m, every 1 s), Distant (every 5 s).
  Lower tiers batch time, but NPCs cover the same ground; no NPC ever jumps (tested).
- **Hold:** an NPC stops while the player talks to them.
- **Witnesses** for fights come from NPCs actually within 25 m.
- **Time awareness:** dialogue variants by time segment, and conversation rules by NPC activity (sleeping Grandma).
- NPC positions, including mid-trip, persist in saves.

## Not implemented
- Background crowds with archetype schedules (the tier system is ready for them).
- NPCs reacting to the player en route (greetings, avoidance); pedestrians don't avoid the player's car.
- **Autonomous world events:** releases, business changes, promotions, disappearances, championships. Missed
  opportunities can expire.
- **Romance** (Maya): own schedule, goals and boundaries; can reject, lose trust, end things, forgive or move on.
