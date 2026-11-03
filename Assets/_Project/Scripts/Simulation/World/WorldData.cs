using System.Collections.Generic;
using FFTS.Core;
using Newtonsoft.Json;

namespace FFTS.World
{
    public sealed class DistrictDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string ControllingCrew;      // null = contested / independent
        public List<string> Features = new List<string>();
        public List<string> Landmarks = new List<string>();
        public bool InVerticalSlice;
    }

    public sealed class InteractionDefinition
    {
        public string Id;
        public string Verb;                 // talk, enter, fight, record, shop, look, rest
        public string Prompt;
        public string Trigger;              // mission trigger reported on use
        public string DialogueId;
        public string FighterId;            // opponent for fight interactions
        public string VenueType;            // Underground, Sanctioned, Street
        public float OffsetX, OffsetZ;      // relative to the location centre
        public int Priority;
    }

    public sealed class LocationDefinition
    {
        public string Id;
        public string DisplayName;
        public string Type;                 // residence, shop, studio, gym, venue, garage, court, hq, street...
        public float X, Z;                  // metres, district-local
        public float Width = 10, Depth = 10, Height = 6;
        public string Color = "#808080";
        public string EnterTrigger;         // reported when the player enters the location volume
        public List<InteractionDefinition> Interactions = new List<InteractionDefinition>();
    }

    public sealed class RoadDefinition
    {
        public string Id;
        public float X1, Z1, X2, Z2;
        public float Width = 10;
    }

    public sealed class NpcPlacement
    {
        public string PlacementId;
        public string ShowAfterObjective;   // "missionId/objectiveId": hidden until that objective is done
        public string HideAfterObjective;   // "missionId/objectiveId": hidden once that objective is done
        public string NpcId;
        public string DisplayName;
        public string LocationId;
        public string InteractionId;
        public float OffsetX, OffsetZ;
        public string Color = "#c0c0c0";
    }

    public sealed class DistrictLayout
    {
        public string DistrictId;
        public float SizeX = 400, SizeZ = 400;
        public float SpawnX, SpawnZ;
        public List<RoadDefinition> Roads = new List<RoadDefinition>();
        public List<LocationDefinition> Locations = new List<LocationDefinition>();
        public List<NpcPlacement> Npcs = new List<NpcPlacement>();

        public static DistrictLayout Parse(string json, ValidationReport report)
        {
            var layout = JsonConvert.DeserializeObject<DistrictLayout>(json);
            var ids = new IdRegistry();
            foreach (var l in layout.Locations)
            {
                ids.Register(l.Id, "location");
                if (l.Width <= 0 || l.Depth <= 0 || l.Height <= 0) report.Error($"{l.Id}: invalid size.");
                if (System.Math.Abs(l.X) > layout.SizeX / 2 || System.Math.Abs(l.Z) > layout.SizeZ / 2) report.Error($"{l.Id}: outside district bounds.");
                foreach (var i in l.Interactions)
                {
                    ids.Register(i.Id, $"interaction in {l.Id}");
                    if (i.Verb == "fight" && string.IsNullOrEmpty(i.FighterId)) report.Error($"{i.Id}: fight interaction without fighter.");
                    if (i.Verb == "talk" && string.IsNullOrEmpty(i.DialogueId)) report.Error($"{i.Id}: talk interaction without dialogue.");
                }
            }
            foreach (var r in layout.Roads) ids.Register(r.Id, "road");
            foreach (var n in layout.Npcs)
            {
                ids.Register(n.PlacementId, "npc placement");
                if (!layout.Locations.Exists(l => l.Id == n.LocationId)) report.Error($"NPC {n.NpcId}: unknown location {n.LocationId}.");
            }
            report.AddRange(ids.Errors);
            return layout;
        }

        public LocationDefinition Find(string id) => Locations.Find(l => l.Id == id);
    }

    public sealed class DistrictFile
    {
        public List<DistrictDefinition> Districts = new List<DistrictDefinition>();
    }
}
