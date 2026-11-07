using FFTS.SaveSystem;
using FFTS.Session;
using System.Linq;
using FFTS.Core;
using FFTS.Economy;
using FFTS.Racing;
using FFTS.Relationships;
using FFTS.Reputation;
using FFTS.Vehicles;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class RacingTests
    {
        private const string Sprint = "race.southside.pecan_sprint";
        private const float Dt = 1f / 60f;

        public static RaceDatabase Races(VehicleDatabase vdb)
        {
            var r = new ValidationReport();
            var db = RaceDatabase.FromJson(new[] { TestData.Read("Racing/races.json") }, vdb, r);
            Assert.IsTrue(r.IsValid, r.ToString());
            return db;
        }

        private static VehicleDynamics PlayerCar(VehicleDatabase vdb, string id = "vehicle.magnolia_cruiser")
            => new VehicleDynamics(VehicleStats.From(vdb.Get(id)), HandlingPreset.Street);

        /// <summary>Drives the player with an AI of the given skill (stand-in for a human in tests).</summary>
        private static void RunToEnd(RaceSession s, float playerSkill, ulong seed = 99)
        {
            var bot = new RaceDriverAI(playerSkill, new DeterministicRandom(seed));
            for (int i = 0; i < 60 * 400 && !s.IsOver; i++) s.Step(bot.Think(s, s.Player), Dt);
        }

        [Test]
        public void ShippedRaceDataIsValid() => Races(VehicleDataTests.Db());

        [Test]
        public void CountdownHoldsEveryoneThenGo()
        {
            var vdb = VehicleDataTests.Db();
            var s = new RaceSession(Races(vdb).Get(Sprint), PlayerCar(vdb), vdb, 1);
            float x0 = s.Player.Car.X;
            bool sawGo = false;
            for (int i = 0; i < 60 * 2; i++) { s.Step(new DriveInput { Throttle = 1 }, Dt); }
            Assert.AreEqual(x0, s.Player.Car.X, 0.001f, "no jump starts");
            for (int i = 0; i < 70; i++) { s.Step(new DriveInput { Throttle = 1 }, Dt); sawGo |= s.Events.Any(e => e.Type == RaceEventType.Go); }
            Assert.IsTrue(sawGo);
        }

        [Test]
        public void CheckpointsOnlyCountInOrder()
        {
            var vdb = VehicleDataTests.Db();
            var def = Races(vdb).Get(Sprint);
            var s = new RaceSession(def, PlayerCar(vdb), vdb, 1);
            for (int i = 0; i < 200; i++) s.Step(new DriveInput(), Dt); // past countdown
            var finish = def.Checkpoints.Last();
            s.Player.Car.X = finish.X; s.Player.Car.Z = finish.Z; // teleport to the finish line
            s.Step(new DriveInput(), Dt);
            Assert.IsFalse(s.Player.Finished, "skipping checkpoints does nothing");
            Assert.AreEqual(0, s.Player.NextCheckpoint);
            foreach (var c in def.Checkpoints) { s.Player.Car.X = c.X; s.Player.Car.Z = c.Z; s.Player.Car.Speed = 0; s.Step(new DriveInput(), Dt); }
            Assert.IsTrue(s.Player.Finished);
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(1, s.Player.Place);
        }

        [Test]
        public void AiRacersFinishTheCourseWithinTheLimit()
        {
            var vdb = VehicleDataTests.Db();
            var def = Races(vdb).Get(Sprint);
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var s = new RaceSession(def, PlayerCar(vdb), vdb, seed);
                // Player parks; the AI must finish on its own.
                for (int i = 0; i < 60 * 140; i++) s.Step(new DriveInput(), Dt);
                foreach (var r in s.Racers.Where(r => !r.IsPlayer))
                    Assert.IsTrue(r.Finished, $"seed {seed}: {r.DisplayName} stuck at checkpoint {r.NextCheckpoint} ({r.Car.X:0},{r.Car.Z:0})");
            }
        }

        [Test]
        public void AiNeverExceedsItsCarsRealTopSpeed()
        {
            var vdb = VehicleDataTests.Db();
            var s = new RaceSession(Races(vdb).Get(Sprint), PlayerCar(vdb), vdb, 3);
            for (int i = 0; i < 60 * 140; i++) s.Step(new DriveInput(), Dt);
            foreach (var r in s.Racers.Where(r => !r.IsPlayer))
                Assert.LessOrEqual(r.TopSpeedSeen, r.Car.Stats.MaxSpeed + 0.01f, r.DisplayName);
        }

        [Test]
        public void SkillMatters()
        {
            var vdb = VehicleDataTests.Db();
            var def = Races(vdb).Get(Sprint);
            float Time(float skill, ulong seed)
            {
                var s = new RaceSession(def, PlayerCar(vdb, "vehicle.kudzu_rs"), vdb, seed);
                RunToEnd(s, skill, seed);
                return s.PlayerDnf ? 999 : s.Player.FinishTime;
            }
            int better = 0;
            for (ulong seed = 1; seed <= 6; seed++) if (Time(0.95f, seed) < Time(0.2f, seed)) better++;
            Assert.GreaterOrEqual(better, 5);
        }

        [Test]
        public void TimeLimitDnfRanksPlayerLast()
        {
            var vdb = VehicleDataTests.Db();
            var def = Races(vdb).Get(Sprint);
            var s = new RaceSession(def, PlayerCar(vdb), vdb, 1);
            for (int i = 0; i < 60 * 160 && !s.IsOver; i++) s.Step(new DriveInput(), Dt);
            Assert.IsTrue(s.IsOver);
            Assert.IsTrue(s.PlayerDnf);
            Assert.AreEqual(s.Racers.Count, s.Player.Place);
        }

        private static (RaceManager m, FinessBank bank, SocialManager social, ReputationManager rep) Manager(long cash)
        {
            var bus = new EventBus();
            var vdb = VehicleDataTests.Db();
            var bank = FinessBank.CreateStarter(bus, () => 0, cash);
            var social = new SocialManager(bus); var rep = new ReputationManager(bus);
            return (new RaceManager(Races(vdb), vdb, bank, rep, social, bus), bank, social, rep);
        }

        private static RaceSession WinScripted(RaceManager m, VehicleDatabase vdb)
        {
            Assert.AreEqual(RaceEntryResult.Ok, m.Begin(Sprint, PlayerCar(vdb), 7, out var s));
            for (int i = 0; i < 200; i++) s.Step(new DriveInput(), Dt);
            foreach (var c in s.Definition.Checkpoints) { s.Player.Car.X = c.X; s.Player.Car.Z = c.Z; s.Player.Car.Speed = 0; s.Step(new DriveInput(), Dt); }
            Assert.IsTrue(s.IsOver);
            return s;
        }

        [Test]
        public void EntryFeePurseAndFirstWinBonusPayCorrectlyOnce()
        {
            var (m, bank, social, rep) = Manager(10000);
            var vdb = VehicleDataTests.Db();
            var s = WinScripted(m, vdb);
            Assert.AreEqual(10000 - 2000, bank.PersonalTotal(), "entry fee only so far");
            var o = m.Resolve(s);
            Assert.AreEqual(1, o.Place);
            Assert.AreEqual(30000, o.PurseCents);
            Assert.AreEqual(25000, o.BonusCents);
            Assert.AreEqual(10000 - 2000 + 30000 + 25000, bank.PersonalTotal());
            Assert.IsTrue(m.Resolve(s).AlreadyResolved);
            Assert.AreEqual(10000 - 2000 + 30000 + 25000, bank.PersonalTotal(), "resolving twice pays nothing");

            var s2 = WinScripted(m, vdb);
            var o2 = m.Resolve(s2);
            Assert.AreEqual(0, o2.BonusCents, "first-win bonus is once ever");
            Assert.AreEqual(30000, o2.PurseCents, "a new attempt can pay a new purse");
            Assert.AreEqual(2, m.Wins(Sprint));
            Assert.AreEqual(2, m.Attempts(Sprint));
            Assert.Greater(social.Get("npc.rell", "player").Rivalry, 0);
            Assert.Greater(rep.Get(PublicImageAttribute.Popularity), 0, "clean win raises popularity");
        }

        [Test]
        public void CannotEnterBrokeOrWreckedOrWithoutACar()
        {
            var (m, _, _, _) = Manager(500);
            var vdb = VehicleDataTests.Db();
            Assert.AreEqual(RaceEntryResult.InsufficientFunds, m.Begin(Sprint, PlayerCar(vdb), 1, out _));
            Assert.AreEqual(0, m.Attempts(Sprint), "a refused entry is not an attempt");
            var (rich, _, _, _) = Manager(100000);
            Assert.AreEqual(RaceEntryResult.NoVehicle, rich.Begin(Sprint, null, 1, out _));
            var wreck = PlayerCar(vdb); wreck.Damage = 100;
            Assert.AreEqual(RaceEntryResult.VehicleWrecked, rich.Begin(Sprint, wreck, 1, out _));
        }

        [Test]
        public void RankRisesWithPoints()
        {
            Assert.AreEqual(RacingRank.UnknownDriver, RaceManager.RankFor(99));
            Assert.AreEqual(RacingRank.NeighborhoodRunner, RaceManager.RankFor(100));
            Assert.AreEqual(RacingRank.SouthernRoadLegend, RaceManager.RankFor(5000));
            var (m, _, _, _) = Manager(1_000_000);
            var vdb = VehicleDataTests.Db();
            m.Resolve(WinScripted(m, vdb)); m.Resolve(WinScripted(m, vdb));
            Assert.AreEqual(120, m.RankPoints);
            Assert.AreEqual(RacingRank.NeighborhoodRunner, m.Rank);
        }

        [Test]
        public void RacingStateRoundTripsAndStillBlocksDoublePay()
        {
            var (m, bank, social, rep) = Manager(100000);
            var vdb = VehicleDataTests.Db();
            var s = WinScripted(m, vdb);
            m.Resolve(s);
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(m.Capture());
            var m2 = new RaceManager(Races(vdb), vdb, bank, rep, social, new EventBus());
            m2.Restore(Newtonsoft.Json.JsonConvert.DeserializeObject<RacingState>(saved));
            Assert.AreEqual(60, m2.RankPoints);
            Assert.IsTrue(m2.Resolve(s).AlreadyResolved);
            Assert.IsNotNull(m2.BestTime(Sprint));
        }
    }
}

