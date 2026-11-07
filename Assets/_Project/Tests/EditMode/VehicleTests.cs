using System.Linq;
using FFTS.Core;
using FFTS.Economy;
using FFTS.Vehicles;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class VehicleDataTests
    {
        public static VehicleDatabase Db()
        {
            var r = new ValidationReport();
            var db = VehicleDatabase.FromJson(new[] { TestData.Read("Vehicles/vehicles.json") }, r);
            Assert.IsTrue(r.IsValid, r.ToString());
            return db;
        }

        [Test]
        public void ShippedVehicleDataIsValid() => Db();

        [Test]
        public void ValidationCatchesDuplicatesBadStatsAndMissingPrefabs()
        {
            const string json = @"{ ""Vehicles"": [
                { ""Id"": ""vehicle.a"", ""DisplayName"": ""A"", ""MaxSpeedKph"": 900, ""BaseValueCents"": 100, ""PrefabKey"": ""p.a"" },
                { ""Id"": ""vehicle.a"", ""DisplayName"": ""A2"", ""BaseValueCents"": 100, ""PrefabKey"": ""p.a"" },
                { ""Id"": ""vehicle.b"", ""DisplayName"": ""B"", ""BaseValueCents"": 0 } ],
              ""Garages"": [ { ""Id"": ""garage.x"", ""ForSale"": [""vehicle.nope""] } ] }";
            var r = new ValidationReport();
            VehicleDatabase.FromJson(new[] { json }, r, new[] { "p.a" });
            StringAssert.Contains("duplicate id 'vehicle.a'", r.ToString());
            StringAssert.Contains("MaxSpeedKph=900", r.ToString());
            StringAssert.Contains("vehicle.b: missing prefab key", r.ToString());
            StringAssert.Contains("base value must be positive", r.ToString());
            StringAssert.Contains("unknown vehicle vehicle.nope", r.ToString());
        }

        [Test]
        public void UpgradesChangeEffectiveStats()
        {
            var db = Db();
            var inst = new VehicleInstance { DefinitionId = "vehicle.magnolia_cruiser" };
            var stock = db.StatsFor(inst);
            inst.Upgrades.Add("upgrade.engine_stage1"); inst.Upgrades.Add("upgrade.nitrous_stage1");
            var tuned = db.StatsFor(inst);
            Assert.Greater(tuned.Acceleration, stock.Acceleration);
            Assert.Greater(tuned.MaxSpeed, stock.MaxSpeed);
            Assert.AreEqual(4f, tuned.NitrousCapacitySeconds);
        }
    }

    public class VehicleDynamicsTests
    {
        private const float Dt = 1f / 60f;
        private static VehicleDynamics Car(string id = "vehicle.magnolia_cruiser", HandlingPreset p = HandlingPreset.Street, params string[] upgrades)
        {
            var db = VehicleDataTests.Db();
            var inst = new VehicleInstance { DefinitionId = id };
            inst.Upgrades.AddRange(upgrades);
            return new VehicleDynamics(db.StatsFor(inst), p);
        }

        private static void Run(VehicleDynamics d, DriveInput input, float seconds)
        {
            for (int i = 0; i < seconds * 60; i++) d.Step(input, Dt);
        }

        [Test]
        public void AcceleratesTowardButNotPastTopSpeed()
        {
            var d = Car();
            Run(d, new DriveInput { Throttle = 1 }, 5);
            float at5 = d.Speed;
            Run(d, new DriveInput { Throttle = 1 }, 60);
            Assert.Greater(at5, 15f, "should be quick off the line");
            Assert.LessOrEqual(d.Speed, d.Stats.MaxSpeed + 0.01f);
            Assert.Greater(d.Speed, d.Stats.MaxSpeed * 0.85f);
            Assert.Greater(d.Z, 500f, "heading 0 drives +Z");
        }

        [Test]
        public void BrakingStopsFasterThanCoasting()
        {
            var a = Car(); var b = Car();
            Run(a, new DriveInput { Throttle = 1 }, 6); Run(b, new DriveInput { Throttle = 1 }, 6);
            float za = a.Z, zb = b.Z;
            int brakeTicks = 0;
            while (a.Speed > 0.01f && brakeTicks < 3600) { a.Step(new DriveInput { Brake = 1 }, Dt); brakeTicks++; }
            Run(b, new DriveInput(), brakeTicks / 60f);
            Assert.AreEqual(0f, a.Speed, 0.01f);
            Assert.Greater(b.Speed, 5f);
            Assert.Less(a.Z - za, b.Z - zb);
        }

        [Test]
        public void BrakeHeldWhenStoppedReversesWithLimit()
        {
            var d = Car();
            Run(d, new DriveInput { Brake = 1 }, 10);
            Assert.Less(d.Speed, -1f);
            Assert.GreaterOrEqual(d.Speed, -VehicleDynamics.ReverseMaxSpeed);
            Assert.Less(d.Z, 0f);
        }

        [Test]
        public void SteeringNeedsSpeedAndTurnsTheCar()
        {
            var parked = Car();
            Run(parked, new DriveInput { Steer = 1 }, 2);
            Assert.AreEqual(0f, parked.HeadingDeg, 0.001f, "no turning on the spot");
            var d = Car();
            Run(d, new DriveInput { Throttle = 0.5f }, 3);
            Run(d, new DriveInput { Throttle = 0.5f, Steer = 1 }, 1);
            Assert.Greater(d.HeadingDeg, 20f, "steer right = clockwise toward +X");
        }

        [Test]
        public void HandbrakeCreatesMoreDriftThanSteeringAlone()
        {
            float MaxDrift(bool handbrake)
            {
                var d = Car();
                Run(d, new DriveInput { Throttle = 1 }, 4);
                float max = 0;
                for (int i = 0; i < 60; i++) { d.Step(new DriveInput { Throttle = 0.6f, Steer = 1, Handbrake = handbrake }, Dt); max = System.Math.Max(max, System.Math.Abs(d.DriftDeg)); }
                return max;
            }
            Assert.Greater(MaxDrift(true), MaxDrift(false) * 1.5f);
        }

        [Test]
        public void LowGripSurfaceSlidesMore()
        {
            float Drift(float surface)
            {
                var d = Car();
                Run(d, new DriveInput { Throttle = 1, SurfaceGrip = surface }, 4);
                for (int i = 0; i < 40; i++) d.Step(new DriveInput { Throttle = 0.6f, Steer = 1, SurfaceGrip = surface }, Dt);
                return System.Math.Abs(d.DriftDeg);
            }
            Assert.Greater(Drift(0.55f), Drift(1f));
        }

        [Test]
        public void NitrousExceedsNormalTopSpeedThenRunsOutAndBleedsDown()
        {
            var d = Car(upgrades: "upgrade.nitrous_stage1");
            Run(d, new DriveInput { Throttle = 1 }, 60);
            float cruise = d.Speed;
            Run(d, new DriveInput { Throttle = 1, Nitrous = true }, 3.5f);
            Assert.Greater(d.Speed, cruise + 1f);
            Run(d, new DriveInput { Throttle = 1, Nitrous = true }, 1);
            Assert.AreEqual(0f, d.NitrousRemaining, 0.001f);
            Run(d, new DriveInput { Throttle = 1, Nitrous = true }, 10);
            Assert.LessOrEqual(d.Speed, d.Stats.MaxSpeed + 0.01f);
        }

        [Test]
        public void NoNitrousWithoutTheKit()
        {
            var d = Car();
            Run(d, new DriveInput { Throttle = 1, Nitrous = true }, 5);
            Assert.IsFalse(d.NitrousActive);
        }

        [Test]
        public void ImpactsDamageAndHeavyDamageSlowsTheCar()
        {
            var d = Car();
            Assert.AreEqual(0f, d.ApplyImpact(1f), "taps are free");
            float dmg = d.ApplyImpact(20f);
            Assert.Greater(dmg, 10f);
            var healthy = Car(); var wrecked = Car(); wrecked.Damage = 95;
            Run(healthy, new DriveInput { Throttle = 1 }, 4); Run(wrecked, new DriveInput { Throttle = 1 }, 4);
            Assert.Less(wrecked.Speed, healthy.Speed * 0.7f);
            wrecked.Damage = 100;
            float before = wrecked.Speed;
            Run(wrecked, new DriveInput { Throttle = 1 }, 3);
            Assert.Less(wrecked.Speed, before, "a wrecked car has no drive");
        }

        [Test]
        public void ClassesFeelDifferent()
        {
            float TurnAfter(string id) { var d = Car(id); Run(d, new DriveInput { Throttle = 0.5f }, 3); Run(d, new DriveInput { Throttle = 0.5f, Steer = 1 }, 1.5f); return d.HeadingDeg; }
            Assert.Greater(TurnAfter("vehicle.kudzu_rs"), TurnAfter("vehicle.magnolia_cruiser") * 1.15f, "tuner turns in harder than the muscle car");
            var lux = Car("vehicle.crestmore_eight"); var muscle = Car();
            Run(lux, new DriveInput { Throttle = 1 }, 60); Run(muscle, new DriveInput { Throttle = 1 }, 60);
            Assert.Greater(lux.Speed, muscle.Speed, "luxury sedan has the higher top end");
        }

        [Test]
        public void PresetsChangeAssistsNotTopSpeed()
        {
            float Turn(HandlingPreset p) { var d = Car(p: p); Run(d, new DriveInput { Throttle = 0.5f }, 3); Run(d, new DriveInput { Throttle = 0.5f, Steer = 1 }, 1); return d.HeadingDeg; }
            Assert.Greater(Turn(HandlingPreset.Arcade), Turn(HandlingPreset.Expert));
            var a = Car(p: HandlingPreset.Arcade); var e = Car(p: HandlingPreset.Expert);
            Run(a, new DriveInput { Throttle = 1 }, 60); Run(e, new DriveInput { Throttle = 1 }, 60);
            Assert.AreEqual(a.Speed, e.Speed, 0.5f);
        }

        [Test]
        public void DrivingIsDeterministic()
        {
            string Drive()
            {
                var d = Car("vehicle.kudzu_rs");
                for (int i = 0; i < 1200; i++) d.Step(new DriveInput { Throttle = 1, Steer = (float)System.Math.Sin(i * 0.02), Handbrake = i % 200 < 20 }, Dt);
                return $"{d.X:R}|{d.Z:R}|{d.HeadingDeg:R}|{d.Speed:R}";
            }
            Assert.AreEqual(Drive(), Drive());
        }
    }

    public class GarageTests
    {
        private static (GarageManager g, FinessBank bank) Make(long cash)
        {
            var bus = new EventBus();
            var bank = FinessBank.CreateStarter(bus, () => 0, cash);
            return (new GarageManager(VehicleDataTests.Db(), bank, bus), bank);
        }

        [Test]
        public void BuyingChargesOnceAndParksAtHome()
        {
            var (g, bank) = Make(100000);
            Assert.AreEqual(VehicleResult.Ok, g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "tap1", out var car));
            Assert.AreEqual(VehicleResult.Duplicate, g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "tap1", out _));
            Assert.AreEqual(25000, bank.PersonalTotal());
            Assert.AreEqual(1, g.Vehicles.Count);
            Assert.AreEqual("garage.grandma_driveway", car.GarageId, "Southside Motors isn't the player's garage; car goes home");
            Assert.AreEqual(VehicleAvailability.Stored, car.Availability);
        }

        [Test]
        public void CannotBuyWithoutMoneyOrSpaceOrWhereNotSold()
        {
            var (poor, _) = Make(1000);
            Assert.AreEqual(VehicleResult.InsufficientFunds, poor.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out _));
            Assert.AreEqual(0, poor.Vehicles.Count);
            var (g, _) = Make(10_000_000);
            Assert.AreEqual(VehicleResult.NotForSaleHere, g.Buy("vehicle.crestmore_eight", "garage.southside_motors", "k0", out _));
            Assert.AreEqual(VehicleResult.Ok, g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k1", out _));
            Assert.AreEqual(VehicleResult.GarageFull, g.Buy("vehicle.kudzu_rs", "garage.southside_motors", "k2", out _), "starter driveway holds one");
            Assert.AreEqual(VehicleResult.Ok, g.BuyGarageSlot("garage.grandma_driveway"));
            Assert.AreEqual(VehicleResult.Ok, g.Buy("vehicle.kudzu_rs", "garage.southside_motors", "k3", out _));
            Assert.AreEqual(VehicleResult.AtMaxCapacity, g.BuyGarageSlot("garage.grandma_driveway"));
        }

        [Test]
        public void RetrieveDriveStoreRepairCycle()
        {
            var (g, bank) = Make(200000);
            g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out var car);
            Assert.AreEqual(VehicleResult.Ok, g.Retrieve(car.OwnershipId, out var from));
            Assert.AreEqual(VehicleAvailability.Out, car.Availability);
            Assert.AreEqual(car.OwnershipId, g.ActiveVehicle);
            Assert.AreEqual(from.SpawnX, car.WorldX);
            Assert.AreEqual(VehicleResult.WrongState, g.Retrieve(car.OwnershipId, out _));

            var dyn = g.CreateDynamics(car.OwnershipId);
            for (int i = 0; i < 600; i++) dyn.Step(new DriveInput { Throttle = 1 }, 1f / 60f);
            dyn.ApplyImpact(25f);
            g.RecordDrive(car.OwnershipId, dyn, dyn.DistanceMeters);
            Assert.Greater(car.MileageKm, 0.05f);
            Assert.Greater(car.Damage, 0f);

            Assert.AreEqual(VehicleResult.GarageNotOwned, g.Store(car.OwnershipId, "garage.southside_motors"));
            Assert.AreEqual(VehicleResult.Ok, g.Store(car.OwnershipId, "garage.grandma_driveway"));
            long before = bank.PersonalTotal();
            Assert.AreEqual(VehicleResult.Ok, g.Repair(car.OwnershipId, out var cost));
            Assert.AreEqual(before - cost, bank.PersonalTotal());
            Assert.AreEqual(0f, car.Damage);
            Assert.AreEqual(VehicleResult.NothingToRepair, g.Repair(car.OwnershipId, out _));
        }

        [Test]
        public void WreckedCarsMustBeRecoveredAndRepaired()
        {
            var (g, _) = Make(500000);
            g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out var car);
            g.Retrieve(car.OwnershipId, out _);
            var dyn = g.CreateDynamics(car.OwnershipId);
            dyn.Damage = 100;
            g.RecordDrive(car.OwnershipId, dyn, 0);
            Assert.AreEqual(VehicleAvailability.Wrecked, car.Availability);
            Assert.AreEqual(VehicleResult.WrongState, g.Retrieve(car.OwnershipId, out _));
            Assert.AreEqual(VehicleResult.Ok, g.Recover(car.OwnershipId, "tow1"));
            Assert.AreEqual(VehicleResult.Duplicate, g.Recover(car.OwnershipId, "tow1"));
            Assert.AreEqual(VehicleResult.Ok, g.Repair(car.OwnershipId, out _));
            Assert.AreEqual(VehicleAvailability.Stored, car.Availability);
            Assert.AreEqual(VehicleResult.Ok, g.Retrieve(car.OwnershipId, out _));
        }

        [Test]
        public void SellingPaysResaleOnceAndRemovesTheCar()
        {
            var (g, bank) = Make(100000);
            g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out var car);
            long resale = g.ResaleValue(car);
            Assert.Less(resale, 75000);
            Assert.AreEqual(VehicleResult.Ok, g.Sell(car.OwnershipId, out var price));
            Assert.AreEqual(resale, price);
            Assert.AreEqual(25000 + price, bank.PersonalTotal());
            Assert.AreEqual(VehicleResult.NotOwned, g.Sell(car.OwnershipId, out _));
        }

        [Test]
        public void UpgradesRespectSlotsAndPayOnce()
        {
            var (g, bank) = Make(1_000_000);
            g.Buy("vehicle.crestmore_eight", "garage.southside_motors", "k", out _); // not sold there
            g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k2", out var car);
            long before = bank.PersonalTotal();
            Assert.AreEqual(VehicleResult.Ok, g.InstallUpgrade(car.OwnershipId, "upgrade.nitrous_stage1"));
            Assert.AreEqual(VehicleResult.Duplicate, g.InstallUpgrade(car.OwnershipId, "upgrade.nitrous_stage1"));
            Assert.AreEqual(before - 60000, bank.PersonalTotal());
            Assert.Greater(g.CreateDynamics(car.OwnershipId).NitrousRemaining, 0f);
        }

        [Test]
        public void GarageStateRoundTripsThroughJson()
        {
            var (g, bank) = Make(1_000_000);
            g.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out var car);
            g.InstallUpgrade(car.OwnershipId, "upgrade.tires_street");
            g.Preset = HandlingPreset.Arcade;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(g.Capture());
            var g2 = new GarageManager(VehicleDataTests.Db(), bank, new EventBus());
            g2.Restore(Newtonsoft.Json.JsonConvert.DeserializeObject<GarageState>(json));
            Assert.AreEqual(1, g2.Vehicles.Count);
            Assert.AreEqual(HandlingPreset.Arcade, g2.Preset);
            CollectionAssert.AreEqual(new[] { "upgrade.tires_street" }, g2.Get(car.OwnershipId).Upgrades);
            Assert.AreEqual(VehicleResult.Duplicate, g2.Buy("vehicle.magnolia_cruiser", "garage.southside_motors", "k", out _),
                "after reload the bank still remembers the original purchase key");
        }
    }
}
