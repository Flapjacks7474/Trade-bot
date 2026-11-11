using System.Collections.Generic;
using System.Linq;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Economy;
using NUnit.Framework;

namespace FFTS.Tests
{
    /// <summary>Fighting styles, stances and hybrids. Mirrored by WebPrototype/tests/styles.test.mjs.</summary>
    public sealed class StyleTests
    {
        private static FighterInput Idle => FighterInput.None;

        private static List<CombatEvent> Run(CombatSimulation sim, int ticks, System.Func<int, FighterInput> a, System.Func<int, FighterInput> b = null)
        {
            var all = new List<CombatEvent>();
            for (int i = 0; i < ticks && !sim.IsOver; i++) { sim.Step(a(i), b != null ? b(i) : Idle); all.AddRange(sim.Events); }
            return all;
        }

        private static CombatSimulation FightWith(CombatDatabase db, string[] aStyles, string[] bStyles = null, float gap = 1f)
        {
            var pa = db.GetFighter("fighter.player"); var pb = db.GetFighter("fighter.rell");
            return new CombatSimulation(new CombatConfig(),
                new FighterRuntime(pa, db.BuildMoveset(aStyles, true), -gap / 2f),
                new FighterRuntime(pb, db.BuildMoveset(bStyles ?? new[] { "style.street" }, true), gap / 2f), "t");
        }

        [Test]
        public void TenBaseStylesAndSixHybridsShipAndValidate()
        {
            var db = TestData.Combat(out var report);
            Assert.IsTrue(report.IsValid, report.ToString());
            Assert.AreEqual(10, db.Styles.Count(s => !s.IsHybrid));
            Assert.GreaterOrEqual(db.Styles.Count(s => s.IsHybrid), 6);
        }

        [Test]
        public void EveryBaseStyleIsACompleteKitWithItsOwnFinisher()
        {
            var db = TestData.Combat();
            foreach (var s in db.Styles.Where(x => !x.IsHybrid))
            {
                var ms = Moveset.Build(new[] { s });
                Assert.IsNotNull(ms.Light, s.Id); Assert.IsNotNull(ms.Heavy, s.Id); Assert.IsNotNull(ms.Kick, s.Id);
                Assert.IsNotNull(ms.Grapple, s.Id); Assert.IsNotNull(ms.Counter, s.Id); Assert.IsNotNull(ms.Finisher, s.Id);
                Assert.IsTrue(s.Moves.Contains(ms.Finisher), s.Id + " finisher must be its own");
                Assert.IsNotNull(s.Stance, s.Id);
            }
        }