namespace FFTS.Tests
{
    public class DrivingSessionTests
    {
        [Test]
        public void BuyDriveRaceSaveLoadThroughTheSession()
        {
            var s = TestContent.NewSession();
            s.Bank.Post(new TransactionRequest { IdempotencyKey = "test.cash", To = FinessBank.Cash, AmountCents = 200000 });
            Assert.AreEqual(VehicleResult.Ok, s.Garage.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "ui.1", out var car));
            Assert.AreEqual(VehicleResult.Ok, s.Garage.Retrieve(car.OwnershipId, out _));
            var dyn = s.Garage.CreateDynamics(car.OwnershipId);

            Assert.AreEqual(RaceEntryResult.Ok, s.Racing.Begin("race.southside.pecan_sprint", dyn, 5, out var race));
            for (int i = 0; i < 200; i++) race.Step(new DriveInput(), 1f / 60f);
            foreach (var c in race.Definition.Checkpoints) { race.Player.Car.X = c.X; race.Player.Car.Z = c.Z; race.Player.Car.Speed = 0; race.Step(new DriveInput(), 1f / 60f); }
            var outcome = s.ResolveRace(race, car.OwnershipId);
            Assert.AreEqual(1, outcome.Place);

            var storage = new InMemorySaveStorage();
            var saves = GameSession.CreateSaveManager(storage);
            saves.Save("manual_0", s.Capture());
            var r = TestContent.NewSession();
            r.Restore(saves.Load("manual_0").Data);

