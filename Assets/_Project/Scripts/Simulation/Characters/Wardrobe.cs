using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Economy;

namespace FFTS.Characters
{
    public enum WardrobeResult { Ok, UnknownItem, AlreadyOwned, NotOwned, NotSoldHere, InsufficientFunds, InvalidLook, CreatorLocked, NotABarber }

    public sealed class WardrobeState
    {
        public List<string> Owned = new List<string>();
        public CharacterAppearance Look;
        public bool CreatorLocked;
        public int BarberVisits;
    }

    public sealed class LookChanged : IGameEvent { public string Reason; }
    public sealed class ClothingBought : IGameEvent { public string ItemId; public long PriceCents; }

    /// <summary>
    /// The player's clothes and look. Clothes are bought once (key "wardrobe.buy.&lt;item&gt;", so an item can never be
    /// charged twice) and then worn in any of their colourways. Face and body are set in the character creator and
    /// locked when the player commits; hair changes at a barber, one paid visit per change (key "barber.visit.&lt;n&gt;").
    /// Every change is validated against the catalog, so the presentation layer never receives an impossible look.
    /// </summary>
    public sealed class WardrobeManager
    {
        public const string DefaultLook = "look.player.default";
        private readonly AppearanceCatalog _cat;
        private readonly FinessBank _bank;
        private readonly EventBus _bus;
        private readonly HashSet<string> _owned = new HashSet<string>();
        private int _barberVisits;

        public CharacterAppearance Look { get; private set; }
        public bool CreatorLocked { get; private set; }
        public AppearanceCatalog Catalog => _cat;

        public WardrobeManager(AppearanceCatalog catalog, FinessBank bank, EventBus bus)
        {
            _cat = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _bank = bank; _bus = bus;
            Reset();
        }

        public bool Owns(string itemId) => _owned.Contains(itemId);
        public IEnumerable<string> OwnedItems => _owned;

        public WardrobeResult Buy(string itemId, string atStoreId)
        {
            var item = _cat.Item(itemId);
            if (item == null) return WardrobeResult.UnknownItem;
            if (Owns(itemId)) return WardrobeResult.AlreadyOwned;
            if (item.StoreId != atStoreId) return WardrobeResult.NotSoldHere;
            if (item.PriceCents > 0)
            {
                var status = _bank?.Pay("wardrobe.buy." + itemId, item.PriceCents, TransactionKind.Purchase, "Bought " + item.DisplayName) ?? PostStatus.InsufficientFunds;
                if (status != PostStatus.Posted && status != PostStatus.Duplicate) return WardrobeResult.InsufficientFunds;
            }
            _owned.Add(itemId);
            _bus?.Publish(new ClothingBought { ItemId = itemId, PriceCents = item.PriceCents });
            return WardrobeResult.Ok;
        }

        /// <summary>Wears an owned item in one of its colourways. Fails (and changes nothing) if the result would be invalid.</summary>
        public WardrobeResult Wear(string itemId, string colorway)
        {
            var item = _cat.Item(itemId);
            if (item == null) return WardrobeResult.UnknownItem;
            if (!Owns(itemId)) return WardrobeResult.NotOwned;
            var next = Look.Clone();
            next.Outfit[item.Slot] = new OutfitPiece { ItemId = itemId, Colorway = colorway ?? item.Colorways[0] };
            return Commit(next, "wear");
        }

        /// <summary>Takes off an optional piece (head, outerwear, neck, wrist, eyewear). Top, bottoms and shoes stay on.</summary>
        public WardrobeResult Remove(OutfitSlot slot)
        {
            if (Array.IndexOf(AppearanceCatalog.RequiredSlots, slot) >= 0) return WardrobeResult.InvalidLook;
            var next = Look.Clone(); next.Outfit.Remove(slot);
            return Commit(next, "remove");
        }

        /// <summary>Character creator: face, body, skin, eyes, hair and starter clothes, free, until <see cref="LockCreator"/>.</summary>
        public WardrobeResult CreatorApply(CharacterAppearance look)
        {
            if (CreatorLocked) return WardrobeResult.CreatorLocked;
            if (look == null) return WardrobeResult.InvalidLook;
            foreach (var p in look.Outfit.Values) if (p != null && !Owns(p.ItemId)) return WardrobeResult.NotOwned;
            return Commit(look.Clone(), "creator");
        }

        public void LockCreator() => CreatorLocked = true;

        /// <summary>A paid barber visit: hair style, hair colour and facial hair.</summary>
        public WardrobeResult Barber(string storeId, string hair, string hairColor, string facialHair)
        {
            var store = _cat.StoreById(storeId);
            if (store == null || !store.IsBarber) return WardrobeResult.NotABarber;
            var next = Look.Clone();
            next.Hair = hair; next.HairColor = hairColor; next.FacialHair = facialHair;
            if (_cat.Check(next).Count > 0) return WardrobeResult.InvalidLook;
            if (store.BarberPriceCents > 0)
            {
                var status = _bank?.Pay("barber.visit." + (_barberVisits + 1), store.BarberPriceCents, TransactionKind.Purchase, store.DisplayName) ?? PostStatus.InsufficientFunds;
                if (status != PostStatus.Posted && status != PostStatus.Duplicate) return WardrobeResult.InsufficientFunds;
            }
            _barberVisits++;
            return Commit(next, "barber");
        }

