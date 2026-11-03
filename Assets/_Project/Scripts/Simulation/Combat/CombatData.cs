using System;
using System.Collections.Generic;
using FFTS.Core;
using Newtonsoft.Json;

namespace FFTS.Combat
{
    public enum MoveKind { Light, Heavy, Kick, Grapple, ClinchStrike, Throw, EnvironmentalThrow, Counter, Signature, Finisher, GroundAttack, Taunt }

    /// <summary>Discrete commands a fighter can issue. Block is a held state, not a command.</summary>
    public enum CombatCommand { None, Light, Heavy, Kick, Grapple, Dodge, Counter, Finisher, Taunt }

    public enum FighterArchetype { Enforcer, Technician, Striker, Grappler, Scrapper, Showman }

    /// <summary>Immutable move data. Timings are in simulation ticks (60 per second).</summary>
    public sealed class MoveDefinition
    {
        public string Id;
        public string DisplayName;
        public MoveKind Kind;
        public int Startup = 6;
        public int Active = 3;
        public int Recovery = 12;
        public float Damage;
        public float StaminaCost;
        public float StaminaDamage;   // drained from a blocking defender
        public float MomentumGain;
        public float Range = 1.2f;    // metres from body edge
        public int HitStun = 18;
        public int BlockStun = 10;
        public bool Knockdown;
        public float Pushback = 0.3f;
        public int CancelWindow = 14; // ticks into recovery during which a linked move may be started
        public string AnimationKey;   // presentation hook (Animator state / timeline id)

        [JsonIgnore] public bool IsStrike => Kind == MoveKind.Light || Kind == MoveKind.Heavy || Kind == MoveKind.Kick
                                          || Kind == MoveKind.Counter || Kind == MoveKind.Signature || Kind == MoveKind.GroundAttack;
        [JsonIgnore] public int TotalTicks => Startup + Active + Recovery;
    }

    /// <summary>A combo route: while <see cref="From"/> has connected, <see cref="Command"/> cancels into <see cref="To"/>.</summary>
    public sealed class MoveLink
    {
        public string From;
        public CombatCommand Command;
        public string To;
    }

