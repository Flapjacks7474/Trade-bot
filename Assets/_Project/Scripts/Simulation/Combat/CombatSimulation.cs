using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Combat
{
    /// <summary>
    /// Deterministic fixed-tick 1v1 fight simulation along a single fight line.
    /// Rock-paper-scissors core: strikes beat grapples, grapples beat blocks and counters,
    /// blocks/counters beat strikes. Dodges beat everything during invulnerability but cost stamina.
    /// Presentation (animation, camera, VFX, audio) consumes <see cref="Events"/> and never mutates state.
    /// </summary>
    public sealed class CombatSimulation
    {
        public readonly CombatConfig Config;
        public readonly string FightId;
        public FighterRuntime A { get; }
        public FighterRuntime B { get; }
        public int Tick { get; private set; }
        public FightResult Result { get; private set; }
        public bool IsOver => Result != null;

        private readonly List<CombatEvent> _events = new List<CombatEvent>();
        /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
        public IReadOnlyList<CombatEvent> Events => _events;

        public CombatSimulation(CombatConfig config, FighterRuntime a, FighterRuntime b, string fightId)
        {
            Config = config ?? new CombatConfig();
            A = a ?? throw new ArgumentNullException(nameof(a));
            B = b ?? throw new ArgumentNullException(nameof(b));
            FightId = fightId;
            UpdateFacing();
        }

        public FighterRuntime OpponentOf(FighterRuntime f) => f == A ? B : A;
        public float Distance => Math.Abs(A.X - B.X);

        public void Step(FighterInput inputA, FighterInput inputB)
        {
            _events.Clear();
            if (IsOver) return;
            Tick++;

            Buffer(A, inputA); Buffer(B, inputB);
            UpdateFighter(A, B, inputA);
            UpdateFighter(B, A, inputB);
            ResolveHits();
            ResolvePositions();
            if (!IsOver && Tick >= Config.RoundTicks) EndOnTime();
        }

        // ---------------------------------------------------------------- input

        private void Buffer(FighterRuntime f, FighterInput input)
        {
            if (input.Command != CombatCommand.None) { f.Buffered = input.Command; f.BufferedAge = 0; }
            else if (f.Buffered != CombatCommand.None && ++f.BufferedAge > Config.InputBufferTicks) f.Buffered = CombatCommand.None;
            if (input.BlockHeld && !f.PrevBlockHeld) f.BlockStartTick = Tick;
            f.PrevBlockHeld = input.BlockHeld;
        }

        private static CombatCommand Take(FighterRuntime f)
        {
            var c = f.Buffered; f.Buffered = CombatCommand.None; f.BufferedAge = 0; return c;
        }

        // ---------------------------------------------------------------- per-fighter update

        private void UpdateFighter(FighterRuntime f, FighterRuntime o, FighterInput input)
        {
            if (f.State == CombatState.Defeated || IsOver) return;
            f.StateTicks++;

            switch (f.State)
            {
                case CombatState.Idle:
                case CombatState.Moving:
                case CombatState.Blocking:
                    Neutral(f, o, input); break;
                case CombatState.Attacking:
                case CombatState.Combo:
                case CombatState.Throwing:
                case CombatState.Finisher:
                    AdvanceMove(f, o); break;
                case CombatState.Countering:
                    if (f.StateTicks >= Config.CounterStanceTicks) SetState(f, CombatState.Idle);
                    break;
                case CombatState.HitStun:
                    if (TryBreakaway(f, o)) break;
                    if (f.StateTicks >= f.StateDuration) SetState(f, CombatState.Idle);
                    break;
                case CombatState.BlockStun:
                case CombatState.Stunned:
                    if (f.StateTicks >= f.StateDuration) SetState(f, CombatState.Idle);
                    break;
                case CombatState.Dodging:
                    if (f.StateTicks <= 12) f.X -= f.Facing * Config.DodgeDistance * f.Stance.DodgeDistance / 12f;
                    if (f.StateTicks >= f.StateDuration) SetState(f, CombatState.Idle);
                    break;
                case CombatState.KnockedDown:
                    if (f.Buffered == CombatCommand.Dodge && f.StateTicks >= Config.QuickRiseAfterTicks)
                    {
                        Take(f);
                        Emit(CombatEventType.QuickRise, f, null, null, 0);
                        SetState(f, CombatState.GettingUp, Config.GetUpTicks / 2);
                    }
                    else if (f.StateTicks >= f.StateDuration) SetState(f, CombatState.GettingUp, Config.GetUpTicks);
                    break;
                case CombatState.GettingUp:
                    if (f.StateTicks >= f.StateDuration) { SetState(f, CombatState.Idle); Emit(CombatEventType.GotUp, f, null, null, 0); }
                    break;
                case CombatState.Grappling:
                    Clinch(f, o); break;
                case CombatState.Grappled:
                    ClinchDefender(f, o); break;
                case CombatState.FinisherVictim:
                    if (o.State != CombatState.Finisher) SetState(f, CombatState.Idle); // safety: attacker interrupted
                    break;
            }
            RegenStamina(f);
        }

        private void Neutral(FighterRuntime f, FighterRuntime o, FighterInput input)
        {
            if (f.Buffered != CombatCommand.None && TryCommand(f, o, Take(f))) return;

            if (input.BlockHeld)
            {
                if (f.State != CombatState.Blocking) SetState(f, CombatState.Blocking);
                return;
            }
            if (Math.Abs(input.Move) > 0.1f)
            {
                f.X += f.Facing * MathUtil.Clamp(input.Move, -1f, 1f) * f.WalkSpeed * Config.Dt;
                if (f.State != CombatState.Moving) SetState(f, CombatState.Moving);
            }
            else if (f.State != CombatState.Idle) SetState(f, CombatState.Idle);
        }

        private bool TryCommand(FighterRuntime f, FighterRuntime o, CombatCommand cmd)
        {
            switch (cmd)
            {
                case CombatCommand.Finisher:
                {
                    var denial = CanFinish(f);
                    if (denial == FinisherDenial.None) StartFinisher(f, o);
                    else Emit(CombatEventType.FinisherDenied, f, o, null, 0, denial.ToString());
                    return true;
                }
                case CombatCommand.Dodge:
                {
                    float dodgeCost = Config.DodgeStaminaCost * f.Stance.DodgeCost;
                    if (f.Stamina < dodgeCost) { Emit(CombatEventType.NotEnoughStamina, f, null, "dodge", 0); return true; }
                    f.Stamina -= dodgeCost;
                    SetState(f, CombatState.Dodging, Config.DodgeTicks);
                    return true;
                }
                case CombatCommand.Counter:
                    if (f.Moves.Counter == null) return false;
                    if (f.Stamina < Config.CounterStaminaCost) { Emit(CombatEventType.NotEnoughStamina, f, null, "counter", 0); return true; }
                    f.Stamina -= Config.CounterStaminaCost;
                    SetState(f, CombatState.Countering, Config.CounterStanceTicks);
                    return true;
                case CombatCommand.Heavy when o.State == CombatState.KnockedDown && f.Moves.GroundAttack != null
                                              && Distance <= f.Moves.GroundAttack.Range + Config.BodyReach:
                    StartMove(f, f.Moves.GroundAttack, CombatState.Attacking);
                    return true;
                default:
                {
                    var move = f.Moves.EntryFor(cmd);
                    if (move == null) return false;
                    StartMove(f, move, CombatState.Attacking);
                    return true;
                }
            }
        }

        private bool StartMove(FighterRuntime f, MoveDefinition move, CombatState state)
        {
            if (f.Stamina < move.StaminaCost)
            {
                Emit(CombatEventType.NotEnoughStamina, f, null, move.Id, move.StaminaCost);
                return false;
            }
            f.Stamina -= move.StaminaCost;
            SetState(f, state);
            f.CurrentMove = move;
            f.MoveTick = 0;
            f.MoveStartup = f.AdjustStartup(move.Startup);
            f.MoveConnected = false;
            f.MoveWasDodged = false;
            Emit(CombatEventType.MoveStarted, f, null, move.Id, 0, move.Kind.ToString());
            return true;
        }

        private void AdvanceMove(FighterRuntime f, FighterRuntime o)
        {
            var m = f.CurrentMove;
            if (m == null) { SetState(f, CombatState.Idle); return; }
            f.MoveTick++;

            if (!f.MoveConnected && f.MoveTick >= f.MoveStartup)
            {
                if (f.State == CombatState.Throwing) { ApplyThrow(f, o, m); return; }
                if (f.State == CombatState.Finisher) { ApplyFinisher(f, o, m); return; }
            }

            int recoveryStart = f.MoveStartup + m.Active;
            if (f.MoveConnected && f.MoveTick >= recoveryStart && f.MoveTick - recoveryStart < m.CancelWindow
                && f.Buffered != CombatCommand.None && f.State != CombatState.Throwing && f.State != CombatState.Finisher)
            {
                var next = f.Moves.FindLink(m.Id, f.Buffered);
                if (next != null)
                {
                    Take(f);
                    StartMove(f, next, CombatState.Combo);
                    return;
                }
            }

            if (f.MoveTick >= recoveryStart + m.Recovery)
            {
                if (m.Kind == MoveKind.Taunt)
                {
                    f.Momentum = Math.Min(Config.MomentumMax, f.Momentum + Config.TauntMomentum);
                    Emit(CombatEventType.TauntCompleted, f, null, m.Id, Config.TauntMomentum);
                }
                SetState(f, CombatState.Idle);
            }
        }

        private bool TryBreakaway(FighterRuntime f, FighterRuntime o)
        {
            if (f.Buffered != CombatCommand.Dodge || f.ComboCounter < Config.BreakawayMinCombo) return false;
            if (f.Momentum < Config.BreakawayMomentumCost || f.Stamina < Config.BreakawayStaminaCost) return false;
            Take(f);
            f.Momentum -= Config.BreakawayMomentumCost;
            f.Stamina -= Config.BreakawayStaminaCost;
            SetState(f, CombatState.Dodging, Config.DodgeTicks);
            f.StateTicks = Config.DodgeInvulnStart; // invulnerable immediately
            if (o.State == CombatState.Attacking || o.State == CombatState.Combo) SetState(o, CombatState.BlockStun, 24);
            Push(o, f, 0.8f);
            Emit(CombatEventType.Breakaway, f, o, null, 0);
            return true;
        }

        // ---------------------------------------------------------------- clinch

        private void Clinch(FighterRuntime a, FighterRuntime d)
        {
            a.ClinchTicks++;
            if (a.ClinchCooldown > 0) a.ClinchCooldown--;
            if (d.State != CombatState.Grappled) { SetState(a, CombatState.Idle); return; }
            if (a.ClinchTicks >= Config.ClinchMaxTicks) { ReleaseClinch(a, d); return; }

            var cmd = a.Buffered;
            if (cmd == CombatCommand.Light && a.ClinchCooldown == 0 && a.ClinchStrikes < Config.ClinchStrikesMax + a.Stance.ClinchStrikesBonus && a.Moves.ClinchStrike != null)
            {
                Take(a);
                var m = a.Moves.ClinchStrike;
                if (a.Stamina < m.StaminaCost) { Emit(CombatEventType.NotEnoughStamina, a, null, m.Id, m.StaminaCost); return; }
                a.Stamina -= m.StaminaCost;
                float dmg = ScaledDamage(a, d, m);
                d.Health -= dmg; d.ComboCounter++;
                AddMomentum(a, m.MomentumGain);
                a.ClinchStrikes++; a.ClinchCooldown = Config.ClinchStrikeCooldown;
                Emit(CombatEventType.ClinchStrike, a, d, m.Id, dmg);
                if (d.Health <= 0) { SetState(a, CombatState.Idle); Defeat(d, a, FightEndMethod.Knockout); }
            }
            else if (cmd == CombatCommand.Heavy || cmd == CombatCommand.Grapple)
            {
                Take(a);
                var throwMove = cmd == CombatCommand.Grapple && NearWall(d) && a.Moves.EnvironmentalThrow != null
                    ? a.Moves.EnvironmentalThrow : a.Moves.ClinchThrow;
                if (throwMove != null) StartMove(a, throwMove, CombatState.Throwing);
            }
        }

        private void ClinchDefender(FighterRuntime d, FighterRuntime a)
        {
            if (a.State != CombatState.Grappling && a.State != CombatState.Throwing) { SetState(d, CombatState.Idle); return; }
            if (a.State == CombatState.Throwing) return; // committed

            var cmd = Take(d);
            if (cmd == CombatCommand.Counter && a.ClinchTicks <= Config.ReversalWindowTicks
                && d.Momentum >= Config.ReversalMomentumCost && d.Moves.Reversal != null && d.Stamina >= d.Moves.Reversal.StaminaCost)
            {
                d.Momentum -= Config.ReversalMomentumCost;
                SetState(a, CombatState.Grappled);
                StartMove(d, d.Moves.Reversal, CombatState.Throwing);
                Emit(CombatEventType.Reversal, d, a, d.Moves.Reversal.Id, 0);
            }
            else if (cmd == CombatCommand.Grapple || cmd == CombatCommand.Dodge)
            {
                float gain = 16f + d.Stats.Technique * 0.2f + d.Stats.Strength * 0.1f - a.Stats.Strength * 0.08f;
                d.EscapeProgress += Math.Max(6f, gain) * d.Stance.EscapeRate;
                if (d.EscapeProgress >= Config.EscapeThreshold)
                {
                    d.EscapeProgress = 0;
                    SetState(a, CombatState.HitStun, 20);
                    SetState(d, CombatState.Idle);
                    Push(a, d, 0.5f); Push(d, a, 0.5f);
                    Emit(CombatEventType.GrappleEscaped, d, a, null, 0);
                }
            }
        }

        private void ReleaseClinch(FighterRuntime a, FighterRuntime d)
        {
            SetState(a, CombatState.Idle); SetState(d, CombatState.Idle);
            Push(d, a, 0.6f);
            Emit(CombatEventType.ClinchReleased, a, d, null, 0);
        }

        private bool NearWall(FighterRuntime f) => Config.ArenaHalfWidth - Math.Abs(f.X) <= Config.WallThrowDistance;

        // ---------------------------------------------------------------- hit resolution

        private enum Attempt { None, Strike, Grapple }

        private void ResolveHits()
        {
            var aTry = GetAttempt(A, B);
            var bTry = GetAttempt(B, A);
            if (aTry == Attempt.Grapple && bTry == Attempt.Grapple)
            {
                SetState(A, CombatState.BlockStun, 14); SetState(B, CombatState.BlockStun, 14);
                Push(A, B, 0.4f); Push(B, A, 0.4f);
                Emit(CombatEventType.Clash, A, B, null, 0);
                return;
            }
            // Strikes beat grapples that become active on the same tick.
            if (aTry == Attempt.Grapple && bTry == Attempt.Strike) aTry = Attempt.None;
            if (bTry == Attempt.Grapple && aTry == Attempt.Strike) bTry = Attempt.None;

            // Capture both moves before applying so simultaneous strikes trade.
            var aMove = A.CurrentMove; var bMove = B.CurrentMove;
            if (aTry == Attempt.Strike) ApplyStrike(A, B, aMove);
            else if (aTry == Attempt.Grapple) ApplyGrapple(A, B);
            if (IsOver) return;
            if (bTry == Attempt.Strike) ApplyStrike(B, A, bMove);
            else if (bTry == Attempt.Grapple && B.State == CombatState.Attacking) ApplyGrapple(B, A);
        }

        private Attempt GetAttempt(FighterRuntime f, FighterRuntime o)
        {
            if (f.State != CombatState.Attacking && f.State != CombatState.Combo) return Attempt.None;
            var m = f.CurrentMove;
            if (m == null || f.MoveConnected || !f.InMovePhaseActive) return Attempt.None;
            if (m.Kind == MoveKind.Taunt || m.Kind == MoveKind.Throw || m.Kind == MoveKind.EnvironmentalThrow
                || m.Kind == MoveKind.Finisher || m.Kind == MoveKind.ClinchStrike) return Attempt.None;
            if (Distance > m.Range + Config.BodyReach) return Attempt.None;

            switch (o.State)
            {
                case CombatState.Defeated: case CombatState.GettingUp: case CombatState.FinisherVictim:
                case CombatState.Grappled: case CombatState.Grappling: case CombatState.Throwing: case CombatState.Finisher:
                    return Attempt.None;
            }
            if (o.IsDodgeInvulnerable(Config))
            {
                if (!f.MoveWasDodged) { f.MoveWasDodged = true; Emit(CombatEventType.Dodged, o, f, m.Id, 0); }
                return Attempt.None;
            }
            bool down = o.State == CombatState.KnockedDown;
            if (m.Kind == MoveKind.GroundAttack) return down ? Attempt.Strike : Attempt.None;
            if (down) return Attempt.None;
            return m.Kind == MoveKind.Grapple ? Attempt.Grapple : Attempt.Strike;
        }

        private void ApplyStrike(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            a.MoveConnected = true;
            bool parry = d.State == CombatState.Blocking && Tick - d.BlockStartTick <= d.ParryWindow;
            bool stanceCatch = d.State == CombatState.Countering && d.StateTicks <= d.CounterWindow;
            if (m.Kind != MoveKind.GroundAttack && (parry || stanceCatch) && d.Moves.Counter != null)
            {
                ApplyCounter(d, a, parry ? "parry" : "stance");
                return;
            }
            if (d.State == CombatState.Blocking || d.State == CombatState.BlockStun) { ApplyBlock(a, d, m); return; }
            ApplyHit(a, d, m);
        }

        private void ApplyCounter(FighterRuntime c, FighterRuntime v, string how)
        {
            var cm = c.Moves.Counter;
            float dmg = ScaledDamage(c, v, cm);
            v.Health -= dmg; v.ComboCounter++;
            AddMomentum(c, cm.MomentumGain);
            c.CountersLanded++;
            Emit(CombatEventType.Countered, c, v, cm.Id, dmg, how);
            if (v.Health <= 0) { Defeat(v, c, FightEndMethod.Knockout); return; }
            SetState(v, CombatState.HitStun, cm.HitStun);

            // Place the counter-attacker at the start of the counter move's recovery so its combo links open.
            SetState(c, CombatState.Attacking);
            c.CurrentMove = cm;
            c.MoveStartup = c.AdjustStartup(cm.Startup);
            c.MoveTick = c.MoveStartup + cm.Active;
            c.MoveConnected = true;
            c.MoveWasDodged = false;
        }

        private void ApplyBlock(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            float chip = m.Damage * Config.ChipDamageFraction * a.StrengthMultiplier * a.Stance.DamageFor(m.Kind) * d.Stance.BlockChip;
            d.Health = Math.Max(1f, d.Health - chip);
            d.Stamina -= m.StaminaDamage * d.Stance.GuardDrain;
            AddMomentum(a, m.MomentumGain * 0.3f);
            Push(d, a, m.Pushback * 0.7f);
            if (d.Stamina <= 0)
            {
                d.Stamina = d.MaxStamina * 0.25f;
                SetState(d, CombatState.Stunned, Config.GuardBreakStunTicks);
                Emit(CombatEventType.GuardBreak, a, d, m.Id, chip);
                return;
            }
            SetState(d, CombatState.BlockStun, m.BlockStun);
            Emit(CombatEventType.Blocked, a, d, m.Id, chip);
        }

        private void ApplyHit(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            float dmg = ScaledDamage(a, d, m);
            float stunScale = Math.Max(Config.MinHitStunScale, 1f - Config.HitStunDecay * d.ComboCounter);
            d.Health -= dmg;
            d.ComboCounter++;
            AddMomentum(a, m.MomentumGain);
            AddMomentum(d, m.MomentumGain * 0.25f); // comeback meter
            Emit(CombatEventType.Hit, a, d, m.Id, dmg, "combo " + d.ComboCounter);
            Push(d, a, m.Pushback);
            if (d.Health <= 0) { Defeat(d, a, FightEndMethod.Knockout); return; }
            if (m.Knockdown) { Knockdown(d); return; }
            SetState(d, CombatState.HitStun, Math.Max(4, MathUtil.RoundToInt(m.HitStun * stunScale)));
        }

        private void ApplyGrapple(FighterRuntime a, FighterRuntime d)
        {
            a.MoveConnected = true;
            SetState(a, CombatState.Grappling);
            a.ClinchTicks = 0; a.ClinchStrikes = 0; a.ClinchCooldown = 10;
            SetState(d, CombatState.Grappled);
            d.EscapeProgress = 0;
            d.X = MathUtil.Clamp(a.X + a.Facing * Config.MinSeparation, -Config.ArenaHalfWidth, Config.ArenaHalfWidth);
            Emit(CombatEventType.GrappleStarted, a, d, null, 0);
        }

        private void ApplyThrow(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            a.MoveConnected = true;
            bool env = m.Kind == MoveKind.EnvironmentalThrow;
            float dmg = ScaledDamage(a, d, m);
            d.Health -= dmg; d.ComboCounter++;
            AddMomentum(a, m.MomentumGain);
            a.ThrowsLanded++;
            Emit(CombatEventType.Thrown, a, d, m.Id, dmg, env ? "environment" : "throw");
            d.X = env ? Math.Sign(d.X == 0 ? a.Facing : d.X) * Config.ArenaHalfWidth
                      : MathUtil.Clamp(a.X + a.Facing * 1.4f, -Config.ArenaHalfWidth, Config.ArenaHalfWidth);
            if (d.Health <= 0) { Defeat(d, a, FightEndMethod.Knockout); return; }
            Knockdown(d);
        }

        private void Knockdown(FighterRuntime d)
        {
            int ticks = Math.Max(30, MathUtil.RoundToInt(Config.KnockdownTicks * (1.1f - d.Stats.Durability / 500f)));
            SetState(d, CombatState.KnockedDown, ticks);
            Emit(CombatEventType.KnockedDown, d, null, null, 0);
        }

        // ---------------------------------------------------------------- finisher

        public FinisherDenial CanFinish(FighterRuntime f)
        {
            var o = OpponentOf(f);
            if (f.Moves.Finisher == null) return FinisherDenial.NoFinisherMove;
            if (f.State != CombatState.Idle && f.State != CombatState.Moving && f.State != CombatState.Blocking) return FinisherDenial.AttackerBusy;
            if (f.Momentum < Config.FinisherMomentum) return FinisherDenial.NotEnoughMomentum;
            switch (o.State)
            {
                case CombatState.Idle: case CombatState.Moving: case CombatState.Blocking: case CombatState.BlockStun:
                case CombatState.HitStun: case CombatState.Stunned: case CombatState.Countering:
                    break;
                default: return FinisherDenial.OpponentStateInvalid;
            }
            if (!(o.HealthFraction <= Config.FinisherHealthFraction || o.State == CombatState.Stunned)) return FinisherDenial.OpponentNotVulnerable;
            if (Distance > Config.FinisherRange) return FinisherDenial.OutOfRange;
            return FinisherDenial.None;
        }

        private void StartFinisher(FighterRuntime f, FighterRuntime o)
        {
            f.Momentum = 0;
            var m = f.Moves.Finisher;
            f.Stamina += m.StaminaCost; // finishers are paid for with momentum, never blocked by stamina
            StartMove(f, m, CombatState.Finisher);
            SetState(o, CombatState.FinisherVictim);
            Emit(CombatEventType.FinisherStarted, f, o, m.Id, 0);
        }

        private void ApplyFinisher(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            a.MoveConnected = true;
            float dmg = m.Damage * a.StrengthMultiplier * d.DurabilityMultiplier;
            d.Health -= dmg;
            Emit(CombatEventType.Hit, a, d, m.Id, dmg, "finisher");
            if (d.Health <= 0) { Defeat(d, a, FightEndMethod.Finisher); return; }
            Knockdown(d);
        }

        // ---------------------------------------------------------------- helpers

        private float ScaledDamage(FighterRuntime a, FighterRuntime d, MoveDefinition m)
        {
            float scale = Math.Max(Config.MinDamageScale, 1f - Config.ComboDamageDecay * d.ComboCounter);
            return m.Damage * a.StrengthMultiplier * d.DurabilityMultiplier * scale * a.Stance.DamageFor(m.Kind);
        }

        private void AddMomentum(FighterRuntime f, float amount) => f.Momentum = MathUtil.Clamp(f.Momentum + amount, 0f, Config.MomentumMax);

        private void SetState(FighterRuntime f, CombatState s, int duration = 0)
        {
            bool moveState = s == CombatState.Attacking || s == CombatState.Combo || s == CombatState.Throwing || s == CombatState.Finisher;
            if (!moveState) f.CurrentMove = null;
            f.State = s; f.StateTicks = 0; f.StateDuration = duration;
            if (s == CombatState.Idle || s == CombatState.Moving || s == CombatState.Blocking || s == CombatState.GettingUp) f.ComboCounter = 0;
        }

        private void RegenStamina(FighterRuntime f)
        {
            float rate;
            switch (f.State)
            {
                case CombatState.Idle: case CombatState.Moving: rate = 1f; break;
                case CombatState.Blocking: rate = 0.25f; break;
                case CombatState.KnockedDown: case CombatState.Grappled: rate = 0.5f; break;
                default: rate = 0f; break;
            }
            if (rate <= 0) return;
            float perTick = Config.StaminaRegenPerSecond * (0.5f + f.Stats.Stamina / 200f) * Config.Dt * rate;
            f.Stamina = Math.Min(f.MaxStamina, f.Stamina + perTick);
        }

        private void Push(FighterRuntime target, FighterRuntime from, float amount)
        {
            float dir = Math.Sign(target.X - from.X);
            if (dir == 0) dir = from.Facing;
            target.X += dir * amount;
        }

        private void UpdateFacing()
        {
            if (A.X < B.X) { A.Facing = 1; B.Facing = -1; }
            else if (A.X > B.X) { A.Facing = -1; B.Facing = 1; }
        }

        private void ResolvePositions()
        {
            float h = Config.ArenaHalfWidth;
            A.X = MathUtil.Clamp(A.X, -h, h); B.X = MathUtil.Clamp(B.X, -h, h);
            bool clinch = A.State == CombatState.Grappling || B.State == CombatState.Grappling;
            float gap = Distance;
            if (!clinch && gap < Config.MinSeparation)
            {
                float push = (Config.MinSeparation - gap) / 2f;
                var left = A.X <= B.X ? A : B; var right = left == A ? B : A;
                left.X -= push; right.X += push;
                if (left.X < -h) { right.X += -h - left.X; left.X = -h; }
                if (right.X > h) { left.X -= right.X - h; right.X = h; }
            }
            UpdateFacing();
        }

        private void Defeat(FighterRuntime loser, FighterRuntime winner, FightEndMethod method)
        {
            loser.Health = 0;
            SetState(loser, CombatState.Defeated);
            Emit(CombatEventType.Defeated, winner, loser, null, 0, method.ToString());
            Result = MakeResult(winner, loser, method);
        }

        private void EndOnTime()
        {
            Emit(CombatEventType.TimeUp, A, B, null, 0);
            float a = A.HealthFraction, b = B.HealthFraction;
            if (Math.Abs(a - b) < 0.0001f) { Result = MakeResult(null, null, FightEndMethod.Draw); return; }
            Result = a > b ? MakeResult(A, B, FightEndMethod.TimeDecision) : MakeResult(B, A, FightEndMethod.TimeDecision);
        }

        private FightResult MakeResult(FighterRuntime winner, FighterRuntime loser, FightEndMethod method) => new FightResult
        {
            FightId = FightId,
            WinnerId = winner?.Id,
            LoserId = loser?.Id,
            Method = method,
            DurationTicks = Tick,
            WinnerHealthFraction = winner?.HealthFraction ?? 0f,
            CountersLanded = winner?.CountersLanded ?? 0,
            ThrowsLanded = winner?.ThrowsLanded ?? 0
        };

        private void Emit(CombatEventType type, FighterRuntime actor, FighterRuntime target, string move, float amount, string detail = null)
        {
            _events.Add(new CombatEvent { Type = type, ActorId = actor?.Id, TargetId = target?.Id, MoveId = move, Amount = amount, Tick = Tick, Detail = detail });
        }
    }
}
