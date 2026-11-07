using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Vehicles;

namespace FFTS.Racing
{
    public enum RaceEventType { Countdown, Go, CheckpointPassed, LapCompleted, Finished, TimeExpired }

    public struct RaceEvent
    {
        public RaceEventType Type;
        public string RacerId;
        public int Value;      // checkpoint index, lap, place or countdown second
        public float Time;
    }

    public sealed class Racer
    {
        public string RacerId;
        public string DisplayName;
        public string NpcId;
        public bool IsPlayer;
        public VehicleDynamics Car;
        public int NextCheckpoint;
        public int Lap;              // laps completed
        public bool Finished;
        public float FinishTime;
        public int Place;            // 1-based once finished/ranked
        public float TopSpeedSeen;
        public int Impacts;
        public RaceDriverAI Driver;
    }

    /// <summary>
    /// One running race. Fixed-step and engine-independent: the Unity layer feeds the player's input and reports
    /// collisions; the session moves AI cars with the same <see cref="VehicleDynamics"/> the player uses.
    /// Checkpoints only count in order, so cutting across the map does nothing.
    /// </summary>
    public sealed class RaceSession
    {
        public const string PlayerRacerId = "racer.player";
        public readonly RaceDefinition Definition;
        public readonly List<Racer> Racers = new List<Racer>();
        public Racer Player { get; }
        public float Elapsed { get; private set; }       // seconds since GO
        public float CountdownRemaining { get; private set; }
        public bool IsOver { get; private set; }
        public bool PlayerDnf { get; private set; }
        public int Attempt { get; internal set; }

        private readonly List<RaceEvent> _events = new List<RaceEvent>();
        public IReadOnlyList<RaceEvent> Events => _events;
        private int _lastCountdownSecond = -1;
        private int _finishedCount;

        public RaceSession(RaceDefinition def, VehicleDynamics playerCar, VehicleDatabase vehicles, ulong seed)
        {
            Definition = def;
            CountdownRemaining = def.CountdownSeconds;
            Player = AddRacer(PlayerRacerId, "You", null, true, playerCar, 0);
            var rng = new DeterministicRandom(seed);
            for (int i = 0; i < def.Opponents.Count; i++)
            {
                var o = def.Opponents[i];
                var car = new VehicleDynamics(VehicleStats.From(vehicles.Get(o.VehicleId)), HandlingPreset.Street);
                var r = AddRacer(o.RacerId, o.DisplayName, o.NpcId, false, car, i + 1);
                r.Driver = new RaceDriverAI(o.Skill, new DeterministicRandom(rng.State ^ (ulong)(i + 1) * 0x9E3779B97F4A7C15UL));
            }
        }

        private Racer AddRacer(string id, string name, string npc, bool player, VehicleDynamics car, int slot)
        {
            var g = Definition.Grid[slot];
            car.X = g.X; car.Z = g.Z; car.HeadingDeg = g.HeadingDeg; car.Speed = 0; car.DriftDeg = 0;
            var r = new Racer { RacerId = id, DisplayName = name, NpcId = npc, IsPlayer = player, Car = car };
            Racers.Add(r);
            return r;
        }

        public int CheckpointCount => Definition.Checkpoints.Count;
        public Checkpoint CheckpointFor(Racer r) => Definition.Checkpoints[Math.Min(r.NextCheckpoint, CheckpointCount - 1)];

        public void Step(DriveInput playerInput, float dt)
        {
            _events.Clear();
            if (IsOver) return;

            if (CountdownRemaining > 0)
            {
                CountdownRemaining -= dt;
                int sec = (int)Math.Ceiling(Math.Max(0, CountdownRemaining));
                if (sec != _lastCountdownSecond) { _lastCountdownSecond = sec; _events.Add(new RaceEvent { Type = sec > 0 ? RaceEventType.Countdown : RaceEventType.Go, Value = sec }); }
                foreach (var r in Racers) { r.Car.Speed = 0; }
                return;
            }

            Elapsed += dt;
            foreach (var r in Racers)
            {
                if (r.Finished) { r.Car.Step(new DriveInput { Brake = 1 }, dt); continue; } // roll to a stop past the line
                var input = r.IsPlayer ? playerInput : r.Driver.Think(this, r);
                r.Car.Step(input, dt);
                r.TopSpeedSeen = Math.Max(r.TopSpeedSeen, r.Car.Speed);
                CheckProgress(r);
            }

            if (Player.Finished) { Finish(); return; }
            if (Elapsed >= Definition.TimeLimitSeconds)
            {
                PlayerDnf = true;
                _events.Add(new RaceEvent { Type = RaceEventType.TimeExpired, RacerId = PlayerRacerId, Time = Elapsed });
                Finish();
            }
        }

        private void CheckProgress(Racer r)
        {
            var c = Definition.Checkpoints[r.NextCheckpoint];
            float dx = r.Car.X - c.X, dz = r.Car.Z - c.Z;
            if (dx * dx + dz * dz > c.Radius * c.Radius) return;
            _events.Add(new RaceEvent { Type = RaceEventType.CheckpointPassed, RacerId = r.RacerId, Value = r.NextCheckpoint, Time = Elapsed });
            r.NextCheckpoint++;
            if (r.NextCheckpoint < CheckpointCount) return;
            r.Lap++;
            _events.Add(new RaceEvent { Type = RaceEventType.LapCompleted, RacerId = r.RacerId, Value = r.Lap, Time = Elapsed });
            if (r.Lap >= Definition.Laps)
            {
                r.Finished = true; r.FinishTime = Elapsed; r.Place = ++_finishedCount;
                _events.Add(new RaceEvent { Type = RaceEventType.Finished, RacerId = r.RacerId, Value = r.Place, Time = Elapsed });
            }
            else r.NextCheckpoint = 0;
        }

