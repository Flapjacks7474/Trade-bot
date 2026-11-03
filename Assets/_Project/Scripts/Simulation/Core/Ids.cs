using System;
using System.Collections.Generic;

namespace FFTS.Core
{
    /// <summary>
    /// Stable unique ID rules. IDs are lowercase, dot-separated segments, e.g. "npc.dre",
    /// "mission.ch1.home_again". They are authored once and never renamed after content ships,
    /// because save files reference them.
    /// </summary>
    public static class Ids
    {
        public const int MaxLength = 96;

        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLength) return false;
            if (id[0] == '.' || id[id.Length - 1] == '.') return false;
            char previous = '\0';
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
                if (c == '.' && previous == '.') return false;
                previous = c;
            }
            return true;
        }

        public static void Require(string id, string context)
        {
            if (!IsValid(id)) throw new ArgumentException($"Invalid id '{id}' ({context}).");
        }
    }

    /// <summary>Collects IDs during content validation and reports invalid or duplicate entries.</summary>
    public sealed class IdRegistry
    {
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public bool Contains(string id) => id != null && _ids.Contains(id);

        public bool Register(string id, string context)
        {
            if (!Ids.IsValid(id)) { _errors.Add($"{context}: invalid id '{id}'."); return false; }
            if (!_ids.Add(id)) { _errors.Add($"{context}: duplicate id '{id}'."); return false; }
            return true;
        }
    }

    /// <summary>Accumulates human-readable content validation problems.</summary>
    public sealed class ValidationReport
    {
        private readonly List<string> _errors = new List<string>();
        public IReadOnlyList<string> Errors => _errors;
        public bool IsValid => _errors.Count == 0;
        public void Error(string message) => _errors.Add(message);
        public void AddRange(IEnumerable<string> messages) => _errors.AddRange(messages);
        public override string ToString() => IsValid ? "OK" : string.Join("\n", _errors);
    }
}
