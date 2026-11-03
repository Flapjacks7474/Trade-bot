using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Story;

namespace FFTS.Missions
{
    /// <summary>Receives rewards. Implementations must also be idempotent on <paramref name="key"/>.</summary>
    public interface IRewardSink
    {
        void Grant(string key, RewardDefinition reward);
    }

    public enum MissionActionResult { Ok, UnknownMission, NotAvailable, NotActive, AlreadyChosen, UnknownBranch, UnknownObjective, WrongBranch, NotReplayable }

    /// <summary>
    /// Owns mission definitions and progress. Guarantees:
    /// rewards are granted at most once per key (ledger persisted in saves); branch choices are locked once made;
    /// replays never grant live rewards, set flags or rewrite completed decisions; failures follow the
    /// mission's recovery rule.
    /// </summary>
    public sealed class MissionManager
    {
        private readonly EventBus _bus;
        private readonly StoryManager _story;
        private readonly IRewardSink _sink;
        private readonly Dictionary<string, MissionDefinition> _defs = new Dictionary<string, MissionDefinition>();
        private readonly List<MissionDefinition> _ordered = new List<MissionDefinition>();
        private readonly Dictionary<string, MissionProgress> _progress = new Dictionary<string, MissionProgress>();
        private readonly HashSet<string> _granted = new HashSet<string>();

        public string TrackedMission { get; set; }

        public MissionManager(EventBus bus, StoryManager story, IRewardSink sink)
        {
            _bus = bus; _story = story; _sink = sink;
            story.MissionReached = id => _progress.TryGetValue(id, out var p) && (p.Status == MissionStatus.Active || p.Status == MissionStatus.Completed);
        }

        public IEnumerable<MissionDefinition> Definitions => _ordered;
        public MissionDefinition Definition(string id) => _defs.TryGetValue(id, out var d) ? d : null;
        public MissionProgress Progress(string id) => _progress.TryGetValue(id, out var p) ? p : null;
        public MissionStatus Status(string id) => _progress.TryGetValue(id, out var p) ? p.Status : MissionStatus.Locked;
        public bool RewardGranted(string key) => _granted.Contains(key);

        public void Load(IEnumerable<MissionDefinition> definitions, ValidationReport report)
        {
            var ids = new IdRegistry();
            foreach (var d in definitions)
            {
                if (!ids.Register(d.Id, "mission")) continue;
                _defs[d.Id] = d; _ordered.Add(d);
                _progress[d.Id] = new MissionProgress { MissionId = d.Id, Status = MissionStatus.Locked };
            }
            _ordered.Sort((a, b) => a.Chapter != b.Chapter ? a.Chapter.CompareTo(b.Chapter) : a.Order.CompareTo(b.Order));
            report.AddRange(ids.Errors);
            Validate(report);
            RefreshAvailability();
        }

        private void Validate(ValidationReport report)
        {
            foreach (var d in _ordered)
            {
                if (string.IsNullOrEmpty(d.Title)) report.Error($"{d.Id}: missing title.");
                if (string.IsNullOrEmpty(d.Cause) || string.IsNullOrEmpty(d.Purpose)) report.Error($"{d.Id}: every mission needs a cause and a purpose.");
                if (d.Chapter < 0 || d.Chapter > 8) report.Error($"{d.Id}: chapter {d.Chapter} out of range.");
                if (d.Objectives.Count == 0) report.Error($"{d.Id}: no objectives.");
                var objIds = new HashSet<string>(); var branchIds = new HashSet<string>();
                foreach (var b in d.Branches) if (!branchIds.Add(b.Id)) report.Error($"{d.Id}: duplicate branch {b.Id}.");
                foreach (var o in d.Objectives)
                {
                    if (!objIds.Add(o.Id)) report.Error($"{d.Id}: duplicate objective {o.Id}.");
                    if (string.IsNullOrEmpty(o.Trigger)) report.Error($"{d.Id}/{o.Id}: objective has no trigger.");
                    if (o.Branch != null && !branchIds.Contains(o.Branch)) report.Error($"{d.Id}/{o.Id}: unknown branch {o.Branch}.");
                }
                if (d.BranchRequired && d.Branches.Count == 0) report.Error($"{d.Id}: branch required but none defined.");
                foreach (var p in d.Prerequisites.MissionsCompleted) if (!_defs.ContainsKey(p)) report.Error($"{d.Id}: unknown prerequisite {p}.");
                foreach (var u in d.Unlocks) if (!_defs.ContainsKey(u)) report.Error($"{d.Id}: unlocks unknown mission {u}.");
                foreach (var b in d.Branches) foreach (var u in b.Unlocks) if (!_defs.ContainsKey(u)) report.Error($"{d.Id}/{b.Id}: unlocks unknown mission {u}.");
                foreach (var r in AllRewards(d)) ValidateReward(d.Id, r, report);
                if (d.Chapter < 7 && MentionsReveal(d)) report.Error($"{d.Id}: sets the Chairman reveal flag before chapter 7.");
            }
        }