        /// <summary>Progress for ordering unfinished racers: laps, checkpoints, then closeness to the next checkpoint.</summary>
        public double Progress(Racer r)
        {
            if (r.Finished) return 1e9 - r.FinishTime;
            var c = Definition.Checkpoints[r.NextCheckpoint];
            double dist = Math.Sqrt((r.Car.X - c.X) * (r.Car.X - c.X) + (r.Car.Z - c.Z) * (r.Car.Z - c.Z));
            return (r.Lap * CheckpointCount + r.NextCheckpoint) * 10000.0 - dist;
        }

        public List<Racer> Standings()
        {
            var list = new List<Racer>(Racers);
            list.Sort((a, b) => Progress(b).CompareTo(Progress(a)));
            return list;
        }

        public int PositionOf(Racer r) => Standings().IndexOf(r) + 1;

        private void Finish()
        {
            IsOver = true;
            // Unfinished racers are ranked behind finishers by progress (a DNF player ranks last).
            int place = _finishedCount;
            foreach (var r in Standings())
                if (!r.Finished && !(r.IsPlayer && PlayerDnf)) r.Place = ++place;
            if (PlayerDnf) Player.Place = Racers.Count;
        }

        /// <summary>Physics layer reports a crash for a racer.</summary>
        public void ReportImpact(string racerId, float closingSpeed)
        {
            var r = Racers.Find(x => x.RacerId == racerId);
            if (r == null) return;
            if (r.Car.ApplyImpact(closingSpeed) > 0) r.Impacts++;
        }
    }

    /// <summary>
    /// Honest racing AI: aims along a racing line through upcoming checkpoints, brakes for corners based on the
    /// same braking stat the car really has, and makes skill-dependent mistakes. It never gets speed or grip the
    /// car doesn't have.
    /// </summary>
    public sealed class RaceDriverAI
    {
        public readonly float Skill;
        private readonly IRandom _rng;
        private int _mistakeForCheckpoint = -1;
        private bool _mistake;

        public RaceDriverAI(float skill, IRandom rng) { Skill = MathUtil.Clamp01(skill); _rng = rng; }

        public DriveInput Think(RaceSession s, Racer me)
        {
            var car = me.Car;
            var cps = s.Definition.Checkpoints;
            int i = me.NextCheckpoint;
            var c = cps[i];
            var next = i + 1 < cps.Count ? cps[i + 1] : (s.Definition.Laps > 1 ? cps[0] : null);

            float distToC = Dist(car.X, car.Z, c.X, c.Z);
            // Racing line: start turning in toward the next checkpoint when close, scaled by skill.
            float blend = next == null ? 0 : MathUtil.Clamp01(1f - distToC / (18f + 12f * Skill)) * (0.3f + 0.4f * Skill);
            float tx = c.X, tz = c.Z;
            if (next != null) { tx += (next.X - c.X) * blend * 0.5f; tz += (next.Z - c.Z) * blend * 0.5f; }

            float desired = (float)(Math.Atan2(tx - car.X, tz - car.Z) * 180.0 / Math.PI);
            float err = VehicleDynamics.Normalize(desired - car.HeadingDeg);
            float steer = MathUtil.Clamp(err / (20f + 10f * (1f - Skill)), -1f, 1f);

            // Traffic: ease to one side if a racer is right ahead.
            foreach (var o in s.Racers)
            {
                if (o == me) continue;
                float ox = o.Car.X - car.X, oz = o.Car.Z - car.Z;
                float d = (float)Math.Sqrt(ox * ox + oz * oz);
                if (d > 9f || d < 0.01f) continue;
                float bearing = VehicleDynamics.Normalize((float)(Math.Atan2(ox, oz) * 180.0 / Math.PI) - car.HeadingDeg);
                if (Math.Abs(bearing) < 25f) steer = MathUtil.Clamp(steer + (bearing >= 0 ? -0.35f : 0.35f), -1f, 1f);
            }

            // Corner speed: how sharply does the course turn at this checkpoint?
            float cornerSpeed = car.Stats.MaxSpeed;
            if (next != null)
            {
                float inHeading = (float)(Math.Atan2(c.X - car.X, c.Z - car.Z) * 180.0 / Math.PI);
                float outHeading = (float)(Math.Atan2(next.X - c.X, next.Z - c.Z) * 180.0 / Math.PI);
                float corner = Math.Abs(VehicleDynamics.Normalize(outHeading - inHeading));
                cornerSpeed = car.Stats.MaxSpeed * MathUtil.Clamp(1f - corner / 120f, 0.3f, 1f);
            }

            if (_mistakeForCheckpoint != i)
            {
                _mistakeForCheckpoint = i;
                _mistake = _rng.Chance((1f - Skill) * 0.35f); // late braking or over-cautious entry this corner
            }
            float brakeMargin = _mistake ? 0.55f : 0.9f + 0.1f * Skill; // mistakes brake too late
            float v = car.Speed;
            float needed = v > cornerSpeed ? (v * v - cornerSpeed * cornerSpeed) / (2f * car.Stats.Braking) : 0f;

            var input = new DriveInput { Steer = steer, Throttle = 0.82f + 0.18f * Skill };
            if (distToC * brakeMargin < needed) { input.Throttle = 0; input.Brake = 1; }
            if (Math.Abs(err) > 70f && v > 12f)
            {
                input.Throttle = 0.3f;
                input.Handbrake = Skill > 0.6f; // only confident drivers yank the handbrake
                if (!input.Handbrake) input.Brake = 0.6f;
            }
            return input;
        }

        private static float Dist(float x1, float z1, float x2, float z2) => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));
    }
}
