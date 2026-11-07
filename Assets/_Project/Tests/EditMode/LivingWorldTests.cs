using System;
using System.Linq;
using System.Numerics;
using FFTS.Core;
using FFTS.LivingWorld;
using FFTS.Missions;
using FFTS.World;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class LivingWorldTests
    {
        private static DistrictLayout Layout() => DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), new ValidationReport());

        private static LivingWorldManager World(out DistrictLayout layout)
        {
            layout = Layout();
            var report = new ValidationReport();
            var profiles = LivingWorldManager.Parse(TestData.Read("Characters/npc_profiles.json"), layout, report);
            Assert.IsTrue(report.IsValid, report.ToString());
            return new LivingWorldManager(profiles, layout, new EventBus());
        }

        [Test]
        public void ProfilesCoverEveryHourOfTheWeek() => World(out _);

        [Test]
        public void ValidationCatchesScheduleGapsAndUnknownPlaces()
        {
            const string json = @"{ ""Npcs"": [ { ""NpcId"": ""npc.x"", ""HomeLocation"": ""location.southside.apartments"",
                ""Schedule"": [ { ""StartHour"": 8, ""EndHour"": 20, ""LocationId"": ""location.southside.nowhere"" } ] } ] }";
            var r = new ValidationReport();
            LivingWorldManager.Parse(json, Layout(), r);
            StringAssert.Contains("no routine entry", r.ToString());
            StringAssert.Contains("unknown location location.southside.nowhere", r.ToString());
        }

        [Test]
        public void RoadGraphConnectsTheDistrictAndRoutesStayOnRoads()
        {
            var layout = Layout();
            var g = RoadGraph.Build(layout);
            Assert.Greater(g.Nodes.Count, 8);
            var from = new Vector2(-60, 33); var to = new Vector2(110, -92); // Grandma's front → the venue
            var path = g.FindPath(from, to);
            Assert.AreEqual(from, path.First()); Assert.AreEqual(to, path.Last());
            Assert.Greater(RoadGraph.Length(path), Vector2.Distance(from, to), "follows streets, not a straight line");
            for (int i = 1; i < path.Count - 1; i++)
                Assert.AreEqual(1f, layout.SurfaceGripAt(path[i].X, path[i].Y), $"waypoint {i} {path[i]} is on a road");
            // Every segment between interior waypoints lies along a road (sample midpoints).
            for (int i = 2; i < path.Count - 1; i++)
            {
                var mid = (path[i - 1] + path[i]) / 2;
                Assert.AreEqual(1f, layout.SurfaceGripAt(mid.X, mid.Y), $"segment {i} midpoint {mid}");
            }
        }

        [Test]
        public void RoutinesFollowTheClock()
        {
            var w = World(out _);
            Assert.AreEqual("sleep", w.DesiredEntry("npc.grandma", 0, 23).Activity);
            Assert.AreEqual("location.southside.barbershop", w.DesiredEntry("npc.lou", 0, 10).LocationId, "Monday morning: Lou cuts hair");
            Assert.AreNotEqual("location.southside.barbershop", w.DesiredEntry("npc.lou", 6, 10).LocationId, "Sunday: closed");
            Assert.AreEqual("location.southside.underground_venue", w.DesiredEntry("npc.big_reece", 2, 22).LocationId, "nights at the fights");
            Assert.AreEqual("location.southside.community_gym", w.DesiredEntry("npc.big_reece", 2, 10).LocationId, "days training people");
        }

        [Test]
        public void StoryPinsOutrankRoutines()
        {
            var w = World(out _);
            Assert.AreEqual("sleep", w.DesiredEntry("npc.rell", 0, 5).Activity);
            w.StoryConditionsMet = e => e.MissionActive == "mission.ch1.first_recording" && e.ObjectivesDone.Count == 0;
            var pinned = w.DesiredEntry("npc.rell", 0, 5);
            Assert.AreEqual("location.southside.tone_studio", pinned.LocationId, "Rell waits at the studio for the confrontation, even at 5am");
        }

        [Test]
        public void NpcsTravelContinuouslyAndNeverTeleport()
        {
            var w = World(out _);
            w.InitializeAt(0, 8);                    // Monday 08:00: Lou at home
            var lou = w.Get("npc.lou");
            Assert.AreEqual("location.southside.apartments", lou.LocationId);
            w.UpdateSchedules(0, 9);                 // 09:00: off to work
            Assert.AreEqual(NpcStatus.Traveling, lou.Status);
            float lastX = lou.X, lastZ = lou.Z; int ticks = 0;
            while (lou.Status == NpcStatus.Traveling && ticks < 20000)
            {
                w.Step(0.1f, lou.X, lou.Z);          // player right next to him: full simulation
                float moved = Vector2.Distance(new Vector2(lastX, lastZ), new Vector2(lou.X, lou.Z));
                Assert.LessOrEqual(moved, LivingWorldManager.WalkSpeed * 0.1f + 0.001f, "no jumps");
                lastX = lou.X; lastZ = lou.Z; ticks++;
            }
            Assert.IsTrue(w.IsAt("npc.lou", "location.southside.barbershop"));
        }

        [Test]
        public void FarAwayNpcsUpdateLessOftenButCoverTheSameGround()
        {
            Vector2 After(float playerX, float playerZ)
            {
                var w = World(out _);
                w.InitializeAt(0, 8); w.UpdateSchedules(0, 9);
                for (int i = 0; i < 600; i++) w.Step(0.1f, playerX, playerZ); // 60 s
                var n = w.Get("npc.lou");
                return new Vector2(n.X, n.Z);
            }
            var near = After(-110, 40);       // standing by the apartments
            var far = After(1000, 1000);      // other side of the city
            Assert.Less(Vector2.Distance(near, far), LivingWorldManager.WalkSpeed * LivingWorldManager.DistantInterval + 0.01f);
            var w2 = World(out _);
            w2.InitializeAt(0, 8);
            Assert.AreEqual(SimulationLevel.Distant, w2.LevelFor(w2.Get("npc.lou"), 1000, 1000));
            Assert.AreEqual(SimulationLevel.Full, w2.LevelFor(w2.Get("npc.lou"), w2.Get("npc.lou").X, w2.Get("npc.lou").Z));
        }

        [Test]
        public void HeldNpcsStopUntilReleased()
        {
            var w = World(out _);
            w.InitializeAt(0, 8); w.UpdateSchedules(0, 9);
            var lou = w.Get("npc.lou");
            w.Step(1f, lou.X, lou.Z);
            w.Hold("npc.lou", true);
            float x = lou.X, z = lou.Z;
            for (int i = 0; i < 50; i++) w.Step(0.1f, lou.X, lou.Z);
            Assert.AreEqual(x, lou.X); Assert.AreEqual(z, lou.Z);
            w.Hold("npc.lou", false);
            w.Step(1f, lou.X, lou.Z);
            Assert.AreNotEqual(new Vector2(x, z), new Vector2(lou.X, lou.Z));
        }

        [Test]
        public void DriversAreFasterThanWalkers()
        {
            var w = World(out _);
            Assert.Greater(w.SpeedOf("npc.rell"), w.SpeedOf("npc.grandma") * 4);
        }
    }

    public class LivingWorldSessionTests
    {
        [Test]
        public void PersonalitiesAreLoadedAndShapeReactions()
        {
            var s = TestContent.NewSession();
            Assert.AreEqual(65, s.Social.GetPersonality("npc.rell").Temper);
            s.Social.Modify("npc.rell", "player", new FFTS.Relationships.RelationshipDelta { Resentment = 20 }, "t");
            s.Social.Modify("npc.grandma", "player", new FFTS.Relationships.RelationshipDelta { Resentment = 20 }, "t");
            Assert.Greater(s.Social.Get("npc.rell", "player").Resentment, s.Social.Get("npc.grandma", "player").Resentment,
                "hot-tempered Rell holds a grudge harder than patient Grandma");
        }

        [Test]
        public void SleepingGrandmaBarksButStoryStillWorks()
        {
            var s = TestContent.NewSession();
            var rules = TestRules.Rules(s);
            Assert.AreEqual("dialogue.grandma.welcome", rules.Choose(s, "npc.grandma").DialogueId, "mission 1 first");
            s.ReportTrigger("location.southside.grandma_house");
            TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
            s.Clock.Advance(15 * 60);                // 23:00
            for (int i = 0; i < 400; i++) s.TickWorld(1f, -60, 30);
            Assert.AreEqual("sleep", s.LivingWorld.Get("npc.grandma").Activity);
            Assert.IsNotNull(rules.Choose(s, "npc.grandma").Bark);
            var home = s.StartDialogue("dialogue.grandma.home");
            StringAssert.Contains("late", home.CurrentLine, "night-time variant");
        }

        [Test]
        public void ChapterOneStoryNpcsReachTheirPinsFromAnyHour()
        {
            foreach (int startHour in new[] { 2, 9, 15, 21 })
            {
                var s = TestContent.NewSession();
                s.Clock.Advance((startHour + 24 - 8) % 24 * 60);
                s.LivingWorld.InitializeAt(s.Clock.Day, s.Clock.Hour);
                // Play to mission 4 (Rell pinned to the studio).
                s.ReportTrigger("location.southside.grandma_house");
                TestContent.Talk(s, "dialogue.grandma.welcome", "respect", "leave");
                TestContent.Talk(s, "dialogue.lou.barbershop", "ask", "more", "sharp");
                s.ReportTrigger("location.southside.corner_store");
                s.ReportTrigger("location.southside.tone_studio");
                TestContent.Talk(s, "dialogue.dre.reunion", "challenge", "go");
                s.ReportTrigger("location.southside.community_gym");
                s.OnFightCompleted(TestContent.Win("fighter.dre"), "fighter.dre", FFTS.Session.VenueType.Sanctioned, "location.southside.community_gym", null);
                Assert.AreEqual(MissionStatus.Active, s.Missions.Status("mission.ch1.first_recording"));
                for (int i = 0; i < 1200 && !s.LivingWorld.IsAt("npc.rell", "location.southside.tone_studio"); i++) s.TickWorld(1f, 0, 0);
                Assert.IsTrue(s.LivingWorld.IsAt("npc.rell", "location.southside.tone_studio"), $"start hour {startHour}");
            }
        }

        [Test]
        public void NpcPositionsSurviveSaveAndLoadMidTrip()
        {
            var s = TestContent.NewSession();
            s.Clock.Advance(60);   // 09:00 Monday: Lou heads to work
            s.TickWorld(5f, 0, 0);
            var lou = s.LivingWorld.Get("npc.lou");
            Assert.AreEqual(NpcStatus.Traveling, lou.Status);
            var storage = new FFTS.SaveSystem.InMemorySaveStorage();
            var saves = FFTS.Session.GameSession.CreateSaveManager(storage);
            saves.Save("manual_0", s.Capture());
            var r = TestContent.NewSession();
            r.Restore(saves.Load("manual_0").Data);
            var lou2 = r.LivingWorld.Get("npc.lou");
            Assert.AreEqual(lou.X, lou2.X, 0.001f); Assert.AreEqual(lou.Z, lou2.Z, 0.001f);
            Assert.AreEqual(NpcStatus.Traveling, lou2.Status);
            for (int i = 0; i < 600 && lou2.Status == NpcStatus.Traveling; i++) r.TickWorld(1f, lou2.X, lou2.Z);
            Assert.IsTrue(r.LivingWorld.IsAt("npc.lou", "location.southside.barbershop"));
        }

        [Test]
        public void WitnessesComeFromWhoIsActuallyNearby()
        {
            var s = TestContent.NewSession();  // Monday 08:00
            var near = s.LivingWorld.NpcsNear(-110, 40, 30);   // the apartments, where most of Southside sleeps in
            CollectionAssert.Contains(near, "npc.lou");
            CollectionAssert.DoesNotContain(near, "npc.grandma");
        }
    }
}
