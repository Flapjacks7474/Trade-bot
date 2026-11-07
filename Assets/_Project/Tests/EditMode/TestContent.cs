using System.Collections.Generic;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Session;
using FFTS.World;

namespace FFTS.Tests
{
    public static class TestContent
    {
        public static ContentSources Sources() => new ContentSources
        {
            Missions = { TestData.Read("Missions/chapter1.json") },
            Dialogues = { TestData.Read("Dialogue/chapter1_dialogue.json") },
            Styles = { TestData.Read("Moves/street_fighting.json") },
            Fighters = { TestData.Read("Characters/fighters.json") },
            Layouts = { TestData.Read("World/southside_layout.json") },
            Vehicles = { TestData.Read("Vehicles/vehicles.json") },
            Races = { TestData.Read("Racing/races.json") },
        };

        public static GameSession NewSession(out ValidationReport report, ulong seed = 1)
        {
            report = new ValidationReport();
            var content = GameContentLoader.Load(Sources(), report, out _);
            return new GameSession(content, seed, report);
        }

        public static GameSession NewSession()
        {
            var s = NewSession(out var report);
            if (!report.IsValid) throw new System.InvalidOperationException(report.ToString());
            return s;
        }

        /// <summary>Plays a dialogue choosing options by id in order; returns any fight requested.</summary>
        public static string Talk(GameSession s, string dialogueId, params string[] choices)
        {
            var run = s.StartDialogue(dialogueId);
            string fight = null;
            foreach (var c in choices)
            {
                var outcome = run.Choose(c);
                if (outcome == null) throw new System.InvalidOperationException($"Option '{c}' not available at node '{run.Current?.Id}' of {dialogueId}.");
                fight = outcome.StartFight ?? fight;
            }
            return fight;
        }

        public static FightResult Win(string opponent, FightEndMethod method = FightEndMethod.Knockout)
            => new FightResult { FightId = null, WinnerId = "fighter.player", LoserId = opponent, Method = method };

        public static FightResult Lose(string opponent)
            => new FightResult { WinnerId = opponent, LoserId = "fighter.player", Method = FightEndMethod.Knockout };
    }
}

namespace FFTS.Tests
{
    public static class TestRules
    {
        public static NpcConversationRules Rules(GameSession s, FFTS.Core.ValidationReport report = null)
            => NpcConversationRules.Parse(TestData.Read("Dialogue/npc_conversations.json"), s.Content, report ?? new FFTS.Core.ValidationReport());
    }
}
