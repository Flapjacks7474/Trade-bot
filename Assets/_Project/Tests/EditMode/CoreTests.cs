using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FFTS.Core;
using FFTS.Input;
using FFTS.Interaction;
using FFTS.SaveSystem;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class CoreTests
    {
        private sealed class Ping : IGameEvent { public int N; }

        [Test]
        public void IdRulesRejectBadIdsAndRegistryCatchesDuplicates()
        {
            Assert.IsTrue(Ids.IsValid("mission.ch1.home_again"));
            foreach (var bad in new[] { "", "Upper.case", "a..b", ".a", "a.", "has space", new string('a', 97) })
                Assert.IsFalse(Ids.IsValid(bad), bad);
            var reg = new IdRegistry();
            Assert.IsTrue(reg.Register("npc.dre", "t"));
            Assert.IsFalse(reg.Register("npc.dre", "t"));
            Assert.AreEqual(1, reg.Errors.Count);
        }

        [Test]
        public void EventBusDeliversUnsubscribesAndIsolatesFailures()
        {
            var bus = new EventBus();
            int got = 0; Exception failure = null;
            bus.HandlerFailed += (e, ex) => failure = ex;
            bus.Subscribe<Ping>(_ => throw new InvalidCastException("boom"));
            var sub = bus.Subscribe<Ping>(p => got += p.N);
            bus.Publish(new Ping { N = 2 });
            Assert.AreEqual(2, got);
            Assert.IsInstanceOf<InvalidCastException>(failure);
            sub.Dispose();
            bus.Publish(new Ping { N = 5 });
            Assert.AreEqual(2, got);
        }

        [Test]
        public void EventBusDetectsEventLoops()
        {
            var bus = new EventBus();
            bus.Subscribe<Ping>(p => bus.Publish(new Ping { N = p.N + 1 }));
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping()));
        }

        [Test]
        public void ClockSegmentsAndEvents()
        {
            var bus = new EventBus();
            var segs = new List<TimeSegment>(); int days = 0;
            bus.Subscribe<TimeSegmentChanged>(e => segs.Add(e.Current));
            bus.Subscribe<DayChanged>(_ => days++);
            var clock = new GameClock(bus, 8 * 60);
            Assert.AreEqual(TimeSegment.Morning, clock.Segment);
            clock.Advance(24 * 60);
            CollectionAssert.AreEqual(new[] { TimeSegment.Afternoon, TimeSegment.Evening, TimeSegment.Night, TimeSegment.LateNight, TimeSegment.Morning }, segs);
            Assert.AreEqual(1, days);
            Assert.AreEqual(1, clock.Day);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        }

        [Test]
        public void RandomIsDeterministicAndRestorable()
        {
            var a = new DeterministicRandom(42); var b = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Range(0, 1000), b.Range(0, 1000));
            var state = a.State; int next = a.Range(0, 1000);
            b.Restore(state);
            Assert.AreEqual(next, b.Range(0, 1000));
            for (int i = 0; i < 1000; i++) { float v = a.Value(); Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f)); }
        }

        [Test]
        public void InteractionSelectorPrefersInFrontPriorityAndRange()
        {
            var cands = new[]
            {
                new InteractionCandidate { Id = "behind", Position = new Vector3(0, 0, -1), Radius = 3 },
                new InteractionCandidate { Id = "front_far", Position = new Vector3(0, 0, 2.5f), Radius = 3 },
                new InteractionCandidate { Id = "front_near", Position = new Vector3(0.2f, 0, 1), Radius = 3 },
                new InteractionCandidate { Id = "out_of_range", Position = new Vector3(0, 0, 10), Radius = 3, Priority = 10 },
            };
            Assert.AreEqual("front_near", InteractionSelector.SelectBest(Vector3.Zero, Vector3.UnitZ, cands).Id);
            cands[1].Priority = 1;
            Assert.AreEqual("front_far", InteractionSelector.SelectBest(Vector3.Zero, Vector3.UnitZ, cands).Id);
            cands[1].Enabled = false; cands[2].Enabled = false;
            Assert.IsNull(InteractionSelector.SelectBest(Vector3.Zero, Vector3.UnitZ, cands));
        }

        [Test]
        public void InputEdgesAndRebinding()
        {
            var f = InputFrame.WithEdges(new InputFrame { Held = ButtonMask.LightAttack | ButtonMask.Block }, ButtonMask.Block | ButtonMask.Dodge);
            Assert.IsTrue(f.WasPressed(ButtonMask.LightAttack));
            Assert.IsFalse(f.WasPressed(ButtonMask.Block));
            Assert.AreEqual(ButtonMask.Dodge, f.Released);

            var map = InputBindingMap.CreateDefault();
            Assert.IsEmpty(map.FindConflicts(onFoot: true), string.Join(",", map.FindConflicts(true)));
            map.Rebind(GameAction.LightAttack, "<Keyboard>/k");
            Assert.IsNotEmpty(map.FindConflicts(onFoot: true));
            Assert.That(map.Bindings, Has.Some.Matches<InputBinding>(b => b.Action == GameAction.LightAttack && b.Path == "<Gamepad>/buttonWest"),
                "rebinding the keyboard must not remove gamepad bindings");
        }
    }

    public class SaveSystemTests
    {
        public sealed class Sample { public int Version = 1; public string Name; public long Money; public List<string> Flags = new List<string>(); }

        private static SaveSlotManager<Sample> Manager(ISaveStorage storage, int version = 1, IEnumerable<ISaveMigration> migrations = null)
            => new SaveSlotManager<Sample>(storage, new SaveEnvelopeCodec(version, migrations));

        [Test]
        public void RoundTripInMemory()
        {
            var m = Manager(new InMemorySaveStorage());
            m.Save("manual_0", new Sample { Name = "Southside", Money = 12345, Flags = { "a", "b" } });
            var r = m.Load("manual_0");
            Assert.AreEqual(LoadOutcome.Loaded, r.Outcome);
            Assert.AreEqual(12345, r.Data.Money);
            CollectionAssert.AreEqual(new[] { "a", "b" }, r.Data.Flags);
        }

        [Test]
        public void CorruptPrimaryFallsBackToBackup()
        {
            var storage = new InMemorySaveStorage();
            var m = Manager(storage);
            m.Save("manual_0", new Sample { Money = 1 });
            m.Save("manual_0", new Sample { Money = 2 });
            string text = storage.Read("manual_0");
            storage.CorruptPrimary("manual_0", text.Replace("\\\"Money\\\":2", "\\\"Money\\\":999999"));
            var r = m.Load("manual_0");
            Assert.AreEqual(LoadOutcome.RecoveredFromBackup, r.Outcome, r.Message);
            Assert.AreEqual(1, r.Data.Money);
            StringAssert.Contains("Checksum", r.Message);
        }

        [Test]
        public void CorruptWithoutBackupIsReportedNotLoaded()
        {
            var storage = new InMemorySaveStorage();
            var m = Manager(storage);
            m.Save("manual_1", new Sample());
            storage.CorruptPrimary("manual_1", "{ not json");
            var r = m.Load("manual_1");
            Assert.AreEqual(LoadOutcome.Corrupt, r.Outcome);
            Assert.IsNull(r.Data);
            Assert.AreEqual(LoadOutcome.NotFound, m.Load("manual_9").Outcome);
        }

        private sealed class RenameMoneyToCash : ISaveMigration
        {
            public int FromVersion => 1;
            public void Apply(JObject p) { p["Money"] = (long)p["Cash"] * 100; p.Remove("Cash"); }
        }

        [Test]
        public void MigrationsUpgradeOldSaves()
        {
            var storage = new InMemorySaveStorage();
            var codec1 = new SaveEnvelopeCodec(1);
            storage.WriteAtomic("manual_0", codec1.Encode(new JObject { ["Name"] = "old", ["Cash"] = 7 }));
            var r = Manager(storage, 2, new[] { new RenameMoneyToCash() }).Load("manual_0");
            Assert.AreEqual(LoadOutcome.Loaded, r.Outcome, r.Message);
            Assert.AreEqual(700, r.Data.Money);
            Assert.AreEqual(1, r.OriginalVersion);

            var missing = Manager(storage, 3, new[] { new RenameMoneyToCash() }).Load("manual_0");
            Assert.AreEqual(LoadOutcome.MigrationFailed, missing.Outcome);
        }

        [Test]
        public void NewerSaveVersionIsRefused()
        {
            var storage = new InMemorySaveStorage();
            Manager(storage, 5).Save("manual_0", new Sample());
            Assert.AreEqual(LoadOutcome.UnsupportedVersion, Manager(storage, 4).Load("manual_0").Outcome);
        }

        [Test]
        public void AutosaveRotatesSlots()
        {
            var m = Manager(new InMemorySaveStorage());
            var slots = new List<string>();
            for (int i = 0; i < 4; i++) slots.Add(m.Autosave(new Sample { Money = i }));
            CollectionAssert.AreEqual(new[] { "autosave_0", "autosave_1", "autosave_2", "autosave_0" }, slots);
            Assert.AreEqual(3, m.Load("autosave_0").Data.Money);
        }

        [Test]
        public void FileStorageWritesAtomicallyWithBackup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ffts_save_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new FileSaveStorage(dir);
                var m = Manager(storage);
                m.Save("manual_0", new Sample { Money = 10 });
                m.Save("manual_0", new Sample { Money = 20 });
                Assert.IsTrue(storage.BackupExists("manual_0"));
                Assert.IsFalse(File.Exists(Path.Combine(dir, "manual_0.ffts.tmp")));
                File.WriteAllText(Path.Combine(dir, "manual_0.ffts"), "garbage");
                var r = m.Load("manual_0");
                Assert.AreEqual(LoadOutcome.RecoveredFromBackup, r.Outcome);
                Assert.AreEqual(10, r.Data.Money);
                Assert.Throws<ArgumentException>(() => m.Save("../escape", new Sample()));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