            Assert.AreEqual(s.Bank.PersonalTotal(), r.Bank.PersonalTotal());
            Assert.AreEqual(1, r.Garage.Vehicles.Count);
            Assert.AreEqual(car.OwnershipId, r.Garage.ActiveVehicle);
            Assert.AreEqual(VehicleAvailability.Out, r.Garage.Get(car.OwnershipId).Availability);
            Assert.AreEqual(60, r.Racing.RankPoints);
            Assert.AreEqual(1, r.Racing.Wins("race.southside.pecan_sprint"));
            Assert.IsTrue(r.ResolveRace(race).AlreadyResolved, "a loaded save can't be paid for the same race again");
            Assert.AreEqual(s.Bank.PersonalTotal(), r.Bank.PersonalTotal());
        }

        [Test]
        public void ContentCrossCheckCoversRacesAndGarages()
        {
            var src = TestContent.Sources();
            src.Races[0] = src.Races[0].Replace("location.southside.race_start", "location.southside.nowhere");
            var report = new FFTS.Core.ValidationReport();
            GameContentLoader.Load(src, report, out _);
            StringAssert.Contains("location.southside.nowhere", report.ToString());
        }
    }
}

namespace FFTS.Tests
{
    public class DrivingInputAndSurfaceTests
    {
        [Test]
        public void KeyboardAndPadMapToDriveControls()
        {
            var f = new FFTS.Input.InputFrame { MoveX = -1, MoveY = 1, Held = FFTS.Input.ButtonMask.Handbrake };
            var d = DriveInputMapper.Map(f, 0.7f);
            Assert.AreEqual(1f, d.Throttle); Assert.AreEqual(0f, d.Brake); Assert.AreEqual(-1f, d.Steer);
            Assert.IsTrue(d.Handbrake); Assert.IsFalse(d.Nitrous); Assert.AreEqual(0.7f, d.SurfaceGrip);
            var pad = DriveInputMapper.Map(new FFTS.Input.InputFrame { Held = FFTS.Input.ButtonMask.Brake | FFTS.Input.ButtonMask.Nitrous }, 1f);
            Assert.AreEqual(1f, pad.Brake); Assert.IsTrue(pad.Nitrous);
        }

        [Test]
        public void RoadsAreAsphaltAndGrassIsNot()
        {
            var layout = FFTS.World.DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), new FFTS.Core.ValidationReport());
            Assert.AreEqual(1f, layout.SurfaceGripAt(50, 0), "Southside Blvd");
            Assert.AreEqual(1f, layout.SurfaceGripAt(0, -150), "Pecan St");
            Assert.AreEqual(1f, layout.SurfaceGripAt(-150, -120), "race lot");
            Assert.AreEqual(FFTS.World.DistrictLayout.OffRoadGrip, layout.SurfaceGripAt(40, -150), "open ground");
        }

        [Test]
        public void EveryRaceCheckpointSitsOnAsphalt()
        {
            var layout = FFTS.World.DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), new FFTS.Core.ValidationReport());
            var race = RacingTests.Races(VehicleDataTests.Db()).Get("race.southside.pecan_sprint");
            foreach (var c in race.Checkpoints) Assert.AreEqual(1f, layout.SurfaceGripAt(c.X, c.Z), $"checkpoint ({c.X},{c.Z})");
            foreach (var g in race.Grid) Assert.AreEqual(1f, layout.SurfaceGripAt(g.X, g.Z), $"grid ({g.X},{g.Z})");
        }
    }
}
