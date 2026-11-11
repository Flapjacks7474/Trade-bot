using System;
using System.Collections.Generic;
using FFTS.Core;
using Newtonsoft.Json;

namespace FFTS.Characters
{
    public enum OutfitSlot { Head, Top, Outerwear, Bottoms, Shoes, Neck, Wrist, Eyewear }

    /// <summary>Body proportions. Realistic adult ranges only: the head-to-body ratio is fixed by the rig (about 1:7.5).</summary>
    public sealed class BodyShape
    {
        public float HeightCm = 180f;   // 155-205
        public float Weight = 0.5f;     // 0 slim .. 1 heavy
        public float Muscle = 0.5f;     // 0 soft .. 1 built
        public float ShoulderWidth = 0.5f, HipWidth = 0.5f, ArmLength = 0.5f, LegLength = 0.5f; // 0..1 around the average

        public BodyShape Clone() => (BodyShape)MemberwiseClone();
    }

    public sealed class OutfitPiece
    {
        public string ItemId;
        public string Colorway;   // swatch id from the item's colorways
        public OutfitPiece Clone() => new OutfitPiece { ItemId = ItemId, Colorway = Colorway };
    }

    /// <summary>Everything that makes a character look like themselves. Presentation reads it; rules validate it.</summary>
    public sealed class CharacterAppearance
    {
        public BodyShape Body = new BodyShape();
        public string SkinTone;
        public string FacePreset;
        /// <summary>Slider offsets on top of the preset, -1..1, keyed by catalog face slider id (blend shapes).</summary>
        public Dictionary<string, float> Face = new Dictionary<string, float>();
        public string EyeColor;
        public string Hair;
        public string HairColor;
        public string FacialHair;
        public Dictionary<OutfitSlot, OutfitPiece> Outfit = new Dictionary<OutfitSlot, OutfitPiece>();

        public CharacterAppearance Clone()
        {
            var c = (CharacterAppearance)MemberwiseClone();
            c.Body = Body.Clone();
            c.Face = new Dictionary<string, float>(Face);
            c.Outfit = new Dictionary<OutfitSlot, OutfitPiece>();
            foreach (var kv in Outfit) c.Outfit[kv.Key] = kv.Value?.Clone();
            return c;
        }
    }

    public sealed class Brand { public string Id, DisplayName, Tagline; }
    public sealed class Swatch { public string Id, DisplayName, Hex; }
    public sealed class FaceSlider { public string Id, DisplayName, BlendShape; }
    public sealed class FacePreset { public string Id, DisplayName, MeshKey; public Dictionary<string, float> Shape = new Dictionary<string, float>(); }

    public sealed class HairStyle
    {
        public string Id, DisplayName, MeshKey;
        public string Length;                                   // Bald, Short, Medium, Long
        /// <summary>Headwear types this hair can be worn under ("Beanie", "Cap", "Durag", "Hood", "Hat").</summary>
        public List<string> FitsUnder = new List<string>();
    }

    public sealed class FacialHairStyle { public string Id, DisplayName, MeshKey; }

    public sealed class ClothingItem
    {
        public string Id, DisplayName, MeshKey, BrandId, StoreId, Description;
        public OutfitSlot Slot;
        public long PriceCents;
        public List<string> Colorways = new List<string>();
        public string HeadwearType;          // Head slot only
        public bool StarterOwned;
        public List<string> Tags = new List<string>();   // style tags used by the NPC generator (streetwear, gym, formal...)
    }

    public sealed class Store { public string Id, DisplayName, DistrictId; public long BarberPriceCents; public bool IsBarber; }

    /// <summary>All appearance content: brands (original, fictional), swatches, hair, faces, clothes, stores.</summary>
    public sealed class AppearanceCatalog
    {
        public static readonly OutfitSlot[] RequiredSlots = { OutfitSlot.Top, OutfitSlot.Bottoms, OutfitSlot.Shoes };

