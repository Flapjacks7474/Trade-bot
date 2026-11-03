using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FFTS.SaveSystem
{
    /// <summary>One schema upgrade step, operating on the raw JSON before typed deserialization.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Apply(JObject payload);
    }

    public enum SaveReadStatus { Ok, NotFound, Corrupt, UnsupportedVersion, MigrationFailed }

    public sealed class EnvelopeReadResult
    {
        public SaveReadStatus Status;
        public JObject Payload;
        public int OriginalVersion;
        public string Message;
    }

    /// <summary>
    /// Wraps a payload with a format tag, schema version and SHA-256 checksum. The payload is stored
    /// as an embedded string so the checksum covers the exact bytes that were written.
    /// </summary>
    public sealed class SaveEnvelopeCodec
    {
        public const string FormatTag = "FFTS_SAVE";
        private readonly int _currentVersion;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        public int CurrentVersion => _currentVersion;

        public SaveEnvelopeCodec(int currentVersion, IEnumerable<ISaveMigration> migrations = null)
        {
            if (currentVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentVersion));
            _currentVersion = currentVersion;
            if (migrations != null)
                foreach (var m in migrations)
                {
                    if (m.FromVersion >= currentVersion) throw new ArgumentException($"Migration from v{m.FromVersion} is not below current v{currentVersion}.");
                    if (_migrations.ContainsKey(m.FromVersion)) throw new ArgumentException($"Duplicate migration from v{m.FromVersion}.");
                    _migrations[m.FromVersion] = m;
                }
        }

        public static string Checksum(string payload)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public string Encode(JObject payload)
        {
            string body = payload.ToString(Formatting.None);
            var envelope = new JObject
            {
                ["format"] = FormatTag,
                ["schemaVersion"] = _currentVersion,
                ["savedUtc"] = DateTime.UtcNow.ToString("o"),
                ["checksum"] = Checksum(body),
                ["payload"] = body
            };
            return envelope.ToString(Formatting.Indented);
        }

        public EnvelopeReadResult Decode(string text)
        {
            var result = new EnvelopeReadResult();
            JObject envelope;
            try { envelope = JObject.Parse(text); }
            catch (JsonException ex) { return Fail(result, SaveReadStatus.Corrupt, "Envelope is not valid JSON: " + ex.Message); }

            if ((string)envelope["format"] != FormatTag) return Fail(result, SaveReadStatus.Corrupt, "Missing or wrong format tag.");
            var versionToken = envelope["schemaVersion"];
            var body = (string)envelope["payload"];
            var checksum = (string)envelope["checksum"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer || body == null || checksum == null)
                return Fail(result, SaveReadStatus.Corrupt, "Envelope is missing required fields.");
            if (!string.Equals(Checksum(body), checksum, StringComparison.Ordinal))
                return Fail(result, SaveReadStatus.Corrupt, "Checksum mismatch.");

            int version = (int)versionToken;
            result.OriginalVersion = version;
            if (version > _currentVersion) return Fail(result, SaveReadStatus.UnsupportedVersion, $"Save v{version} is newer than supported v{_currentVersion}.");
            if (version < 1) return Fail(result, SaveReadStatus.Corrupt, "Invalid schema version.");

            JObject payload;
            try { payload = JObject.Parse(body); }
            catch (JsonException ex) { return Fail(result, SaveReadStatus.Corrupt, "Payload is not valid JSON: " + ex.Message); }

            for (int v = version; v < _currentVersion; v++)
            {
                if (!_migrations.TryGetValue(v, out var migration))
                    return Fail(result, SaveReadStatus.MigrationFailed, $"No migration registered from v{v}.");
                try { migration.Apply(payload); }
                catch (Exception ex) { return Fail(result, SaveReadStatus.MigrationFailed, $"Migration from v{v} failed: {ex.Message}"); }
            }

            result.Status = SaveReadStatus.Ok;
            result.Payload = payload;
            return result;
        }

        private static EnvelopeReadResult Fail(EnvelopeReadResult r, SaveReadStatus s, string msg)
        {
            r.Status = s; r.Message = msg; return r;
        }
    }
}
