using System.Collections.Generic;
using System.Linq;
using FFTS.Characters;
using FFTS.Core;
using FFTS.Economy;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FFTS.Tests
{
    /// <summary>Character appearance and customisation. Mirrored by WebPrototype/tests/appearance.test.mjs.</summary>
    public sealed class AppearanceTests
    {
        private static AppearanceCatalog Catalog(out ValidationReport report)
        {
            report = new ValidationReport();
            return AppearanceCatalog.Parse(TestData.Appearance(), report);
        }

        private static AppearanceCatalog Catalog()
        {
            var c = Catalog(out var r);
            Assert.IsTrue(r.IsValid, r.ToString());
            return c;
        }

        private static CharacterAppearance Copy(CharacterAppearance a) => JsonConvert.DeserializeObject<CharacterAppearance>(JsonConvert.SerializeObject(a));

        [Test]
        public void CatalogAndAuthoredLooksValidate()
        {
            Catalog(out var report);
            Assert.IsTrue(report.IsValid, report.ToString());
        }

        [Test]
        public void PlayerDefaultLookFollowsTheReferenceCharacter()
        {
            var l = Catalog().Looks["look.player.default"];
            Assert.AreEqual("hair.shoulder_dreads", l.Hair);
            Assert.AreEqual("cloth.knit_beanie", l.Outfit[OutfitSlot.Head].ItemId);
            Assert.AreEqual("cloth.layered_chains", l.Outfit[OutfitSlot.Neck].ItemId);
            Assert.AreEqual("cloth.cargo_pants", l.Outfit[OutfitSlot.Bottoms].ItemId);
            Assert.AreEqual("cloth.court_hightop", l.Outfit[OutfitSlot.Shoes].ItemId);
        }

        [Test]
        public void NamedCharactersAreDistinct()
        {
            var looks = Catalog().Looks.ToList();
            for (int i = 0; i < looks.Count; i++)
                for (int j = i + 1; j < looks.Count; j++)
                    Assert.Less(AppearanceCatalog.Similarity(looks[i].Value, looks[j].Value), 0.6f, looks[i].Key + " vs " + looks[j].Key);
        }

        [Test]
        public void InvalidLooksAreRejectedWithReasons()
        {
            var cat = Catalog();
            var l = Copy(cat.Looks["look.player.default"]);
            l.Body.HeightCm = 120; l.Hair = "hair.afro";
            l.Outfit.Remove(OutfitSlot.Shoes);
            l.Outfit[OutfitSlot.Top] = new OutfitPiece { ItemId = "cloth.cargo_pants", Colorway = "fab.olive" };
            var p = string.Join(" | ", cat.Check(l));
            StringAssert.Contains("height", p);
            StringAssert.Contains("can't be worn under a Beanie", p);
            StringAssert.Contains("missing Shoes", p);
            StringAssert.Contains("is a Bottoms item", p);
        }

        [Test]
        public void GeneratedCrowdIsValidDeterministicAndHasNoLookAlikes()
        {
            var cat = Catalog();
            Assert.AreEqual(JsonConvert.SerializeObject(AppearanceGenerator.Generate(cat, 42)), JsonConvert.SerializeObject(AppearanceGenerator.Generate(cat, 42)));
            var crowd = new List<CharacterAppearance>();
            for (int i = 1; i <= 60; i++)
            {
                var a = AppearanceGenerator.Generate(cat, (ulong)(1000 + i), i % 3 == 0 ? "streetwear" : i % 3 == 1 ? "workwear" : null, crowd);
                CollectionAssert.IsEmpty(cat.Check(a), "npc " + i);
                crowd.Add(a);
            }
            float worst = 0;
            for (int i = 0; i < crowd.Count; i++)
                for (int j = i + 1; j < crowd.Count; j++) worst = System.Math.Max(worst, AppearanceCatalog.Similarity(crowd[i], crowd[j]));
            Assert.LessOrEqual(worst, AppearanceGenerator.MaxSimilarity);
        }

        [Test]
        public void WardrobeChargesOnceBarberPerVisitAndRoundTrips()
        {
            var cat = Catalog();
            var bank = FinessBank.CreateStarter(new EventBus(), () => 0, 100000);
            var w = new WardrobeManager(cat, bank, null);
            Assert.AreEqual(WardrobeResult.NotOwned, w.Wear("cloth.puffer", "fab.orange"));
            Assert.AreEqual(WardrobeResult.NotSoldHere, w.Buy("cloth.puffer", "store.pecan_kicks"));
            Assert.AreEqual(WardrobeResult.Ok, w.Buy("cloth.puffer", "store.magnolia_flagship"));
            long after = bank.PersonalTotal();
            Assert.AreEqual(WardrobeResult.AlreadyOwned, w.Buy("cloth.puffer", "store.magnolia_flagship"));
            Assert.AreEqual(after, bank.PersonalTotal());
            Assert.AreEqual(WardrobeResult.Ok, w.Wear("cloth.puffer", "fab.orange"));
            Assert.AreEqual(WardrobeResult.InvalidLook, w.Remove(OutfitSlot.Shoes));
            Assert.AreEqual(WardrobeResult.InvalidLook, w.Barber("store.fade_kings", "hair.afro", "haircol.black", null));
            Assert.AreEqual(WardrobeResult.Ok, w.Remove(OutfitSlot.Head));
            long before = bank.PersonalTotal();
            Assert.AreEqual(WardrobeResult.Ok, w.Barber("store.fade_kings", "hair.afro", "haircol.black", null));
            Assert.AreEqual(WardrobeResult.Ok, w.Barber("store.fade_kings", "hair.waves", "haircol.black", "facial.stubble"));
            Assert.AreEqual(3000, before - bank.PersonalTotal());
            var copy = new WardrobeManager(cat, bank, null);
            copy.Restore(JsonConvert.DeserializeObject<WardrobeState>(JsonConvert.SerializeObject(w.Capture())));
            Assert.AreEqual(JsonConvert.SerializeObject(w.Capture()), JsonConvert.SerializeObject(copy.Capture()));
        }

        [Test]
        public void SessionSavesTheLookAndOlderSavesGetTheDefault()
        {
            var s = TestContent.NewSession();
            Assert.IsNotNull(s.Wardrobe);
            Assert.AreEqual("hair.shoulder_dreads", s.Wardrobe.Look.Hair);
            var save = s.Capture();
            save.Wardrobe = null; // a save from before customisation existed
            var t = TestContent.NewSession();
            t.Restore(save);
            Assert.AreEqual("hair.shoulder_dreads", t.Wardrobe.Look.Hair);
        }
    }
}
