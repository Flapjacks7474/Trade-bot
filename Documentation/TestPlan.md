# Test Plan

## Levels
1. **Simulation unit and integration tests** (NUnit, `Assets/_Project/Tests/EditMode`). Run outside Unity with `Tools/run-tests.sh`
   and inside Unity with Test Runner > EditMode. Must stay green on every commit.
2. **Content validation**: every JSON file parsed and cross-checked (`ContentTests`, menu **FFTS > Validate Content**,
   and before batch builds).
3. **Runtime compile check**: `Tools/UnityCompileCheck` against Unity reference assemblies.
4. **PlayMode tests** *(to add once in-editor)*: slice boots, player moves, a location trigger fires, dialogue opens,
   a fight starts and ends, quicksave/quickload restores position and mission state.
5. **Manual play scripts**: Chapter 1 on each branch; save/load at every objective; lose every fight once.

## Acceptance tests per system (from the brief) → coverage
| System | Acceptance | Automated coverage |
|---|---|---|
| Combat | fight, block, dodge, counter, grapple, win, lose, recover | CombatTests (all), FighterAITests |
| Missions | branches persist; duplicate rewards prevented | CampaignTests (branch lock, replay, save/load, failure recovery) |
| Relationships | NPCs react differently by memory and values | SocialTests (witness, rumor, attitude, personality) |
| Save system | restores important state | CampaignTests.SaveLoadPreserves…, SaveSystemTests |
| Economy | no duplicate transactions | BankTests |
| Story | reveal can't happen early | StoryGuardTests + content validation |
| Driving / Racing / Music / Business / Police | — | not implemented |

## Rules
- A test is only reported as passed after it has actually run (TEST_RESULTS.md records each run with environment).
- Every bug fix gets a regression test where possible.
- Determinism tests guard the combat sim; AI tests use fixed seeds and assert behavioural tendencies, not exact frames.
