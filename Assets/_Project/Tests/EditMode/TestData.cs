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
            return CombatDatabase.FromJson(new[] { Read("Moves/street_fighting.json") }, new[] { Read("Characters/fighters.json") }, report);
        }

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
