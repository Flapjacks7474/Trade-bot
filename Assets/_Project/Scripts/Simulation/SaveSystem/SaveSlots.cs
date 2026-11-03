using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FFTS.SaveSystem
{
    public enum LoadOutcome { Loaded, RecoveredFromBackup, NotFound, Corrupt, UnsupportedVersion, MigrationFailed }

    public sealed class LoadResult<T>
    {
        public LoadOutcome Outcome;
        public T Data;
        public string Message;
        public int OriginalVersion;
        public bool Succeeded => Outcome == LoadOutcome.Loaded || Outcome == LoadOutcome.RecoveredFromBackup;
    }

    /// <summary>
    /// Typed save/load on top of the envelope codec and storage. Falls back to the backup copy when
    /// the primary is corrupt, and never overwrites a save it could not read.
    /// </summary>
    public sealed class SaveSlotManager<T> where T : class
    {
        public const int AutosaveSlotCount = 3;
        private readonly ISaveStorage _storage;
        private readonly SaveEnvelopeCodec _codec;
        private readonly JsonSerializer _serializer;
        private int _nextAutosave;

        public SaveSlotManager(ISaveStorage storage, SaveEnvelopeCodec codec)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _serializer = JsonSerializer.Create(SerializerSettings);
        }

        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            TypeNameHandling = TypeNameHandling.None, // never deserialize arbitrary types from disk
            Formatting = Formatting.None
        };

        public static string ManualSlot(int index) => "manual_" + index;
        public static string AutosaveSlot(int index) => "autosave_" + index;

        public void Save(string slot, T data)
        {
            SaveKeys.Require(slot);
            var payload = JObject.FromObject(data, _serializer);
            _storage.WriteAtomic(slot, _codec.Encode(payload));
        }

        /// <summary>Writes to the next slot of a rotating autosave set and returns the slot used.</summary>
        public string Autosave(T data)
        {
            string slot = AutosaveSlot(_nextAutosave);
            Save(slot, data);
            _nextAutosave = (_nextAutosave + 1) % AutosaveSlotCount;
            return slot;
        }

        public LoadResult<T> Load(string slot)
        {
            SaveKeys.Require(slot);
            var result = new LoadResult<T>();
            if (!_storage.Exists(slot) && !_storage.BackupExists(slot))
            {
                result.Outcome = LoadOutcome.NotFound; result.Message = $"No save in slot '{slot}'.";
                return result;
            }

            SaveReadStatus? primaryStatus = SaveReadStatus.NotFound;
            string primaryProblem = "primary missing";
            if (_storage.Exists(slot))
            {
                primaryStatus = TryDecode(_storage.Read(slot), result);
                if (primaryStatus == null) { result.Outcome = LoadOutcome.Loaded; return result; }
                // A newer-version save is not corrupt; do not silently fall back to an older backup.
                if (primaryStatus == SaveReadStatus.UnsupportedVersion) { result.Outcome = LoadOutcome.UnsupportedVersion; return result; }
                primaryProblem = result.Message;
            }

            if (_storage.BackupExists(slot))
            {
                var backupStatus = TryDecode(_storage.ReadBackup(slot), result);
                if (backupStatus == null)
                {
                    result.Outcome = LoadOutcome.RecoveredFromBackup;
                    result.Message = "Primary save unreadable (" + primaryProblem + "); restored backup.";
                    return result;
                }
            }

            result.Data = null;
            result.Outcome = primaryStatus == SaveReadStatus.MigrationFailed ? LoadOutcome.MigrationFailed : LoadOutcome.Corrupt;
            result.Message = primaryProblem;
            return result;
        }

        private SaveReadStatus? TryDecode(string text, LoadResult<T> result)
        {
            var env = _codec.Decode(text);
            result.OriginalVersion = env.OriginalVersion;
            if (env.Status != SaveReadStatus.Ok) { result.Message = env.Message; return env.Status; }
            try
            {
                result.Data = env.Payload.ToObject<T>(_serializer);
                if (result.Data == null) { result.Message = "Payload deserialized to null."; return SaveReadStatus.Corrupt; }
                return null;
            }
            catch (JsonException ex) { result.Message = "Payload type mismatch: " + ex.Message; return SaveReadStatus.Corrupt; }
        }

        public IEnumerable<string> ListSlots() => _storage.ListKeys();
        public void Delete(string slot) => _storage.Delete(slot);
    }
}
