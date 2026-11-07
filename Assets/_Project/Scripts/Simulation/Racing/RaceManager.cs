using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Economy;
using FFTS.Relationships;
using FFTS.Reputation;
using FFTS.Vehicles;

namespace FFTS.Racing
{
    public sealed class RaceCompleted : IGameEvent
    {
        public readonly string RaceId; public readonly int Attempt; public readonly int Place; public readonly bool Dnf; public readonly float Time;
        public RaceCompleted(string raceId, int attempt, int place, bool dnf, float time) { RaceId = raceId; Attempt = attempt; Place = place; Dnf = dnf; Time = time; }
    }

    public sealed class RacingState
    {
        public int RankPoints;
        public Dictionary<string, int> Attempts = new Dictionary<string, int>();
        public Dictionary<string, int> Wins = new Dictionary<string, int>();
        public Dictionary<string, float> BestTimes = new Dictionary<string, float>();
        public List<string> CompletedAttempts = new List<string>();
    }

    public enum RaceEntryResult { Ok, UnknownRace, RankTooLow, NoVehicle, VehicleWrecked, InsufficientFunds }

    public sealed class RaceOutcome
    {
        public int Place;
        public bool Dnf;
        public long PurseCents;
        public long BonusCents;
        public int RankPointsGained;
        public RacingRank RankBefore, RankAfter;
        public bool AlreadyResolved;
    }

    /// <summary>
    /// Racing career: entry, results, purses, first-win bonuses, rank. Every payout is keyed by race and attempt,
    /// so resolving the same result twice (or after a reload) never pays twice.
    /// </summary>
    public sealed class RaceManager
    {
        public static readonly int[] RankThresholds = { 0, 100, 300, 700, 1500 };

        private readonly RaceDatabase _races;
        private readonly VehicleDatabase _vehicles;
        private readonly FinessBank _bank;
        private readonly ReputationManager _rep;
        private readonly SocialManager _social;
        private readonly EventBus _bus;
        private RacingState _s = new RacingState();

        public RaceDatabase Races => _races;
        public int RankPoints => _s.RankPoints;
        public RacingRank Rank => RankFor(_s.RankPoints);
        public int Attempts(string raceId) => _s.Attempts.TryGetValue(raceId, out var n) ? n : 0;
        public int Wins(string raceId) => _s.Wins.TryGetValue(raceId, out var n) ? n : 0;
        public float? BestTime(string raceId) => _s.BestTimes.TryGetValue(raceId, out var t) ? t : (float?)null;

        public RaceManager(RaceDatabase races, VehicleDatabase vehicles, FinessBank bank, ReputationManager rep, SocialManager social, EventBus bus)
        {
            _races = races; _vehicles = vehicles; _bank = bank; _rep = rep; _social = social; _bus = bus;
        }

        public static RacingRank RankFor(int points)
        {
            var rank = RacingRank.UnknownDriver;
            for (int i = 0; i < RankThresholds.Length; i++) if (points >= RankThresholds[i]) rank = (RacingRank)i;
            return rank;
        }

        public RaceEntryResult CanEnter(string raceId, VehicleDynamics playerCar)
        {
            var def = _races.Get(raceId);
            if (def == null) return RaceEntryResult.UnknownRace;
            if (Rank < def.MinRank) return RaceEntryResult.RankTooLow;
            if (playerCar == null) return RaceEntryResult.NoVehicle;
            if (playerCar.IsWrecked) return RaceEntryResult.VehicleWrecked;
            if (def.EntryFeeCents > 0 && _bank.Balance(FinessBank.Cash) < def.EntryFeeCents && _bank.Balance(FinessBank.Checking) < def.EntryFeeCents)
                return RaceEntryResult.InsufficientFunds;
            return RaceEntryResult.Ok;
        }

