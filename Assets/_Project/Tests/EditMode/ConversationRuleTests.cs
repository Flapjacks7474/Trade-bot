using FFTS.Core;
using FFTS.Session;
using FFTS.World;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class ConversationRuleTests
    {
        [Test]
        public void RulesFileValidates()
        {
            var s = TestContent.NewSession();
            var report = new ValidationReport();
            TestRules.Rules(s, report);
            var layout = DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), report);
            foreach (var n in layout.Npcs)
            {
                if (n.ShowAfterObjective != null) NpcConversationRules.CheckObjectiveRef(n.ShowAfterObjective, s.Content, report);
                if (n.HideAfterObjective != null) NpcConversationRules.CheckObjectiveRef(n.HideAfterObjective, s.Content, report);
            }
            Assert.IsTrue(report.IsValid, report.ToString());
        }

        [Test]
        public void TalkingToNpcsFollowsMissionState()
        {
            var s = TestContent.NewSession();
            var rules = TestRules.Rules(s);
            Assert.AreEqual("dialogue.grandma.welcome", rules.Choose(s, "npc.grandma").DialogueId);
            Assert.IsNotNull(rules.Choose(s, "npc.brick").Bark, "Brick won't fight before Reece books it");

            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            Assert.AreEqual("dialogue.grandma.home", rules.Choose(s, "npc.grandma").DialogueId);

            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            s.ReportTrigger("location.southside.corner_store");
            s.ReportTrigger("location.southside.tone_studio");
            Assert.AreEqual("dialogue.dre.reunion", rules.Choose(s, "npc.dre").DialogueId);
            TestContent.Talk(s, "dialogue.dre.reunion", "challenge", "go");
            Assert.IsNotNull(rules.Choose(s, "npc.dre").Bark, "must reach the gym first");
            s.ReportTrigger("location.southside.community_gym");
            var spar = rules.Choose(s, "npc.dre");
            Assert.AreEqual("fighter.dre", spar.FightId);
            Assert.AreEqual(VenueType.Sanctioned, spar.Venue);
        }

        [Test]
        public void DreMovesFromCourtsToGymAfterAgreeingToSpar()
        {
            var s = TestContent.NewSession();
            Assert.AreNotEqual(FFTS.Missions.MissionActionResult.Ok, s.Missions.Start("mission.ch1.old_friends"), "locked missions cannot be forced");
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            s.ReportTrigger("location.southside.corner_store");
            s.ReportTrigger("location.southside.tone_studio");
            s.TickWorld(0.1f, 0, 0);
            Assert.AreEqual("location.southside.basketball_courts", s.LivingWorld.Get("npc.dre").TargetLocationId, "waiting at the courts for the reunion");
            TestContent.Talk(s, "dialogue.dre.reunion", "challenge", "go");
            s.TickWorld(0.1f, 0, 0);
            var dre = s.LivingWorld.Get("npc.dre");
            Assert.AreEqual("location.southside.community_gym", dre.TargetLocationId);
            Assert.AreEqual(FFTS.LivingWorld.NpcStatus.Traveling, dre.Status, "he walks there; no teleport");
            for (int i = 0; i < 600 && dre.Status == FFTS.LivingWorld.NpcStatus.Traveling; i++) s.TickWorld(0.5f, dre.X, dre.Z);
            Assert.IsTrue(s.LivingWorld.IsAt("npc.dre", "location.southside.community_gym"));
        }
    }
}

namespace FFTS.Tests
{
    public class MissionQueryTests
    {
        [Test]
        public void WantsTriggerOnlyForUnfinishedLiveObjectives()
        {
            var s = TestContent.NewSession();
            Assert.IsTrue(s.Missions.WantsTrigger("location.southside.grandma_house"));
            Assert.IsFalse(s.Missions.WantsTrigger("interact.corner_store.prescription"), "errand not started yet");
            s.ReportTrigger("location.southside.grandma_house");
            Assert.IsFalse(s.Missions.WantsTrigger("location.southside.grandma_house"));
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            Assert.IsTrue(s.Missions.WantsTrigger("interact.corner_store.prescription"));
        }

        [Test]
        public void EveryButtonActionHasAMask()
        {
            foreach (FFTS.Input.GameAction a in System.Enum.GetValues(typeof(FFTS.Input.GameAction)))
            {
                bool axis = a == FFTS.Input.GameAction.Move || a == FFTS.Input.GameAction.Look || a == FFTS.Input.GameAction.Steer;
                Assert.AreEqual(axis, FFTS.Input.InputBindingMap.MaskFor(a) == FFTS.Input.ButtonMask.None, a.ToString());
            }
        }
    }
}
