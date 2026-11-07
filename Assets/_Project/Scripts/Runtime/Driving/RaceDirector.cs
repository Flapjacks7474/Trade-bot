using System.Collections.Generic;
using FFTS.Racing;
using FFTS.Vehicles;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Runs a <see cref="RaceSession"/>: spawns rivals, steps all cars at 60 Hz, shows the next checkpoint, resolves results.</summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        public const float StartRadius = 25f;

        public bool Active => Session != null;
        public RaceSession Session { get; private set; }
        public string Banner { get; private set; }

        private VehicleView _playerCar;
        private readonly Dictionary<string, VehicleView> _views = new Dictionary<string, VehicleView>();
        private GameObject _marker;
        private float _accumulator, _cleanupAt;
        private bool _resolved;

        private CoreGameManager Gm => CoreGameManager.Instance;

        public RaceDefinition RaceStartNearby(Vector3 pos)
        {
            if (Active) return null;
            foreach (var r in Gm.Session.Racing.Races.Races)
            {
                var l = Gm.Layout.Find(r.LocationId);
                if (l == null) continue;
                if (Vector2.Distance(new Vector2(l.X, l.Z), new Vector2(pos.x, pos.z)) <= StartRadius + Mathf.Max(l.Width, l.Depth) / 2f) return r;
            }
            return null;
        }

        public void TryStart(RaceDefinition def, VehicleView playerCar)
        {
            var result = Gm.Session.Racing.Begin(def.Id, playerCar.Dyn, Gm.Session.Random.State ^ (ulong)Gm.Session.Clock.TotalMinutes, out var session);
            if (result != RaceEntryResult.Ok) { Gm.Notify("Can't start: " + result); return; }
            Session = session; _playerCar = playerCar; _resolved = false; _accumulator = 0;
            _views.Clear();
            playerCar.RacerId = RaceSession.PlayerRacerId;
            playerCar.Teleport();
            _views[RaceSession.PlayerRacerId] = playerCar;
            foreach (var r in session.Racers)
            {
                if (r.IsPlayer) continue;
                var def2 = Gm.Session.Garage.Database.Get(Gm.Session.Racing.Races.Get(def.Id).Opponents.Find(o => o.RacerId == r.RacerId).VehicleId);
                var v = VehicleView.Spawn(r.DisplayName, r.Car, null, r.RacerId == "racer.rell" ? "#2ec4b6" : "#8d99ae");
                v.RacerId = r.RacerId;
                Graybox.Label(v.transform, r.DisplayName + " · " + def2.DisplayName, Vector3.up * 2.4f, 0.1f);
                _views[r.RacerId] = v;
            }
            _marker = Graybox.Box("Checkpoint", null, Vector3.zero, new Vector3(1, 12, 1), "#ffd166", collider: false);
            Gm.Notify(def.DisplayName + " — get ready!");
        }

        private void Update()
        {
            if (!Active) return;
            if (Session.IsOver)
            {
                if (!_resolved) Resolve();
                foreach (var v in _views.Values) if (v != null) v.SyncFromSim(null);
                if (Time.time >= _cleanupAt) Cleanup();
                return;
            }

            _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            const float dt = 1f / 60f;
            var input = DriveInputMapper.Map(Gm.Frame, Gm.Layout.SurfaceGripAt(_playerCar.Dyn.X, _playerCar.Dyn.Z));
            while (_accumulator >= dt && !Session.IsOver)
            {
                _accumulator -= dt;
                Session.Step(input, dt);
                foreach (var e in Session.Events) HandleEvent(e);
                foreach (var kv in _views)
                {
                    string id = kv.Key;
                    kv.Value.SyncFromSim(closing => Session.ReportImpact(id, closing));
                }
            }

            var cp = Session.CheckpointFor(Session.Player);
            _marker.transform.position = new Vector3(cp.X, 6, cp.Z);
            _marker.transform.localScale = new Vector3(cp.Radius * 0.25f, 12, cp.Radius * 0.25f);
        }

        private void HandleEvent(RaceEvent e)
        {
            switch (e.Type)
            {
                case RaceEventType.Countdown: Banner = e.Value.ToString(); break;
                case RaceEventType.Go: Banner = "GO!"; Gm.Notify("GO!"); break;
                case RaceEventType.CheckpointPassed:
                    if (e.RacerId == RaceSession.PlayerRacerId) { Banner = null; Gm.Notify($"Checkpoint {e.Value + 1}/{Session.CheckpointCount}"); }
                    break;
                case RaceEventType.Finished:
                    if (e.RacerId == RaceSession.PlayerRacerId) Banner = Ordinal(e.Value) + " place";
                    break;
                case RaceEventType.TimeExpired: Banner = "Time's up"; break;
            }
        }

        private void Resolve()
        {
            _resolved = true;
            var outcome = Gm.Session.ResolveRace(Session, _playerCar.OwnershipId);
            string money = outcome.PurseCents + outcome.BonusCents > 0 ? " · won " + FFTS.Economy.FinessBank.Format(outcome.PurseCents + outcome.BonusCents) : "";
            Gm.Notify(outcome.Dnf ? "Did not finish." : $"Finished {Ordinal(outcome.Place)}{money} · +{outcome.RankPointsGained} rank pts");
            if (outcome.RankAfter != outcome.RankBefore) Gm.Notify("Racing rank: " + outcome.RankAfter);
            _cleanupAt = Time.time + 3f;
        }

        private void Cleanup()
        {
            foreach (var kv in _views) if (kv.Key != RaceSession.PlayerRacerId && kv.Value != null) Destroy(kv.Value.gameObject);
            _views.Clear();
            if (_marker != null) Destroy(_marker);
            Session = null; Banner = null;
            if (_playerCar != null) _playerCar.RacerId = null;
        }

        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
    }
}
