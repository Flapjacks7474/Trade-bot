using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFTS.SaveSystem
{
    /// <summary>Platform storage abstraction. Keys are slot names, never raw paths.</summary>
    public interface ISaveStorage
    {
        bool Exists(string key);
        bool BackupExists(string key);
        string Read(string key);
        string ReadBackup(string key);
        /// <summary>Writes content atomically; the previous primary becomes the backup.</summary>
        void WriteAtomic(string key, string content);
        void Delete(string key);
        IEnumerable<string> ListKeys();
    }

    public static class SaveKeys
    {
        public static bool IsValid(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 48) return false;
            foreach (char c in key)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        public static void Require(string key)
        {
            if (!IsValid(key)) throw new ArgumentException($"Invalid save slot key '{key}'. Use [a-z0-9_-], max 48 chars.");
        }
    }

    /// <summary>
    /// File storage with atomic replace: write to .tmp, flush to disk, then swap into place while
    /// moving the old file to .bak. A crash mid-write leaves either the old or the new file intact.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private const string Ext = ".ffts";
        private readonly string _directory;

        public FileSaveStorage(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(_directory);
        }

        private string PathFor(string key) { SaveKeys.Require(key); return Path.Combine(_directory, key + Ext); }
        private string BackupPathFor(string key) => PathFor(key) + ".bak";

        public bool Exists(string key) => File.Exists(PathFor(key));
        public bool BackupExists(string key) => File.Exists(BackupPathFor(key));
        public string Read(string key) => File.ReadAllText(PathFor(key), Encoding.UTF8);
        public string ReadBackup(string key) => File.ReadAllText(BackupPathFor(key), Encoding.UTF8);

        public void WriteAtomic(string key, string content)
        {
            string path = PathFor(key), tmp = path + ".tmp", bak = BackupPathFor(key);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, bak); }
                catch (PlatformNotSupportedException)
                {
                    // Some platforms (e.g. certain mobile/web file systems) lack File.Replace.
                    File.Copy(path, bak, true);
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public void Delete(string key)
        {
            if (File.Exists(PathFor(key))) File.Delete(PathFor(key));
            if (File.Exists(BackupPathFor(key))) File.Delete(BackupPathFor(key));
        }

        public IEnumerable<string> ListKeys()
        {
            foreach (var f in Directory.GetFiles(_directory, "*" + Ext))
                yield return Path.GetFileNameWithoutExtension(f);
        }
    }

    /// <summary>In-memory storage for tests and for platforms without a writable file system.</summary>
    public sealed class InMemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, string> _primary = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _backup = new Dictionary<string, string>();

        public bool Exists(string key) => _primary.ContainsKey(key);
        public bool BackupExists(string key) => _backup.ContainsKey(key);
        public string Read(string key) => _primary[key];
        public string ReadBackup(string key) => _backup[key];

        public void WriteAtomic(string key, string content)
        {
            SaveKeys.Require(key);
            if (_primary.TryGetValue(key, out var old)) _backup[key] = old;
            _primary[key] = content;
        }

        public void Delete(string key) { _primary.Remove(key); _backup.Remove(key); }
        public IEnumerable<string> ListKeys() => _primary.Keys;

        /// <summary>Test hook: overwrite stored bytes without updating the checksum.</summary>
        public void CorruptPrimary(string key, string content) => _primary[key] = content;
    }
}
