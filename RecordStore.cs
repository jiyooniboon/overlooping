using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KnitSprite.Records
{
    /// <summary>
    /// Loads, holds and saves records.json. Plain C# (not a MonoBehaviour) so the analyzer and
    /// unit tests can use it without a scene.
    ///
    /// Writes are buffered: attempts accumulate in memory and only hit disk on Flush(). Writing a
    /// file per stitch would cause visible frame hitches during play, and a stitch resolves every
    /// second or two.
    /// </summary>
    public class RecordStore
    {
        public const string FileName = "records.json";

        /// <summary>
        /// Oldest attempts are trimmed past this. Unbounded growth over a semester of playtesting
        /// is avoidable, and the analyzer weights recent attempts far more heavily anyway.
        /// </summary>
        public int maxAttempts = 5000;

        public RecordFile Data { get; private set; } = new RecordFile();
        public string Path { get; }
        public bool IsDirty { get; private set; }

        // Signature -> config id, so repeated attempts under identical windows don't append a new
        // config every stitch.
        private readonly Dictionary<string, int> configLookup = new();

        public RecordStore(string path = null)
        {
            Path = path ?? System.IO.Path.Combine(Application.persistentDataPath, FileName);
        }

        public void Load()
        {
            Data = new RecordFile();
            configLookup.Clear();

            if (!File.Exists(Path))
            {
                IsDirty = false;
                return;
            }

            try
            {
                string json = File.ReadAllText(Path);
                var loaded = JsonUtility.FromJson<RecordFile>(json);

                if (loaded == null)
                {
                    Debug.LogWarning($"RecordStore: {Path} could not be parsed - starting fresh.");
                }
                else if (loaded.schemaVersion != RecordFile.CurrentSchemaVersion)
                {
                    // Discard rather than migrate. JsonUtility silently defaults fields it can't
                    // find, so loading an outdated file produces attempts full of plausible zeros
                    // ("0.0s, Perfect") that corrupt every average with no visible error.
                    Debug.LogWarning(
                        $"RecordStore: schema {loaded.schemaVersion} != {RecordFile.CurrentSchemaVersion}, " +
                        "discarding old records.");
                }
                else
                {
                    Data = loaded;
                    Data.configs ??= new List<WindowConfig>();
                    Data.attempts ??= new List<StitchAttempt>();

                    for (int i = 0; i < Data.configs.Count; i++)
                        configLookup[Data.configs[i].Signature()] = Data.configs[i].id;
                }
            }
            catch (System.Exception e)
            {
                // A corrupt save must never stop the game from starting.
                Debug.LogWarning($"RecordStore: failed to read {Path} ({e.Message}) - starting fresh.");
                Data = new RecordFile();
                configLookup.Clear();
            }

            IsDirty = false;
        }

        /// <summary>
        /// Returns the id for these windows, adding the config if it hasn't been seen. Dedup is by
        /// rounded value signature, so drill scaling that lands on the same numbers reuses the id.
        /// </summary>
        public int RegisterConfig(WindowConfig config)
        {
            if (config == null) return -1;

            string sig = config.Signature();
            if (configLookup.TryGetValue(sig, out int existing)) return existing;

            config.id = Data.configs.Count;
            Data.configs.Add(config);
            configLookup[sig] = config.id;
            IsDirty = true;
            return config.id;
        }

        public void Append(StitchAttempt attempt)
        {
            if (attempt == null) return;

            Data.attempts.Add(attempt);

            int overflow = Data.attempts.Count - maxAttempts;
            if (overflow > 0) Data.attempts.RemoveRange(0, overflow);

            IsDirty = true;
        }

        /// <summary>Writes to disk if anything changed. Safe to call often.</summary>
        public void Flush(bool force = false)
        {
            if (!IsDirty && !force) return;

            try
            {
                string json = JsonUtility.ToJson(Data);
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Write to a temp file then move, so a crash mid-write can't leave a truncated
                // records.json that fails to parse on next launch.
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);

                IsDirty = false;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"RecordStore: failed to write {Path} ({e.Message}).");
            }
        }

        public void Clear()
        {
            Data = new RecordFile();
            configLookup.Clear();
            IsDirty = true;
        }

        public WindowConfig GetConfig(int id)
        {
            if (id < 0 || id >= Data.configs.Count) return null;
            return Data.configs[id];
        }
    }
}
