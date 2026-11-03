# Racing Specification — status: NOT IMPLEMENTED

Race types: Sprint, Circuit, Time Trial, Crew, Rival, Championship, Invitation-only, Special.
Ranks: Unknown Driver → Neighborhood Runner → City Contender → Street Circuit Elite → Southern Road Legend.

**AI:** racing-line splines with braking points, traffic avoidance, skill-based mistakes, damage-aware pace.
**No rubber-band speed boosts** (same no-cheating rule as fighter AI; assert in tests).

**Race flow:** start → ordered checkpoints (must be passed in order) → finish → `RaceCompleted` event → rewards through the
mission reward ledger (issued once) → consequences for money, rank, Face Card, Public Image, rivals, crew influence,
sponsors and invitations. First race: Pecan Street Lot sprint (Southside layout `location.southside.race_start`).
