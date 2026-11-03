using System.Collections.Generic;
using FFTS.Core;
using Newtonsoft.Json;

namespace FFTS.Crews
{
    public enum CrewRank { Outsider, Prospect, Member, RespectedMember, Veteran, RightHand, SecondInCommand, Leader }

    /// <summary>
    /// A character's casting is separate from what the game shows. Characters inspired by real people
    /// display their placeholder name unless likeness/name rights are recorded as cleared.
    /// </summary>
    public sealed class CastingInfo
    {
        public string CastingReference;     // creative casting proposal (real artist) — NOT shown in-game unless cleared
        public string PlaceholderName;      // original fictional name used until rights are cleared
        public bool LikenessRightsCleared;  // set only with a signed agreement on file (see ASSET_REGISTER.md)

        [JsonIgnore] public string DisplayName => LikenessRightsCleared && !string.IsNullOrEmpty(CastingReference) ? CastingReference : PlaceholderName;
    }

    public sealed class CrewDefinition
    {
        public string Id;
        public string DisplayName;
        public string Colors;
        public string Headquarters;
        public List<string> Territory = new List<string>();
        public List<string> BusinessInterests = new List<string>();
        public string Identity;
        public CastingInfo Leader = new CastingInfo();
        public List<CastingInfo> Roster = new List<CastingInfo>();
    }

    public sealed class CrewFile
    {
        public List<CrewDefinition> Crews = new List<CrewDefinition>();

        public static CrewFile Parse(string json, ValidationReport report)
        {
            var f = JsonConvert.DeserializeObject<CrewFile>(json);
            var ids = new IdRegistry();
            foreach (var c in f.Crews)
            {
                ids.Register(c.Id, "crew");
                if (string.IsNullOrEmpty(c.Leader.PlaceholderName)) report.Error($"{c.Id}: leader needs an original placeholder name.");
                foreach (var m in c.Roster) if (string.IsNullOrEmpty(m.PlaceholderName)) report.Error($"{c.Id}: roster entry '{m.CastingReference}' has no placeholder name.");
            }
            report.AddRange(ids.Errors);
            return f;
        }
    }
}
