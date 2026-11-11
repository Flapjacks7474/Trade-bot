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

    /// <summary>
    /// How a style stands and defends. Multipliers are 1 and bonuses 0 for a neutral stance, so a style without
    /// a stance plays exactly like the original Street Fighting rules.
    /// </summary>
    public sealed class StanceDefinition
    {
        public string Name = "Neutral";
        public string Defense = "Block";
        public string Description;
        public string IdleAnimation;   // presentation hooks for the stance loop and the held guard
        public string GuardAnimation;
        public float WalkSpeed = 1f;
        public float StrikeDamage = 1f;     // punches, elbows, counters, signatures, clinch strikes, ground attacks
        public float KickDamage = 1f;
        public float ThrowDamage = 1f;      // clinch throws, environmental throws, reversals
        public int ParryWindowBonus;        // extra ticks for a timed block to become a parry
        public int CounterWindowBonus;      // extra ticks the counter stance catches strikes
        public float BlockChip = 1f;        // chip damage taken while blocking
        public float GuardDrain = 1f;       // stamina drained from this fighter's guard
        public float DodgeDistance = 1f;
        public float DodgeCost = 1f;
        public float EscapeRate = 1f;       // grapple escape progress per press
        public int ClinchStrikesBonus;      // extra clinch strikes before the clinch must end

        public static readonly StanceDefinition Neutral = new StanceDefinition();

        public float DamageFor(MoveKind kind)
        {
            switch (kind)
            {
                case MoveKind.Kick: return KickDamage;
                case MoveKind.Throw: case MoveKind.EnvironmentalThrow: return ThrowDamage;
                case MoveKind.Finisher: case MoveKind.Taunt: case MoveKind.Grapple: return 1f;
                default: return StrikeDamage;
            }
        }
    }

    public sealed class FightingStyleDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Discipline;          // Striking, Grappling, Mixed, Hybrid (menus and AI flavour only)
        public long UnlockCostCents;       // paid once through FINESS BANK when the style is learned
        public string LearnAt;             // gym or condition shown in the menu
        public string Strengths, Weaknesses;
        public StanceDefinition Stance;
        /// <summary>Set on hybrids only: the two base styles that must both be equipped for this hybrid to apply.</summary>
        public List<string> HybridOf;
        [JsonIgnore] public bool IsHybrid => HybridOf != null && HybridOf.Count > 0;
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
        /// <summary>The stance in effect: the hybrid's when one applies, else the first style's that defines one.</summary>
        public StanceDefinition Stance { get; private set; } = StanceDefinition.Neutral;
        /// <summary>Id of the hybrid style merged in, or null.</summary>
        public string HybridId { get; private set; }

        public static Moveset Build(IList<FightingStyleDefinition> styles) => Build(styles, null);

        /// <summary>
        /// Merges styles in priority order. A hybrid, when given, goes first: its moves, links (which may start from
        /// either parent's moves), slots and stance take priority over its parents'.
        /// </summary>
        public static Moveset Build(IList<FightingStyleDefinition> styles, FightingStyleDefinition hybrid)
        {
            if (styles == null || styles.Count == 0) throw new ArgumentException("A fighter needs at least one style.");
            if (styles.Count > MaxStyles) throw new ArgumentException($"A fighter can combine at most {MaxStyles} styles.");
            var ordered = new List<FightingStyleDefinition>();
            if (hybrid != null) ordered.Add(hybrid);
            ordered.AddRange(styles);
            var set = new Moveset { HybridId = hybrid?.Id };
            foreach (var s in ordered) if (s.Stance != null) { set.Stance = s.Stance; break; }
            foreach (var s in ordered)
            {
                foreach (var m in s.Moves) if (!set._moves.ContainsKey(m.Id)) set._moves[m.Id] = m;
                set._links.AddRange(s.Links);
            }
            foreach (var s in ordered)
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
            var hybridPairs = new HashSet<string>();
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
                ValidateStance(s, report);
                if (s.UnlockCostCents < 0) report.Error($"Style {s.Id}: negative unlock cost.");

                // Moves a style's slots and links may use: its own, plus both parents' for a hybrid.
                var usable = new HashSet<string>();
                foreach (var m in s.Moves) usable.Add(m.Id);
                var graph = new List<FightingStyleDefinition> { s };
                if (s.IsHybrid)
                {
                    if (s.HybridOf.Count != 2 || s.HybridOf[0] == s.HybridOf[1])
                        report.Error($"Hybrid {s.Id}: must name exactly two different parent styles.");
                    foreach (var pid in s.HybridOf)
                    {
                        var parent = GetStyle(pid);
                        if (parent == null) { report.Error($"Hybrid {s.Id}: unknown parent style '{pid}'."); continue; }
                        if (parent.IsHybrid) report.Error($"Hybrid {s.Id}: parent '{pid}' is itself a hybrid.");
                        foreach (var m in parent.Moves) usable.Add(m.Id);
                        graph.Add(parent);
                    }
                    if (s.HybridOf.Count == 2 && !hybridPairs.Add(PairKey(s.HybridOf[0], s.HybridOf[1])))
                        report.Error($"Hybrid {s.Id}: another hybrid already uses {s.HybridOf[0]} + {s.HybridOf[1]}.");
                }
                else
                {
                    // A base style must stand on its own: every core slot filled.
                    foreach (var kv in new[] { ("Light", s.Light), ("Heavy", s.Heavy), ("Kick", s.Kick), ("Grapple", s.Grapple), ("Counter", s.Counter),
                                               ("Finisher", s.Finisher), ("ClinchStrike", s.ClinchStrike), ("ClinchThrow", s.ClinchThrow) })
                        if (string.IsNullOrEmpty(kv.Item2)) report.Error($"Style {s.Id}: base styles must fill the {kv.Item1} slot.");
                }
                foreach (var slot in new[] { s.Light, s.Heavy, s.Kick, s.Grapple, s.Counter, s.Finisher, s.GroundAttack, s.Taunt, s.ClinchStrike, s.ClinchThrow, s.EnvironmentalThrow, s.Reversal })
                    if (slot != null && !usable.Contains(slot)) report.Error($"Style {s.Id}: slot references unknown move '{slot}'.");
                var linkKeys = new HashSet<string>();
                foreach (var l in s.Links)
                {
                    if (!usable.Contains(l.From) || !usable.Contains(l.To)) { report.Error($"Style {s.Id}: link {l.From}->{l.To} references unknown move."); continue; }
                    if (!linkKeys.Add(l.From + "|" + l.Command)) report.Error($"Style {s.Id}: ambiguous link from {l.From} on {l.Command}.");
                    var from = FindMove(graph, l.From);
                    if (from != null && from.CancelWindow <= 0) report.Error($"Style {s.Id}: link from {l.From} can never fire (no cancel window).");
                }
                if (HasLinkCycle(graph)) report.Error($"Style {s.Id}: combo links contain a cycle (infinite combo risk).");
            }
            report.AddRange(moveIds.Errors);

            foreach (var f in _fighters.Values)
            {
                if (f.Styles == null || f.Styles.Count == 0 || f.Styles.Count > Moveset.MaxStyles)
                    report.Error($"Fighter {f.Id}: must have 1-{Moveset.MaxStyles} styles.");
                else foreach (var sid in f.Styles)
                {
                    var st = GetStyle(sid);
                    if (st == null) report.Error($"Fighter {f.Id}: unknown style '{sid}'.");
                    else if (st.IsHybrid) report.Error($"Fighter {f.Id}: list the two parent styles, not the hybrid '{sid}' (it applies automatically).");
                }
                foreach (var kv in f.Stats.All())
                    if (kv.Value < 1 || kv.Value > 100) report.Error($"Fighter {f.Id}: {kv.Key}={kv.Value} outside 1-100.");
                if (string.IsNullOrEmpty(f.DisplayName)) report.Error($"Fighter {f.Id}: missing display name.");
            }
        }

        private static void ValidateStance(FightingStyleDefinition s, ValidationReport report)
        {
            var st = s.Stance;
            if (st == null) return; // neutral
            bool Mul(float v) => v >= 0.5f && v <= 1.5f;
            if (!Mul(st.WalkSpeed) || !Mul(st.StrikeDamage) || !Mul(st.KickDamage) || !Mul(st.ThrowDamage) || !Mul(st.BlockChip)
                || !Mul(st.GuardDrain) || !Mul(st.DodgeDistance) || !Mul(st.DodgeCost) || !Mul(st.EscapeRate))
                report.Error($"Style {s.Id}: stance multipliers must stay within 0.5-1.5.");
            if (st.ParryWindowBonus < 0 || st.ParryWindowBonus > 6 || st.CounterWindowBonus < 0 || st.CounterWindowBonus > 6)
                report.Error($"Style {s.Id}: parry/counter window bonuses must be 0-6 ticks.");
            if (st.ClinchStrikesBonus < 0 || st.ClinchStrikesBonus > 3) report.Error($"Style {s.Id}: clinch strike bonus must be 0-3.");
            if (string.IsNullOrEmpty(st.IdleAnimation) || string.IsNullOrEmpty(st.GuardAnimation))
                report.Error($"Style {s.Id}: stance needs idle and guard animation keys.");
        }

        private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "+" + b : b + "+" + a;

        private static MoveDefinition FindMove(List<FightingStyleDefinition> styles, string id)
        {
            foreach (var st in styles) foreach (var m in st.Moves) if (m.Id == id) return m;
            return null;
        }

        private static bool HasLinkCycle(List<FightingStyleDefinition> styles)
        {
            var edges = new Dictionary<string, List<string>>();
            // The hybrid's links shadow its parents' for the same (move, command), exactly as Moveset.FindLink resolves them.
            var taken = new HashSet<string>();
            foreach (var s in styles)
                foreach (var l in s.Links)
                {
                    if (!taken.Add(l.From + "|" + l.Command)) continue;
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

        /// <summary>The hybrid unlocked by combining these two base styles, or null.</summary>
        public FightingStyleDefinition FindHybrid(string styleA, string styleB)
        {
            if (styleA == null || styleB == null || styleA == styleB) return null;
            string key = PairKey(styleA, styleB);
            foreach (var s in _styles.Values)
                if (s.IsHybrid && s.HybridOf.Count == 2 && PairKey(s.HybridOf[0], s.HybridOf[1]) == key) return s;
            return null;
        }

        /// <summary>Builds a fighter's moveset. When its first two styles form a hybrid, the hybrid applies on top.</summary>
        public Moveset BuildMoveset(FighterDefinition f) => BuildMoveset(f.Styles, true);

        public Moveset BuildMoveset(IList<string> styleIds, bool allowHybrid, ICollection<string> unlockedHybrids = null)
        {
            var styles = new List<FightingStyleDefinition>();
            foreach (var id in styleIds) styles.Add(GetStyle(id) ?? throw new ArgumentException($"Unknown style {id}"));
            FightingStyleDefinition hybrid = null;
            if (allowHybrid && styleIds.Count >= 2)
            {
                hybrid = FindHybrid(styleIds[0], styleIds[1]);
                if (hybrid != null && unlockedHybrids != null && !unlockedHybrids.Contains(hybrid.Id)) hybrid = null;
            }
            return Moveset.Build(styles, hybrid);
        }
    }
}
