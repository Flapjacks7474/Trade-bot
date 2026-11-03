using System.Linq;
using FFTS.Core;
using FFTS.Crews;
using FFTS.Economy;
using FFTS.Relationships;
using FFTS.Reputation;
using FFTS.Session;
using FFTS.Story;
using FFTS.World;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class BankTests
    {
        private static FinessBank Bank() => FinessBank.CreateStarter(new EventBus(), () => 0, 10000);

        [Test]
        public void DuplicateIdempotencyKeyNeverPostsTwice()
        {
            var bank = Bank();
            var req = new TransactionRequest { IdempotencyKey = "reward.x", Kind = TransactionKind.Reward, To = FinessBank.Cash, AmountCents = 500 };
            Assert.AreEqual(PostStatus.Posted, bank.Post(req));
            Assert.AreEqual(PostStatus.Duplicate, bank.Post(req));
            Assert.AreEqual(10500, bank.Balance(FinessBank.Cash));
        }

        [Test]
        public void InsufficientFundsAndInvalidRequestsAreRejectedWithoutSideEffects()
        {
            var bank = Bank();
            Assert.AreEqual(PostStatus.InsufficientFunds, bank.Post(new TransactionRequest { IdempotencyKey = "k1", From = FinessBank.Cash, AmountCents = 999999 }));
            Assert.AreEqual(PostStatus.InvalidAmount, bank.Post(new TransactionRequest { IdempotencyKey = "k2", To = FinessBank.Cash, AmountCents = -5 }));
            Assert.AreEqual(PostStatus.UnknownAccount, bank.Post(new TransactionRequest { IdempotencyKey = "k3", To = "acct.nope", AmountCents = 5 }));
            Assert.AreEqual(10000, bank.Balance(FinessBank.Cash));
            Assert.IsFalse(bank.WasPosted("k1"), "failed requests must not burn their key");
        }

        [Test]
        public void TransfersAndBusinessMoneyStaySeparate()
        {
            var bank = Bank();
            bank.OpenAccount("acct.biz.southside_motors", AccountKind.Business, "business.southside_motors");
            Assert.Throws<System.ArgumentException>(() => bank.OpenAccount("acct.biz.bad", AccountKind.Business, FinessBank.PlayerOwner));
            bank.Post(new TransactionRequest { IdempotencyKey = "sale.1", Kind = TransactionKind.Sale, To = "acct.biz.southside_motors", AmountCents = 300000 });
            Assert.AreEqual(10000, bank.PersonalTotal(), "business revenue is not personal money");
            Assert.AreEqual(PostStatus.Posted, bank.Post(new TransactionRequest { IdempotencyKey = "draw.1", Kind = TransactionKind.Transfer, From = "acct.biz.southside_motors", To = FinessBank.Checking, AmountCents = 50000 }));
            Assert.AreEqual(60000, bank.PersonalTotal());
            Assert.AreEqual(250000, bank.Balance("acct.biz.southside_motors"));
        }

        [Test]
        public void PayUsesCashThenCheckingAndIsIdempotent()
        {
            var bank = Bank();
            bank.Post(new TransactionRequest { IdempotencyKey = "dep", To = FinessBank.Checking, AmountCents = 100000 });
            Assert.AreEqual(PostStatus.Posted, bank.Pay("bill.phone.1", 50000, TransactionKind.Bill, "Phone"));
            Assert.AreEqual(50000, bank.Balance(FinessBank.Checking));
            Assert.AreEqual(PostStatus.Duplicate, bank.Pay("bill.phone.1", 50000, TransactionKind.Bill, "Phone"));
            Assert.AreEqual("$1,234.05", FinessBank.Format(123405));
        }

        [Test]
        public void BankStateRoundTripsAndKeepsDedupe()
        {
            var bank = Bank();
            bank.Post(new TransactionRequest { IdempotencyKey = "once", To = FinessBank.Cash, AmountCents = 1 });
            var restored = new FinessBank(new EventBus(), () => 0);
            restored.Restore(bank.Capture());
            Assert.AreEqual(10001, restored.Balance(FinessBank.Cash));
            Assert.AreEqual(PostStatus.Duplicate, restored.Post(new TransactionRequest { IdempotencyKey = "once", To = FinessBank.Cash, AmountCents = 1 }));
        }
    }

    public class ReputationTests
    {
        [Test]
        public void SameFightMovesFaceCardAndPublicImageDifferently()
        {
            var rep = new ReputationManager(new EventBus());
            int prof = rep.Get(PublicImageAttribute.Professionalism);
            rep.Apply(FightConsequences.ReputationFor(VenueType.Underground, true, FFTS.Combat.FightEndMethod.Knockout), "t");
            Assert.Greater(rep.Get(FaceCardAttribute.Respect), 0);
            Assert.Greater(rep.Get(FaceCardAttribute.Fear), 0);
            Assert.Less(rep.Get(PublicImageAttribute.Professionalism), prof);

            var clean = new ReputationManager(new EventBus());
            clean.Apply(FightConsequences.ReputationFor(VenueType.Sanctioned, true, FFTS.Combat.FightEndMethod.Knockout), "t");
            Assert.Greater(clean.Get(PublicImageAttribute.Popularity), 0);
            Assert.Greater(clean.Get(PublicImageAttribute.Professionalism), prof);
        }

        [Test]
        public void ValuesClampTo0To100()
        {
            var rep = new ReputationManager(new EventBus());
            rep.Apply(new ReputationChange().Face(FaceCardAttribute.Respect, 500).Image(PublicImageAttribute.Credibility, -500), "t");
            Assert.AreEqual(100, rep.Get(FaceCardAttribute.Respect));
            Assert.AreEqual(0, rep.Get(PublicImageAttribute.Credibility));
        }
    }

    public class SocialTests
    {
        private static WorldEvent Humiliation(params string[] witnesses)
        {
            var e = new WorldEvent { EventId = "event.humiliation.1", EventType = "player.humiliated", ActorId = "player", TargetId = "npc.rell", LocationId = "location.southside.tone_studio" };
            e.Witnesses.AddRange(witnesses);
            return e;
        }

        [Test]
        public void OnlyWitnessesKnowAnEvent()
        {
            var s = new SocialManager(new EventBus());
            s.Record(Humiliation("npc.rell", "npc.tone"));
            Assert.IsTrue(s.Knows("npc.tone", "event.humiliation.1"));
            Assert.IsFalse(s.Knows("npc.dre", "event.humiliation.1"));
            Assert.AreEqual(KnowledgeSource.Witnessed, s.FindMemory("npc.tone", "event.humiliation.1").Source);
        }

        [Test]
        public void RumorsNeedASourceAndDegrade()
        {
            var s = new SocialManager(new EventBus());
            s.Record(Humiliation("npc.tone"));
            Assert.IsFalse(s.ShareRumor("npc.dre", "npc.lou", "event.humiliation.1"), "Dre can't spread what he doesn't know");
            Assert.IsTrue(s.ShareRumor("npc.tone", "npc.dre", "event.humiliation.1"));
            var m = s.FindMemory("npc.dre", "event.humiliation.1");
            Assert.AreEqual(KnowledgeSource.HeardRumor, m.Source);
            Assert.AreEqual("npc.tone", m.HeardFrom);
            Assert.Less(m.Reliability, 1f);
            // Repeated retelling eventually stops being believed.
            string[] chain = { "npc.dre", "npc.lou", "npc.nia", "npc.marlo", "npc.grandma", "npc.brick", "npc.x1" };
            int spread = 0;
            for (int i = 0; i + 1 < chain.Length; i++) if (s.ShareRumor(chain[i], chain[i + 1], "event.humiliation.1")) spread++;
            Assert.Less(spread, chain.Length - 1);
        }

        [Test]
        public void MemoryChangesAttitudeUntilResolved()
        {
            var s = new SocialManager(new EventBus());
            Assert.AreEqual(Attitude.Neutral, s.EvaluateAttitude("npc.rell", "player"));
            s.Record(Humiliation("npc.rell"));
            Assert.That(s.EvaluateAttitude("npc.rell", "player"), Is.LessThan(Attitude.Neutral));
            s.Resolve("npc.rell", "event.humiliation.1");
            Assert.AreEqual(Attitude.Neutral, s.EvaluateAttitude("npc.rell", "player"));
        }

        [Test]
        public void PersonalityChangesReactionToTheSameThreat()
        {
            var s = new SocialManager(new EventBus());
            s.SetPersonality("npc.brave", new Personality { Courage = 85 });
            s.SetPersonality("npc.timid", new Personality { Courage = 15 });
            var threat = new RelationshipDelta { Fear = 45 };
            s.Modify("npc.brave", "player", threat, "t");
            s.Modify("npc.timid", "player", threat, "t");
            Assert.AreEqual(Attitude.Intimidated, s.EvaluateAttitude("npc.timid", "player"));
            Assert.AreNotEqual(Attitude.Intimidated, s.EvaluateAttitude("npc.brave", "player"));
        }

        [Test]
        public void TemperAmplifiesResentment()
        {
            var s = new SocialManager(new EventBus());
            s.SetPersonality("npc.hot", new Personality { Temper = 90, Patience = 10 });
            s.SetPersonality("npc.calm", new Personality { Temper = 10, Patience = 90 });
            s.Modify("npc.hot", "player", new RelationshipDelta { Resentment = 20 }, "t");
            s.Modify("npc.calm", "player", new RelationshipDelta { Resentment = 20 }, "t");
            Assert.Greater(s.Get("npc.hot", "player").Resentment, s.Get("npc.calm", "player").Resentment);
        }

        [Test]
        public void SocialStateRoundTrips()
        {
            var s = new SocialManager(new EventBus());
            s.Record(Humiliation("npc.rell"));
            s.Modify("npc.rell", "player", new RelationshipDelta { Rivalry = 30 }, "t");
            var r = new SocialManager(new EventBus());
            r.Restore(Newtonsoft.Json.JsonConvert.DeserializeObject<SocialState>(Newtonsoft.Json.JsonConvert.SerializeObject(s.Capture())));
            Assert.AreEqual(30, r.Get("npc.rell", "player").Rivalry);
            Assert.IsTrue(r.Knows("npc.rell", "event.humiliation.1"));
        }
    }

    public class StoryGuardTests
    {
        [Test]
        public void ChairmanRevealIsBlockedBeforeChapterSevenMission()
        {
            var story = new StoryManager(new EventBus());
            Assert.IsFalse(story.TrySet(StoryFlags.ChairmanIdentityRevealed));
            story.AdvanceChapter(7);
            Assert.IsFalse(story.TrySet(StoryFlags.ChairmanIdentityRevealed), "chapter alone is not enough");
            story.MissionReached = id => id == StoryFlags.RevealMission;
            Assert.IsTrue(story.TrySet(StoryFlags.ChairmanIdentityRevealed));
            Assert.AreEqual(2, story.BlockedAttempts.Count);
        }

        [Test]
        public void ChaptersOnlyMoveForward()
        {
            var story = new StoryManager(new EventBus());
            Assert.IsTrue(story.AdvanceChapter(3));
            Assert.IsFalse(story.AdvanceChapter(2));
            Assert.IsFalse(story.AdvanceChapter(9));
            Assert.AreEqual(3, story.Chapter);
        }
    }

    public class ContentTests
    {
        [Test]
        public void AllShippedContentValidatesAndCrossReferences()
        {
            var report = new ValidationReport();
            GameContentLoader.Load(TestContent.Sources(), report, out var layouts);
            CrewFile.Parse(TestData.Read("Crews/crews.json"), report);
            Newtonsoft.Json.JsonConvert.DeserializeObject<DistrictFile>(TestData.Read("World/districts.json"));
            Assert.IsTrue(report.IsValid, report.ToString());
            Assert.AreEqual(1, layouts.Count);
        }

        [Test]
        public void SouthsideHasEveryRequiredSliceLocation()
        {
            var layout = DistrictLayout.Parse(TestData.Read("World/southside_layout.json"), new ValidationReport());
            foreach (var type in new[] { "grandma_house", "tone_studio", "community_gym", "underground_venue", "barbershop", "clothing_store", "used_cars" })
                Assert.IsNotNull(layout.Find("location.southside." + type), type);
        }

        [Test]
        public void EightDistrictsFiveCrewsAndCastingGateDefaultsClosed()
        {
            var d = Newtonsoft.Json.JsonConvert.DeserializeObject<DistrictFile>(TestData.Read("World/districts.json"));
            Assert.AreEqual(8, d.Districts.Count);
            var crews = CrewFile.Parse(TestData.Read("Crews/crews.json"), new ValidationReport());
            Assert.AreEqual(5, crews.Crews.Count);
            foreach (var c in crews.Crews)
            {
                Assert.IsFalse(c.Leader.LikenessRightsCleared, c.Id);
                Assert.AreEqual(c.Leader.PlaceholderName, c.Leader.DisplayName, "uncleared casting must show the placeholder name");
            }
        }

        [Test]
        public void CrossCheckCatchesUnreachableTriggers()
        {
            var report = new ValidationReport();
            var src = TestContent.Sources();
            src.Missions[0] = src.Missions[0].Replace("\"dialogue.tone.demo_recorded\"", "\"dialogue.tone.typo\"");
            GameContentLoader.Load(src, report, out _);
            Assert.IsTrue(report.Errors.Any(e => e.Contains("dialogue.tone.typo")), report.ToString());
        }
    }
}
