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
            var layout = DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), new ValidationReport());
            var courts = layout.Npcs.Find(n => n.PlacementId == "placement.dre");
            var gym = layout.Npcs.Find(n => n.PlacementId == "placement.dre_gym");
            Assert.IsTrue(NpcConversationRules.IsVisible(s, courts));
            Assert.IsFalse(NpcConversationRules.IsVisible(s, gym));
            Assert.AreNotEqual(FFTS.Missions.MissionActionResult.Ok, s.Missions.Start("mission.ch1.old_friends"), "locked missions cannot be forced");
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
            s.ReportTrigger("location.southside.corner_store");
            s.ReportTrigger("location.southside.tone_studio");
            TestContent.Talk(s, "dialogue.dre.reunion", "challenge", "go");
            Assert.IsFalse(NpcConversationRules.IsVisible(s, courts));
            Assert.IsTrue(NpcConversationRules.IsVisible(s, gym));
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