        public RaceEntryResult Begin(string raceId, VehicleDynamics playerCar, ulong seed, out RaceSession session)
        {
            session = null;
            var check = CanEnter(raceId, playerCar);
            if (check != RaceEntryResult.Ok) return check;
            var def = _races.Get(raceId);
            int attempt = Attempts(raceId) + 1;
            if (def.EntryFeeCents > 0 && _bank.Pay($"race.{raceId}.attempt.{attempt}.entry", def.EntryFeeCents, TransactionKind.Purchase, def.DisplayName + " entry") != PostStatus.Posted)
                return RaceEntryResult.InsufficientFunds;
            _s.Attempts[raceId] = attempt;
            session = new RaceSession(def, playerCar, _vehicles, seed ^ (ulong)attempt) { Attempt = attempt };
            return RaceEntryResult.Ok;
        }

        public RaceOutcome Resolve(RaceSession session)
        {
            if (!session.IsOver) throw new InvalidOperationException("Race not over.");
            var def = session.Definition;
            string attemptKey = $"race.{def.Id}.attempt.{session.Attempt}";
            var p = session.Player;
            var outcome = new RaceOutcome { Place = p.Place, Dnf = session.PlayerDnf, RankBefore = Rank };
            if (_s.CompletedAttempts.Contains(attemptKey)) { outcome.AlreadyResolved = true; outcome.RankAfter = Rank; return outcome; }
            _s.CompletedAttempts.Add(attemptKey);

            if (!outcome.Dnf)
            {
                int idx = p.Place - 1;
                if (idx < def.PurseCents.Count && def.PurseCents[idx] > 0)
                {
                    outcome.PurseCents = def.PurseCents[idx];
                    _bank.Post(new TransactionRequest { IdempotencyKey = attemptKey + ".purse", Kind = TransactionKind.Income, To = FinessBank.Cash, AmountCents = outcome.PurseCents, Memo = def.DisplayName + " purse" });
                }
                if (p.Place == 1)
                {
                    _s.Wins[def.Id] = Wins(def.Id) + 1;
                    if (def.FirstWinBonusCents > 0 && _bank.Post(new TransactionRequest { IdempotencyKey = $"race.{def.Id}.first_win", Kind = TransactionKind.Reward, To = FinessBank.Cash, AmountCents = def.FirstWinBonusCents, Memo = def.DisplayName + " first win" }) == PostStatus.Posted)
                        outcome.BonusCents = def.FirstWinBonusCents;
                }
                if (idx < def.RankPoints.Count) outcome.RankPointsGained = def.RankPoints[idx];
                _s.RankPoints += outcome.RankPointsGained;
                if (!_s.BestTimes.TryGetValue(def.Id, out var best) || p.FinishTime < best) _s.BestTimes[def.Id] = p.FinishTime;
            }

            ApplyConsequences(session, outcome, attemptKey);
            outcome.RankAfter = Rank;
            _bus?.Publish(new RaceCompleted(def.Id, session.Attempt, outcome.Place, outcome.Dnf, p.FinishTime));
            return outcome;
        }

        private void ApplyConsequences(RaceSession session, RaceOutcome outcome, string key)
        {
            var p = session.Player;
            var change = new ReputationChange();
            bool clean = p.Impacts <= 1;
            if (!outcome.Dnf && outcome.Place == 1)
            {
                change.Face(FaceCardAttribute.Respect, 3).Image(PublicImageAttribute.Popularity, clean ? 3 : 1);
                if (!clean) change.Face(FaceCardAttribute.Notoriety, 2).Image(PublicImageAttribute.Credibility, -1);
            }
            else if (!outcome.Dnf && outcome.Place <= 3) change.Face(FaceCardAttribute.Respect, 1);
            else change.Face(FaceCardAttribute.Respect, -1);
            _rep?.Apply(change, key);

            if (_social == null) return;
            foreach (var r in session.Racers)
            {
                if (r.IsPlayer || r.NpcId == null) continue;
                bool beatThem = !outcome.Dnf && p.Place < r.Place;
                _social.Modify(r.NpcId, "player", beatThem ? new RelationshipDelta { Respect = 4, Rivalry = 5 } : new RelationshipDelta { Respect = -1, Rivalry = 2 }, key);
            }
        }

        public RacingState Capture() => Newtonsoft.Json.JsonConvert.DeserializeObject<RacingState>(Newtonsoft.Json.JsonConvert.SerializeObject(_s));
        public void Restore(RacingState s) { _s = s ?? new RacingState(); }
    }
}
