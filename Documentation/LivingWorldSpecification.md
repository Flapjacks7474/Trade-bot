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
- **NPC presence** gated by objective progress (Dre moves from the courts to the gym).

## Not implemented
- **Schedules** (Morning/Afternoon/Evening/Night/LateNight; `GameClock.Segment` exists) and travel along roads.
  Important NPCs must not teleport to convenient spots.
- **Simulation tiers:** full (near), simplified (district), statistical (off-district).
- **Autonomous world events:** releases, business changes, promotions, disappearances, championships. Missed
  opportunities can expire.
- **Romance** (Maya): own schedule, goals and boundaries; can reject, lose trust, end things, forgive or move on.
