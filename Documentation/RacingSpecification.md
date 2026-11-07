# Racing Specification — status: SPRINT IMPLEMENTED (simulation tested; Unity layer unverified)

## Implemented
- `RaceDefinition` data + validation (`Data/Racing/races.json`); `RaceSession` with countdown (no jump starts), checkpoints that
  only count in order, laps, finish order, time-limit DNF; `RaceDriverAI` (racing line through upcoming checkpoints, brakes for
  corners using the car's real braking stat, skill-based mistakes, traffic avoidance, handbrake only for confident drivers); `RaceManager`
  (entry fee per attempt, purse by place, once-ever first-win bonus, rank points, best times, reputation and rival relationships,
  `race.won.<id>` / `race.finished.<id>` mission triggers). Every payout is keyed by race + attempt, so it is never paid twice, including after a reload.
- Tested: AI racers finish on their own, never exceed their car's top speed, higher skill is faster, DNF ranks last, double resolution pays nothing.
- **Pecan Street Sprint** (Southside): lot → West St → Southside Blvd → Industrial Rd, 8 checkpoints, ~520 m. Rivals: Rell (Kudzu RS, skill 0.7) and Deuce (Magnolia Cruiser, 0.45). Entry $20; purse $300/$150/$50; first win +$250.
- Not yet: circuit/time-trial/crew/championship content, traffic cars, sponsors, invitations.

## Original plan

Race types: Sprint, Circuit, Time Trial, Crew, Rival, Championship, Invitation-only, Special.
Ranks: Unknown Driver → Neighborhood Runner → City Contender → Street Circuit Elite → Southern Road Legend.

**AI:** racing-line splines with braking points, traffic avoidance, skill-based mistakes, damage-aware pace.
**No rubber-band speed boosts** (same no-cheating rule as fighter AI; assert in tests).

**Race flow:** start → ordered checkpoints (must be passed in order) → finish → `RaceCompleted` event → rewards through the
mission reward ledger (issued once) → consequences for money, rank, Face Card, Public Image, rivals, crew influence,
sponsors and invitations. First race: Pecan Street Lot sprint (Southside layout `location.southside.race_start`).
