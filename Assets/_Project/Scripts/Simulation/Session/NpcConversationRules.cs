using System.Collections.Generic;
using FFTS.Core;
using FFTS.Missions;
using FFTS.World;
using Newtonsoft.Json;

namespace FFTS.Session
{
    /// <summary>One candidate response when the player talks to an NPC. The first matching rule wins.</summary>
    public sealed class ConversationRule
    {
        public string NpcId;
        public string MissionActive;
        public List<string> ObjectivesDone = new List<string>();     // "missionId/objectiveId"
        public List<string> ObjectivesNotDone = new List<string>();
        public string Branch;                                         // "missionId:branchId"
        public List<string> RequireFlags = new List<string>();
        public List<string> ForbidFlags = new List<string>();
        public string DialogueId;
        public string FightId;
        public VenueType Venue = VenueType.Street;
        public string Bark;
    }

    public sealed class ConversationRuleFile { public List<ConversationRule> Rules = new List<ConversationRule>(); }

    /// <summary>Data-driven "what happens when I talk to this person right now" plus NPC placement visibility.</summary>
    public sealed class NpcConversationRules
    {
        private readonly List<ConversationRule> _rules;
        public NpcConversationRules(List<ConversationRule> rules) { _rules = rules; }

        public static NpcConversationRules Parse(string json, GameContent content, ValidationReport report)
        {
            var file = JsonConvert.DeserializeObject<ConversationRuleFile>(json);
            foreach (var r in file.Rules)
            {
                int outcomes = (r.DialogueId != null ? 1 : 0) + (r.FightId != null ? 1 : 0) + (r.Bark != null ? 1 : 0);
                if (outcomes != 1) report.Error($"Conversation rule for {r.NpcId}: needs exactly one of DialogueId/FightId/Bark.");
                if (r.DialogueId != null && !content.Dialogues.ContainsKey(r.DialogueId)) report.Error($"Rule {r.NpcId}: unknown dialogue {r.DialogueId}.");
                if (r.FightId != null && content.Combat?.GetFighter(r.FightId) == null) report.Error($"Rule {r.NpcId}: unknown fighter {r.FightId}.");
                foreach (var o in r.ObjectivesDone) CheckObjectiveRef(o, content, report);
                foreach (var o in r.ObjectivesNotDone) CheckObjectiveRef(o, content, report);
                if (r.MissionActive != null && !content.Missions.Exists(m => m.Id == r.MissionActive)) report.Error($"Rule {r.NpcId}: unknown mission {r.MissionActive}.");
            }
            return new NpcConversationRules(file.Rules);
        }

        public static void CheckObjectiveRef(string reference, GameContent content, ValidationReport report)
        {
            var parts = reference.Split('/');
            var m = parts.Length == 2 ? content.Missions.Find(x => x.Id == parts[0]) : null;
            if (m == null || !m.Objectives.Exists(o => o.Id == parts[1])) report.Error($"Unknown objective reference '{reference}'.");
        }

        public ConversationRule Choose(GameSession s, string npcId)
        {
            foreach (var r in _rules)
                if (r.NpcId == npcId && Matches(s, r)) return r;
            return null;
        }

        private static bool Matches(GameSession s, ConversationRule r)
        {
            if (r.MissionActive != null && s.Missions.Status(r.MissionActive) != MissionStatus.Active) return false;
            foreach (var o in r.ObjectivesDone) if (!ObjectiveDone(s, o)) return false;
            foreach (var o in r.ObjectivesNotDone) if (ObjectiveDone(s, o)) return false;
            if (r.Branch != null)
            {
                var parts = r.Branch.Split(':');
                if (s.Missions.Progress(parts[0])?.ChosenBranch != parts[1]) return false;
            }
            foreach (var f in r.RequireFlags) if (!s.Story.Has(f)) return false;
            foreach (var f in r.ForbidFlags) if (s.Story.Has(f)) return false;
            return true;
        }

        public static bool ObjectiveDone(GameSession s, string reference)
        {
            var parts = reference.Split('/');
            var p = s.Missions.Progress(parts[0]);
            if (p == null) return false;
            return p.Status == MissionStatus.Completed || p.CompletedObjectives.Contains(parts[1]);
        }

        public static bool IsVisible(GameSession s, NpcPlacement n)
        {
            if (n.ShowAfterObjective != null && !ObjectiveDone(s, n.ShowAfterObjective)) return false;
            if (n.HideAfterObjective != null && ObjectiveDone(s, n.HideAfterObjective)) return false;
            return true;
        }
    }
}
