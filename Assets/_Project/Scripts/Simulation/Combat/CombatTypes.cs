using System;

namespace FFTS.Combat
{
    /// <summary>
    /// Fighter states. The first thirteen come from the combat spec; HitStun, BlockStun, Dodging,
    /// Grappled and FinisherVictim are explicit sub-states needed to make timing deterministic.
    /// </summary>
    public enum CombatState
    {
        Idle, Moving, Attacking, Combo, Finisher, Blocking, Countering, Grappling, Throwing,
        KnockedDown, GettingUp, Stunned, Defeated,
        HitStun, BlockStun, Dodging, Grappled, FinisherVictim
    }

    public enum CombatEventType
    {
        MoveStarted, Hit, Blocked, GuardBreak, Countered, Dodged, Clash, NotEnoughStamina,
        GrappleStarted, ClinchStrike, Thrown, Reversal, GrappleEscaped, ClinchReleased,
        KnockedDown, QuickRise, GotUp, Breakaway, TauntCompleted,
        FinisherStarted, FinisherDenied, Defeated, TimeUp
    }

    public enum FinisherDenial { None, AttackerBusy, NotEnoughMomentum, OpponentNotVulnerable, OutOfRange, OpponentStateInvalid, NoFinisherMove }

    public enum FightEndMethod { None, Knockout, Finisher, TimeDecision, Draw }

    public struct CombatEvent
    {
        public CombatEventType Type;
        public string ActorId;
        public string TargetId;
        public string MoveId;
        public float Amount;
        public int Tick;
        public string Detail;
        public override string ToString() => $"[{Tick}] {Type} {ActorId}->{TargetId} {MoveId} {Amount:0.#} {Detail}";
    }

    /// <summary>Per-tick input for one fighter. Move is along the fight line: +1 toward the opponent.</summary>
    public struct FighterInput
    {
        public float Move;
        public bool BlockHeld;
        public CombatCommand Command;

        public static FighterInput None => new FighterInput();
        public static FighterInput Press(CombatCommand c) => new FighterInput { Command = c };
        public static FighterInput Hold() => new FighterInput { BlockHeld = true };
        public static FighterInput Walk(float m) => new FighterInput { Move = m };
    }

    public sealed class FightResult
    {
        public string FightId;
        public string WinnerId;
        public string LoserId;
        public FightEndMethod Method;
        public int DurationTicks;
        public float WinnerHealthFraction;
        public int CountersLanded;
        public int ThrowsLanded;
    }

    /// <summary>Tunable rules. Values are in ticks (60/s) and metres.</summary>
    public sealed class CombatConfig
    {
        public int TickRate = 60;
        public float ArenaHalfWidth = 6f;
        public float MinSeparation = 0.7f;
        public float BodyReach = 0.35f;
        public float WallThrowDistance = 1.3f;

        public int InputBufferTicks = 8;

        public int DodgeTicks = 20, DodgeInvulnStart = 2, DodgeInvulnEnd = 14;
        public float DodgeDistance = 1.6f, DodgeStaminaCost = 12f;

        public int CounterStanceTicks = 26;   // total stance; the catch window scales with Technique
        public float CounterStaminaCost = 6f;

        public int KnockdownTicks = 70, GetUpTicks = 24, QuickRiseAfterTicks = 20;
        public int StunTicks = 90, GuardBreakStunTicks = 75;

        public int ClinchMaxTicks = 110, ClinchStrikeCooldown = 20, ClinchStrikesMax = 3;
        public float EscapeThreshold = 100f;
        public int ReversalWindowTicks = 14;
        public float ReversalMomentumCost = 25f;

        public int BreakawayMinCombo = 3;
        public float BreakawayMomentumCost = 30f, BreakawayStaminaCost = 15f;

        public float MomentumMax = 100f, FinisherMomentum = 100f, FinisherHealthFraction = 0.35f, FinisherRange = 1.8f;
        public float ComboDamageDecay = 0.15f, MinDamageScale = 0.35f;
        public float HitStunDecay = 0.12f, MinHitStunScale = 0.4f;
        public float ChipDamageFraction = 0.1f;

        public float StaminaRegenPerSecond = 14f;
        public float TauntMomentum = 15f;

        public int RoundTicks = 60 * 180;

        public float Dt => 1f / TickRate;
    }
}
