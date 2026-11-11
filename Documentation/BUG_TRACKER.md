# Bug Tracker

| ID | Severity | Status | Summary | Repro | Fix |
|---|---|---|---|---|---|
| BUG-001 | High | Fixed | A draw counted as a player win when resolving fight consequences | `OnFightCompleted` with `WinnerId == null` | `won = WinnerId != null && WinnerId != opponentId` (GameSession.cs) |
| BUG-002 | Medium | Fixed | Normal-difficulty AI could never react to any strike (20-tick reaction > every startup), so defence was random | FighterAITests adaptation run | Reaction times retuned; haymaker startup 12→16; habit-based anticipation (TD-006) |
| BUG-003 | Medium | Fixed | Wrestler AI wasn't distinct enough from Technical AI (grapple share 24% vs 17%) | `OpponentsHaveDistinctFightingBehaviour` | Style-specific whiff punish; style-weighted grapple adaptation |
| BUG-004 | Medium | Fixed | Unity `??`/`?.` used on `UnityEngine.Object` (fake-null in editor) | Code review | Explicit `== null` checks; `Rig()` helper |
| BUG-005 | High | Fixed | HUD called legacy `Input.mousePresent`, which throws when only the Input System is active | Code review | Removed; cursor relocks when dialogue closes |
| BUG-006 | Low | Fixed | Two .NET projects shared one `obj/` folder (restore failure) | `dotnet test` | Projects moved to separate folders |
| BUG-007 | Low | Fixed | Guard-break test never broke guard: block pushback moved the defender out of range and the scripted attacker never stepped back in; heavies also drained too little stamina | `BlockingDrainsStaminaUntilGuardBreak` | Test attacker now walks forward between swings; heavy/kick `StaminaDamage` raised (tuning) |

| BUG-008 | Medium | Fixed | Cars topped out at ~83% of their rated top speed (aero drag too strong) | `AcceleratesTowardButNotPastTopSpeed` | Drag coefficient 0.0009 → 0.0005 |
| BUG-009 | High | Fixed | After loading a save, repeating a purchase key reported "garage full" instead of "already bought" (in-memory dedupe only) | `GarageStateRoundTripsThroughJson` | `Buy` checks the bank's persisted ledger first |
| BUG-010 | Low | Fixed | Braking from very low speed switched into reverse before stopping | `BrakingStopsFasterThanCoasting` | Brake-to-reverse threshold 0.5 → 0.05 m/s |
| BUG-011 | Low | Fixed | `GaragePanel` missing `using System.Linq` | Runtime compile check | Added |

| BUG-012 | Medium | Fixed | Karate won 78% of AI round-robin fights (outranged everyone) | `styles.test.mjs` round robin | Karate ranges 1.15→1.05/1.08, mae geri 1.55→1.45, oi zuki startup 13→14; counter bonus 4→3 |
| BUG-013 | Medium | Fixed | Grapplers too weak in AI fights (Wrestling 28%, Judo 30%) | same | Wrestling strikes/startups and throw multiplier up; Judo strike and throw multipliers up |
| BUG-014 | Low | Fixed | Blender clips for clinch strikes had zero length (sim frame data 1/1/0) | template dry-run | Clinch-strike clips span the 20-tick clinch cooldown, impact on tick 6 |
| BUG-015 | Low | Open | Storyboard intro renders crop the head and feet (intro camera too tight) | `storyboard/frames/*/000_intro.png` | Widen intro lens (42 mm → 30 mm) in `ffts_export_and_boards.py` and re-render |
| BUG-017 | Low | Open | Five clips and their board panels were made before the balance pass, so they are 1 tick off the data (karate.oi_zuki startup 13→14; wrestling.shiver 6→5, elbow_smash 10→9, double_leg 7→6; reach changes on karate/taekwondo ranges don't affect timing) | compare `fight_rig_report.txt` with the JSON | Re-run the Blender bootstrap (rig + export + boards) and the regenerated board script |
| BUG-016 | Low | Open | Opponent dummy in board renders doesn't react to throws (stays in guard) | throw panels | Drive the dummy with `react.thrown` / `react.grappled` in the board render |

Other open bugs: none known. Expect many once the slice runs in Unity; log them here with repro steps.
