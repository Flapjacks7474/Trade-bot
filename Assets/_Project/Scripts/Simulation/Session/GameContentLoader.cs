using System.Collections.Generic;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Dialogue;
using FFTS.LivingWorld;
using FFTS.Missions;
using FFTS.Racing;
using FFTS.Vehicles;
using FFTS.World;
using Newtonsoft.Json;

namespace FFTS.Session
{
    /// <summary>Raw JSON text of every content file, gathered by the platform layer (TextAssets in Unity, files in tests).</summary>
    public sealed class ContentSources
    {
        public List<string> Missions = new List<string>();
        public List<string> Dialogues = new List<string>();
        public List<string> Styles = new List<string>();
        public List<string> Fighters = new List<string>();
        public List<string> Layouts = new List<string>();
        public List<string> Vehicles = new List<string>();
        public List<string> Races = new List<string>();
        public List<string> NpcProfiles = new List<string>();
        public List<string> Appearance = new List<string>();
    }

    public static class GameContentLoader
    {
        /// <summary>Triggers emitted by runtime code rather than content files.</summary>
        public static readonly string[] RuntimeTriggers =
        {
            "combat.tutorial.blocked", "combat.tutorial.combo", "combat.tutorial.thrown"
        };

        public static GameContent Load(ContentSources src, ValidationReport report, out List<DistrictLayout> layouts)
        {
            var content = new GameContent();
            foreach (var json in src.Missions) content.Missions.AddRange(JsonConvert.DeserializeObject<MissionFile>(json).Missions);
            foreach (var json in src.Dialogues)
                foreach (var d in DialogueRunner.Parse(json, report))
                    if (!content.Dialogues.ContainsKey(d.Id)) content.Dialogues[d.Id] = d;
            content.Combat = CombatDatabase.FromJson(src.Styles, src.Fighters, report);
            if (src.Appearance.Count > 0) content.Appearance = FFTS.Characters.AppearanceCatalog.Parse(src.Appearance, report);
            content.Vehicles = VehicleDatabase.FromJson(src.Vehicles, report);
            content.Races = RaceDatabase.FromJson(src.Races, content.Vehicles, report);
            layouts = new List<DistrictLayout>();
            foreach (var json in src.Layouts) layouts.Add(DistrictLayout.Parse(json, report));
            content.Layout = layouts.Count > 0 ? layouts[0] : null;
            if (content.Layout != null)
                foreach (var json in src.NpcProfiles) content.NpcProfiles.AddRange(LivingWorldManager.Parse(json, content.Layout, report));
            CrossCheck(content, layouts, report);
            return content;
        }

        /// <summary>
        /// Every mission objective/failure trigger must be producible by something in the game, every
        /// talk interaction must reference a real dialogue, and every fight must reference a real fighter.
        /// </summary>
        public static void CrossCheck(GameContent content, List<DistrictLayout> layouts, ValidationReport report)
        {
            var producible = new HashSet<string>(RuntimeTriggers);
            foreach (var layout in layouts)
                foreach (var l in layout.Locations)
                {
                    if (l.EnterTrigger != null) producible.Add(l.EnterTrigger);
                    foreach (var i in l.Interactions)
                    {
                        if (i.Trigger != null) producible.Add(i.Trigger);
                        if (i.DialogueId != null && !content.Dialogues.ContainsKey(i.DialogueId)) report.Error($"Interaction {i.Id}: unknown dialogue {i.DialogueId}.");
                        if (i.FighterId != null && content.Combat?.GetFighter(i.FighterId) == null) report.Error($"Interaction {i.Id}: unknown fighter {i.FighterId}.");
                    }
                }
            foreach (var d in content.Dialogues.Values)
            {
                if (d.EndTrigger != null) producible.Add(d.EndTrigger);
                foreach (var n in d.Nodes)
                    foreach (var o in n.Options)
                    {
                        if (o.Trigger != null) producible.Add(o.Trigger);
                        if (o.StartFight != null && content.Combat?.GetFighter(o.StartFight) == null) report.Error($"Dialogue {d.Id}/{o.Id}: unknown fighter {o.StartFight}.");
                        if (o.MissionBranch != null)
                        {
                            var parts = o.MissionBranch.Split(':');
                            var m = content.Missions.Find(x => x.Id == parts[0]);
                            if (m == null || parts.Length < 2 || !m.Branches.Exists(b => b.Id == parts[1])) report.Error($"Dialogue {d.Id}/{o.Id}: unknown mission branch {o.MissionBranch}.");
                        }
                    }
            }
            foreach (var p in content.NpcProfiles)
                foreach (var e in p.Schedule)
                {
                    foreach (var o in e.ObjectivesDone) NpcConversationRules.CheckObjectiveRef(o, content, report);
                    foreach (var o in e.ObjectivesNotDone) NpcConversationRules.CheckObjectiveRef(o, content, report);
                    if (e.MissionActive != null && !content.Missions.Exists(m => m.Id == e.MissionActive)) report.Error($"NPC {p.NpcId}: schedule pin on unknown mission {e.MissionActive}.");
                }
            if (content.Races != null)
                foreach (var r in content.Races.Races)
                {
                    producible.Add("race.won." + r.Id); producible.Add("race.finished." + r.Id);
                    if (layouts.Count > 0 && !layouts.Exists(l => l.Find(r.LocationId) != null)) report.Error($"Race {r.Id}: start location {r.LocationId} not in any layout.");
                }
            if (content.Vehicles != null)
                foreach (var g in content.Vehicles.Garages)
                    if (layouts.Count > 0 && !layouts.Exists(l => l.Find(g.LocationId) != null)) report.Error($"Garage {g.Id}: location {g.LocationId} not in any layout.");
            if (content.Combat != null)
                foreach (var f in content.Combat.Fighters) { producible.Add("fight.won." + f.Id); producible.Add("fight.lost." + f.Id); }

            foreach (var m in content.Missions)
            {
                foreach (var o in m.Objectives)
                    if (o.Trigger != null && !producible.Contains(o.Trigger)) report.Error($"{m.Id}/{o.Id}: nothing in the game produces trigger '{o.Trigger}'.");
                foreach (var f in m.FailureConditions)
                    if (!producible.Contains(f)) report.Error($"{m.Id}: failure trigger '{f}' is never produced.");
            }
        }
    }
}
