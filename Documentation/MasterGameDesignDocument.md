# FIGHT FOR THE SOUTH — Master Game Design Document

**Tagline:** Nobody owns the South. Until somebody earns it.
**Genre:** Open-world action RPG: street fighting, music career, racing, business, relationships, crew politics.
**Setting:** Finess City, a fictional Southern metro with eight districts (WorldMap.md).
**Platform:** PC single-player first; architecture kept compatible with iOS/Android; browser feasibility to be assessed;
multiplayer is a future expansion only.

## Pillars
1. **Fighting that feels like a real fighting game inside an open world.** Frame data, RPS core, counters, grapples, finishers (CombatSpecification.md).
2. **A city that remembers.** NPCs know only what they saw or heard; relationships and reputation persist (LivingWorldSpecification.md).
3. **Freedom of identity.** Rapper, producer, fighter, racer, mogul, crew leader, or any mix, with no class locks.
4. **Choices with weight.** Locked branches, persistent consequences, endings computed from accumulated state.
5. **Money that matters without chores.** One ledger, meaningful purchases, few tedious bills (EconomySpecification.md).

## Player start
Unknown kid from Southside living with Grandma: no crew, no reputation, basic hands, $40, basic phone and clothes.

## Progression tracks
| Track | Ranks / measures | Spec |
|---|---|---|
| Underground standing | Face Card: Respect, Fear, Loyalty, Influence, Notoriety, Threat, Price on head | EconomySpecification.md §Reputation |
| Public standing | Public Image: Popularity, Credibility, Controversy, Professionalism, Fan loyalty, Industry relationships | same |
| Crews | Outsider → Prospect → Member → Respected → Veteran → Right-Hand → Second → Leader | RivalEncounterSpecification.md |
| Fighting | stats 1–100, up to 3 styles | CombatSpecification.md |
| Music | Local → Independent → Regional → Major attention → National → Executive | MusicCareerSpecification.md |
| Racing | Unknown Driver → Neighborhood Runner → City Contender → Street Circuit Elite → Southern Road Legend | RacingSpecification.md |
| Business | Small Operator → Established Owner → Local Leader → Regional Entrepreneur → Empire Builder | EconomySpecification.md |

## Story
Eight chapters, branching, with a hidden-brother central mystery revealed in Chapter 7 (StoryBible.md, MissionDatabase.md).

## Systems map and status
See PROJECT_STATUS.md for live status and TechnicalArchitecture.md for module ownership.

## Out of scope for the foundation
Final art, animation, audio, licensed music, voice, real-artist likenesses, multiplayer.