        private static IEnumerable<RewardDefinition> AllRewards(MissionDefinition d)
        {
            foreach (var r in d.Rewards) yield return r;
            foreach (var b in d.Branches) foreach (var r in b.Effects) yield return r;
        }

        private static bool MentionsReveal(MissionDefinition d)
        {
            foreach (var r in AllRewards(d)) if (r.Type == RewardType.Flag && r.Target == StoryFlags.ChairmanIdentityRevealed) return true;
            return false;
        }

        private static void ValidateReward(string mission, RewardDefinition r, ValidationReport report)
        {
            switch (r.Type)
            {
                case RewardType.Money: if (r.AmountCents == 0) report.Error($"{mission}: zero money reward."); break;
                case RewardType.Relationship: if (r.Relationship == null || !Ids.IsValid(r.Target)) report.Error($"{mission}: relationship reward needs target and values."); break;
                case RewardType.Reputation: if (r.FaceCard == null && r.PublicImage == null) report.Error($"{mission}: empty reputation reward."); break;
                default: if (!Ids.IsValid(r.Target)) report.Error($"{mission}: {r.Type} reward has invalid target '{r.Target}'."); break;
            }
        }

        // ---------------------------------------------------------------- availability

        public bool PrerequisitesMet(MissionDefinition d)
        {
            var pre = d.Prerequisites;
            if (_story.Chapter < Math.Max(pre.MinChapter, d.Category == MissionCategory.Main ? d.Chapter : 0)) return false;
            foreach (var m in pre.MissionsCompleted) if (Status(m) != MissionStatus.Completed) return false;
            foreach (var kv in pre.FlagsAtLeast) if (_story.Get(kv.Key) < kv.Value) return false;
            foreach (var f in pre.FlagsAbsent) if (_story.Has(f)) return false;
            return true;
        }

        public void RefreshAvailability()
        {
            foreach (var d in _ordered)
            {
                var p = _progress[d.Id];
                if (p.Status == MissionStatus.Locked && PrerequisitesMet(d)) SetStatus(p, MissionStatus.Available);
            }
        }

        public List<MissionDefinition> ByStatus(MissionStatus s)
        {
            var list = new List<MissionDefinition>();
            foreach (var d in _ordered) if (_progress[d.Id].Status == s) list.Add(d);
            return list;
        }

        // ---------------------------------------------------------------- lifecycle

        public MissionActionResult Start(string id)
        {
            if (!_progress.TryGetValue(id, out var p)) return MissionActionResult.UnknownMission;
            var d = _defs[id];
            if (p.Status == MissionStatus.Completed) return StartReplay(id);
            bool retry = p.Status == MissionStatus.Failed && d.Recovery != FailureRecovery.FailPermanently;
            if (p.Status != MissionStatus.Available && !retry) return MissionActionResult.NotAvailable;
            if (retry && d.Recovery == FailureRecovery.RestartMission) { p.CompletedObjectives.Clear(); }
            p.Attempts++;
            SetStatus(p, MissionStatus.Active);
            TrackedMission = id;
            return MissionActionResult.Ok;
        }

