using System.Collections.Generic;
using System.Linq;
using FFTS.Combat;
using FFTS.Core;
using NUnit.Framework;

namespace FFTS.Tests
{
    public class FighterAITests
    {
        private static FighterAI AiFor(CombatSimulation sim, FighterRuntime f, ulong seed, AIDifficultyParams p = null)
            => new FighterAI(f, f.Definition.AiStyle, f.Definition.AiDifficulty, new DeterministicRandom(seed), p);

        private static List<CombatEvent> Bout(CombatSimulation sim, FighterAI a, FighterAI b, int maxTicks = 60 * 180)
        {
            var log = new List<CombatEvent>();
            for (int i = 0; i < maxTicks && !sim.IsOver; i++)
            {
                sim.Step(a.Think(sim), b.Think(sim));
                log.AddRange(sim.Events);
            }
            return log;
        }

        [Test]
        public void AiDefeatsPassiveOpponent([Values("fighter.rell", "fighter.brick")] string id)
        {
            var sim = TestData.Fight(id, "fighter.player", 2f);
            var ai = AiFor(sim, sim.A, 7);
            for (int i = 0; i < 60 * 120 && !sim.IsOver; i++) sim.Step(ai.Think(sim), FighterInput.None);
            Assert.IsTrue(sim.IsOver, sim.A + " / " + sim.B);
            Assert.AreEqual(id, sim.Result.WinnerId);
        }

        [Test]
        public void AiVersusAiFightsAlwaysResolve()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var sim = TestData.Fight("fighter.rell", "fighter.brick", 2f);
                Bout(sim, AiFor(sim, sim.A, seed), AiFor(sim, sim.B, seed + 100));
                Assert.IsTrue(sim.IsOver);
                Assert.That(sim.Result.Method, Is.Not.EqualTo(FightEndMethod.None));
            }
        }

        [Test]
        public void OpponentsHaveDistinctFightingBehaviour()
        {
            int rellGrapples = 0, brickGrapples = 0, rellCounters = 0, brickCounters = 0, rellMoves = 0, brickMoves = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                foreach (var id in new[] { "fighter.rell", "fighter.brick" })
                {
                    var sim = TestData.Fight(id, "fighter.player", 2f);
                    var opp = new FighterAI(sim.B, AIStyle.Aggressive, AIDifficulty.Normal, new DeterministicRandom(seed + 50));
                    var ev = Bout(sim, AiFor(sim, sim.A, seed), opp);
                    int g = ev.Count(e => e.Type == CombatEventType.MoveStarted && e.ActorId == id && e.Detail == "Grapple");
                    int c = ev.Count(e => e.Type == CombatEventType.Countered && e.ActorId == id);
                    int all = ev.Count(e => e.Type == CombatEventType.MoveStarted && e.ActorId == id);
                    if (id == "fighter.rell") { rellGrapples += g; rellCounters += c; rellMoves += all; } else { brickGrapples += g; brickCounters += c; brickMoves += all; }
                }
            }
            float brickShare = brickGrapples / (float)brickMoves, rellShare = rellGrapples / (float)rellMoves;
            Assert.Greater(brickShare, rellShare * 1.5f, $"Brick grapple share {brickShare:P0} ({brickGrapples}/{brickMoves}) vs Rell {rellShare:P0} ({rellGrapples}/{rellMoves})");
            Assert.Greater(rellCounters, brickCounters, $"Rell counters {rellCounters} vs Brick {brickCounters}");
        }

        /// <summary>A predictable player who opens with Haymaker every time they are in range.</summary>
        private static FighterInput HeavySpammer(CombatSimulation sim, FighterRuntime me)
        {
            if (me.State != CombatState.Idle && me.State != CombatState.Moving) return FighterInput.None;
            return sim.Distance > 1.4f ? FighterInput.Walk(1f) : FighterInput.Press(CombatCommand.Heavy);
        }

        private static float BlockRateAgainstSpam(float adaptation, out OpponentModel model)
        {
            int blocked = 0, landed = 0;
            model = null;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var sim = TestData.Fight("fighter.player", "fighter.rell", 2f, new CombatConfig { RoundTicks = 60 * 120 });
                var p = AIDifficultyParams.For(AIDifficulty.Normal);
                p.AdaptationStrength = adaptation;
                var ai = new FighterAI(sim.B, AIStyle.Defensive, AIDifficulty.Normal, new DeterministicRandom(seed), p);
                for (int i = 0; i < 60 * 120 && !sim.IsOver; i++)
                {
                    sim.Step(HeavySpammer(sim, sim.A), ai.Think(sim));
                    foreach (var e in sim.Events)
                    {
                        if (e.ActorId != "fighter.player" || e.MoveId != "street.haymaker") continue;
                        if (e.Type == CombatEventType.Hit) landed++;
                        if (e.Type == CombatEventType.Blocked || e.Type == CombatEventType.GuardBreak) blocked++;
                    }
                    if (sim.Events.Any(e => e.Type == CombatEventType.Countered && e.ActorId == "fighter.rell")) blocked++;
                }
                model = ai.Model;
            }
            return blocked / (float)System.Math.Max(1, blocked + landed);
        }

        [Test]
        public void AiAdaptsToRepeatedHeavySpam()
        {
            float without = BlockRateAgainstSpam(0f, out _);
            float with = BlockRateAgainstSpam(0.6f, out var model);
            Assert.Greater(model.Frequency(MoveKind.Heavy), 0.8f, "model should learn the habit");
            Assert.Greater(with, without + 0.1f, $"adaptive defence {with:P0} vs non-adaptive {without:P0}");
        }

        [Test]
        public void AiCannotReactFasterThanItsReactionTime()
        {
            // Jab startup (~5 ticks) is far below Easy's 24-tick reaction: an Easy AI with no habit data
            // must never parry or counter a first-ever jab from neutral.
            int defended = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var sim = TestData.Fight("fighter.player", "fighter.dre", 1.0f);
                var p = AIDifficultyParams.For(AIDifficulty.Easy);
                p.DefenseSkill = 1f; p.CounterSkill = 1f; p.MistakeRate = 0f; p.DecisionIntervalTicks = 1000;
                var ai = new FighterAI(sim.B, AIStyle.Aggressive, AIDifficulty.Easy, new DeterministicRandom(seed), p);
                for (int i = 0; i < 12; i++)
                {
                    sim.Step(i == 0 ? FighterInput.Press(CombatCommand.Light) : FighterInput.None, ai.Think(sim));
                    if (sim.Events.Any(e => e.Type == CombatEventType.Countered || e.Type == CombatEventType.Blocked)) defended++;
                }
            }
            Assert.AreEqual(0, defended);
        }

        [Test]
        public void HigherDifficultyWinsMoreOften()
        {
            int hardWins = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var sim = TestData.Fight("fighter.rell", "fighter.player", 2f);
                var hard = new FighterAI(sim.A, AIStyle.Technical, AIDifficulty.Hard, new DeterministicRandom(seed));
                var easy = new FighterAI(sim.B, AIStyle.Aggressive, AIDifficulty.Easy, new DeterministicRandom(seed + 9));
                Bout(sim, hard, easy);
                if (sim.Result.WinnerId == "fighter.rell") hardWins++;
            }
            Assert.GreaterOrEqual(hardWins, 7);
        }
    }
}
