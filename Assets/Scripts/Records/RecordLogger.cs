using UnityEngine;

namespace KnitSprite.Records
{
    /// <summary>
    /// Bridges ProgressManager's OnStitchOutcome to persistent storage. Drop this on the same
    /// GameObject as ProgressManager (or assign the reference) and recording is live.
    ///
    /// Because story mode and practice mode share the same FSM, ordinary story play feeds the
    /// analyzer for free - practice mode has data to work with the first time it opens.
    ///
    /// Set `mode` to "practice" and fill `drillId` from the drill runner when practice mode lands;
    /// story play can leave the defaults.
    /// </summary>
    public class RecordLogger : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use a ProgressManager on this GameObject.")]
        public ProgressManager progressManager;

        [Header("Tagging")]
        [Tooltip("\"story\" or \"practice\" - lets the analyzer separate free play from drills.")]
        public string mode = "story";

        [Tooltip("Set by the drill runner in practice mode; empty during story play.")]
        public string drillId = "";

        [Header("Persistence")]
        [Tooltip("Seconds between background saves. OnApplicationQuit is not reliable when the " +
                 "editor is force-stopped, so periodic flushing protects a playtest session.")]
        public float autoFlushSeconds = 60f;

        [Tooltip("Log each recorded attempt to the console. Useful while wiring up, noisy after.")]
        public bool logAttempts = false;

        public RecordStore Store { get; private set; }

        private float flushTimer;
        private bool subscribed;

        // Cached so a WindowConfig isn't rebuilt and re-signed on every single stitch.
        private string cachedSignature;
        private int cachedConfigId = -1;

        // Rolling average frame time for the session - recorded on each attempt so timing data
        // captured at 30fps is at least distinguishable from data captured at 144fps.
        private float frameMsAccum;
        private int frameCount;

        void Awake()
        {
            if (progressManager == null) progressManager = GetComponent<ProgressManager>();

            Store = new RecordStore();
            Store.Load();
        }

        void OnEnable()
        {
            if (progressManager == null)
            {
                Debug.LogWarning("RecordLogger: no ProgressManager assigned - nothing will be recorded.", this);
                return;
            }

            // Guarded because a double subscribe would record every attempt twice, and duplicated
            // attempts skew every statistic the analyzer produces.
            if (subscribed) return;
            progressManager.OnStitchOutcome += HandleOutcome;
            subscribed = true;
        }

        void OnDisable()
        {
            if (subscribed && progressManager != null)
            {
                progressManager.OnStitchOutcome -= HandleOutcome;
                subscribed = false;
            }

            Store?.Flush();
        }

        void Update()
        {
            frameMsAccum += Time.unscaledDeltaTime * 1000f;
            frameCount++;

            flushTimer += Time.unscaledDeltaTime;
            if (flushTimer >= autoFlushSeconds)
            {
                flushTimer = 0f;
                Store?.Flush();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Store?.Flush();
        }

        void OnApplicationQuit()
        {
            Store?.Flush();
        }

        void HandleOutcome(StitchOutcome outcome)
        {
            if (outcome == null || Store == null) return;

            var attempt = new StitchAttempt
            {
                timestampUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                mode = mode,
                drillId = drillId,
                stitchType = (int)outcome.stitchType,
                kind = (int)outcome.kind,
                overallQuality = outcome.overallQuality,
                endedAtPhase = outcome.endedAtPhase ?? "",
                phases = outcome.phases,
                windowConfigId = CurrentConfigId(),
                sessionFrameMs = frameCount > 0 ? frameMsAccum / frameCount : 0f,
            };

            Store.Append(attempt);

            if (logAttempts)
            {
                Debug.Log($"RecordLogger: {outcome.stitchType} {outcome.kind} " +
                          $"(phases: {outcome.phases.Count}, config {attempt.windowConfigId})", this);
            }
        }

        /// <summary>
        /// Registers the tolerances currently on ProgressManager, reusing the cached id while they
        /// haven't changed. Practice mode will rescale these per drill, so this cannot simply be
        /// resolved once at startup.
        /// </summary>
        int CurrentConfigId()
        {
            if (progressManager == null) return -1;

            WindowConfig config = progressManager.BuildWindowConfig();
            string sig = config.Signature();

            if (sig == cachedSignature) return cachedConfigId;

            cachedConfigId = Store.RegisterConfig(config);
            cachedSignature = sig;
            return cachedConfigId;
        }

        /// <summary>Call when entering/leaving a drill so attempts are tagged correctly.</summary>
        public void SetContext(string newMode, string newDrillId)
        {
            mode = newMode;
            drillId = newDrillId ?? "";
        }

        [ContextMenu("Flush Now")]
        public void FlushNow() => Store?.Flush(true);

        [ContextMenu("Log Record Summary")]
        public void LogSummary()
        {
            if (Store == null) { Debug.Log("RecordLogger: no store."); return; }
            Debug.Log($"RecordLogger: {Store.Data.attempts.Count} attempts, " +
                      $"{Store.Data.configs.Count} window configs\n{Store.Path}");
        }
    }
}
