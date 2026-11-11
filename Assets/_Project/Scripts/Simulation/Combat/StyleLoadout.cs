using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Economy;

namespace FFTS.Combat
{
    public enum StyleActionResult { Ok, AlreadyLearned, UnknownStyle, ParentsNotLearned, InsufficientFunds, NotLearned, InvalidLoadout }

    /// <summary>Saved fighting-style progress of the player.</summary>
    public sealed class StyleLoadoutState
    {
        public List<string> Learned = new List<string>();
        /// <summary>Primary first (slot priority), then the optional secondary.</summary>
        public List<string> Equipped = new List<string>();
    }

    public sealed class StyleLearned : IGameEvent { public string StyleId; public long CostCents; }
    public sealed class StylesEquipped : IGameEvent { public string Primary; public string Secondary; public string HybridId; }

    /// <summary>
    /// Which styles the player knows and has equipped. Styles are learned once, paid through FINESS BANK with the
    /// idempotency key "style.learn.&lt;id&gt;" so a style can never be charged twice. Up to two base styles are
    /// equipped; when the pair has a hybrid that the player has learned, the hybrid's moves and stance apply on top.
    /// </summary>
    public sealed class StyleLoadout
    {
        public const string StarterStyle = "style.street";
        public const int MaxEquipped = 2;

        private readonly CombatDatabase _db;
        private readonly FinessBank _bank;
        private readonly EventBus _bus;
        private readonly List<string> _learned = new List<string>();
        private readonly List<string> _equipped = new List<string>();

        public StyleLoadout(CombatDatabase db, FinessBank bank, EventBus bus)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _bank = bank; _bus = bus;
            ResetToStarter();
        }

        public IReadOnlyList<string> Learned => _learned;
        public IReadOnlyList<string> Equipped => _equipped;
        public bool Knows(string styleId) => _learned.Contains(styleId);

        /// <summary>The hybrid that currently applies, or null.</summary>
        public FightingStyleDefinition ActiveHybrid
        {
            get
            {
                if (_equipped.Count < 2) return null;
                var h = _db.FindHybrid(_equipped[0], _equipped[1]);
                return h != null && Knows(h.Id) ? h : null;
            }
        }

        /// <summary>Why a style can or cannot be learned right now (no money moves).</summary>
        public StyleActionResult CanLearn(string styleId)
        {
            var s = _db.GetStyle(styleId);
            if (s == null) return StyleActionResult.UnknownStyle;
            if (Knows(styleId)) return StyleActionResult.AlreadyLearned;
            if (s.IsHybrid && !(Knows(s.HybridOf[0]) && Knows(s.HybridOf[1]))) return StyleActionResult.ParentsNotLearned;
            return StyleActionResult.Ok;
        }

        public StyleActionResult Learn(string styleId)
        {
            var check = CanLearn(styleId);
            if (check != StyleActionResult.Ok) return check;
            var s = _db.GetStyle(styleId);
            if (s.UnlockCostCents > 0)
            {
                if (_bank == null) return StyleActionResult.InsufficientFunds;
                var status = _bank.Pay("style.learn." + styleId, s.UnlockCostCents, TransactionKind.Purchase, "Training: " + s.DisplayName);
                // Duplicate means this exact lesson was already paid for (e.g. a save restored behind the ledger): don't charge, just learn.
                if (status != PostStatus.Posted && status != PostStatus.Duplicate) return StyleActionResult.InsufficientFunds;
            }
            _learned.Add(styleId);
            _bus?.Publish(new StyleLearned { StyleId = styleId, CostCents = s.UnlockCostCents });
            return StyleActionResult.Ok;
        }

        /// <summary>Equips one or two learned base styles (primary has slot priority).</summary>
        public StyleActionResult Equip(string primary, string secondary = null)
        {
            var p = _db.GetStyle(primary);
            if (p == null || p.IsHybrid) return StyleActionResult.InvalidLoadout;
            if (!Knows(primary)) return StyleActionResult.NotLearned;
            if (secondary != null)
            {
                var s = _db.GetStyle(secondary);
                if (s == null || s.IsHybrid || secondary == primary) return StyleActionResult.InvalidLoadout;
                if (!Knows(secondary)) return StyleActionResult.NotLearned;
            }
            _equipped.Clear(); _equipped.Add(primary);
            if (secondary != null) _equipped.Add(secondary);
            _bus?.Publish(new StylesEquipped { Primary = primary, Secondary = secondary, HybridId = ActiveHybrid?.Id });
            return StyleActionResult.Ok;
        }

        /// <summary>The player's resolved moveset for the next fight.</summary>
        public Moveset BuildMoveset()
        {
            var hybrids = new List<string>();
            foreach (var id in _learned) { var s = _db.GetStyle(id); if (s != null && s.IsHybrid) hybrids.Add(id); }
            return _db.BuildMoveset(_equipped, true, hybrids);
        }

        public StyleLoadoutState Capture() => new StyleLoadoutState { Learned = new List<string>(_learned), Equipped = new List<string>(_equipped) };

        /// <summary>Restores a save. Unknown ids (removed content) are dropped; an empty or invalid save falls back to the starter style.</summary>
        public void Restore(StyleLoadoutState s)
        {
            ResetToStarter();
            if (s == null) return;
            foreach (var id in s.Learned) if (_db.GetStyle(id) != null && !_learned.Contains(id)) _learned.Add(id);
            var eq = new List<string>();
            foreach (var id in s.Equipped)
            {
                var st = _db.GetStyle(id);
                if (st != null && !st.IsHybrid && Knows(id) && !eq.Contains(id) && eq.Count < MaxEquipped) eq.Add(id);
            }
            if (eq.Count > 0) { _equipped.Clear(); _equipped.AddRange(eq); }
        }

        private void ResetToStarter()
        {
            _learned.Clear(); _equipped.Clear();
            if (_db.GetStyle(StarterStyle) != null) { _learned.Add(StarterStyle); _equipped.Add(StarterStyle); }
        }
    }
}