        public List<Brand> Brands = new List<Brand>();
        public List<Swatch> SkinTones = new List<Swatch>();
        public List<Swatch> HairColors = new List<Swatch>();
        public List<Swatch> EyeColors = new List<Swatch>();
        public List<Swatch> FabricColors = new List<Swatch>();
        public List<FaceSlider> FaceSliders = new List<FaceSlider>();
        public List<FacePreset> FacePresets = new List<FacePreset>();
        public List<HairStyle> HairStyles = new List<HairStyle>();
        public List<FacialHairStyle> FacialHair = new List<FacialHairStyle>();
        public List<ClothingItem> Clothing = new List<ClothingItem>();
        public List<Store> Stores = new List<Store>();
        /// <summary>Authored looks: the player's starting look and every named character.</summary>
        public Dictionary<string, CharacterAppearance> Looks = new Dictionary<string, CharacterAppearance>();

        [JsonIgnore] private Dictionary<string, ClothingItem> _items;
        public ClothingItem Item(string id) { Index(); return id != null && _items.TryGetValue(id, out var i) ? i : null; }
        public HairStyle Hair(string id) => HairStyles.Find(h => h.Id == id);
        public Store StoreById(string id) => Stores.Find(s => s.Id == id);
        private void Index()
        {
            if (_items != null) return;
            _items = new Dictionary<string, ClothingItem>();
            foreach (var c in Clothing) if (c.Id != null && !_items.ContainsKey(c.Id)) _items[c.Id] = c;
        }

        public static AppearanceCatalog Parse(IEnumerable<string> json, ValidationReport report)
        {
            var cat = new AppearanceCatalog();
            foreach (var j in json)
            {
                var part = JsonConvert.DeserializeObject<AppearanceCatalog>(j);
                cat.Brands.AddRange(part.Brands); cat.SkinTones.AddRange(part.SkinTones); cat.HairColors.AddRange(part.HairColors);
                cat.EyeColors.AddRange(part.EyeColors); cat.FabricColors.AddRange(part.FabricColors); cat.FaceSliders.AddRange(part.FaceSliders);
                cat.FacePresets.AddRange(part.FacePresets); cat.HairStyles.AddRange(part.HairStyles); cat.FacialHair.AddRange(part.FacialHair);
                cat.Clothing.AddRange(part.Clothing); cat.Stores.AddRange(part.Stores);
                foreach (var kv in part.Looks)
                {
                    if (cat.Looks.ContainsKey(kv.Key)) report.Error($"Look '{kv.Key}' defined twice.");
                    else cat.Looks[kv.Key] = kv.Value;
                }
            }
            cat.Validate(report);
            return cat;
        }

