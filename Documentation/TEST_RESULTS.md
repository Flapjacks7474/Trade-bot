# Test Results

## Run 2026-11-03 — simulation suite (outside Unity)

| Item | Value |
|---|---|
| Command | `Tools/run-tests.sh` (`dotnet test` on `Tests/DotNet/SimulationTests`) |
| Environment | Linux 6.18 container, .NET SDK 8.0.133 (Ubuntu package), NUnit 3.14, NUnit3TestAdapter 4.5 |
| Simulation compiled as | netstandard2.1, C# 9, warnings as errors (0 warnings, 0 errors) |
| Result | **82 passed, 0 failed, 0 skipped** |
| Unity editor | **Not available — EditMode tests NOT yet run inside Unity** |

| Test | Result |
|---|---|
| AiAdaptsToRepeatedHeavySpam | Passed |
| AiCannotReactFasterThanItsReactionTime | Passed |
| AiDefeatsPassiveOpponent("fighter.brick") | Passed |
| AiDefeatsPassiveOpponent("fighter.rell") | Passed |
| AiVersusAiFightsAlwaysResolve | Passed |
| AllShippedContentValidatesAndCrossReferences | Passed |
| AutosaveRotatesSlots | Passed |
| BankStateRoundTripsAndKeepsDedupe | Passed |
| BlockingDrainsStaminaUntilGuardBreak | Passed |
| BranchChoiceIsLockedAndOnlyItsObjectivesCount | Passed |
| BreakawayEscapesLongCombo | Passed |
| ChairmanRevealIsBlockedBeforeChapterSevenMission | Passed |
| ChaptersOnlyMoveForward | Passed |
| ClockSegmentsAndEvents | Passed |
| ConditionalOptionsHideUntilRequirementsMet | Passed |
| CorruptPrimaryFallsBackToBackup | Passed |
| CorruptWithoutBackupIsReportedNotLoaded | Passed |
| CounterStanceBeatsHeavyThenCounterHeavySignatureRoute | Passed |
| CrossCheckCatchesUnreachableTriggers | Passed |
| DeniedFinisherReportsReason | Passed |
| DialogueEffectsApplyOnceEvenWhenRepeated | Passed |
| DodgeAvoidsAttackDuringInvulnerability | Passed |
| DreMovesFromCourtsToGymAfterAgreeingToSpar | Passed |
| DuplicateIdempotencyKeyNeverPostsTwice | Passed |
| EightDistrictsFiveCrewsAndCastingGateDefaultsClosed | Passed |
| EnvironmentalThrowNearWallDealsMoreDamage | Passed |
| EventBusDeliversUnsubscribesAndIsolatesFailures | Passed |
| EventBusDetectsEventLoops | Passed |
| EveryButtonActionHasAMask | Passed |
| FileStorageWritesAtomicallyWithBackup | Passed |
| FinisherDefeatsWeakenedOpponent | Passed |
| FinisherRequiresMomentumVulnerabilityAndRange | Passed |
| FullChapterOnePlaythroughReachesChapterTwo | Passed |
| GrappleBeatsBlockAndThrowKnocksDown | Passed |
| GroundAttackOnlyHitsDownedOpponent | Passed |
| HeldBlockBlocksStrikeAndTakesOnlyChip | Passed |
| HigherDifficultyWinsMoreOften | Passed |
| HitstunDecayPreventsInfiniteLightSpam | Passed |
| IdRulesRejectBadIdsAndRegistryCatchesDuplicates | Passed |
| InputEdgesAndRebinding | Passed |
| InputMapperPrioritisesAndOrientsMovement | Passed |
| InsufficientFundsAndInvalidRequestsAreRejectedWithoutSideEffects | Passed |
| InteractionSelectorPrefersInFrontPriorityAndRange | Passed |
| KnockdownRecoversAndQuickRiseIsFaster | Passed |
| KnockoutEndsFightAndTimeDecisionWorks | Passed |
| LightAttackHitsAndDealsDamage | Passed |
| LightHeavyKickRouteEndsInKnockdown | Passed |
| LightLightHeavyComboLandsThreeHitsWithDamageScaling | Passed |
| LosingAFightFailsThenRetriesFromCheckpoint | Passed |
| MashingEscapesTheClinch | Passed |
| MemoryChangesAttitudeUntilResolved | Passed |
| MigrationsUpgradeOldSaves | Passed |
| MissionOneStartsAutomaticallyAndOthersAreLocked | Passed |
| MovesetCombinesUpToThreeStyles | Passed |
| NewerSaveVersionIsRefused | Passed |
| OnlyWitnessesKnowAnEvent | Passed |
| OpponentsHaveDistinctFightingBehaviour | Passed |
| OptionalMissionChangesLaterDialogueAndCostsMoney | Passed |
| PayUsesCashThenCheckingAndIsIdempotent | Passed |
| PersonalityChangesReactionToTheSameThreat | Passed |
| RandomIsDeterministicAndRestorable | Passed |
| ReplayNeverDuplicatesRewardsOrRewritesChoices | Passed |
| ReversalRequiresMomentumAndSwapsRoles | Passed |
| RoundTripInMemory | Passed |
| RulesFileValidates | Passed |
| RumorsNeedASourceAndDegrade | Passed |
| SameFightMovesFaceCardAndPublicImageDifferently | Passed |
| SaveLoadPreservesMissionsBranchesMoneyRelationshipsAndDedupe | Passed |
| SessionSaveSurvivesCorruptionViaBackup | Passed |
| ShippedCombatDataIsValid | Passed |
| SimulationIsDeterministic | Passed |
| SocialStateRoundTrips | Passed |
| SouthsideHasEveryRequiredSliceLocation | Passed |
| StaminaPreventsEndlessHeavies | Passed |
| StrikeBeatsGrappleOnSameTick | Passed |
| TalkingToNpcsFollowsMissionState | Passed |
| TemperAmplifiesResentment | Passed |
| TimedBlockParriesIntoCounter | Passed |
| TransfersAndBusinessMoneyStaySeparate | Passed |
| ValidationRejectsDuplicateIdsBadStatsAndCycles | Passed |
| ValuesClampTo0To100 | Passed |
| WantsTriggerOnlyForUnfinishedLiveObjectives | Passed |

Parameterised `AiDefeatsPassiveOpponent` counts as two cases (Rell and Brick).

## Run 2026-11-03 — Unity runtime layer compile check (outside Unity)

| Item | Value |
|---|---|
| Command | `dotnet build Tools/UnityCompileCheck -p:UnityManagedDir=<UnityEngine.Modules 2021.3.33 lib/net45>` |
| Scope | `Assets/_Project/Scripts/Runtime/**/*.cs` against UnityEngine 2021.3 reference assemblies |
| Result | **0 errors, 1 warning** (CS0649 on a serialized field Unity assigns; expected) |
| Not covered | `#if ENABLE_INPUT_SYSTEM` code (Input System package unavailable), all Editor scripts, Unity 6.3-only APIs |

## Builds
No player build has been made. No build is claimed.

## Failures encountered and fixed during development
See BUG_TRACKER.md (BUG-001 … BUG-007). Intermediate failing runs: combat timing assumptions in tests (6),
AI distinctness/adaptation (3), a locked-mission test setup error (1). All resolved before the final run above.