    public sealed class FightingStyleDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public List<MoveDefinition> Moves = new List<MoveDefinition>();
        // Entry slots (move ids). A style may leave slots empty so a hybrid's other style fills them.
        public string Light, Heavy, Kick, Grapple, Counter, Finisher, GroundAttack, Taunt;
        public string ClinchStrike, ClinchThrow, EnvironmentalThrow, Reversal;
        public List<MoveLink> Links = new List<MoveLink>();
    }

    /// <summary>1-100 attributes. Personality values influence AI and composure under pressure.</summary>
    public sealed class FighterStats
    {
        public int Strength = 50, Speed = 50, Stamina = 50, Durability = 50, Technique = 50;
        public int Aggression = 50, Confidence = 50, Composure = 50;

        public IEnumerable<KeyValuePair<string, int>> All()
        {
            yield return new KeyValuePair<string, int>(nameof(Strength), Strength);
            yield return new KeyValuePair<string, int>(nameof(Speed), Speed);
            yield return new KeyValuePair<string, int>(nameof(Stamina), Stamina);
            yield return new KeyValuePair<string, int>(nameof(Durability), Durability);
            yield return new KeyValuePair<string, int>(nameof(Technique), Technique);
            yield return new KeyValuePair<string, int>(nameof(Aggression), Aggression);
            yield return new KeyValuePair<string, int>(nameof(Confidence), Confidence);
            yield return new KeyValuePair<string, int>(nameof(Composure), Composure);
        }
    }

    public sealed class FighterDefinition
    {
        public string Id;
        public string DisplayName;
        public FighterArchetype Archetype;
        public List<string> Styles = new List<string>();
        public FighterStats Stats = new FighterStats();
        public AIStyle AiStyle = AIStyle.Aggressive;
        public AIDifficulty AiDifficulty = AIDifficulty.Normal;
        public string Bio;
        public string PlaceholderModel; // documented placeholder until final art exists
    }

    /// <summary>Resolved moveset for a fighter combining up to three styles (first style has slot priority).</summary>
    public sealed class Moveset
    {
        public const int MaxStyles = 3;
        private readonly Dictionary<string, MoveDefinition> _moves = new Dictionary<string, MoveDefinition>();
        private readonly List<MoveLink> _links = new List<MoveLink>();

        public MoveDefinition Light, Heavy, Kick, Grapple, Counter, Finisher, GroundAttack, Taunt;
        public MoveDefinition ClinchStrike, ClinchThrow, EnvironmentalThrow, Reversal;

        public IReadOnlyList<MoveLink> Links => _links;
        public MoveDefinition Get(string id) => id != null && _moves.TryGetValue(id, out var m) ? m : null;

        public static Moveset Build(IList<FightingStyleDefinition> styles)
        {
            if (styles == null || styles.Count == 0) throw new ArgumentException("A fighter needs at least one style.");
            if (styles.Count > MaxStyles) throw new ArgumentException($"A fighter can combine at most {MaxStyles} styles.");
            var set = new Moveset();
            foreach (var s in styles)
            {
                foreach (var m in s.Moves) if (!set._moves.ContainsKey(m.Id)) set._moves[m.Id] = m;
                set._links.AddRange(s.Links);
            }
            foreach (var s in styles)
            {
                set.Light = set.Light ?? set.Get(s.Light);
                set.Heavy = set.Heavy ?? set.Get(s.Heavy);
                set.Kick = set.Kick ?? set.Get(s.Kick);
                set.Grapple = set.Grapple ?? set.Get(s.Grapple);
                set.Counter = set.Counter ?? set.Get(s.Counter);
                set.Finisher = set.Finisher ?? set.Get(s.Finisher);
                set.GroundAttack = set.GroundAttack ?? set.Get(s.GroundAttack);
                set.Taunt = set.Taunt ?? set.Get(s.Taunt);
                set.ClinchStrike = set.ClinchStrike ?? set.Get(s.ClinchStrike);
                set.ClinchThrow = set.ClinchThrow ?? set.Get(s.ClinchThrow);
                set.EnvironmentalThrow = set.EnvironmentalThrow ?? set.Get(s.EnvironmentalThrow);
                set.Reversal = set.Reversal ?? set.Get(s.Reversal);
            }
            return set;
        }

        public MoveDefinition FindLink(string fromId, CombatCommand command)
        {
            foreach (var l in _links)
                if (l.From == fromId && l.Command == command) return Get(l.To);
            return null;
        }

        public MoveDefinition EntryFor(CombatCommand c)
        {
            switch (c)
            {
                case CombatCommand.Light: return Light;
                case CombatCommand.Heavy: return Heavy;
                case CombatCommand.Kick: return Kick;
                case CombatCommand.Grapple: return Grapple;
                case CombatCommand.Taunt: return Taunt;
                default: return null;
            }
        }
    }

    /// <summary>Loads and validates styles and fighters from JSON content files.</summary>
    public sealed class CombatDatabase
    {
        private readonly Dictionary<string, FightingStyleDefinition> _styles = new Dictionary<string, FightingStyleDefinition>();
        private readonly Dictionary<string, FighterDefinition> _fighters = new Dictionary<string, FighterDefinition>();

        public IEnumerable<FighterDefinition> Fighters => _fighters.Values;
        public IEnumerable<FightingStyleDefinition> Styles => _styles.Values;

        public FightingStyleDefinition GetStyle(string id) => _styles.TryGetValue(id, out var s) ? s : null;
        public FighterDefinition GetFighter(string id) => _fighters.TryGetValue(id, out var f) ? f : null;

        private sealed class StyleFile { public List<FightingStyleDefinition> Styles = new List<FightingStyleDefinition>(); }
        private sealed class FighterFile { public List<FighterDefinition> Fighters = new List<FighterDefinition>(); }

        public static CombatDatabase FromJson(IEnumerable<string> styleJson, IEnumerable<string> fighterJson, ValidationReport report)
        {
            var db = new CombatDatabase();
            var styleIds = new IdRegistry(); var fighterIds = new IdRegistry();
            foreach (var json in styleJson)
                foreach (var s in JsonConvert.DeserializeObject<StyleFile>(json).Styles)
                    if (styleIds.Register(s.Id, "style")) db._styles[s.Id] = s;
            foreach (var json in fighterJson)
                foreach (var f in JsonConvert.DeserializeObject<FighterFile>(json).Fighters)
                    if (fighterIds.Register(f.Id, "fighter")) db._fighters[f.Id] = f;
            report.AddRange(styleIds.Errors); report.AddRange(fighterIds.Errors);
            db.Validate(report);
            return db;
        }

        public void Validate(ValidationReport report)
        {
            var moveIds = new IdRegistry();
            foreach (var s in _styles.Values)
            {
                foreach (var m in s.Moves)
                {
                    moveIds.Register(m.Id, $"style {s.Id} move");
                    if (m.Startup < 1 || m.Active < 1 || m.Recovery < 0) report.Error($"Move {m.Id}: invalid frame data.");
                    if (m.Damage < 0 || m.StaminaCost < 0 || m.Range <= 0) report.Error($"Move {m.Id}: invalid damage/stamina/range.");
                    if (m.CancelWindow < 0 || m.CancelWindow > m.Recovery) report.Error($"Move {m.Id}: cancel window must fit inside recovery.");
                    if (string.IsNullOrEmpty(m.AnimationKey)) report.Error($"Move {m.Id}: missing animation key.");
                }
                var local = new HashSet<string>();
                foreach (var m in s.Moves) local.Add(m.Id);
                foreach (var slot in new[] { s.Light, s.Heavy, s.Kick, s.Grapple, s.Counter, s.Finisher, s.GroundAttack, s.Taunt, s.ClinchStrike, s.ClinchThrow, s.EnvironmentalThrow, s.Reversal })
                    if (slot != null && !local.Contains(slot)) report.Error($"Style {s.Id}: slot references unknown move '{slot}'.");
                var linkKeys = new HashSet<string>();
                foreach (var l in s.Links)
                {
                    if (!local.Contains(l.From) || !local.Contains(l.To)) report.Error($"Style {s.Id}: link {l.From}->{l.To} references unknown move.");
                    if (!linkKeys.Add(l.From + "|" + l.Command)) report.Error($"Style {s.Id}: ambiguous link from {l.From} on {l.Command}.");
                }
                if (HasLinkCycle(s)) report.Error($"Style {s.Id}: combo links contain a cycle (infinite combo risk).");
            }
            report.AddRange(moveIds.Errors);

            foreach (var f in _fighters.Values)
            {
                if (f.Styles == null || f.Styles.Count == 0 || f.Styles.Count > Moveset.MaxStyles)
                    report.Error($"Fighter {f.Id}: must have 1-{Moveset.MaxStyles} styles.");
                else foreach (var sid in f.Styles) if (!_styles.ContainsKey(sid)) report.Error($"Fighter {f.Id}: unknown style '{sid}'.");
                foreach (var kv in f.Stats.All())
                    if (kv.Value < 1 || kv.Value > 100) report.Error($"Fighter {f.Id}: {kv.Key}={kv.Value} outside 1-100.");
                if (string.IsNullOrEmpty(f.DisplayName)) report.Error($"Fighter {f.Id}: missing display name.");
            }
        }

        private static bool HasLinkCycle(FightingStyleDefinition s)
        {
            var edges = new Dictionary<string, List<string>>();
            foreach (var l in s.Links)
            {
                if (!edges.TryGetValue(l.From, out var list)) edges[l.From] = list = new List<string>();
                list.Add(l.To);
            }
            var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
            bool Visit(string n)
            {
                if (state.TryGetValue(n, out var st)) return st == 1;
                state[n] = 1;
                if (edges.TryGetValue(n, out var next)) foreach (var x in next) if (Visit(x)) return true;
                state[n] = 2; return false;
            }
            foreach (var k in edges.Keys) if (Visit(k)) return true;
            return false;
        }

        public Moveset BuildMoveset(FighterDefinition f)
        {
            var styles = new List<FightingStyleDefinition>();
            foreach (var id in f.Styles) styles.Add(GetStyle(id) ?? throw new ArgumentException($"Unknown style {id}"));
            return Moveset.Build(styles);
        }
    }
}
