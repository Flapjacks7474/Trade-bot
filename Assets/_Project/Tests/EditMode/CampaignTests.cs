using System.Linq;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Economy;
using FFTS.Missions;
using FFTS.Reputation;
using FFTS.SaveSystem;
using FFTS.Session;
using FFTS.Story;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class CampaignTests
    {
        private const string M1 = "mission.ch1.home_again", M2 = "mission.ch1.southside_stories", M3 = "mission.ch1.old_friends",
                             M4 = "mission.ch1.first_recording", M5 = "mission.ch1.prove_yourself", M6 = "mission.ch1.the_invitation",
                             Errand = "mission.side.grandmas_errand";

        private static void PlayToMission4(GameSession s)
        {
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "photo", "ok");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M1));
            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            s.ReportTrigger("location.southside.corner_store");
            s.ReportTrigger("location.southside.tone_studio");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M2));
            TestContent.Talk(s, "dialogue.dre.reunion", "challenge", "go");
            s.ReportTrigger("location.southside.community_gym");
            s.ReportTrigger("combat.tutorial.blocked");
            s.OnFightCompleted(TestContent.Win("fighter.dre"), "fighter.dre", VenueType.Sanctioned, "location.southside.community_gym", null);
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M3));
            s.ReportTrigger("location.southside.tone_studio");
            Assert.AreEqual(MissionStatus.Active, s.Missions.Status(M4));
        }

        [Test]
        public void MissionOneStartsAutomaticallyAndOthersAreLocked()
        {
            var s = TestContent.NewSession();
            Assert.AreEqual(MissionStatus.Active, s.Missions.Status(M1));
            Assert.AreEqual(M1, s.Missions.TrackedMission);
            Assert.AreEqual(MissionStatus.Locked, s.Missions.Status(M2));
            Assert.AreEqual(MissionStatus.Locked, s.Missions.Status(Errand));
        }

        [Test]
        public void FullChapterOnePlaythroughReachesChapterTwo()
        {
            var s = TestContent.NewSession();
            PlayToMission4(s);
            string fight = TestContent.Talk(s, "dialogue.rell.confrontation", "fight");
            Assert.AreEqual("fighter.rell", fight);
            Assert.AreEqual("fight", s.Missions.Progress(M4).ChosenBranch);
            s.OnFightCompleted(TestContent.Win("fighter.rell"), "fighter.rell", VenueType.Street, "location.southside.tone_studio", new[] { "npc.tone" });
            TestContent.Talk(s, "dialogue.tone.studio", "record", "thanks");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M4));

            s.ReportTrigger("location.southside.underground_venue");
            TestContent.Talk(s, "dialogue.reece.venue", "yes");
            s.OnFightCompleted(TestContent.Win("fighter.brick", FightEndMethod.Finisher), "fighter.brick", VenueType.Underground, "location.southside.underground_venue", new[] { "npc.big_reece" });
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M5));

            TestContent.Talk(s, "dialogue.nia.invitation", "negotiate", "yes");
            TestContent.Talk(s, "dialogue.grandma.invitation", "tell", "promise");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M6));
            Assert.AreEqual(2, s.Story.Chapter);
            Assert.IsTrue(s.Story.Has("story.ch2.showcase_invited"));
            Assert.IsTrue(s.Story.Has(StoryFlags.PhotoFirstSeen));
            Assert.IsFalse(s.Story.Has(StoryFlags.ChairmanIdentityRevealed));
            CollectionAssert.IsSupersetOf(s.Player.Contacts, new[] { "npc.lou", "npc.tone", "npc.big_reece", "npc.nia" });

            // $40 start + $200 grandma + $100 Dre + $500 purse.
            Assert.AreEqual(4000 + 20000 + 10000 + 50000, s.Bank.PersonalTotal());
            Assert.Greater(s.Reputation.Get(FaceCardAttribute.Respect), 10);
            Assert.Greater(s.Social.Get("npc.rell", "player").Rivalry, 0);
            Assert.IsTrue(s.Social.Knows("npc.tone", s.Social.MemoriesOf("npc.tone").Single().EventId), "Tone witnessed the Rell fight");
            Assert.IsFalse(s.Social.MemoriesOf("npc.nia").Any(), "Nia witnessed nothing");
        }

        [Test]
        public void BranchChoiceIsLockedAndOnlyItsObjectivesCount()
        {
            var s = TestContent.NewSession();
            PlayToMission4(s);
            TestContent.Talk(s, "dialogue.rell.confrontation", "talk");
            Assert.AreEqual(MissionActionResult.AlreadyChosen, s.Missions.ChooseBranch(M4, "fight"));
            Assert.AreEqual(MissionActionResult.WrongBranch, s.Missions.CompleteObjective(M4, "win_rell"));
            TestContent.Talk(s, "dialogue.tone.studio", "record", "thanks");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M4), "talk branch needs no fight");
            Assert.IsTrue(s.Story.Has("ch1.rell_talked"));
            // Talking it down shows up in Big Reece's lines later.
            s.ReportTrigger("location.southside.underground_venue");
            StringAssert.Contains("talked Rell down", s.StartDialogue("dialogue.reece.venue").CurrentLine);
        }

        [Test]
        public void LosingAFightFailsThenRetriesFromCheckpoint()
        {
            var s = TestContent.NewSession();
            PlayToMission4(s);
            TestContent.Talk(s, "dialogue.rell.confrontation", "fight");
            s.OnFightCompleted(TestContent.Lose("fighter.rell"), "fighter.rell", VenueType.Street, "location.southside.tone_studio", null);
            var p = s.Missions.Progress(M4);
            Assert.AreEqual(MissionStatus.Active, p.Status, "recoverable missions restart automatically");
            Assert.AreEqual(2, p.Attempts);
            Assert.AreEqual("fight.lost.fighter.rell", p.LastFailure);
            CollectionAssert.AreEquivalent(new[] { "go_studio", "handle_rell" }, p.CompletedObjectives, "checkpoint objectives survive");
            Assert.AreEqual("fight", p.ChosenBranch, "the choice itself persists through failure");
            s.OnFightCompleted(TestContent.Win("fighter.rell"), "fighter.rell", VenueType.Street, "location.southside.tone_studio", null);
            TestContent.Talk(s, "dialogue.tone.studio", "record", "thanks");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(M4));
        }

        [Test]
        public void ReplayNeverDuplicatesRewardsOrRewritesChoices()
        {
            var s = TestContent.NewSession();
            PlayToMission4(s);
            TestContent.Talk(s, "dialogue.rell.confrontation", "talk");
            TestContent.Talk(s, "dialogue.tone.studio", "record", "thanks");
            long money = s.Bank.PersonalTotal();
            int respect = s.Reputation.Get(FaceCardAttribute.Respect);
            int ledger = s.Bank.Ledger.Count;

            Assert.AreEqual(MissionActionResult.Ok, s.Missions.Start(M1)); // replay a completed mission
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            Assert.AreEqual(MissionActionResult.Ok, s.Missions.Start(M4));
            s.Missions.ChooseBranch(M4, "fight");
            Assert.AreEqual("talk", s.Missions.Progress(M4).ChosenBranch, "replay must not rewrite the live decision");

            Assert.AreEqual(money, s.Bank.PersonalTotal());
            Assert.AreEqual(respect, s.Reputation.Get(FaceCardAttribute.Respect));
            Assert.AreEqual(ledger, s.Bank.Ledger.Count);
            Assert.IsFalse(s.Story.Has("ch1.rell_fought"));
        }

        [Test]
        public void OptionalMissionChangesLaterDialogueAndCostsMoney()
        {
            var s = TestContent.NewSession();
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            Assert.AreEqual(MissionStatus.Active, s.Missions.Status(Errand));
            long before = s.Bank.PersonalTotal();
            s.ReportTrigger("interact.corner_store.prescription");
            s.ReportTrigger("interact.grandma_house.deliver");
            Assert.AreEqual(MissionStatus.Completed, s.Missions.Status(Errand));
            Assert.AreEqual(before - 1500, s.Bank.PersonalTotal());
            var run = s.StartDialogue("dialogue.grandma.invitation"); run.Choose("tell");
            StringAssert.Contains("prescription", run.CurrentLine);

            var other = TestContent.NewSession();
            var run2 = other.StartDialogue("dialogue.grandma.invitation"); run2.Choose("tell");
            StringAssert.DoesNotContain("prescription", run2.CurrentLine);
        }

        [Test]
        public void DialogueEffectsApplyOnceEvenWhenRepeated()
        {
            var s = TestContent.NewSession();
            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            int f = s.Social.Get("npc.lou", "player").Friendship;
            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            Assert.AreEqual(f, s.Social.Get("npc.lou", "player").Friendship);
        }

        [Test]
        public void ConditionalOptionsHideUntilRequirementsMet()
        {
            var s = TestContent.NewSession();
            var tone = s.StartDialogue("dialogue.tone.studio");
            Assert.IsFalse(tone.AvailableOptions().Any(o => o.Id == "record"));
            StringAssert.Contains("Rell", tone.CurrentLine);
            var grandma = s.StartDialogue("dialogue.grandma.welcome"); grandma.Choose("respect");
            Assert.IsTrue(grandma.AvailableOptions().Any(o => o.Id == "photo"));
            grandma.Choose("photo"); grandma.Choose("ok");
            var again = s.StartDialogue("dialogue.grandma.welcome"); again.Choose("respect");
            Assert.IsFalse(again.AvailableOptions().Any(o => o.Id == "photo"), "photo question only asked once");
        }

        [Test]
        public void SaveLoadPreservesMissionsBranchesMoneyRelationshipsAndDedupe()
        {
            var s = TestContent.NewSession();
            PlayToMission4(s);
            TestContent.Talk(s, "dialogue.rell.confrontation", "stand", "ok");
            s.Clock.Advance(125);
            s.Position.X = 12.5f; s.Player.StageName = "Lil Southside";

            var storage = new InMemorySaveStorage();
            var saves = GameSession.CreateSaveManager(storage);
            saves.Save("manual_0", s.Capture("test"));

            var loaded = saves.Load("manual_0");
            Assert.AreEqual(LoadOutcome.Loaded, loaded.Outcome, loaded.Message);
            var r = TestContent.NewSession();
            r.Restore(loaded.Data);

            Assert.AreEqual(s.Bank.PersonalTotal(), r.Bank.PersonalTotal());
            Assert.AreEqual(s.Clock.TotalMinutes, r.Clock.TotalMinutes);
            Assert.AreEqual("stand_ground", r.Missions.Progress(M4).ChosenBranch);
            Assert.AreEqual(MissionStatus.Active, r.Missions.Status(M4));
            Assert.AreEqual(MissionStatus.Completed, r.Missions.Status(M3));
            Assert.AreEqual(s.Social.Get("npc.rell", "player").Respect, r.Social.Get("npc.rell", "player").Respect);
            Assert.AreEqual(s.Reputation.Get(FaceCardAttribute.Respect), r.Reputation.Get(FaceCardAttribute.Respect));
            Assert.AreEqual("Lil Southside", r.Player.StageName);
            Assert.AreEqual(12.5f, r.Position.X);
            Assert.AreEqual(s.Random.State, r.Random.State);

            // Rewards already granted before the save stay granted after it.
            long money = r.Bank.PersonalTotal();
            r.Missions.Start(M3);
            r.OnFightCompleted(TestContent.Win("fighter.dre"), "fighter.dre", VenueType.Sanctioned, "location.southside.community_gym", null);
            Assert.AreEqual(money, r.Bank.PersonalTotal());
            Assert.AreEqual(MissionActionResult.AlreadyChosen, r.Missions.ChooseBranch(M4, "talk"));

            // Finish after load.
            TestContent.Talk(r, "dialogue.tone.studio", "record", "thanks");
            Assert.AreEqual(MissionStatus.Completed, r.Missions.Status(M4));
        }

        [Test]
        public void SessionSaveSurvivesCorruptionViaBackup()
        {
            var s = TestContent.NewSession();
            var storage = new InMemorySaveStorage();
            var saves = GameSession.CreateSaveManager(storage);
            saves.Save("autosave_0", s.Capture());
            s.ReportTrigger("location.southside.grandma_house");
            saves.Save("autosave_0", s.Capture());
            storage.CorruptPrimary("autosave_0", storage.Read("autosave_0").Substring(0, 200));
            var r = saves.Load("autosave_0");
            Assert.AreEqual(LoadOutcome.RecoveredFromBackup, r.Outcome);
        }
    }
}
