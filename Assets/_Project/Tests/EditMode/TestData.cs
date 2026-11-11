using System;
using System.IO;
using FFTS.Combat;
using FFTS.Core;

namespace FFTS.Tests
{
    /// <summary>Locates the real content files so tests validate shipped data, not copies.</summary>
    public static class TestData
    {
        public static string DataRoot
        {
            get
            {
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        var candidate = Path.Combine(dir.FullName, "Assets", "_Project", "Data");
                        if (Directory.Exists(candidate)) return candidate;
                        dir = dir.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Could not locate Assets/_Project/Data from " + Directory.GetCurrentDirectory());
            }
        }

        public static string Read(string relative) => File.ReadAllText(Path.Combine(DataRoot, relative));

        public static CombatDatabase Combat(out ValidationReport report)
        {
            report = new ValidationReport();
            return CombatDatabase.FromJson(AllStyles(), new[] { Read("Characters/fighters.json") }, report);
        }

        /// <summary>Every style file under Data/Moves (street first), as the Unity scene builder loads them.</summary>
        public static string[] AllStyles()
        {
            var files = new System.Collections.Generic.List<string>(Directory.GetFiles(Path.Combine(DataRoot, "Moves"), "*.json"));
            files.Sort((x, y) =>
            {
                bool sx = Path.GetFileName(x) == "street_fighting.json", sy = Path.GetFileName(y) == "street_fighting.json";
                return sx != sy ? (sx ? -1 : 1) : string.CompareOrdinal(x, y);
            });
            return files.ConvertAll(File.ReadAllText).ToArray();
        }

        public static string[] Appearance() => new[] { Read("Appearance/appearance_catalog.json") };

        public static CombatDatabase Combat()
        {
            var db = Combat(out var report);
            if (!report.IsValid) throw new InvalidOperationException(report.ToString());
            return db;
        }

        public static CombatSimulation Fight(string a = "fighter.player", string b = "fighter.rell", float distance = 1.0f, CombatConfig config = null)
        {
            var db = Combat();
            var fa = db.GetFighter(a); var fb = db.GetFighter(b);
            return new CombatSimulation(config ?? new CombatConfig(),
                new FighterRuntime(fa, db.BuildMoveset(fa), -distance / 2f),
                new FighterRuntime(fb, db.BuildMoveset(fb), distance / 2f), "fight.test");
        }
    }
}
