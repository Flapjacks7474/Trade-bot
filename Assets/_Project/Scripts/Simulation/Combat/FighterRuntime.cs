using FFTS.Core;

namespace FFTS.Combat
{
    /// <summary>Mutable per-fight state of one fighter. Owned and mutated only by <see cref="CombatSimulation"/>.</summary>
    public sealed class FighterRuntime
    {
        public readonly string Id;
        public readonly FighterDefinition Definition;
        public readonly Moveset Moves;
        public FighterStats Stats => Definition.Stats;

        public float Health { get; internal set; }
        public float MaxHealth { get; }
        public float Stamina { get; internal set; }
        public float MaxStamina { get; }
        public float Momentum { get; internal set; }
        public float X { get; internal set; }
        public int Facing { get; internal set; } = 1;

        public CombatState State { get; internal set; }
        public int StateTicks { get; internal set; }
        public int StateDuration { get; internal set; }

        public MoveDefinition CurrentMove { get; internal set; }
        public int MoveTick { get; internal set; }
        public int MoveStartup { get; internal set; }
        public bool MoveConnected { get; internal set; }
        internal bool MoveWasDodged;

        public int ComboCounter { get; internal set; }   // hits received in the current combo
        internal int BlockStartTick;
        internal CombatCommand Buffered;
        internal bool PrevBlockHeld;
        internal int BufferedAge;

        // Clinch (only meaningful for the grappling fighter)
        public int ClinchTicks { get; internal set; }
        internal int ClinchStrikes;
        internal int ClinchCooldown;
        public float EscapeProgress { get; internal set; }

        public int CountersLanded { get; internal set; }
        public int ThrowsLanded { get; internal set; }

        public FighterRuntime(FighterDefinition def, Moveset moves, float x)
        {
            Id = def.Id; Definition = def; Moves = moves;
            MaxHealth = 100f + def.Stats.Durability;
            MaxStamina = 60f + def.Stats.Stamina * 0.8f;
            Health = MaxHealth; Stamina = MaxStamina;
            X = x;
            State = CombatState.Idle;
        }

        /// <summary>
        /// Scripted fight setup (story fights that start injured or with meter, tutorials, tests).
        /// Only valid before the fight's first tick; values are clamped to legal ranges.
        /// </summary>
        public void ApplyScenario(float? health = null, float? momentum = null, float? stamina = null)
        {
            if (health.HasValue) Health = MathUtil.Clamp(health.Value, 1f, MaxHealth);
            if (momentum.HasValue) Momentum = MathUtil.Clamp(momentum.Value, 0f, 100f);
            if (stamina.HasValue) Stamina = MathUtil.Clamp(stamina.Value, 0f, MaxStamina);
        }

        public float HealthFraction => Health / MaxHealth;
        public bool IsDefeated => State == CombatState.Defeated;

        // Stat-derived values, kept here so tests and UI read the same numbers.
        public float WalkSpeed => 2.2f + Stats.Speed / 50f;
        public float StrengthMultiplier => 0.7f + Stats.Strength / 166f;
        public float DurabilityMultiplier => 1.15f - Stats.Durability / 333f;
        public int CounterWindow => 6 + Stats.Technique / 15;
        public int ParryWindow => 3 + Stats.Technique / 25;
        public int AdjustStartup(int startup) => System.Math.Max(1, MathUtil.RoundToInt(startup * (1.15f - Stats.Speed / 400f)));

        public bool InMovePhaseActive => CurrentMove != null && MoveTick >= MoveStartup && MoveTick < MoveStartup + CurrentMove.Active;
        public bool InMoveStartup => CurrentMove != null && MoveTick < MoveStartup;
        public bool InMoveRecovery => CurrentMove != null && MoveTick >= MoveStartup + CurrentMove.Active;

        public bool IsDodgeInvulnerable(CombatConfig c) => State == CombatState.Dodging && StateTicks >= c.DodgeInvulnStart && StateTicks < c.DodgeInvulnEnd;

        public override string ToString() => $"{Id} {State} HP {Health:0}/{MaxHealth:0} ST {Stamina:0} MO {Momentum:0} X {X:0.00}";
    }
}