        [Test]
        public void StylesHaveDistinctStancesAndUniqueAnimationKeys()
        {
            var db = TestData.Combat();
            var seen = new Dictionary<string, string>();
            foreach (var s in db.Styles)
            {
                var st = s.Stance;
                string sig = string.Join(",", st.WalkSpeed, st.StrikeDamage, st.KickDamage, st.ThrowDamage, st.ParryWindowBonus, st.CounterWindowBonus,
                    st.BlockChip, st.GuardDrain, st.DodgeDistance, st.DodgeCost, st.EscapeRate, st.ClinchStrikesBonus);
                Assert.IsFalse(seen.ContainsKey(sig), $"{s.Id} copies the stance of {(seen.ContainsKey(sig) ? seen[sig] : "")}");
                seen[sig] = s.Id;
            }
            var keys = db.Styles.SelectMany(s => s.Moves).Select(m => m.AnimationKey).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void HybridAppliesForItsParentPairInEitherOrder()
        {
            var db = TestData.Combat();
            Assert.AreEqual("hybrid.southside_hands", db.FindHybrid("style.street", "style.boxing").Id);
            Assert.AreEqual("hybrid.southside_hands", db.FindHybrid("style.boxing", "style.street").Id);
            Assert.IsNull(db.FindHybrid("style.boxing", "style.karate"));
            var ms = db.BuildMoveset(new[] { "style.street", "style.boxing" }, true);
            Assert.AreEqual("hybrid.southside_hands", ms.HybridId);
            Assert.AreEqual("southside_hands.last_call", ms.Finisher.Id);
            Assert.AreEqual("Low Shell", ms.Stance.Name);
            Assert.AreEqual("southside_hands.double_hook", ms.FindLink("boxing.jab", CombatCommand.Heavy).Id);
            Assert.IsNull(db.BuildMoveset(new[] { "style.street", "style.boxing" }, true, new List<string>()).HybridId, "a locked hybrid must not apply");
        }

        [Test]
        public void FightersListParentsNeverHybrids()
        {
            var report = new ValidationReport();
            string fighters = @"{ ""Fighters"": [ { ""Id"": ""f.h"", ""DisplayName"": ""H"", ""Styles"": [""hybrid.tricking""] } ] }";
            CombatDatabase.FromJson(TestData.AllStyles(), new[] { fighters }, report);
            Assert.IsTrue(report.Errors.Any(e => e.Contains("not the hybrid")), report.ToString());
        }

        [Test]
        public void MuayThaiStanceAllowsFiveClinchKneesStreetThree()
        {
            var db = TestData.Combat();
            int Knees(string style)
            {
                var sim = FightWith(db, new[] { style });
                var ev = Run(sim, 200, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : i % 21 == 15 ? FighterInput.Press(CombatCommand.Light) : Idle,
                                       i => FighterInput.Hold());
                return ev.Count(e => e.Type == CombatEventType.ClinchStrike);
            }
            Assert.AreEqual(3, Knees("style.street"));
            Assert.AreEqual(5, Knees("style.muay_thai"));
        }

        [Test]
        public void StreetStanceIsNeutralSoOriginalNumbersHold()
        {
            var db = TestData.Combat();
            var st = db.GetStyle("style.street").Stance;
            Assert.AreEqual(1f, st.StrikeDamage); Assert.AreEqual(1f, st.KickDamage); Assert.AreEqual(1f, st.WalkSpeed);
            Assert.AreEqual(0, st.ParryWindowBonus); Assert.AreEqual(0, st.ClinchStrikesBonus);
        }

        [Test]
        public void TaekwondoKickOutdamagesStreetKick()
        {
            var db = TestData.Combat();
            float FirstHit(string style) => Run(FightWith(db, new[] { style }, null, 1.2f), 60, i => i == 0 ? FighterInput.Press(CombatCommand.Kick) : Idle)
                .First(e => e.Type == CombatEventType.Hit).Amount;
            Assert.Greater(FirstHit("style.taekwondo"), FirstHit("style.street") * 1.4f);
        }

        [Test]
        public void LearningAStyleChargesOnceAndHybridsNeedBothParents()
        {
            var db = TestData.Combat();
            var bus = new EventBus();
            var bank = FinessBank.CreateStarter(bus, () => 0, 100000);
            var lo = new StyleLoadout(db, bank, bus);
            Assert.AreEqual(StyleActionResult.ParentsNotLearned, lo.Learn("hybrid.southside_hands"));
            Assert.AreEqual(StyleActionResult.Ok, lo.Learn("style.boxing"));
            long after = bank.PersonalTotal();
            Assert.AreEqual(StyleActionResult.AlreadyLearned, lo.Learn("style.boxing"));
            Assert.AreEqual(after, bank.PersonalTotal());
            Assert.AreEqual(PostStatus.Duplicate, bank.Pay("style.learn.style.boxing", 25000, TransactionKind.Purchase, "again"));
            Assert.AreEqual(StyleActionResult.Ok, lo.Equip("style.street", "style.boxing"));
            Assert.IsNull(lo.ActiveHybrid);
            Assert.AreEqual(StyleActionResult.Ok, lo.Learn("hybrid.southside_hands"));
            Assert.AreEqual("hybrid.southside_hands", lo.ActiveHybrid.Id);
            Assert.AreEqual("southside_hands.last_call", lo.BuildMoveset().Finisher.Id);
            Assert.AreEqual(StyleActionResult.InvalidLoadout, lo.Equip("hybrid.southside_hands"));

            var poor = new StyleLoadout(db, FinessBank.CreateStarter(new EventBus(), () => 0, 1000), null);
            Assert.AreEqual(StyleActionResult.InsufficientFunds, poor.Learn("style.mma"));
        }

        [Test]
        public void StyleLoadoutSurvivesSaveAndDropsUnknownStyles()
        {
            var s = TestContent.NewSession();
            s.Bank.Post(new TransactionRequest { IdempotencyKey = "test.gift", Kind = TransactionKind.Reward, To = FinessBank.Cash, AmountCents = 200000, Memo = "gift" });
            Assert.AreEqual(StyleActionResult.Ok, s.Styles.Learn("style.wrestling"));
            Assert.AreEqual(StyleActionResult.Ok, s.Styles.Equip("style.wrestling", "style.street"));
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(s.Capture());
            var t = TestContent.NewSession();
            t.Restore(Newtonsoft.Json.JsonConvert.DeserializeObject<FFTS.Session.GameSaveData>(json));
            CollectionAssert.AreEqual(new[] { "style.wrestling", "style.street" }, t.Styles.Equipped.ToArray());
            t.Styles.Restore(new StyleLoadoutState { Learned = { "style.gone" }, Equipped = { "style.gone" } });
            CollectionAssert.AreEqual(new[] { "style.street" }, t.Styles.Equipped.ToArray());
        }
    }
}
