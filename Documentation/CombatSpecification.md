# Combat Specification (implemented)

Source of truth: `Scripts/Simulation/Combat/*`, data `Data/Moves/street_fighting.json`, `Data/Characters/fighters.json`.

## Simulation
- 60 ticks/s fixed step, deterministic. Two fighters on a line (`X` in metres), arena half-width 6 m, walls at the ends.
- A hit lands when the attacker's move is in **active** frames, distance ≤ `Range + 0.35` and the defender is hittable.
- Simultaneous strikes trade. Strike vs grapple on the same tick: strike wins. Grapple vs grapple: clash and push apart.
- Input buffer: 8 ticks. Presses are latched between render frames by `FightDirector`.

## States
Spec: IDLE, MOVING, ATTACKING, COMBO, FINISHER, BLOCKING, COUNTERING, GRAPPLING, THROWING, KNOCKED_DOWN, GETTING_UP,
STUNNED, DEFEATED. Added: HIT_STUN, BLOCK_STUN, DODGING, GRAPPLED (held), FINISHER_VICTIM.

## Stats → numbers
| Stat | Effect |
|---|---|
| Strength | damage × (0.7 + S/166) |
| Durability | max HP 100 + D; damage taken × (1.15 − D/333); shorter knockdowns |
| Stamina | max stamina 60 + 0.8·St; regen ∝ (0.5 + St/200) |
| Speed | walk 2.2 + Sp/50 m/s; startup × (1.15 − Sp/400) |
| Technique | counter-stance window 6 + T/15 ticks; parry window 3 + T/25; faster clinch escape |
| Aggression/Confidence/Composure | authored now, consumed by AI and future composure rules |

## Mechanics
| Mechanic | Rule |
|---|---|
| Light / Heavy / Kick | Entry moves from the style slots |
| Combos | `MoveLink`s cancel recovery within `CancelWindow` after a hit or block. Street routes: L→L→H, L→H→K (knockdown), H→G→Throw, Counter→H→Signature |
| Block | Hold. Chip = 10% damage. Drains `StaminaDamage`; at 0 → guard break → Stunned 75 ticks |
| Parry | Block pressed ≤ parry window before impact → automatic counter. Measured from the press edge, so held block never parries |
| Counter stance | Counter command; catches strikes inside the window, not grapples; whiffing costs recovery |
| Dodge | 20 ticks, invulnerable ticks 2–13, retreats 1.6 m, 12 stamina |
| Grapple | Beats block and counter stance. Clinch up to 110 ticks: Light = knee (≤3), Heavy = hip toss, Grapple near a wall (≤1.3 m) = wall slam |
| Escape | Defender mashes Grapple/Dodge. Progress per press 16 + 0.2·Tech + 0.1·Str − 0.08·attacker Str (min 6); ≥100 breaks free and staggers the attacker |
| Reversal | Defender presses Counter within 14 ticks of the clinch with ≥25 momentum → throws the attacker |
| Knockdown | 70 × (1.1 − Dur/500) ticks, then 24-tick invulnerable get-up. Quick rise: press Dodge after 20 ticks (half get-up) |
| Ground attack | Heavy near a downed opponent → stomp; only hits downed targets |
| Breakaway | In hitstun with ≥3 combo hits, ≥30 momentum and ≥15 stamina: Dodge → invulnerable escape, attacker staggered |
| Momentum | Gained on hit (defender gets 25% "comeback"), counters, throws, taunts (+15). Max 100 |
| Finisher | Requires 100 momentum, opponent ≤35% HP **or** Stunned, distance ≤1.8 m, attacker neutral, opponent in a compatible state. Cinematic camera, 45 base damage, resets momentum. Denial reasons are reported to UI |
| Anti-infinite | Combo DAG (cycles rejected) + damage scale max(0.35, 1 − 0.15n) + hitstun scale max(0.4, 1 − 0.12n) + stamina + breakaway |
| Round | 180 s; time-out decided by health fraction; equal = draw |

## AI
| Style | Behaviour |
|---|---|
| Aggressive | strike-heavy, rarely waits |
| Technical | counters perceived strikes, keeps spacing |
| Wrestler | 50% grapple openings, punishes whiffs with grabs, wall slams |
| Defensive | waits, blocks, punishes recovery |
| Showman | taunts at range, dodges flashily |

| Difficulty | Reaction (ticks) | Defence | Counter | Combo | Escape presses/s | Mistakes |
|---|---|---|---|---|---|---|
| Easy | 24 | .20 | .05 | .25 | 3 | .30 |
| Normal | 14 | .40 | .15 | .55 | 5 | .15 |
| Hard | 10 | .60 | .30 | .80 | 7 | .08 |
| Expert | 8 | .72 | .40 | .90 | 9 | .04 |
| Boss | 6 | .80 | .50 | .95 | 10 | .02 |

No hidden stat, damage or speed bonuses at any difficulty. Adaptation: decayed counts of the opponent's opening moves
raise defence against the habit and allow pre-emptive guarding or sidesteps. Opponents who turtle get grappled more.

## Roster (slice)
| Fighter | Archetype | AI | Notable stats |
|---|---|---|---|
| Player | Scrapper | — | 45–50 across the board |
| Dre | Showman | Showman / Easy | tutorial sparring partner |
| Rell | Technician | Technical / Normal | Speed 62, Technique 65 |
| Darnell "Brick" Hollis | Grappler | Wrestler / Normal | Strength 70, Durability 70, Speed 35 |

## Animation production target (178 slots)
Idle/stance 12 · Walk/run 10 · Punches 24 · Kicks/knees 16 · Blocks/counters 16 · Grapples/throws 24 · Ground 16 ·
Knockdowns/recovery 12 · Finishers 20 · Taunts 16 · Entrances 12. None exist yet. Each move's `AnimationKey` plus
startup/active/recovery give the timing markers animators must hit. Root motion is presentation-only; the
simulation owns position.