        public MissionActionResult StartReplay(string id)
        {
            if (!_progress.TryGetValue(id, out var p)) return MissionActionResult.UnknownMission;
            if (p.Status != MissionStatus.Completed) return MissionActionResult.NotAvailable;
            if (!_defs[id].Replayable) return MissionActionResult.NotReplayable;
            p.IsReplay = true; p.ReplayObjectives.Clear(); p.ReplayBranch = null;
            return MissionActionResult.Ok;
        }

        private bool IsLive(MissionProgress p) => p.Status == MissionStatus.Active;
        private bool IsRunning(MissionProgress p) => IsLive(p) || (p.Status == MissionStatus.Completed && p.IsReplay);

        /// <summary>Feeds a world trigger to every running mission. Returns the number of objectives completed.</summary>
        public int ReportTrigger(string trigger)
        {
            int count = 0;
            foreach (var d in _ordered)
            {
                var p = _progress[d.Id];
                if (!IsRunning(p)) continue;
                if (IsLive(p) && d.FailureConditions.Contains(trigger)) { Fail(d.Id, trigger); continue; }
                foreach (var o in d.Objectives)
                    if (o.Trigger == trigger && CompleteObjective(d.Id, o.Id) == MissionActionResult.Ok) count++;
            }
            return count;
        }

        /// <summary>True if any live mission has an unfinished objective completed by this trigger.</summary>
        public bool WantsTrigger(string trigger)
        {
            foreach (var d in _ordered)
            {
                var p = _progress[d.Id];
                if (!IsLive(p)) continue;
                foreach (var o in d.Objectives)
                    if (o.Trigger == trigger && !p.CompletedObjectives.Contains(o.Id) && (o.Branch == null || o.Branch == p.ChosenBranch)) return true;
            }
            return false;
        }

        public MissionActionResult CompleteObjective(string missionId, string objectiveId)
        {
            if (!_progress.TryGetValue(missionId, out var p)) return MissionActionResult.UnknownMission;
            if (!IsRunning(p)) return MissionActionResult.NotActive;
            var d = _defs[missionId];
            var o = d.Objectives.Find(x => x.Id == objectiveId);
            if (o == null) return MissionActionResult.UnknownObjective;
            string branch = p.IsReplay ? p.ReplayBranch : p.ChosenBranch;
            if (o.Branch != null && o.Branch != branch) return MissionActionResult.WrongBranch;
            var done = p.IsReplay ? p.ReplayObjectives : p.CompletedObjectives;
            if (done.Contains(objectiveId)) return MissionActionResult.NotActive;
            done.Add(objectiveId);
            if (!p.IsReplay) _bus?.Publish(new ObjectiveCompleted(missionId, objectiveId));
            TryComplete(d, p);
            return MissionActionResult.Ok;
        }

        public MissionActionResult ChooseBranch(string missionId, string branchId)
        {
            if (!_progress.TryGetValue(missionId, out var p)) return MissionActionResult.UnknownMission;
            if (!IsRunning(p)) return MissionActionResult.NotActive;
            var d = _defs[missionId];
            var b = d.Branches.Find(x => x.Id == branchId);
            if (b == null) return MissionActionResult.UnknownBranch;
            if (p.IsReplay)
            {
                if (p.ReplayBranch != null) return MissionActionResult.AlreadyChosen;
                p.ReplayBranch = branchId; // replay choices are sandboxed: no effects, no rewrite
                TryComplete(d, p);
                return MissionActionResult.Ok;
            }
            if (p.ChosenBranch != null) return MissionActionResult.AlreadyChosen;
            p.ChosenBranch = branchId;
            for (int i = 0; i < b.Effects.Count; i++) Grant($"branch.{d.Id}.{b.Id}.{i}", b.Effects[i]);
            TryComplete(d, p);
            return MissionActionResult.Ok;
        }