        public void Validate(ValidationReport report)
        {
            var ids = new IdRegistry();
            foreach (var b in Brands) ids.Register(b.Id, "brand");
            foreach (var s in SkinTones) ids.Register(s.Id, "skin tone");
            foreach (var s in HairColors) ids.Register(s.Id, "hair colour");
            foreach (var s in EyeColors) ids.Register(s.Id, "eye colour");
            foreach (var s in FabricColors) ids.Register(s.Id, "fabric colour");
            foreach (var s in FaceSliders) ids.Register(s.Id, "face slider");
            foreach (var s in FacePresets) ids.Register(s.Id, "face preset");
            foreach (var s in HairStyles) ids.Register(s.Id, "hair");
            foreach (var s in FacialHair) ids.Register(s.Id, "facial hair");
            foreach (var s in Clothing) ids.Register(s.Id, "clothing");
            foreach (var s in Stores) ids.Register(s.Id, "store");
            report.AddRange(ids.Errors);

            foreach (var s in SkinTones.ConcatAll(HairColors, EyeColors, FabricColors))
                if (!IsHex(s.Hex)) report.Error($"Swatch {s.Id}: '{s.Hex}' is not a #RRGGBB colour.");
            foreach (var f in FaceSliders) if (string.IsNullOrEmpty(f.BlendShape)) report.Error($"Face slider {f.Id}: missing blend shape name.");
            foreach (var p in FacePresets)
                foreach (var kv in p.Shape)
                {
                    if (!FaceSliders.Exists(f => f.Id == kv.Key)) report.Error($"Face preset {p.Id}: unknown slider '{kv.Key}'.");
                    if (kv.Value < -1f || kv.Value > 1f) report.Error($"Face preset {p.Id}: {kv.Key} outside -1..1.");
                }
            var headTypes = new HashSet<string> { "Beanie", "Cap", "Durag", "Hood", "Hat" };
            foreach (var h in HairStyles)
                foreach (var t in h.FitsUnder) if (!headTypes.Contains(t)) report.Error($"Hair {h.Id}: unknown headwear type '{t}'.");
            var starterSlots = new HashSet<OutfitSlot>();
            foreach (var c in Clothing)
            {
                if (string.IsNullOrEmpty(c.DisplayName) || string.IsNullOrEmpty(c.MeshKey)) report.Error($"Clothing {c.Id}: needs a display name and mesh key.");
                if (!Brands.Exists(b => b.Id == c.BrandId)) report.Error($"Clothing {c.Id}: unknown brand '{c.BrandId}'.");
                if (c.StoreId != null && StoreById(c.StoreId) == null) report.Error($"Clothing {c.Id}: unknown store '{c.StoreId}'.");
                if (c.PriceCents < 0) report.Error($"Clothing {c.Id}: negative price.");
                if (c.Colorways.Count == 0) report.Error($"Clothing {c.Id}: needs at least one colourway.");
                foreach (var cw in c.Colorways) if (!FabricColors.Exists(f => f.Id == cw)) report.Error($"Clothing {c.Id}: unknown colourway '{cw}'.");
                if (c.Slot == OutfitSlot.Head && !headTypes.Contains(c.HeadwearType ?? "")) report.Error($"Clothing {c.Id}: head items need a HeadwearType.");
                if (c.Slot != OutfitSlot.Head && c.HeadwearType != null) report.Error($"Clothing {c.Id}: only head items have a HeadwearType.");
                if (!c.StarterOwned && c.StoreId == null) report.Error($"Clothing {c.Id}: not a starter item and not sold anywhere.");
                if (c.StarterOwned) starterSlots.Add(c.Slot);
            }
            foreach (var slot in RequiredSlots) if (!starterSlots.Contains(slot)) report.Error($"Catalog: no starter item for required slot {slot}.");
            foreach (var kv in Looks)
                foreach (var problem in Check(kv.Value)) report.Error($"Look '{kv.Key}': {problem}");
            if (!Looks.ContainsKey("look.player.default")) report.Error("Catalog: missing look 'look.player.default'.");
        }

