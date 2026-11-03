using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Combat
{
    public enum AIStyle { Aggressive, Technical, Wrestler, Defensive, Showman }
    public enum AIDifficulty { Easy, Normal, Hard, Expert, Boss }

    public sealed class AIDifficultyParams
    {
        public int ReactionTicks;          // perception delay: the AI only sees the opponent as they were this many ticks ago
        public int DecisionIntervalTicks;  // how often offensive decisions are made
        public float DefenseSkill;         // chance to react defensively to a perceived attack
        public float CounterSkill;         // chance to choose a counter instead of a block (technical styles)
        public float ComboSkill;           // chance to continue a combo when a link is available
        public float EscapePressesPerSecond;
        public float MistakeRate;          // chance to ignore a decision entirely
        public float AdaptationStrength;   // how much the opponent model influences defense

        public static AIDifficultyParams For(AIDifficulty d)
        {
            switch (d)
            {
                case AIDifficulty.Easy:   return new AIDifficultyParams { ReactionTicks = 24, DecisionIntervalTicks = 40, DefenseSkill = 0.20f, CounterSkill = 0.05f, ComboSkill = 0.25f, EscapePressesPerSecond = 3, MistakeRate = 0.30f, AdaptationStrength = 0.10f };
                case AIDifficulty.Hard:   return new AIDifficultyParams { ReactionTicks = 10, DecisionIntervalTicks = 18, DefenseSkill = 0.60f, CounterSkill = 0.30f, ComboSkill = 0.80f, EscapePressesPerSecond = 7, MistakeRate = 0.08f, AdaptationStrength = 0.35f };
                case AIDifficulty.Expert: return new AIDifficultyParams { ReactionTicks = 8, DecisionIntervalTicks = 14, DefenseSkill = 0.72f, CounterSkill = 0.40f, ComboSkill = 0.90f, EscapePressesPerSecond = 9, MistakeRate = 0.04f, AdaptationStrength = 0.45f };
                case AIDifficulty.Boss:   return new AIDifficultyParams { ReactionTicks = 6,  DecisionIntervalTicks = 12, DefenseSkill = 0.80f, CounterSkill = 0.50f, ComboSkill = 0.95f, EscapePressesPerSecond = 10, MistakeRate = 0.02f, AdaptationStrength = 0.55f };
                default:                  return new AIDifficultyParams { ReactionTicks = 14, DecisionIntervalTicks = 26, DefenseSkill = 0.40f, CounterSkill = 0.15f, ComboSkill = 0.55f, EscapePressesPerSecond = 5, MistakeRate = 0.15f, AdaptationStrength = 0.25f };
            }
        }
    }

    /// <summary>What the AI is allowed to know about its opponent: publicly visible state only.</summary>
    public struct OpponentSnapshot
    {
        public int Tick;
        public CombatState State;
        public MoveKind? MoveKind;
        public bool InStartup;
        public bool InRecovery;
        public float MoveRange;
        public float X;
        public float HealthFraction;
    }

    /// <summary>
    /// Learns the opponent's habits from observed behaviour (exponentially decayed counts) so the AI
    /// can adapt to repetition — e.g. heavy-spam gets blocked more, grapple-spam gets dodged more.
    /// </summary>
    public sealed class OpponentModel
    {
        private readonly Dictionary<MoveKind, float> _openers = new Dictionary<MoveKind, float>();
        private float _total;
        public float BlockTendency { get; private set; }
        private const float Decay = 0.93f;

        public void ObserveOpener(MoveKind kind)
        {
            var keys = new List<MoveKind>(_openers.Keys);
            foreach (var k in keys) _openers[k] *= Decay;
            _total = _total * Decay + 1f;
            _openers.TryGetValue(kind, out var v);
            _openers[kind] = v + 1f;
        }

        public void ObserveBlocking(bool blocking) => BlockTendency = BlockTendency * 0.995f + (blocking ? 0.005f : 0f);

        /// <summary>Share (0..1) of recent openers that were this kind.</summary>
        public float Frequency(MoveKind kind) => _total <= 0 ? 0 : (_openers.TryGetValue(kind, out var v) ? v / _total : 0);
    }

    /// <summary>
    /// Rule-based fighter AI. It reads its own state directly but perceives the opponent only through
    /// delayed snapshots, never through the opponent's input stream — it cannot react faster than its
    /// difficulty's reaction time and gains no hidden stat or damage bonuses.
    /// </summary>
    public sealed class FighterAI
    {
        private readonly FighterRuntime _self;
        private readonly AIStyle _style;
        private readonly AIDifficultyParams _p;
        private readonly IRandom _rng;
        private readonly Queue<OpponentSnapshot> _perception = new Queue<OpponentSnapshot>();
        private CombatState _lastSeenState = CombatState.Idle;
        private int _blockHold;
        private int _nextDecisionTick;
        private int _lastDefendedMoveTick = -1000;

        public OpponentModel Model { get; } = new OpponentModel();
        public AIStyle Style => _style;
        public AIDifficultyParams Params => _p;

        public FighterAI(FighterRuntime self, AIStyle style, AIDifficulty difficulty, IRandom rng, AIDifficultyParams overrideParams = null)
        {
            _self = self; _style = style; _p = overrideParams ?? AIDifficultyParams.For(difficulty); _rng = rng;
        }

        public FighterInput Think(CombatSimulation sim)
        {
            var opp = sim.OpponentOf(_self);
            _perception.Enqueue(Snapshot(sim, opp));
            OpponentSnapshot seen = default; bool hasSeen = false;
            while (_perception.Count > 0 && sim.Tick - _perception.Peek().Tick >= _p.ReactionTicks) { seen = _perception.Dequeue(); hasSeen = true; }
            if (hasSeen) Learn(seen);
            else seen = new OpponentSnapshot { State = CombatState.Idle, X = opp.X, HealthFraction = 1f };

            float dist = Math.Abs(_self.X - seen.X);
            switch (_self.State)
            {
                case CombatState.Defeated: case CombatState.FinisherVictim: case CombatState.GettingUp:
                case CombatState.Throwing: case CombatState.Finisher: case CombatState.Countering:
                case CombatState.Dodging: case CombatState.BlockStun: case CombatState.Stunned:
                    return FighterInput.None;
                case CombatState.Grappled: return Escape(sim, opp);
                case CombatState.Grappling: return ClinchChoice(sim);
                case CombatState.HitStun:
                    if (_self.ComboCounter >= sim.Config.BreakawayMinCombo && _self.Momentum >= sim.Config.BreakawayMomentumCost && _rng.Chance(_p.DefenseSkill * 0.3f))
                        return FighterInput.Press(CombatCommand.Dodge);
                    return FighterInput.None;
                case CombatState.KnockedDown:
                    return _self.StateTicks >= sim.Config.QuickRiseAfterTicks && _rng.Chance(_p.DefenseSkill * 0.1f)
                        ? FighterInput.Press(CombatCommand.Dodge) : FighterInput.None;
                case CombatState.Attacking: case CombatState.Combo:
                    return ContinueCombo();
            }

            // Neutral: Idle / Moving / Blocking
            if (sim.CanFinish(_self) == FinisherDenial.None) return FighterInput.Press(CombatCommand.Finisher);

            var defense = Defend(seen, dist);
            if (defense.HasValue) return defense.Value;
            if (_blockHold > 0) { _blockHold--; return FighterInput.Hold(); }

            var read = Anticipate(seen, dist);
            if (read.HasValue) return read.Value;

            if (sim.Tick < _nextDecisionTick) return Approach(dist, sim);
            _nextDecisionTick = sim.Tick + _p.DecisionIntervalTicks + _rng.Range(0, _p.DecisionIntervalTicks / 2 + 1);
            if (_rng.Chance(_p.MistakeRate)) return FighterInput.None;

            // Punish a perceived whiff, in character: wrestlers grab, everyone else swings big.
            var punish = _style == AIStyle.Wrestler ? CombatCommand.Grapple : CombatCommand.Heavy;
            var punishMove = _self.Moves.EntryFor(punish);
            if (seen.InRecovery && dist <= ReachOf(punishMove) && _self.Stamina >= Cost(punishMove))
                return FighterInput.Press(punish);

            // Ground pressure.
            if (seen.State == CombatState.KnockedDown && _self.Moves.GroundAttack != null && dist <= ReachOf(_self.Moves.GroundAttack))
                return FighterInput.Press(CombatCommand.Heavy);

            if (_self.Stamina < _self.MaxStamina * 0.25f) return FighterInput.Walk(-0.6f); // recover

            return Offense(dist, sim);
        }

        private OpponentSnapshot Snapshot(CombatSimulation sim, FighterRuntime opp) => new OpponentSnapshot
        {
            Tick = sim.Tick,
            State = opp.State,
            MoveKind = opp.CurrentMove?.Kind,
            InStartup = opp.InMoveStartup,
            InRecovery = opp.InMoveRecovery && !opp.MoveConnected,
            MoveRange = opp.CurrentMove?.Range ?? 0f,
            X = opp.X,
            HealthFraction = opp.HealthFraction
        };

        private void Learn(OpponentSnapshot s)
        {
            bool startedMove = s.State == CombatState.Attacking && _lastSeenState != CombatState.Attacking && _lastSeenState != CombatState.Combo;
            if (startedMove && s.MoveKind.HasValue) Model.ObserveOpener(s.MoveKind.Value);
            Model.ObserveBlocking(s.State == CombatState.Blocking);
            _lastSeenState = s.State;
        }

        private FighterInput? Defend(OpponentSnapshot seen, float dist)
        {
            if (!seen.InStartup || !seen.MoveKind.HasValue) return null;
            if (seen.Tick == _lastDefendedMoveTick) return null;
            if (dist > seen.MoveRange + 0.8f) return null;
            _lastDefendedMoveTick = seen.Tick;

            var kind = seen.MoveKind.Value;
            float adapt = Model.Frequency(kind) * _p.AdaptationStrength;
            if (!_rng.Chance(Math.Min(0.95f, _p.DefenseSkill + adapt))) return null;

            if (kind == MoveKind.Grapple)
                return _self.Stamina >= 12f ? FighterInput.Press(CombatCommand.Dodge) : FighterInput.Press(CombatCommand.Light);

            bool technical = _style == AIStyle.Technical || _style == AIStyle.Defensive;
            if (technical && _self.Moves.Counter != null && _rng.Chance(_p.CounterSkill + adapt))
                return FighterInput.Press(CombatCommand.Counter);
            if (_style == AIStyle.Showman && _rng.Chance(0.3f) && _self.Stamina >= 12f)
                return FighterInput.Press(CombatCommand.Dodge);
            _blockHold = 18 + _rng.Range(0, 10);
            return FighterInput.Hold();
        }

        /// <summary>
        /// Reads habits, not inputs: if the opponent keeps opening with the same kind of attack, guard
        /// (or sidestep grapples) pre-emptively while they are in neutral and in range.
        /// </summary>
        private FighterInput? Anticipate(OpponentSnapshot seen, float dist)
        {
            if (dist > 1.9f || (seen.State != CombatState.Idle && seen.State != CombatState.Moving)) return null;
            float strike = Math.Max(Model.Frequency(MoveKind.Heavy), Math.Max(Model.Frequency(MoveKind.Light), Model.Frequency(MoveKind.Kick)));
            float grab = Model.Frequency(MoveKind.Grapple);
            float perTick = _p.AdaptationStrength * 0.08f;
            if (grab > 0.5f && _self.Stamina > 20f && _rng.Chance(grab * perTick)) return FighterInput.Press(CombatCommand.Dodge);
            if (strike > 0.5f && _rng.Chance(strike * perTick)) { _blockHold = 24; return FighterInput.Hold(); }
            return null;
        }

        private FighterInput Offense(float dist, CombatSimulation sim)
        {
            float preferred = _style == AIStyle.Wrestler ? 0.9f : 1.1f;
            if (dist > preferred + 0.3f) return FighterInput.Walk(1f);

            float light, heavy, kick, grapple, wait, taunt = 0f;
            switch (_style)
            {
                case AIStyle.Aggressive: light = .40f; heavy = .30f; kick = .20f; grapple = .10f; wait = 0f; break;
                case AIStyle.Technical:  light = .45f; heavy = .15f; kick = .25f; grapple = .05f; wait = .10f; break;
                case AIStyle.Wrestler:   light = .20f; heavy = .20f; kick = .10f; grapple = .50f; wait = 0f; break;
                case AIStyle.Defensive:  light = .30f; heavy = .15f; kick = .10f; grapple = .05f; wait = .40f; break;
                default:                 light = .30f; heavy = .25f; kick = .25f; grapple = .05f; wait = 0f; taunt = .15f; break;
            }
            // Adapt: an opponent who turtles gets grappled (grapples beat blocks).
            float grappleAffinity = _style == AIStyle.Wrestler ? 1f : _style == AIStyle.Aggressive ? 0.5f : 0.25f;
            grapple += Math.Min(0.5f, Model.BlockTendency * 2f) * _p.AdaptationStrength * 2f * grappleAffinity;

            float total = light + heavy + kick + grapple + wait + taunt;
            float r = _rng.Value() * total;
            if ((r -= light) < 0) return FighterInput.Press(CombatCommand.Light);
            if ((r -= heavy) < 0) return FighterInput.Press(CombatCommand.Heavy);
            if ((r -= kick) < 0) return FighterInput.Press(CombatCommand.Kick);
            if ((r -= grapple) < 0) return FighterInput.Press(CombatCommand.Grapple);
            if ((r -= taunt) < 0 && dist > 1.5f) return FighterInput.Press(CombatCommand.Taunt);
            _blockHold = 20;
            return FighterInput.Hold();
        }

        private FighterInput Approach(float dist, CombatSimulation _)
        {
            if (_style == AIStyle.Defensive && dist < 2.5f) return FighterInput.None;
            return dist > 1.4f ? FighterInput.Walk(0.8f) : FighterInput.None;
        }

        private FighterInput ContinueCombo()
        {
            if (!_self.MoveConnected || !_self.InMoveRecovery || _self.CurrentMove == null) return FighterInput.None;
            var options = new List<CombatCommand>();
            foreach (var l in _self.Moves.Links) if (l.From == _self.CurrentMove.Id) options.Add(l.Command);
            if (options.Count == 0 || !_rng.Chance(_p.ComboSkill)) return FighterInput.None;
            return FighterInput.Press(options[_rng.Range(0, options.Count)]);
        }

        private FighterInput Escape(CombatSimulation sim, FighterRuntime opp)
        {
            if (opp.State == CombatState.Grappling && opp.ClinchTicks <= sim.Config.ReversalWindowTicks
                && _self.Momentum >= sim.Config.ReversalMomentumCost && _rng.Chance(_p.CounterSkill * 0.15f))
                return FighterInput.Press(CombatCommand.Counter);
            return _rng.Chance(_p.EscapePressesPerSecond / sim.Config.TickRate) ? FighterInput.Press(CombatCommand.Grapple) : FighterInput.None;
        }

        private FighterInput ClinchChoice(CombatSimulation sim)
        {
            if (!_rng.Chance(0.08f)) return FighterInput.None;
            var opp = sim.OpponentOf(_self);
            bool wall = sim.Config.ArenaHalfWidth - Math.Abs(opp.X) <= sim.Config.WallThrowDistance;
            if (wall && _self.Moves.EnvironmentalThrow != null) return FighterInput.Press(CombatCommand.Grapple);
            if (_self.ClinchTicks < 40 && _rng.Chance(0.6f)) return FighterInput.Press(CombatCommand.Light);
            return FighterInput.Press(CombatCommand.Heavy);
        }

        private float ReachOf(MoveDefinition m) => m == null ? 0f : m.Range + 0.35f;
        private static float Cost(MoveDefinition m) => m?.StaminaCost ?? float.MaxValue;
    }
}
