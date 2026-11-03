using System.Collections.Generic;
using System.Linq;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Input;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class CombatTests
    {
        private static readonly FighterInput Idle = FighterInput.None;

        /// <summary>Steps until predicate or limit; returns all events seen.</summary>
        private static List<CombatEvent> Run(CombatSimulation sim, int ticks, System.Func<int, FighterInput> a = null, System.Func<int, FighterInput> b = null)
        {
            var all = new List<CombatEvent>();
            for (int i = 0; i < ticks && !sim.IsOver; i++)
            {
                sim.Step(a?.Invoke(i) ?? Idle, b?.Invoke(i) ?? Idle);
                all.AddRange(sim.Events);
            }
            return all;
        }

        private static void KnockDownB(CombatSimulation sim)
        {
            Run(sim, 1, i => FighterInput.Press(CombatCommand.Grapple));
            for (int i = 0; i < 200 && sim.B.State != CombatState.KnockedDown; i++)
                sim.Step(i == 24 ? FighterInput.Press(CombatCommand.Heavy) : Idle, Idle);
            Assert.AreEqual(CombatState.KnockedDown, sim.B.State);
        }

        [Test]
        public void ShippedCombatDataIsValid()
        {
            TestData.Combat(out var report);
            Assert.IsTrue(report.IsValid, report.ToString());
        }

        [Test]
        public void ValidationRejectsDuplicateIdsBadStatsAndCycles()
        {
            var report = new ValidationReport();
            string styles = @"{ ""Styles"": [ { ""Id"": ""style.bad"", ""Moves"": [
                { ""Id"": ""m.a"", ""Kind"": ""Light"", ""Startup"": 5, ""Active"": 2, ""Recovery"": 10, ""Range"": 1, ""CancelWindow"": 5, ""AnimationKey"": ""a"" },
                { ""Id"": ""m.b"", ""Kind"": ""Light"", ""Startup"": 5, ""Active"": 2, ""Recovery"": 10, ""Range"": 1, ""CancelWindow"": 5, ""AnimationKey"": ""b"" } ],
                ""Light"": ""m.a"", ""Links"": [ { ""From"": ""m.a"", ""Command"": ""Light"", ""To"": ""m.b"" }, { ""From"": ""m.b"", ""Command"": ""Light"", ""To"": ""m.a"" } ] } ] }";
            string fighters = @"{ ""Fighters"": [ { ""Id"": ""f.x"", ""DisplayName"": ""X"", ""Styles"": [""style.bad""], ""Stats"": { ""Strength"": 150 } },
                                               { ""Id"": ""f.x"", ""DisplayName"": ""X2"", ""Styles"": [""style.bad""] } ] }";
            CombatDatabase.FromJson(new[] { styles }, new[] { fighters }, report);
            Assert.IsTrue(report.Errors.Any(e => e.Contains("cycle")), report.ToString());
            Assert.IsTrue(report.Errors.Any(e => e.Contains("duplicate id 'f.x'")), report.ToString());
            Assert.IsTrue(report.Errors.Any(e => e.Contains("Strength=150")), report.ToString());
        }

        [Test]
        public void MovesetCombinesUpToThreeStyles()
        {
            var db = TestData.Combat();
            var s = db.GetStyle("style.street");
            Assert.DoesNotThrow(() => Moveset.Build(new[] { s, s, s }));
            Assert.Throws<System.ArgumentException>(() => Moveset.Build(new[] { s, s, s, s }));
        }

        [Test]
        public void LightAttackHitsAndDealsDamage()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 30, i => i == 0 ? FighterInput.Press(CombatCommand.Light) : Idle);
            var hit = ev.Single(e => e.Type == CombatEventType.Hit);
            Assert.AreEqual("street.jab", hit.MoveId);
            Assert.Less(sim.B.Health, sim.B.MaxHealth);
            Assert.Greater(sim.A.Momentum, 0f);
        }

        [Test]
        public void LightLightHeavyComboLandsThreeHitsWithDamageScaling()
        {
            var sim = TestData.Fight();
            // Mash the route; the input buffer lets early presses land in the cancel window.
            var ev = Run(sim, 80, i => i == 0 ? FighterInput.Press(CombatCommand.Light)
                                    : i == 8 ? FighterInput.Press(CombatCommand.Light)
                                    : i == 18 ? FighterInput.Press(CombatCommand.Heavy) : Idle);
            var hits = ev.Where(e => e.Type == CombatEventType.Hit).ToList();
            CollectionAssert.AreEqual(new[] { "street.jab", "street.cross", "street.haymaker" }, hits.Select(h => h.MoveId).ToArray());
            Assert.IsTrue(hits[2].Detail.EndsWith("3"), "third hit should be combo hit 3");
            // Scaling: haymaker's raw 14 damage is reduced at combo count 2.
            var db = TestData.Combat();
            var raw = 14f * sim.A.StrengthMultiplier * sim.B.DurabilityMultiplier;
            Assert.Less(hits[2].Amount, raw);
        }

        [Test]
        public void LightHeavyKickRouteEndsInKnockdown()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 90, i => i == 0 ? FighterInput.Press(CombatCommand.Light)
                                    : i == 8 ? FighterInput.Press(CombatCommand.Heavy)
                                    : i == 22 ? FighterInput.Press(CombatCommand.Kick) : Idle);
            CollectionAssert.AreEqual(new[] { "street.jab", "street.hook", "street.roundhouse" },
                ev.Where(e => e.Type == CombatEventType.Hit).Select(h => h.MoveId).ToArray());
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.KnockedDown && e.ActorId == "fighter.rell"));
        }

        [Test]
        public void HeldBlockBlocksStrikeAndTakesOnlyChip()
        {
            var sim = TestData.Fight();
            Run(sim, 20, null, i => FighterInput.Hold()); // B holds block long before the attack (no parry)
            float before = sim.B.Health;
            var ev = Run(sim, 30, i => i == 0 ? FighterInput.Press(CombatCommand.Heavy) : Idle, i => FighterInput.Hold());
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Blocked));
            Assert.IsFalse(ev.Any(e => e.Type == CombatEventType.Hit));
            Assert.Less(before - sim.B.Health, 3f);
        }

        [Test]
        public void BlockingDrainsStaminaUntilGuardBreak()
        {
            var sim = TestData.Fight("fighter.brick", "fighter.player");
            var ev = Run(sim, 900, i => i % 40 == 0 ? FighterInput.Press(CombatCommand.Heavy) : FighterInput.Walk(1f), i => FighterInput.Hold());
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.GuardBreak), "repeated heavies should eventually break a held guard");
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Blocked));
        }

        [Test]
        public void TimedBlockParriesIntoCounter()
        {
            var sim = TestData.Fight();
            // Jab startup ~5 ticks after Speed adjustment. Defender taps block 2 ticks before impact.
            var ev = Run(sim, 40, i => i == 0 ? FighterInput.Press(CombatCommand.Light) : Idle,
                                  i => i >= 3 ? FighterInput.Hold() : Idle);
            var counter = ev.SingleOrDefault(e => e.Type == CombatEventType.Countered);
            Assert.AreEqual("fighter.rell", counter.ActorId);
            Assert.AreEqual("parry", counter.Detail);
            Assert.Less(sim.A.Health, sim.A.MaxHealth);
        }

        [Test]
        public void CounterStanceBeatsHeavyThenCounterHeavySignatureRoute()
        {
            var sim = TestData.Fight("fighter.rell", "fighter.player");
            var ev = Run(sim, 120,
                i => i == 9 ? FighterInput.Press(CombatCommand.Counter) : (i == 18 || i == 30) ? FighterInput.Press(CombatCommand.Heavy) : Idle,
                i => i == 0 ? FighterInput.Press(CombatCommand.Heavy) : Idle);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Countered && e.Detail == "stance"), string.Join("\n", ev));
            var hits = ev.Where(e => e.Type == CombatEventType.Hit && e.ActorId == "fighter.rell").Select(e => e.MoveId).ToArray();
            CollectionAssert.AreEqual(new[] { "street.uppercut", "street.southside_signature" }, hits, string.Join("\n", ev));
        }

        [Test]
        public void GrappleBeatsBlockAndThrowKnocksDown()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 120, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : i == 25 ? FighterInput.Press(CombatCommand.Heavy) : Idle,
                                   i => FighterInput.Hold());
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.GrappleStarted));
            var thrown = ev.Single(e => e.Type == CombatEventType.Thrown);
            Assert.AreEqual("throw", thrown.Detail);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.KnockedDown && e.ActorId == "fighter.rell"));
        }

        [Test]
        public void StrikeBeatsGrappleOnSameTick()
        {
            var sim = TestData.Fight();
            // Clinch startup is slower than jab startup, so a simultaneous jab interrupts the grapple.
            var ev = Run(sim, 30, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : Idle,
                                  i => i == 0 ? FighterInput.Press(CombatCommand.Light) : Idle);
            Assert.IsFalse(ev.Any(e => e.Type == CombatEventType.GrappleStarted));
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Hit && e.ActorId == "fighter.rell"));
        }

        [Test]
        public void EnvironmentalThrowNearWallDealsMoreDamage()
        {
            var cfg = new CombatConfig();
            var sim = TestData.Fight(config: cfg);
            // Walk both fighters to the right wall.
            Run(sim, 200, i => FighterInput.Walk(1f), i => FighterInput.Walk(-1f));
            Assert.Greater(sim.B.X, cfg.ArenaHalfWidth - cfg.WallThrowDistance);
            var ev = Run(sim, 120, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : i == 25 ? FighterInput.Press(CombatCommand.Grapple) : Idle);
            var thrown = ev.Single(e => e.Type == CombatEventType.Thrown);
            Assert.AreEqual("environment", thrown.Detail);
            Assert.AreEqual("street.wall_slam", thrown.MoveId);
        }

        [Test]
        public void MashingEscapesTheClinch()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 120, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : Idle,
                                   i => (i > 12 && i % 3 == 0) ? FighterInput.Press(CombatCommand.Grapple) : Idle);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.GrappleEscaped));
            Assert.IsFalse(ev.Any(e => e.Type == CombatEventType.Thrown));
        }

        [Test]
        public void ReversalRequiresMomentumAndSwapsRoles()
        {
            var sim = TestData.Fight();
            sim.B.ApplyScenario(momentum: 40);
            var ev = Run(sim, 80, i => i == 0 ? FighterInput.Press(CombatCommand.Grapple) : Idle,
                                  i => i == 12 ? FighterInput.Press(CombatCommand.Counter) : Idle);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Reversal && e.ActorId == "fighter.rell"), string.Join("\n", ev));
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Thrown && e.ActorId == "fighter.rell"));
        }

        [Test]
        public void DodgeAvoidsAttackDuringInvulnerability()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 40, i => i == 0 ? FighterInput.Press(CombatCommand.Heavy) : Idle,
                                  i => i == 4 ? FighterInput.Press(CombatCommand.Dodge) : Idle);
            Assert.IsFalse(ev.Any(e => e.Type == CombatEventType.Hit));
            Assert.AreEqual(sim.B.MaxHealth, sim.B.Health);
        }

        [Test]
        public void KnockdownRecoversAndQuickRiseIsFaster()
        {
            int RecoveryTicks(bool quickRise)
            {
                var sim = TestData.Fight();
                KnockDownB(sim);
                int t = 0;
                while (sim.B.State != CombatState.Idle && t < 400)
                {
                    sim.Step(Idle, quickRise && t % 5 == 0 ? FighterInput.Press(CombatCommand.Dodge) : Idle);
                    t++;
                }
                return t;
            }
            int normal = RecoveryTicks(false), quick = RecoveryTicks(true);
            Assert.Less(normal, 400, "fighter must get up on their own");
            Assert.Less(quick, normal);
        }

        [Test]
        public void GroundAttackOnlyHitsDownedOpponent()
        {
            var sim = TestData.Fight();
            KnockDownB(sim);
            // A heavy on a standing opponent is a haymaker, not a stomp: confirm the stomp needs a downed target.
            while (sim.A.State != CombatState.Idle) sim.Step(Idle, Idle);
            Assert.AreEqual(CombatState.KnockedDown, sim.B.State);
            var ev = Run(sim, 40, i => i == 0 ? FighterInput.Press(CombatCommand.Heavy) : Idle);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.Hit && e.MoveId == "street.stomp"), string.Join("\n", ev) + "\nA=" + sim.A + "\nB=" + sim.B);
        }

        [Test]
        public void HitstunDecayPreventsInfiniteLightSpam()
        {
            var sim = TestData.Fight();
            bool escaped = false;
            for (int i = 0; i < 300 && !sim.IsOver; i++)
            {
                sim.Step(FighterInput.Press(CombatCommand.Light), Idle);
                if (i > 10 && sim.B.State == CombatState.Idle) { escaped = true; break; }
            }
            Assert.IsTrue(escaped, "defender must regain control during repeated light attacks");
        }

        [Test]
        public void StaminaPreventsEndlessHeavies()
        {
            var sim = TestData.Fight(distance: 4f); // whiff on purpose
            var ev = Run(sim, 900, i => FighterInput.Press(CombatCommand.Heavy));
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.NotEnoughStamina));
        }

        [Test]
        public void BreakawayEscapesLongCombo()
        {
            var sim = TestData.Fight();
            sim.B.ApplyScenario(momentum: 50);
            var ev = Run(sim, 60, i => i == 0 ? FighterInput.Press(CombatCommand.Light) : i == 8 ? FighterInput.Press(CombatCommand.Light) : i == 12 ? FighterInput.Press(CombatCommand.Heavy) : Idle,
                                  i => i == 20 ? FighterInput.Press(CombatCommand.Dodge) : Idle);
            // Breakaway needs 3 combo hits; after only 2 it should not fire.
            Assert.IsFalse(ev.Any(e => e.Type == CombatEventType.Breakaway));

            var sim2 = TestData.Fight();
            sim2.B.ApplyScenario(momentum: 50);
            var ev2 = new List<CombatEvent>();
            for (int i = 0; i < 120 && !sim2.IsOver; i++)
            {
                var press = sim2.B.State == CombatState.HitStun && sim2.B.ComboCounter >= 3 ? FighterInput.Press(CombatCommand.Dodge) : Idle;
                var attack = i == 0 ? FighterInput.Press(CombatCommand.Light) : i == 8 ? FighterInput.Press(CombatCommand.Light)
                           : i == 18 ? FighterInput.Press(CombatCommand.Heavy) : Idle;
                sim2.Step(attack, press);
                ev2.AddRange(sim2.Events);
            }
            Assert.IsTrue(ev2.Any(e => e.Type == CombatEventType.Breakaway), string.Join("\n", ev2));
            Assert.AreEqual(20f, sim2.B.Momentum, 6f, "breakaway costs momentum");
        }

        [Test]
        public void FinisherRequiresMomentumVulnerabilityAndRange()
        {
            var sim = TestData.Fight();
            Assert.AreEqual(FinisherDenial.NotEnoughMomentum, sim.CanFinish(sim.A));
            sim.A.ApplyScenario(momentum: 100);
            Assert.AreEqual(FinisherDenial.OpponentNotVulnerable, sim.CanFinish(sim.A));
            sim.B.ApplyScenario(health: sim.B.MaxHealth * 0.3f);
            Assert.AreEqual(FinisherDenial.None, sim.CanFinish(sim.A));

            var far = TestData.Fight(distance: 4f);
            far.A.ApplyScenario(momentum: 100); far.B.ApplyScenario(health: far.B.MaxHealth * 0.3f);
            Assert.AreEqual(FinisherDenial.OutOfRange, far.CanFinish(far.A));
        }

        [Test]
        public void FinisherDefeatsWeakenedOpponent()
        {
            var sim = TestData.Fight();
            sim.A.ApplyScenario(momentum: 100); sim.B.ApplyScenario(health: sim.B.MaxHealth * 0.3f);
            var ev = Run(sim, 120, i => i == 0 ? FighterInput.Press(CombatCommand.Finisher) : Idle);
            Assert.IsTrue(ev.Any(e => e.Type == CombatEventType.FinisherStarted));
            Assert.IsTrue(sim.IsOver);
            Assert.AreEqual(FightEndMethod.Finisher, sim.Result.Method);
            Assert.AreEqual("fighter.player", sim.Result.WinnerId);
            Assert.AreEqual(0f, sim.A.Momentum);
        }

        [Test]
        public void DeniedFinisherReportsReason()
        {
            var sim = TestData.Fight();
            var ev = Run(sim, 2, i => i == 0 ? FighterInput.Press(CombatCommand.Finisher) : Idle);
            Assert.AreEqual(FinisherDenial.NotEnoughMomentum.ToString(), ev.Single(e => e.Type == CombatEventType.FinisherDenied).Detail);
        }

        [Test]
        public void KnockoutEndsFightAndTimeDecisionWorks()
        {
            var sim = TestData.Fight();
            sim.B.ApplyScenario(health: 2);
            Run(sim, 30, i => i == 0 ? FighterInput.Press(CombatCommand.Light) : Idle);
            Assert.AreEqual(FightEndMethod.Knockout, sim.Result.Method);
            Assert.AreEqual(CombatState.Defeated, sim.B.State);

            var cfg = new CombatConfig { RoundTicks = 60 };
            var timed = TestData.Fight(config: cfg);
            Run(timed, 30, i => i == 0 ? FighterInput.Press(CombatCommand.Light) : Idle);
            Run(timed, 100);
            Assert.AreEqual(FightEndMethod.TimeDecision, timed.Result.Method);
            Assert.AreEqual("fighter.player", timed.Result.WinnerId);
        }

        [Test]
        public void SimulationIsDeterministic()
        {
            string Play()
            {
                var sim = TestData.Fight("fighter.rell", "fighter.brick", 2f);
                var ai1 = new FighterAI(sim.A, AIStyle.Technical, AIDifficulty.Hard, new DeterministicRandom(1));
                var ai2 = new FighterAI(sim.B, AIStyle.Wrestler, AIDifficulty.Hard, new DeterministicRandom(2));
                var log = new System.Text.StringBuilder();
                for (int i = 0; i < 6000 && !sim.IsOver; i++)
                {
                    sim.Step(ai1.Think(sim), ai2.Think(sim));
                    foreach (var e in sim.Events) log.AppendLine(e.ToString());
                }
                return log.ToString();
            }
            Assert.AreEqual(Play(), Play());
        }

        [Test]
        public void InputMapperPrioritisesAndOrientsMovement()
        {
            var frame = new InputFrame { MoveX = -1f, Held = ButtonMask.Block | ButtonMask.LightAttack | ButtonMask.Grapple };
            frame = InputFrame.WithEdges(frame, ButtonMask.Block);
            var fi = CombatInputMapper.Map(frame, -1f);
            Assert.AreEqual(CombatCommand.Grapple, fi.Command);
            Assert.IsTrue(fi.BlockHeld);
            Assert.AreEqual(1f, fi.Move); // pressing left while opponent is on the left = toward
        }
    }
}
