using System;
using System.Collections.Generic;
using FFTS.Core;
using Newtonsoft.Json;

namespace FFTS.Vehicles
{
    public enum VehicleClass { Muscle, Tuner, LuxurySedan, SuvTruck, Exotic, Classic, Lowrider, Motorcycle, Commercial }
    public enum Drivetrain { FWD, RWD, AWD }
    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }
    public enum HandlingPreset { Arcade, Street, Expert }
    public enum UpgradeSlot { Engine, Transmission, Brakes, Suspension, Tires, Nitrous, Exhaust }

    /// <summary>Immutable vehicle content. Stats are real-world-ish units so tuning stays readable.</summary>
    public sealed class VehicleDefinition
    {
        public string Id;
        public string DisplayName;
        public string Make;              // fictional brand only
        public VehicleClass Class;
        public Drivetrain Drivetrain = Drivetrain.RWD;
        public float MassKg = 1500;
        public float EnginePowerKw = 150;
        public float MaxSpeedKph = 180;
        public float Acceleration = 6f;      // m/s² peak at low speed
        public float Braking = 10f;          // m/s²
        public float SteeringResponse = 70f; // degrees/s yaw at full lock, moderate speed
        public float Grip = 1f;              // 0.5 - 1.6
        public float Traction = 1f;          // launch traction multiplier
        public float Durability = 50;        // 1-100
        public int PassengerCapacity = 2;
        public long BaseValueCents;
        public Rarity Rarity;
        public string PrefabKey;
        public string EngineSound;
        public List<UpgradeSlot> UpgradeSlots = new List<UpgradeSlot>();
    }

    public sealed class UpgradeDefinition
    {
        public string Id;
        public string DisplayName;
        public UpgradeSlot Slot;
        public long PriceCents;
        public float AccelerationBonus, MaxSpeedBonusKph, BrakingBonus, GripBonus, SteeringBonus, NitrousCapacity;
    }

    public sealed class Customization
    {
        public string PaintColor = "#7a1f1f";
        public string Wrap;
        public List<string> Decals = new List<string>();
        public string Wheels = "wheels.stock";
        public float RideHeight;            // -1..1
        public string Plate = "SOUTH";
        public string CrewBranding;
    }

    public enum VehicleAvailability { Stored, Out, Wrecked, Impounded }

    /// <summary>A vehicle the player owns. Saved data; never edits the definition.</summary>
    public sealed class VehicleInstance
    {
        public string OwnershipId;
        public string DefinitionId;
        public float Damage;                 // 0-100; 100 = wrecked
        public float MileageKm;
        public List<string> Upgrades = new List<string>();
        public Customization Customization = new Customization();
        public string GarageId;              // where it lives / returns to
        public bool Registered = true;
        public VehicleAvailability Availability = VehicleAvailability.Stored;
        public float WorldX, WorldZ, HeadingDeg; // last known position while Out
        public int RepairCount;
    }

    public sealed class GarageDefinition
    {
        public string Id;
        public string DisplayName;
        public string LocationId;
        public int BaseCapacity = 2;
        public bool StarterOwned;            // usable from the start (e.g. Southside Motors lot space)
        public long SlotPriceCents;          // price of one extra slot
        public int MaxCapacity = 6;
        public float SpawnX, SpawnZ, SpawnHeadingDeg;
        public List<string> ForSale = new List<string>(); // vehicle definition ids sold here
    }

    public sealed class VehicleFile
    {
        public List<VehicleDefinition> Vehicles = new List<VehicleDefinition>();
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
        public List<GarageDefinition> Garages = new List<GarageDefinition>();
    }

    public sealed class VehicleDatabase
    {
        private readonly Dictionary<string, VehicleDefinition> _vehicles = new Dictionary<string, VehicleDefinition>();
        private readonly Dictionary<string, UpgradeDefinition> _upgrades = new Dictionary<string, UpgradeDefinition>();
        private readonly Dictionary<string, GarageDefinition> _garages = new Dictionary<string, GarageDefinition>();

        public IEnumerable<VehicleDefinition> Vehicles => _vehicles.Values;
        public IEnumerable<GarageDefinition> Garages => _garages.Values;
        public IEnumerable<UpgradeDefinition> Upgrades => _upgrades.Values;
        public VehicleDefinition Get(string id) => id != null && _vehicles.TryGetValue(id, out var v) ? v : null;
        public UpgradeDefinition GetUpgrade(string id) => id != null && _upgrades.TryGetValue(id, out var u) ? u : null;
        public GarageDefinition GetGarage(string id) => id != null && _garages.TryGetValue(id, out var g) ? g : null;

        /// <summary>Parses and validates; <paramref name="knownPrefabs"/> (optional) lists prefab keys that exist.</summary>
        public static VehicleDatabase FromJson(IEnumerable<string> json, ValidationReport report, ICollection<string> knownPrefabs = null)
        {
            var db = new VehicleDatabase();
            var ids = new IdRegistry();
            foreach (var text in json)
            {
                var f = JsonConvert.DeserializeObject<VehicleFile>(text);
                foreach (var v in f.Vehicles) if (ids.Register(v.Id, "vehicle")) db._vehicles[v.Id] = v;
                foreach (var u in f.Upgrades) if (ids.Register(u.Id, "upgrade")) db._upgrades[u.Id] = u;
                foreach (var g in f.Garages) if (ids.Register(g.Id, "garage")) db._garages[g.Id] = g;
            }
            report.AddRange(ids.Errors);
            foreach (var v in db._vehicles.Values)
            {
                void Range(string name, float value, float min, float max)
                { if (value < min || value > max || float.IsNaN(value)) report.Error($"Vehicle {v.Id}: {name}={value} outside {min}-{max}."); }
                if (string.IsNullOrEmpty(v.DisplayName)) report.Error($"Vehicle {v.Id}: missing display name.");
                Range("MassKg", v.MassKg, 150, 12000);
                Range("EnginePowerKw", v.EnginePowerKw, 5, 1200);
                Range("MaxSpeedKph", v.MaxSpeedKph, 40, 420);
                Range("Acceleration", v.Acceleration, 0.5f, 15);
                Range("Braking", v.Braking, 2, 20);
                Range("SteeringResponse", v.SteeringResponse, 10, 200);
                Range("Grip", v.Grip, 0.4f, 1.8f);
                Range("Traction", v.Traction, 0.3f, 1.8f);
                Range("Durability", v.Durability, 1, 100);
                if (v.PassengerCapacity < 1 || v.PassengerCapacity > 12) report.Error($"Vehicle {v.Id}: passenger capacity {v.PassengerCapacity}.");
                if (v.BaseValueCents <= 0) report.Error($"Vehicle {v.Id}: base value must be positive.");
                if (string.IsNullOrEmpty(v.PrefabKey)) report.Error($"Vehicle {v.Id}: missing prefab key.");
                else if (knownPrefabs != null && !knownPrefabs.Contains(v.PrefabKey)) report.Error($"Vehicle {v.Id}: prefab '{v.PrefabKey}' not found.");
            }
            foreach (var u in db._upgrades.Values)
                if (u.PriceCents <= 0) report.Error($"Upgrade {u.Id}: price must be positive.");
            foreach (var g in db._garages.Values)
            {
                if (g.BaseCapacity < 1 || g.BaseCapacity > g.MaxCapacity) report.Error($"Garage {g.Id}: capacity {g.BaseCapacity}/{g.MaxCapacity} invalid.");
                foreach (var s in g.ForSale) if (!db._vehicles.ContainsKey(s)) report.Error($"Garage {g.Id}: sells unknown vehicle {s}.");
            }
            return db;
        }

        /// <summary>Effective stats: definition + installed upgrades.</summary>
        public VehicleStats StatsFor(VehicleInstance inst)
        {
            var d = Get(inst.DefinitionId) ?? throw new ArgumentException("Unknown vehicle " + inst.DefinitionId);
            var s = VehicleStats.From(d);
            foreach (var id in inst.Upgrades)
            {
                var u = GetUpgrade(id);
                if (u == null) continue;
                s.Acceleration += u.AccelerationBonus; s.MaxSpeed += u.MaxSpeedBonusKph / 3.6f; s.Braking += u.BrakingBonus;
                s.Grip += u.GripBonus; s.SteeringDegPerSec += u.SteeringBonus; s.NitrousCapacitySeconds += u.NitrousCapacity;
            }
            return s;
        }
    }

    /// <summary>Resolved numbers the driving model consumes (SI units).</summary>
    public struct VehicleStats
    {
        public float MaxSpeed;               // m/s
        public float Acceleration;           // m/s²
        public float Braking;                // m/s²
        public float SteeringDegPerSec;
        public float Grip;
        public float Traction;
        public float Durability;
        public float MassKg;
        public float NitrousCapacitySeconds;

        public static VehicleStats From(VehicleDefinition d) => new VehicleStats
        {
            MaxSpeed = d.MaxSpeedKph / 3.6f, Acceleration = d.Acceleration, Braking = d.Braking, SteeringDegPerSec = d.SteeringResponse,
            Grip = d.Grip, Traction = d.Traction, Durability = d.Durability, MassKg = d.MassKg, NitrousCapacitySeconds = 0
        };
    }
}
