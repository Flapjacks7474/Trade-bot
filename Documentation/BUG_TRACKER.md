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

Open bugs: none known. Expect many once the slice runs in Unity; log them here with repro steps.
