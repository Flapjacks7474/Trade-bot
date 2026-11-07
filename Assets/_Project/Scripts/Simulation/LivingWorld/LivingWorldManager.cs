using System;
using System.Collections.Generic;
using System.Numerics;
using FFTS.Core;
using FFTS.Relationships;
using FFTS.World;
using Newtonsoft.Json;

namespace FFTS.LivingWorld
{
    public enum NpcTier { Important, Background }
    public enum TravelMode { Walk, Drive }
    public enum NpcStatus { AtLocation, Traveling, Held }
    public enum SimulationLevel { Full, Simplified, Distant }

    /// <summary>One schedule rule. The highest-priority matching entry decides where the NPC wants to be.</summary>
    public sealed class ScheduleEntry
    {
        public int StartHour;           // inclusive
        public int EndHour = 24;        // exclusive; may be less than StartHour to wrap past midnight
        public List<int> Days = new List<int>(); // 0 = Monday … 6 = Sunday; empty = every day
        public string LocationId;
        public string Activity = "idle";  // work, sleep, train, socialize, shop, record, eat, club, home...
        public int Priority;
        // Story conditions (mission pins outrank routines so story beats are always reachable).
        public string MissionActive;
        public List<string> ObjectivesDone = new List<string>();     // "missionId/objectiveId"
        public List<string> ObjectivesNotDone = new List<string>();
        public List<string> RequireFlags = new List<string>();

        public bool CoversHour(int hour) => StartHour <= EndHour ? hour >= StartHour && hour < EndHour : hour >= StartHour || hour < EndHour;
        public bool CoversDay(int day) => Days.Count == 0 || Days.Contains(day % 7);
        [JsonIgnore] public bool IsStoryPin => MissionActive != null || ObjectivesDone.Count > 0 || ObjectivesNotDone.Count > 0 || RequireFlags.Count > 0;
    }

    public sealed class NpcProfile
    {
        public string NpcId;
        public string DisplayName;
        public string Color = "#c0c0c0";
        public NpcTier Tier = NpcTier.Important;
        public TravelMode Travel = TravelMode.Walk;
        public string HomeLocation;
        public Personality Personality = new Personality();
        public List<ScheduleEntry> Schedule = new List<ScheduleEntry>();
    }

    public sealed class NpcProfileFile { public List<NpcProfile> Npcs = new List<NpcProfile>(); }

    public sealed class NpcRuntime
    {
        public string NpcId;
        public float X, Z;
        public string LocationId;        // null while travelling
        public string TargetLocationId;
        public string Activity;
        public NpcStatus Status;
        public List<Vector2> Path = new List<Vector2>();
        public int PathIndex;
        public float HeadingDeg;
        [JsonIgnore] public float Accumulated; // seconds waiting to be simulated at lower levels
        [JsonIgnore] public SimulationLevel Level;
        public bool Held;
    }

    public sealed class NpcArrived : IGameEvent
    {
        public readonly string NpcId, LocationId, Activity;
        public NpcArrived(string npc, string loc, string act) { NpcId = npc; LocationId = loc; Activity = act; }
    }

    public sealed class NpcDeparted : IGameEvent
    {
        public readonly string NpcId, FromLocationId, ToLocationId;
        public NpcDeparted(string npc, string from, string to) { NpcId = npc; FromLocationId = from; ToLocationId = to; }
    }

    public sealed class LivingWorldState { public List<NpcRuntime> Npcs = new List<NpcRuntime>(); }

    /// <summary>
    /// Runs NPC daily routines. Each NPC picks a destination from its schedule (story pins first), then travels there
    /// along the road graph at walking or driving speed. Distance from the player only changes how often an NPC
    /// is updated (every frame / once a second / every five seconds), never whether it moves continuously, so
    /// nobody teleports, even off-screen.
    /// </summary>
    public sealed class LivingWorldManager
    {
        public const float WalkSpeed = 1.6f, DriveSpeed = 11f;
        public const float FullRadius = 60f, SimplifiedRadius = 250f;
        public const float SimplifiedInterval = 1f, DistantInterval = 5f;

        private readonly DistrictLayout _layout;
        private readonly RoadGraph _graph;
        private readonly EventBus _bus;
        private readonly Dictionary<string, NpcProfile> _profiles = new Dictionary<string, NpcProfile>();
        private readonly Dictionary<string, NpcRuntime> _npcs = new Dictionary<string, NpcRuntime>();
        private readonly List<string> _order = new List<string>();

        /// <summary>Evaluates story conditions; injected by the session (missions/story live elsewhere).</summary>
        public Func<ScheduleEntry, bool> StoryConditionsMet = _ => false; // no story context = routines only

