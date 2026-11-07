using System.Collections.Generic;
using FFTS.Core;
using FFTS.Vehicles;
using Newtonsoft.Json;

namespace FFTS.Racing
{
    public enum RaceType { Sprint, Circuit, TimeTrial, Crew, Rival, Championship, Invitation, Special }
    public enum RacingRank { UnknownDriver, NeighborhoodRunner, CityContender, StreetCircuitElite, SouthernRoadLegend }

    public sealed class Checkpoint { public float X, Z, Radius = 10f; }
    public sealed class GridSlot { public float X, Z, HeadingDeg; }

    public sealed class RaceOpponent
    {
        public string RacerId;
        public string DisplayName;
        public string NpcId;          // relationship target; may be null for anonymous racers
        public string VehicleId;
        public float Skill = 0.5f;    // 0..1: line accuracy, braking points, mistake rate
    }

    public sealed class RaceDefinition
    {
        public string Id;
        public string DisplayName;
        public RaceType Type = RaceType.Sprint;
        public string LocationId;     // where the start marker is
        public int Laps = 1;
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public List<GridSlot> Grid = new List<GridSlot>();
        public List<RaceOpponent> Opponents = new List<RaceOpponent>();
        public long EntryFeeCents;
        public List<long> PurseCents = new List<long>();      // by finishing place (1st, 2nd, ...)
        public long FirstWinBonusCents;
        public List<int> RankPoints = new List<int>();         // by finishing place
        public RacingRank MinRank;
        public float TimeLimitSeconds = 180f;
        public float CountdownSeconds = 3f;
    }

    public sealed class RaceFile { public List<RaceDefinition> Races = new List<RaceDefinition>(); }

    public sealed class RaceDatabase
    {
        private readonly Dictionary<string, RaceDefinition> _races = new Dictionary<string, RaceDefinition>();
        public IEnumerable<RaceDefinition> Races => _races.Values;
        public RaceDefinition Get(string id) => id != null && _races.TryGetValue(id, out var r) ? r : null;

        public static RaceDatabase FromJson(IEnumerable<string> json, VehicleDatabase vehicles, ValidationReport report)
        {
            var db = new RaceDatabase();
            var ids = new IdRegistry();
            foreach (var text in json)
                foreach (var r in JsonConvert.DeserializeObject<RaceFile>(text).Races)
                    if (ids.Register(r.Id, "race")) db._races[r.Id] = r;
            report.AddRange(ids.Errors);
            foreach (var r in db._races.Values)
            {
                if (string.IsNullOrEmpty(r.DisplayName)) report.Error($"Race {r.Id}: missing name.");
                if (r.Checkpoints.Count < 2) report.Error($"Race {r.Id}: needs at least 2 checkpoints.");
                if (r.Laps < 1 || (r.Type == RaceType.Sprint && r.Laps != 1)) report.Error($"Race {r.Id}: invalid lap count {r.Laps}.");
                if (r.Grid.Count < r.Opponents.Count + 1) report.Error($"Race {r.Id}: grid has {r.Grid.Count} slots for {r.Opponents.Count + 1} racers.");
                if (r.TimeLimitSeconds <= 0) report.Error($"Race {r.Id}: time limit must be positive.");
                foreach (var c in r.Checkpoints) if (c.Radius < 3 || c.Radius > 40) report.Error($"Race {r.Id}: checkpoint radius {c.Radius}.");
                var racerIds = new HashSet<string>();
                foreach (var o in r.Opponents)
                {
                    if (!racerIds.Add(o.RacerId)) report.Error($"Race {r.Id}: duplicate racer {o.RacerId}.");
                    if (vehicles?.Get(o.VehicleId) == null) report.Error($"Race {r.Id}: opponent {o.RacerId} has unknown vehicle {o.VehicleId}.");
                    if (o.Skill < 0 || o.Skill > 1) report.Error($"Race {r.Id}: opponent {o.RacerId} skill {o.Skill} outside 0-1.");
                }
                for (int i = 1; i < r.PurseCents.Count; i++)
                    if (r.PurseCents[i] > r.PurseCents[i - 1]) report.Error($"Race {r.Id}: purse for place {i + 1} exceeds place {i}.");
            }
            return db;
        }
    }
}