        private WardrobeResult Commit(CharacterAppearance next, string reason)
        {
            if (_cat.Check(next).Count > 0) return WardrobeResult.InvalidLook;
            Look = next;
            _bus?.Publish(new LookChanged { Reason = reason });
            return WardrobeResult.Ok;
        }

        public WardrobeState Capture()
        {
            var s = new WardrobeState { Look = Look.Clone(), CreatorLocked = CreatorLocked, BarberVisits = _barberVisits };
            s.Owned.AddRange(_owned);
            s.Owned.Sort(StringComparer.Ordinal);
            return s;
        }

        /// <summary>Restores a save. Items no longer in the catalog are dropped; an invalid look falls back to the default.</summary>
        public void Restore(WardrobeState s)
        {
            Reset();
            if (s == null) return;
            foreach (var id in s.Owned) if (_cat.Item(id) != null) _owned.Add(id);
            CreatorLocked = s.CreatorLocked;
            _barberVisits = Math.Max(0, s.BarberVisits);
            if (s.Look != null && _cat.Check(s.Look).Count == 0)
            {
                bool allOwned = true;
                foreach (var p in s.Look.Outfit.Values) if (p != null && !Owns(p.ItemId)) allOwned = false;
                if (allOwned) Look = s.Look.Clone();
            }
        }

        private void Reset()
        {
            _owned.Clear(); _barberVisits = 0; CreatorLocked = false;
            foreach (var c in _cat.Clothing) if (c.StarterOwned) _owned.Add(c.Id);
            Look = _cat.Looks.TryGetValue(DefaultLook, out var l) ? l.Clone() : new CharacterAppearance();
        }
    }

    /// <summary>
    /// Deterministic look generator for unnamed NPCs. Same seed, same person. Rejects candidates that are too similar
    /// to looks already on screen so a crowd never reads as copies.
    /// </summary>
    public static class AppearanceGenerator
    {
        public const float MaxSimilarity = 0.72f;

        public static CharacterAppearance Generate(AppearanceCatalog cat, ulong seed, string styleTag = null, IList<CharacterAppearance> avoid = null)
        {
            var rng = new DeterministicRandom(seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
            CharacterAppearance best = null; float bestScore = float.MaxValue;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var a = Roll(cat, rng, styleTag);
                float worst = 0f;
                if (avoid != null) foreach (var o in avoid) worst = Math.Max(worst, AppearanceCatalog.Similarity(a, o));
                if (worst <= MaxSimilarity) return a;
                if (worst < bestScore) { bestScore = worst; best = a; }
            }
            return best;
        }

        private static T Pick<T>(IList<T> list, DeterministicRandom rng) => list[rng.Range(0, list.Count)];

        private static CharacterAppearance Roll(AppearanceCatalog cat, DeterministicRandom rng, string styleTag)
        {
            // Heights follow a rough adult spread (mostly 165-192 cm) rather than a flat range.
            float h = 178f + (rng.Value() + rng.Value() + rng.Value() - 1.5f) * 18f;
            var a = new CharacterAppearance
            {
                Body = new BodyShape
                {
                    HeightCm = MathUtil.Clamp(h, 156f, 204f), Weight = rng.Value(), Muscle = rng.Value(),
                    ShoulderWidth = 0.3f + rng.Value() * 0.4f, HipWidth = 0.3f + rng.Value() * 0.4f,
                    ArmLength = 0.35f + rng.Value() * 0.3f, LegLength = 0.35f + rng.Value() * 0.3f
                },
                SkinTone = Pick(cat.SkinTones, rng).Id,
                FacePreset = Pick(cat.FacePresets, rng).Id,
                EyeColor = Pick(cat.EyeColors, rng).Id,
                HairColor = Pick(cat.HairColors, rng).Id,
            };
            foreach (var s in cat.FaceSliders) a.Face[s.Id] = (float)Math.Round((rng.Value() * 2f - 1f) * 0.6f, 2);
            a.Hair = Pick(cat.HairStyles, rng).Id;
            a.FacialHair = rng.Chance(0.55f) && cat.FacialHair.Count > 0 ? Pick(cat.FacialHair, rng).Id : null;
            foreach (OutfitSlot slot in Enum.GetValues(typeof(OutfitSlot)))
            {
                var options = cat.Clothing.FindAll(c => c.Slot == slot && (styleTag == null || c.Tags.Contains(styleTag) || c.Tags.Contains("basic")));
                if (options.Count == 0) options = cat.Clothing.FindAll(c => c.Slot == slot);
                bool required = Array.IndexOf(AppearanceCatalog.RequiredSlots, slot) >= 0;
                if (options.Count == 0 || (!required && !rng.Chance(slot == OutfitSlot.Head ? 0.4f : 0.3f))) continue;
                var item = Pick(options, rng);
                var hair = cat.Hair(a.Hair);
                if (slot == OutfitSlot.Head && hair != null && hair.Length != "Bald" && !hair.FitsUnder.Contains(item.HeadwearType)) continue;
                a.Outfit[slot] = new OutfitPiece { ItemId = item.Id, Colorway = Pick(item.Colorways, rng) };
            }
            return a;
        }
    }
}
