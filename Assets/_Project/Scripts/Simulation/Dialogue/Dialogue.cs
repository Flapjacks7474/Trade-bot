using System.Collections.Generic;
using FFTS.Core;
using FFTS.Missions;
using FFTS.Relationships;
using Newtonsoft.Json;

namespace FFTS.Dialogue
{
    public enum DialogueTone { Neutral, Respectful, Joke, Flirt, Negotiate, Challenge, Apologize, Lie, Truth, Threaten, WalkAway }

    public sealed class DialogueCondition
    {
        public List<string> RequireFlags = new List<string>();
        public List<string> ForbidFlags = new List<string>();
        public int? MinTrust;
        public int? MinRespect;
        public Attitude? MinAttitude;
    }

    public sealed class DialogueVariant
    {
        public DialogueCondition When = new DialogueCondition();
        public string Text;
    }

    public sealed class DialogueOption
    {
        public string Id;
        public string Text;
        public DialogueTone Tone;
        public DialogueCondition When = new DialogueCondition();
        public List<RewardDefinition> Effects = new List<RewardDefinition>();
        public string Next;            // null ends the conversation
        public string Trigger;         // reported to missions when chosen
        public string MissionBranch;   // "missionId:branchId"
        public string StartFight;      // fighter id
    }

    public sealed class DialogueNode
    {
        public string Id;
        public string Speaker;
        public string Text;
        public List<DialogueVariant> Variants = new List<DialogueVariant>(); // first matching variant replaces Text
        public List<DialogueOption> Options = new List<DialogueOption>();
    }

    public sealed class DialogueDefinition
    {
        public string Id;
        public string NpcId;
        public string StartNode;
        public string EndTrigger;      // reported when the conversation ends normally
        public List<DialogueNode> Nodes = new List<DialogueNode>();
    }

    public sealed class DialogueFile { public List<DialogueDefinition> Dialogues = new List<DialogueDefinition>(); }

    public interface IDialogueContext
    {
        bool HasFlag(string flag);
        RelationshipValues Relationship(string npcId);
        Attitude AttitudeOf(string npcId);
        void ApplyEffect(string key, RewardDefinition effect);
        void ReportTrigger(string trigger);
        bool ChooseMissionBranch(string missionId, string branchId);
    }

    public sealed class DialogueOutcome
    {
        public bool Ended;
        public string StartFight;
    }

    /// <summary>
    /// Runs authored conversations. Lines vary by flags, relationship values and attitude; options are
    /// filtered by conditions. Effects use stable keys ("dialogue.&lt;id&gt;.&lt;option&gt;.&lt;n&gt;") so
    /// re-running a conversation never re-grants its consequences.
    /// </summary>
    public sealed class DialogueRunner
    {
        private readonly DialogueDefinition _def;
        private readonly IDialogueContext _ctx;
        private readonly Dictionary<string, DialogueNode> _nodes = new Dictionary<string, DialogueNode>();

        public DialogueNode Current { get; private set; }
        public bool IsFinished => Current == null;

        public DialogueRunner(DialogueDefinition def, IDialogueContext ctx)
        {
            _def = def; _ctx = ctx;
            foreach (var n in def.Nodes) _nodes[n.Id] = n;
            Current = _nodes[def.StartNode];
        }

        public string CurrentLine
        {
            get
            {
                if (Current == null) return null;
                foreach (var v in Current.Variants) if (Matches(v.When)) return v.Text;
                return Current.Text;
            }
        }

        public List<DialogueOption> AvailableOptions()
        {
            var list = new List<DialogueOption>();
            if (Current == null) return list;
            foreach (var o in Current.Options) if (Matches(o.When)) list.Add(o);
            return list;
        }

        public DialogueOutcome Choose(string optionId)
        {
            var option = AvailableOptions().Find(o => o.Id == optionId);
            if (option == null) return null;
            for (int i = 0; i < option.Effects.Count; i++) _ctx.ApplyEffect($"dialogue.{_def.Id}.{option.Id}.{i}", option.Effects[i]);
            if (option.MissionBranch != null)
            {
                var parts = option.MissionBranch.Split(':');
                _ctx.ChooseMissionBranch(parts[0], parts[1]);
            }
            if (option.Trigger != null) _ctx.ReportTrigger(option.Trigger);
            var outcome = new DialogueOutcome { StartFight = option.StartFight };
            Current = option.Next != null && _nodes.TryGetValue(option.Next, out var n) ? n : null;
            if (Current == null)
            {
                outcome.Ended = true;
                if (_def.EndTrigger != null) _ctx.ReportTrigger(_def.EndTrigger);
            }
            return outcome;
        }

        private bool Matches(DialogueCondition c)
        {
            if (c == null) return true;
            foreach (var f in c.RequireFlags) if (!_ctx.HasFlag(f)) return false;
            foreach (var f in c.ForbidFlags) if (_ctx.HasFlag(f)) return false;
            if (c.MinTrust.HasValue || c.MinRespect.HasValue)
            {
                var r = _ctx.Relationship(_def.NpcId);
                if (c.MinTrust.HasValue && r.Trust < c.MinTrust.Value) return false;
                if (c.MinRespect.HasValue && r.Respect < c.MinRespect.Value) return false;
            }
            if (c.MinAttitude.HasValue && _ctx.AttitudeOf(_def.NpcId) < c.MinAttitude.Value) return false;
            return true;
        }

        public static List<DialogueDefinition> Parse(string json, ValidationReport report)
        {
            var file = JsonConvert.DeserializeObject<DialogueFile>(json);
            var ids = new IdRegistry();
            foreach (var d in file.Dialogues)
            {
                ids.Register(d.Id, "dialogue");
                var nodeIds = new HashSet<string>();
                foreach (var n in d.Nodes) if (!nodeIds.Add(n.Id)) report.Error($"{d.Id}: duplicate node {n.Id}.");
                if (!nodeIds.Contains(d.StartNode)) report.Error($"{d.Id}: start node {d.StartNode} missing.");
                foreach (var n in d.Nodes)
                {
                    if (n.Options.Count == 0) report.Error($"{d.Id}/{n.Id}: node has no options (add a 'leave' option).");
                    foreach (var o in n.Options)
                    {
                        if (o.Next != null && !nodeIds.Contains(o.Next)) report.Error($"{d.Id}/{n.Id}/{o.Id}: next node {o.Next} missing.");
                        if (o.MissionBranch != null && o.MissionBranch.Split(':').Length != 2) report.Error($"{d.Id}/{o.Id}: MissionBranch must be 'mission:branch'.");
                        foreach (var e in o.Effects)
                            if (e.Type == RewardType.Flag && e.Target == Story.StoryFlags.ChairmanIdentityRevealed)
                                report.Error($"{d.Id}/{o.Id}: dialogue may not set the Chairman reveal flag directly.");
                    }
                }
            }
            report.AddRange(ids.Errors);
            return file.Dialogues;
        }
    }
}
