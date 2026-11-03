# Mission Database

Data: `Assets/_Project/Data/Missions/chapter1.json`. Every mission has a cause, purpose, prerequisites, objectives
(with triggers), optional objectives, failure conditions, a recovery rule and rewards. Rewards are granted once
(reward ledger). Replays are sandboxed.

## Chapter 1 — Southside Raised (implemented, playthrough-tested)

| # | ID | Title | Objectives | Branches | Failure → recovery | Rewards / consequences |
|---|---|---|---|---|---|---|
| 1 | `mission.ch1.home_again` | Home Again | Arrive at Grandma's (checkpoint) · Talk to Grandma · *Look at the photo (opt.)* | — | restart mission | $200, Grandma trust/affection, `ch1.home`; **photo clue** |
| 2 | `mission.ch1.southside_stories` | Southside Stories | Lou's barbershop · corner store · find Tone's studio | — | checkpoint | Contact Lou, Respect +2, `ch1.heard_five_crews` |
| 3 | `mission.ch1.old_friends` | Old Friends | Meet Dre (cp) · reach the gym (cp) · *block* · *3-hit combo* · *throw* · win the spar | — | lose to Dre → checkpoint | $100, Dre friendship, `ch1.tutorial_complete`. **Combat tutorial** |
| 4 | `mission.ch1.first_recording` | First Recording | Reach the studio (cp) · deal with Rell (cp) · [fight] beat Rell · record the demo | **talk** / **stand_ground** / **fight** (locked once chosen) | lose to Rell → checkpoint (branch persists) | Talk: +Professionalism, mild rivalry. Stand: +Respect, Rell respect + rivalry. Fight: +Respect/Fear, −Professionalism, Rell resentment. All: Tone respect, first-demo item |
| 5 | `mission.ch1.prove_yourself` | Prove Yourself | Find the venue (cp) · sign up with Big Reece (cp) · beat Brick | — | lose to Brick → checkpoint | $500 purse, Respect +10, Fear +5, Notoriety +3, contact Reece. Underground-fight consequences apply |
| 6 | `mission.ch1.the_invitation` | The Invitation | Meet Nia · tell Grandma | — | checkpoint | Contact Nia, invitation item, `story.ch2.showcase_invited`, **→ Chapter 2** |
| S | `mission.side.grandmas_errand` | Grandma's Errand | Pick up the prescription · bring it home | — | restart | **−$15 copay**, Grandma trust/affection, `side.grandmas_errand.done` → changes Grandma's lines in mission 6 |

Cross-mission memory examples already live: Big Reece's opening line changes if you talked Rell down; Rell's
"stand" response softens if his respect for you is ≥10; Tone refuses to record until Rell is handled.

## Chapters 2–8 (planned)
| Ch | Missions |
|---|---|
| 2 The Introduction | The Showcase · Five Kings · The Incident · Nobody Saw Nothing · The First Favor |
| 3 Earn Your Name | The Test · Earn Your Place · No Free Rides · The Underground · A Seat at the Table |
| 4 Blood Isn't Always Thicker | Family Business · Old Wounds · The Leak · Broken Trust · The Chairman |
| 5 The City Divided | Territory Lines · The Price of Loyalty · No More Business · The Missing Records · Five Against One |
| 6 The Southern Table | Behind Closed Doors · The Old Photograph · Solo's Secret · The Family Name · Somebody Knows You |
| 7 The Bloodline | What Grandma Knows · The Last Day · The Man Behind the Table (`mission.ch7.man_behind_table`, sets the reveal flag) · The Reunion |
| 8 Nobody Owns the South | Nobody Owns the South · The Final Decision |

## Trigger conventions
`location.<district>.<place>` · `interact.<place>.<thing>` · `dialogue.<npc>.<event>` · `fight.won|lost.<fighter>` ·
`combat.tutorial.<step>`. `GameContentLoader.CrossCheck` fails the build if an objective's trigger can't be produced.
