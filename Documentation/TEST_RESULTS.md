# Test Results

## Run 2026-11-07 — simulation suite (outside Unity), Milestone 4 added

| Item | Value |
|---|---|
| Command | `Tools/run-tests.sh` (`dotnet test` on `Tests/DotNet/SimulationTests`) |
| Environment | Linux container, .NET SDK 8.0.133, NUnit 3.14, NUnit3TestAdapter 4.5 |
| Simulation compiled as | netstandard2.1, C# 9, warnings as errors (0 warnings, 0 errors) |
| Result | **120 passed, 0 failed, 0 skipped** (82 from the foundation + 38 for vehicles, driving, garages, racing) |
| Unity editor | **Not available — EditMode tests NOT yet run inside Unity** |

| Test | Result |
|---|---|
| AcceleratesTowardButNotPastTopSpeed | Passed |
| AiAdaptsToRepeatedHeavySpam | Passed |
| AiCannotReactFasterThanItsReactionTime | Passed |
| AiDefeatsPassiveOpponent("fighter.brick") | Passed |
| AiDefeatsPassiveOpponent("fighter.rell") | Passed |
| AiNeverExceedsItsCarsRealTopSpeed | Passed |
| AiRacersFinishTheCourseWithinTheLimit | Passed |
| AiVersusAiFightsAlwaysResolve | Passed |
| AllShippedContentValidatesAndCrossReferences | Passed |
| AutosaveRotatesSlots | Passed |
| BankStateRoundTripsAndKeepsDedupe | Passed |
| BlockingDrainsStaminaUntilGuardBreak | Passed |
| BrakeHeldWhenStoppedReversesWithLimit | Passed |
| BrakingStopsFasterThanCoasting | Passed |
| BranchChoiceIsLockedAndOnlyItsObjectivesCount | Passed |
| BreakawayEscapesLongCombo | Passed |
| BuyDriveRaceSaveLoadThroughTheSession | Passed |
| BuyingChargesOnceAndParksAtHome | Passed |
| CannotBuyWithoutMoneyOrSpaceOrWhereNotSold | Passed |
| CannotEnterBrokeOrWreckedOrWithoutACar | Passed |
| ChairmanRevealIsBlockedBeforeChapterSevenMission | Passed |
| ChaptersOnlyMoveForward | Passed |
| CheckpointsOnlyCountInOrder | Passed |
| ClassesFeelDifferent | Passed |
| ClockSegmentsAndEvents | Passed |
| ConditionalOptionsHideUntilRequirementsMet | Passed |
| ContentCrossCheckCoversRacesAndGarages | Passed |
| CorruptPrimaryFallsBackToBackup | Passed |
| CorruptWithoutBackupIsReportedNotLoaded | Passed |
| CountdownHoldsEveryoneThenGo | Passed |
| CounterStanceBeatsHeavyThenCounterHeavySignatureRoute | Passed |
| CrossCheckCatchesUnreachableTriggers | Passed |
| DeniedFinisherReportsReason | Passed |
| DialogueEffectsApplyOnceEvenWhenRepeated | Passed |
| DodgeAvoidsAttackDuringInvulnerability | Passed |
| DreMovesFromCourtsToGymAfterAgreeingToSpar | Passed |
| DrivingIsDeterministic | Passed |
| DuplicateIdempotencyKeyNeverPostsTwice | Passed |
| EightDistrictsFiveCrewsAndCastingGateDefaultsClosed | Passed |
| EntryFeePurseAndFirstWinBonusPayCorrectlyOnce | Passed |
| EnvironmentalThrowNearWallDealsMoreDamage | Passed |
| EventBusDeliversUnsubscribesAndIsolatesFailures | Passed |
| EventBusDetectsEventLoops | Passed |
| EveryButtonActionHasAMask | Passed |
| EveryRaceCheckpointSitsOnAsphalt | Passed |
| FileStorageWritesAtomicallyWithBackup | Passed |
| FinisherDefeatsWeakenedOpponent | Passed |
| FinisherRequiresMomentumVulnerabilityAndRange | Passed |
| FullChapterOnePlaythroughReachesChapterTwo | Passed |
| GarageStateRoundTripsThroughJson | Passed |
| GrappleBeatsBlockAndThrowKnocksDown | Passed |
| GroundAttackOnlyHitsDownedOpponent | Passed |
| HandbrakeCreatesMoreDriftThanSteeringAlone | Passed |
| HeldBlockBlocksStrikeAndTakesOnlyChip | Passed |
| HigherDifficultyWinsMoreOften | Passed |
| HitstunDecayPreventsInfiniteLightSpam | Passed |
| IdRulesRejectBadIdsAndRegistryCatchesDuplicates | Passed |
| ImpactsDamageAndHeavyDamageSlowsTheCar | Passed |
| InputEdgesAndRebinding | Passed |
| InputMapperPrioritisesAndOrientsMovement | Passed |
| InsufficientFundsAndInvalidRequestsAreRejectedWithoutSideEffects | Passed |
| InteractionSelectorPrefersInFrontPriorityAndRange | Passed |
| KeyboardAndPadMapToDriveControls | Passed |
| KnockdownRecoversAndQuickRiseIsFaster | Passed |
| KnockoutEndsFightAndTimeDecisionWorks | Passed |
| LightAttackHitsAndDealsDamage | Passed |
| LightHeavyKickRouteEndsInKnockdown | Passed |
| LightLightHeavyComboLandsThreeHitsWithDamageScaling | Passed |
| LosingAFightFailsThenRetriesFromCheckpoint | Passed |
| LowGripSurfaceSlidesMore | Passed |
| MashingEscapesTheClinch | Passed |
| MemoryChangesAttitudeUntilResolved | Passed |
| MigrationsUpgradeOldSaves | Passed |
| MissionOneStartsAutomaticallyAndOthersAreLocked | Passed |
| MovesetCombinesUpToThreeStyles | Passed |
| NewerSaveVersionIsRefused | Passed |
| NitrousExceedsNormalTopSpeedThenRunsOutAndBleedsDown | Passed |
| NoNitrousWithoutTheKit | Passed |
| OnlyWitnessesKnowAnEvent | Passed |
| OpponentsHaveDistinctFightingBehaviour | Passed |
| OptionalMissionChangesLaterDialogueAndCostsMoney | Passed |
| PayUsesCashThenCheckingAndIsIdempotent | Passed |
| PersonalityChangesReactionToTheSameThreat | Passed |
| PresetsChangeAssistsNotTopSpeed | Passed |
| RacingStateRoundTripsAndStillBlocksDoublePay | Passed |
| RandomIsDeterministicAndRestorable | Passed |
| RankRisesWithPoints | Passed |
| ReplayNeverDuplicatesRewardsOrRewritesChoices | Passed |
| RetrieveDriveStoreRepairCycle | Passed |
| ReversalRequiresMomentumAndSwapsRoles | Passed |
| RoadsAreAsphaltAndGrassIsNot | Passed |
| RoundTripInMemory | Passed |
| RulesFileValidates | Passed |
| RumorsNeedASourceAndDegrade | Passed |
| SameFightMovesFaceCardAndPublicImageDifferently | Passed |
| SaveLoadPreservesMissionsBranchesMoneyRelationshipsAndDedupe | Passed |
| SellingPaysResaleOnceAndRemovesTheCar | Passed |
| SessionSaveSurvivesCorruptionViaBackup | Passed |
| ShippedCombatDataIsValid | Passed |
| ShippedRaceDataIsValid | Passed |
| ShippedVehicleDataIsValid | Passed |
| SimulationIsDeterministic | Passed |
| SkillMatters | Passed |
| SocialStateRoundTrips | Passed |
| SouthsideHasEveryRequiredSliceLocation | Passed |
| StaminaPreventsEndlessHeavies | Passed |
| SteeringNeedsSpeedAndTurnsTheCar | Passed |
| StrikeBeatsGrappleOnSameTick | Passed |
| TalkingToNpcsFollowsMissionState | Passed |
| TemperAmplifiesResentment | Passed |
| TimeLimitDnfRanksPlayerLast | Passed |
| TimedBlockParriesIntoCounter | Passed |
| TransfersAndBusinessMoneyStaySeparate | Passed |
| UpgradesChangeEffectiveStats | Passed |
| UpgradesRespectSlotsAndPayOnce | Passed |
| ValidationCatchesDuplicatesBadStatsAndMissingPrefabs | Passed |
| ValidationRejectsDuplicateIdsBadStatsAndCycles | Passed |
| ValuesClampTo0To100 | Passed |
| WantsTriggerOnlyForUnfinishedLiveObjectives | Passed |
| WreckedCarsMustBeRecoveredAndRepaired | Passed |

## Run 2026-11-07 — Unity runtime layer compile check (outside Unity)

| Item | Value |
|---|---|
| Command | `dotnet build Tools/UnityCompileCheck -p:UnityManagedDir=<UnityEngine.Modules 2021.3.33 lib/net45>` |
| Scope | `Assets/_Project/Scripts/Runtime/**/*.cs` incl. new `Runtime/Driving/*` |
| Result | **0 errors, 1 warning** (CS0649 on a serialized field; expected). One error (missing `using System.Linq`) was caught and fixed before this run |
| Not covered | Input System code, Editor scripts, Unity 6.3-only APIs |

## Earlier run 2026-11-03
82 passed, 0 failed (foundation). Superseded by the run above.

## Race behaviour probe (diagnostic, not a kept test)
Pecan Street Sprint, 3 seeds, test driver at skill 0.8 in the Magnolia Cruiser: Rell (Kudzu RS, skill 0.7) won in 19.2 s
with top speed 136 km/h; test driver 21.5 s / 126 km/h; Deuce (skill 0.45) still on course. Course ≈520 m.

## Builds
No player build has been made. No build is claimed.