        public MissionActionResult Fail(string missionId, string reason)
        {
            if (!_progress.TryGetValue(missionId, out var p)) return MissionActionResult.UnknownMission;
            if (!IsLive(p)) return MissionActionResult.NotActive;
            var d = _defs[missionId];
            p.LastFailure = reason;
            if (d.Recovery == FailureRecovery.RestartFromCheckpoint)
                p.CompletedObjectives.RemoveAll(id => { var o = d.Objectives.Find(x => x.Id == id); return o == null || !o.Checkpoint; });
            SetStatus(p, MissionStatus.Failed);
            return MissionActionResult.Ok;
        }

        private void TryComplete(MissionDefinition d, MissionProgress p)
        {
            string branch = p.IsReplay ? p.ReplayBranch : p.ChosenBranch;
            var done = p.IsReplay ? p.ReplayObjectives : p.CompletedObjectives;
            if (d.BranchRequired && branch == null) return;
            foreach (var o in d.Objectives)
            {
                if (o.Optional) continue;
                if (o.Branch != null && o.Branch != branch) continue;
                if (!done.Contains(o.Id)) return;
            }

            if (p.IsReplay)
            {
                p.IsReplay = false; p.ReplayObjectives.Clear(); p.ReplayBranch = null;
                _bus?.Publish(new MissionCompleted(d.Id, branch, true));
                return;
            }

            SetStatus(p, MissionStatus.Completed);
            for (int i = 0; i < d.Rewards.Count; i++) Grant($"reward.{d.Id}.{i}", d.Rewards[i]);
            if (d.AdvanceToChapter > 0) _story.AdvanceChapter(d.AdvanceToChapter);
            if (TrackedMission == d.Id) TrackedMission = null;
            _bus?.Publish(new MissionCompleted(d.Id, branch, false));
            RefreshAvailability();
        }

        private void Grant(string key, RewardDefinition r)
        {
            if (!_granted.Add(key)) return;
            _sink?.Grant(key, r);
        }

        private void SetStatus(MissionProgress p, MissionStatus s)
        {
            var old = p.Status; p.Status = s;
            if (old != s) _bus?.Publish(new MissionStateChanged(p.MissionId, old.ToString(), s.ToString()));
        }

        // ---------------------------------------------------------------- persistence

        public MissionsState Capture()
        {
            var s = new MissionsState { TrackedMission = TrackedMission };
            foreach (var d in _ordered)
            {
                var p = _progress[d.Id];
                // Replays in progress are not persisted: loading returns to the completed state.
                s.Progress.Add(new MissionProgress
                {
                    MissionId = p.MissionId, Status = p.Status, ChosenBranch = p.ChosenBranch, Attempts = p.Attempts,
                    CompletedObjectives = new List<string>(p.CompletedObjectives), LastFailure = p.LastFailure
                });
            }
            s.GrantedRewardKeys.AddRange(_granted);
            s.GrantedRewardKeys.Sort(StringComparer.Ordinal);
            return s;
        }

        public void Restore(MissionsState s)
        {
            _granted.Clear();
            foreach (var k in s.GrantedRewardKeys) _granted.Add(k);
            foreach (var d in _ordered) _progress[d.Id] = new MissionProgress { MissionId = d.Id, Status = MissionStatus.Locked };
            foreach (var p in s.Progress)
                if (_defs.ContainsKey(p.MissionId)) // missions removed from content are dropped, not crashed on
                    _progress[p.MissionId] = new MissionProgress
                    {
                        MissionId = p.MissionId, Status = p.Status, ChosenBranch = p.ChosenBranch, Attempts = p.Attempts,
                        CompletedObjectives = new List<string>(p.CompletedObjectives), LastFailure = p.LastFailure
                    };
            TrackedMission = s.TrackedMission != null && _defs.ContainsKey(s.TrackedMission) ? s.TrackedMission : null;
            RefreshAvailability();
        }
    }
}
