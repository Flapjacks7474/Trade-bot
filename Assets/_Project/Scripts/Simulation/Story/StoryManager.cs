using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Story
{
    /// <summary>Well-known story flags. Content files may define more; these have rules attached.</summary>
    public static class StoryFlags
    {
        /// <summary>The Chairman's identity as the protagonist's older brother. Chapter 7 only.</summary>
        public const string ChairmanIdentityRevealed = "story.chairman.identity_revealed";
        /// <summary>The Southern Table's existence as an organisation (Chapter 6).</summary>
        public const string SouthernTableRevealed = "story.southern_table.revealed";
        public const string PhotoFirstSeen = "clue.photo.first_seen";
        public const string RevealMission = "mission.ch7.man_behind_table";
    }

    public sealed class StoryFlagRule
    {
        public string Flag;
        public int MinChapter;
        public string RequiredMission; // must be active or completed when the flag is set
    }

    public sealed class StoryState
    {
        public int Chapter = 1;
        public Dictionary<string, int> Flags = new Dictionary<string, int>();
        public List<string> BlockedAttempts = new List<string>();
    }

    /// <summary>
    /// Owns campaign chapter and story flags. Protected flags (the brother reveal above all) can only be set
    /// when their chapter and mission gates are met — content bugs cannot spoil the reveal early.
    /// Blocked attempts are logged for QA rather than silently ignored.
    /// </summary>
    public sealed class StoryManager
    {
        private readonly EventBus _bus;
        private readonly Dictionary<string, int> _flags = new Dictionary<string, int>();
        private readonly Dictionary<string, StoryFlagRule> _rules = new Dictionary<string, StoryFlagRule>();
        private readonly List<string> _blocked = new List<string>();

        /// <summary>Answers "is this mission active or completed?" — injected to avoid a dependency cycle with missions.</summary>
        public Func<string, bool> MissionReached = _ => false;

        public int Chapter { get; private set; } = 1;
        public IReadOnlyList<string> BlockedAttempts => _blocked;

        public StoryManager(EventBus bus)
        {
            _bus = bus;
            AddRule(new StoryFlagRule { Flag = StoryFlags.ChairmanIdentityRevealed, MinChapter = 7, RequiredMission = StoryFlags.RevealMission });
            AddRule(new StoryFlagRule { Flag = StoryFlags.SouthernTableRevealed, MinChapter = 6 });
        }

        public void AddRule(StoryFlagRule rule) { Ids.Require(rule.Flag, "story rule"); _rules[rule.Flag] = rule; }

        public int Get(string flag) => _flags.TryGetValue(flag, out var v) ? v : 0;
        public bool Has(string flag) => Get(flag) != 0;
        public IReadOnlyDictionary<string, int> Flags => _flags;

        public bool CanSet(string flag, out string reason)
        {
            reason = null;
            if (!_rules.TryGetValue(flag, out var r)) return true;
            if (Chapter < r.MinChapter) { reason = $"requires chapter {r.MinChapter} (current {Chapter})"; return false; }
            if (r.RequiredMission != null && !MissionReached(r.RequiredMission)) { reason = $"requires mission {r.RequiredMission}"; return false; }
            return true;
        }

        public bool TrySet(string flag, int value = 1)
        {
            Ids.Require(flag, "story flag");
            if (!CanSet(flag, out var reason)) { _blocked.Add($"{flag}: {reason}"); return false; }
            int old = Get(flag);
            if (old == value) return true;
            _flags[flag] = value;
            _bus?.Publish(new StoryFlagChanged(flag, old, value));
            return true;
        }

        /// <summary>Chapters only move forward.</summary>
        public bool AdvanceChapter(int chapter)
        {
            if (chapter <= Chapter || chapter > 8) return false;
            Chapter = chapter;
            return true;
        }

        public StoryState Capture() => new StoryState { Chapter = Chapter, Flags = new Dictionary<string, int>(_flags), BlockedAttempts = new List<string>(_blocked) };

        public void Restore(StoryState s)
        {
            Chapter = Math.Max(1, Math.Min(8, s.Chapter));
            _flags.Clear(); _blocked.Clear();
            foreach (var kv in s.Flags) _flags[kv.Key] = kv.Value;
            _blocked.AddRange(s.BlockedAttempts);
        }
    }
}