        public RoadGraph Graph => _graph;
        public IEnumerable<NpcProfile> Profiles => _profiles.Values;
        public NpcProfile Profile(string npcId) => _profiles.TryGetValue(npcId, out var p) ? p : null;
        public NpcRuntime Get(string npcId) => _npcs.TryGetValue(npcId, out var n) ? n : null;
        public IEnumerable<NpcRuntime> All { get { foreach (var id in _order) yield return _npcs[id]; } }

        public LivingWorldManager(IEnumerable<NpcProfile> profiles, DistrictLayout layout, EventBus bus)
        {
            _layout = layout; _bus = bus;
            _graph = RoadGraph.Build(layout);
            foreach (var p in profiles)
            {
                _profiles[p.NpcId] = p; _order.Add(p.NpcId);
                _npcs[p.NpcId] = new NpcRuntime { NpcId = p.NpcId };
            }
        }

        public static List<NpcProfile> Parse(string json, DistrictLayout layout, ValidationReport report)
        {
            var file = JsonConvert.DeserializeObject<NpcProfileFile>(json);
            var ids = new IdRegistry();
            foreach (var p in file.Npcs)
            {
                ids.Register(p.NpcId, "npc profile");
                if (p.Schedule.Count == 0) report.Error($"NPC {p.NpcId}: empty schedule.");
                if (p.HomeLocation == null || layout.Find(p.HomeLocation) == null) report.Error($"NPC {p.NpcId}: unknown home {p.HomeLocation}.");
                // Every hour of every day must resolve to somewhere through routine (non-story) entries.
                for (int day = 0; day < 7; day++)
                    for (int h = 0; h < 24; h++)
                        if (!p.Schedule.Exists(e => !e.IsStoryPin && e.CoversDay(day) && e.CoversHour(h)))
                        { report.Error($"NPC {p.NpcId}: no routine entry for day {day} hour {h}."); goto nextNpc; }
                nextNpc:
                foreach (var e in p.Schedule)
                {
                    if (layout.Find(e.LocationId) == null) report.Error($"NPC {p.NpcId}: schedule uses unknown location {e.LocationId}.");
                    if (e.StartHour < 0 || e.StartHour > 23 || e.EndHour < 0 || e.EndHour > 24) report.Error($"NPC {p.NpcId}: bad hours {e.StartHour}-{e.EndHour}.");
                    foreach (var d in e.Days) if (d < 0 || d > 6) report.Error($"NPC {p.NpcId}: bad day {d}.");
                }
            }
            report.AddRange(ids.Errors);
            return file.Npcs;
        }

        // ------------------------------------------------------------------ schedule

        public ScheduleEntry DesiredEntry(string npcId, int day, int hour)
        {
            var p = Profile(npcId);
            if (p == null) return null;
            ScheduleEntry best = null;
            foreach (var e in p.Schedule)
            {
                if (!e.CoversDay(day) || !e.CoversHour(hour)) continue;
                if (e.IsStoryPin && !StoryConditionsMet(e)) continue;
                int pri = e.Priority + (e.IsStoryPin ? 1000 : 0);
                int bestPri = best == null ? int.MinValue : best.Priority + (best.IsStoryPin ? 1000 : 0);
                if (pri > bestPri) best = e;
            }
            return best;
        }

        /// <summary>Where an NPC stands at a location: in front of it on the road side, spread so NPCs don't overlap.</summary>
        public Vector2 SpotAt(string locationId, string npcId)
        {
            var l = _layout.Find(locationId);
            if (l == null) return Vector2.Zero;
            var center = new Vector2(l.X, l.Z);
            var road = _graph.EdgeCount > 0 ? _graph.NearestRoadPoint(center, out _) : center + new Vector2(0, -1);
            var dir = road - center;
            if (dir.LengthSquared() < 0.01f) dir = new Vector2(0, -1);
            dir = Vector2.Normalize(dir);
            float half = Math.Abs(dir.X) * l.Width / 2f + Math.Abs(dir.Y) * l.Depth / 2f;
            var door = center + dir * (half + 2f);
            var side = new Vector2(-dir.Y, dir.X);
            int slot = (int)(StableHash(npcId) % 5) - 2;
            return door + side * slot * 1.4f;
        }

        private static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        /// <summary>Places everyone where their schedule says (new game only, nothing visible yet).</summary>
        public void InitializeAt(int day, int hour)
        {
            foreach (var id in _order)
            {
                var e = DesiredEntry(id, day, hour);
                var n = _npcs[id];
                var loc = e?.LocationId ?? Profile(id).HomeLocation;
                var spot = SpotAt(loc, id);
                n.X = spot.X; n.Z = spot.Y; n.LocationId = loc; n.TargetLocationId = loc;
                n.Activity = e?.Activity ?? "home"; n.Status = NpcStatus.AtLocation; n.Path.Clear();
            }
        }