        /// <summary>Every reason this look is not allowed (empty when valid).</summary>
        public List<string> Check(CharacterAppearance a)
        {
            var p = new List<string>();
            if (a == null) { p.Add("no appearance"); return p; }
            var b = a.Body ?? new BodyShape();
            if (b.HeightCm < 155 || b.HeightCm > 205) p.Add($"height {b.HeightCm}cm outside 155-205");
            foreach (var v in new[] { b.Weight, b.Muscle, b.ShoulderWidth, b.HipWidth, b.ArmLength, b.LegLength })
                if (v < 0f || v > 1f) { p.Add("body sliders must be 0..1"); break; }
            if (!SkinTones.Exists(s => s.Id == a.SkinTone)) p.Add($"unknown skin tone '{a.SkinTone}'");
            if (!FacePresets.Exists(s => s.Id == a.FacePreset)) p.Add($"unknown face preset '{a.FacePreset}'");
            if (!EyeColors.Exists(s => s.Id == a.EyeColor)) p.Add($"unknown eye colour '{a.EyeColor}'");
            foreach (var kv in a.Face)
            {
                if (!FaceSliders.Exists(f => f.Id == kv.Key)) p.Add($"unknown face slider '{kv.Key}'");
                if (kv.Value < -1f || kv.Value > 1f) p.Add($"face slider {kv.Key} outside -1..1");
            }
            var hair = Hair(a.Hair);
            if (hair == null) p.Add($"unknown hair '{a.Hair}'");
            if (!HairColors.Exists(s => s.Id == a.HairColor)) p.Add($"unknown hair colour '{a.HairColor}'");
            if (a.FacialHair != null && !FacialHair.Exists(s => s.Id == a.FacialHair)) p.Add($"unknown facial hair '{a.FacialHair}'");
            foreach (var slot in RequiredSlots) if (!a.Outfit.ContainsKey(slot) || a.Outfit[slot] == null) p.Add($"missing {slot}");
            foreach (var kv in a.Outfit)
            {
                if (kv.Value == null) continue;
                var item = Item(kv.Value.ItemId);
                if (item == null) { p.Add($"unknown item '{kv.Value.ItemId}'"); continue; }
                if (item.Slot != kv.Key) p.Add($"{item.Id} is a {item.Slot} item, not {kv.Key}");
                if (!item.Colorways.Contains(kv.Value.Colorway)) p.Add($"{item.Id} has no colourway '{kv.Value.Colorway}'");
                if (kv.Key == OutfitSlot.Head && hair != null && hair.Length != "Bald" && !hair.FitsUnder.Contains(item.HeadwearType))
                    p.Add($"{hair.DisplayName} can't be worn under a {item.HeadwearType}");
            }
            return p;
        }

        /// <summary>
        /// 0 = nothing in common, 1 = identical. Weighted toward what reads at a distance: silhouette (height, build),
        /// skin, hair, then outfit. Used to keep generated crowds and named characters visually distinct.
        /// </summary>
        public static float Similarity(CharacterAppearance a, CharacterAppearance b)
        {
            float s = 0f, w = 0f;
            void Add(float weight, float same) { s += weight * same; w += weight; }
            Add(2f, 1f - Math.Min(1f, Math.Abs(a.Body.HeightCm - b.Body.HeightCm) / 20f));
            Add(2f, 1f - Math.Min(1f, Math.Abs(a.Body.Weight - b.Body.Weight) * 2f));
            Add(1f, 1f - Math.Min(1f, Math.Abs(a.Body.Muscle - b.Body.Muscle) * 2f));
            Add(2f, a.SkinTone == b.SkinTone ? 1f : 0f);
            Add(2f, a.Hair == b.Hair ? 1f : 0f);
            Add(1f, a.HairColor == b.HairColor ? 1f : 0f);
            Add(1f, a.FacialHair == b.FacialHair ? 1f : 0f);
            Add(1f, a.FacePreset == b.FacePreset ? 1f : 0f);
            foreach (OutfitSlot slot in Enum.GetValues(typeof(OutfitSlot)))
            {
                a.Outfit.TryGetValue(slot, out var pa); b.Outfit.TryGetValue(slot, out var pb);
                float weight = slot == OutfitSlot.Top || slot == OutfitSlot.Outerwear || slot == OutfitSlot.Head ? 1.5f : 0.75f;
                if (pa == null && pb == null) { Add(weight, 1f); continue; }
                if (pa == null || pb == null) { Add(weight, 0f); continue; }
                Add(weight, pa.ItemId == pb.ItemId ? (pa.Colorway == pb.Colorway ? 1f : 0.5f) : 0f);
            }
            return w <= 0 ? 0 : s / w;
        }

        private static bool IsHex(string h)
        {
            if (h == null || h.Length != 7 || h[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (Uri.IsHexDigit(h[i]) == false) return false;
            return true;
        }
    }

    internal static class ListExt
    {
        public static IEnumerable<T> ConcatAll<T>(this List<T> a, params List<T>[] more)
        {
            foreach (var x in a) yield return x;
            foreach (var l in more) foreach (var x in l) yield return x;
        }
    }
}
