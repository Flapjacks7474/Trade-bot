# Test Results

## Run 2026-11-07 (later) — simulation suite, Milestone 5 added

| Item | Value |
|---|---|
| Command | `Tools/run-tests.sh` |
| Environment | Linux container, .NET SDK 8.0.133, NUnit 3.14 |
| Simulation compiled as | netstandard2.1, C# 9, warnings as errors (0/0) |
| Result | **134 passed, 0 failed, 0 skipped** (14 new for NPC schedules, travel, tiers, personalities, time-aware dialogue) |
| Unity runtime compile check | 0 errors, 1 expected warning (CS0649) |
| Unity editor | **Not available — not run inside Unity** |

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
| ChapterOneStoryNpcsReachTheirPinsFromAnyHour | Passed |
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
| DriversAreFasterThanWalkers | Passed |
| DrivingIsDeterministic | Passed |
| DuplicateIdempotencyKeyNeverPostsTwice | Passed |
| EightDistrictsFiveCrewsAndCastingGateDefaultsClosed | Passed |
| EntryFeePurseAndFirstWinBonusPayCorrectlyOnce | Passed |
| EnvironmentalThrowNearWallDealsMoreDamage | Passed |
| EventBusDeliversUnsubscribesAndIsolatesFailures | Passed |
| EventBusDetectsEventLoops | Passed |
| EveryButtonActionHasAMask | Passed |
| EveryRaceCheckpointSitsOnAsphalt | Passed |
| FarAwayNpcsUpdateLessOftenButCoverTheSameGround | Passed |
| FileStorageWritesAtomicallyWithBackup | Passed |
| FinisherDefeatsWeakenedOpponent | Passed |
| FinisherRequiresMomentumVulnerabilityAndRange | Passed |
| FullChapterOnePlaythroughReachesChapterTwo | Passed |
| GarageStateRoundTripsThroughJson | Passed |
| GrappleBeatsBlockAndThrowKnocksDown | Passed |
| GroundAttackOnlyHitsDownedOpponent | Passed |
| HandbrakeCreatesMoreDriftThanSteeringAlone | Passed |
| HeldBlockBlocksStrikeAndTakesOnlyChip | Passed |
| HeldNpcsStopUntilReleased | Passed |
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
| NpcPositionsSurviveSaveAndLoadMidTrip | Passed |
| NpcsTravelContinuouslyAndNeverTeleport | Passed |
| OnlyWitnessesKnowAnEvent | Passed |
| OpponentsHaveDistinctFightingBehaviour | Passed |
| OptionalMissionChangesLaterDialogueAndCostsMoney | Passed |
| PayUsesCashThenCheckingAndIsIdempotent | Passed |
| PersonalitiesAreLoadedAndShapeReactions | Passed |
| PersonalityChangesReactionToTheSameThreat | Passed |
| PresetsChangeAssistsNotTopSpeed | Passed |
| ProfilesCoverEveryHourOfTheWeek | Passed |
| RacingStateRoundTripsAndStillBlocksDoublePay | Passed |
| RandomIsDeterministicAndRestorable | Passed |
| RankRisesWithPoints | Passed |
| ReplayNeverDuplicatesRewardsOrRewritesChoices | Passed |
| RetrieveDriveStoreRepairCycle | Passed |
| ReversalRequiresMomentumAndSwapsRoles | Passed |
| RoadGraphConnectsTheDistrictAndRoutesStayOnRoads | Passed |
| RoadsAreAsphaltAndGrassIsNot | Passed |
| RoundTripInMemory | Passed |
| RoutinesFollowTheClock | Passed |
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
| SleepingGrandmaBarksButStoryStillWorks | Passed |
| SocialStateRoundTrips | Passed |
| SouthsideHasEveryRequiredSliceLocation | Passed |
| StaminaPreventsEndlessHeavies | Passed |
| SteeringNeedsSpeedAndTurnsTheCar | Passed |
| StoryPinsOutrankRoutines | Passed |
| StrikeBeatsGrappleOnSameTick | Passed |
| TalkingToNpcsFollowsMissionState | Passed |
| TemperAmplifiesResentment | Passed |
| TimeLimitDnfRanksPlayerLast | Passed |
| TimedBlockParriesIntoCounter | Passed |
| TransfersAndBusinessMoneyStaySeparate | Passed |
| UpgradesChangeEffectiveStats | Passed |
| UpgradesRespectSlotsAndPayOnce | Passed |
| ValidationCatchesDuplicatesBadStatsAndMissingPrefabs | Passed |
| ValidationCatchesScheduleGapsAndUnknownPlaces | Passed |
| ValidationRejectsDuplicateIdsBadStatsAndCycles | Passed |
| ValuesClampTo0To100 | Passed |
| WantsTriggerOnlyForUnfinishedLiveObjectives | Passed |
| WitnessesComeFromWhoIsActuallyNearby | Passed |
| WreckedCarsMustBeRecoveredAndRepaired | Passed |

## Earlier run 2026-11-07 — Milestone 4: 120 passed, 0 failed (superseded above)

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