        /// <summary>Re-evaluates schedules (call when the game hour changes or story state changes).</summary>
        public void UpdateSchedules(int day, int hour)
        {
            foreach (var id in _order)
            {
                var n = _npcs[id];
                var e = DesiredEntry(id, day, hour);
                if (e == null) continue;
                if (e.LocationId == n.TargetLocationId) { if (n.Status == NpcStatus.AtLocation) n.Activity = e.Activity; continue; }
                var from = n.LocationId;
                n.TargetLocationId = e.LocationId;
                n.Activity = e.Activity;
                n.Path = _graph.FindPath(new Vector2(n.X, n.Z), SpotAt(e.LocationId, id));
                n.PathIndex = 1;
                n.LocationId = null;
                if (!n.Held) n.Status = NpcStatus.Traveling;
                _bus?.Publish(new NpcDeparted(id, from, e.LocationId));
            }
        }

        /// <summary>Freeze an NPC (e.g. while the player talks to them).</summary>
        public void Hold(string npcId, bool hold)
        {
            var n = Get(npcId);
            if (n == null) return;
            n.Held = hold;
            if (hold) n.Status = NpcStatus.Held;
            else n.Status = n.LocationId != null ? NpcStatus.AtLocation : NpcStatus.Traveling;
        }

        public SimulationLevel LevelFor(NpcRuntime n, float playerX, float playerZ)
        {
            float d = Vector2.Distance(new Vector2(n.X, n.Z), new Vector2(playerX, playerZ));
            return d <= FullRadius ? SimulationLevel.Full : d <= SimplifiedRadius ? SimulationLevel.Simplified : SimulationLevel.Distant;
        }

        /// <summary>Advances travel by real seconds. Lower simulation levels batch time but cover the same distance.</summary>
        public void Step(float seconds, float playerX, float playerZ)
        {
            foreach (var id in _order)
            {
                var n = _npcs[id];
                n.Level = LevelFor(n, playerX, playerZ);
                if (n.Status != NpcStatus.Traveling) { n.Accumulated = 0; continue; }
                n.Accumulated += seconds;
                float interval = n.Level == SimulationLevel.Full ? 0f : n.Level == SimulationLevel.Simplified ? SimplifiedInterval : DistantInterval;
                if (n.Accumulated < interval) continue;
                Advance(n, n.Accumulated);
                n.Accumulated = 0;
            }
        }

        public float SpeedOf(string npcId) => Profile(npcId)?.Travel == TravelMode.Drive ? DriveSpeed : WalkSpeed;

        private void Advance(NpcRuntime n, float seconds)
        {
            float budget = SpeedOf(n.NpcId) * seconds;
            while (budget > 0 && n.PathIndex < n.Path.Count)
            {
                var target = n.Path[n.PathIndex];
                var pos = new Vector2(n.X, n.Z);
                var to = target - pos;
                float d = to.Length();
                if (d > 0.001f) n.HeadingDeg = (float)(Math.Atan2(to.X, to.Y) * 180.0 / Math.PI);
                if (d <= budget) { n.X = target.X; n.Z = target.Y; budget -= d; n.PathIndex++; }
                else { var step = to / d * budget; n.X += step.X; n.Z += step.Y; budget = 0; }
            }
            if (n.PathIndex >= n.Path.Count)
            {
                n.Status = NpcStatus.AtLocation;
                n.LocationId = n.TargetLocationId;
                n.Path.Clear();
                _bus?.Publish(new NpcArrived(n.NpcId, n.LocationId, n.Activity));
            }
        }

        public List<string> NpcsNear(float x, float z, float radius)
        {
            var list = new List<string>();
            foreach (var id in _order)
            {
                var n = _npcs[id];
                if (Vector2.Distance(new Vector2(n.X, n.Z), new Vector2(x, z)) <= radius) list.Add(id);
            }
            return list;
        }

        public bool IsAt(string npcId, string locationId) { var n = Get(npcId); return n != null && n.Status != NpcStatus.Traveling && n.LocationId == locationId; }

        public LivingWorldState Capture()
        {
            var s = new LivingWorldState();
            foreach (var id in _order) s.Npcs.Add(JsonConvert.DeserializeObject<NpcRuntime>(JsonConvert.SerializeObject(_npcs[id])));
            return s;
        }

        public void Restore(LivingWorldState s)
        {
            if (s == null) return;
            foreach (var saved in s.Npcs)
            {
                if (!_npcs.ContainsKey(saved.NpcId)) continue; // NPC removed from content
                saved.Held = false;
                if (saved.Status == NpcStatus.Held) saved.Status = saved.LocationId != null ? NpcStatus.AtLocation : NpcStatus.Traveling;
                _npcs[saved.NpcId] = saved;
            }
        }
    }
}
