using System;
using System.Collections.Generic;

namespace KnitSprite.Records
{
    /// <summary>
    /// Which stitch the player was working. Values are explicit and must never be reordered or
    /// renumbered - they are written into records.json as ints, and shifting them would silently
    /// relabel every historical attempt.
    ///
    /// Only CastOn is implemented today; Knit and Purl are declared now so that saved data from
    /// current playtests stays readable once they exist.
    /// </summary>
    public enum StitchType
    {
        CastOn = 0,
        Knit   = 1,
        Purl   = 2,
    }

    /// <summary>How a stitch attempt ended. Explicit values for the same reason as StitchType.</summary>
    public enum OutcomeKind
    {
        Completed    = 0,  // played through to the end and was judged
        DroppedEarly = 1,  // the ready key was released before the stitch finished
        TimedOut     = 2,  // a timing window expired and the stitch reset
    }

    /// <summary>
    /// Stable string ids for the phases within a stitch. Strings rather than an enum because
    /// different stitch types will have different phase sets - knit and purl won't necessarily
    /// share cast-on's "move to A / move to B / tighten" shape - and a per-stitch-type enum would
    /// force either one bloated shared enum or a schema change per stitch added.
    ///
    /// The analyzer groups by (stitchType, phaseId), so "purl tighten is weak but knit tighten is
    /// fine" falls out naturally with no extra work.
    /// </summary>
    public static class PhaseIds
    {
        public const string Ready   = "ready";    // A held, before the first movement key
        public const string YToI    = "yToI";     // Y press -> I press
        public const string Tighten = "tighten";  // tighten hold duration
    }

    /// <summary>
    /// One measured phase within a stitch. `judged` is false for phases that are recorded for
    /// information but have no timing window (currently "ready"), and for phases the player never
    /// reached because they dropped out early. Keeping unjudged phases rather than omitting them
    /// preserves the shape of a partial attempt, which is what makes drop analysis possible.
    /// </summary>
    [Serializable]
    public class PhaseTiming
    {
        public string phaseId;
        public float elapsed;      // seconds; unscaled real time
        public bool judged;
        public int quality;        // cast from ProgressManager.StitchQuality; ignore when !judged

        public PhaseTiming() { }

        public PhaseTiming(string phaseId, float elapsed, bool judged = false, int quality = 0)
        {
            this.phaseId = phaseId;
            this.elapsed = elapsed;
            this.judged = judged;
            this.quality = quality;
        }
    }

    /// <summary>
    /// What ProgressManager announces when a stitch resolves, in any way. A class rather than a
    /// struct because it carries a list; one small allocation per stitch is irrelevant next to the
    /// clarity of not having to pre-size a fixed array per stitch type.
    /// </summary>
    public class StitchOutcome
    {
        public StitchType stitchType;
        public OutcomeKind kind;

        /// <summary>Phases actually entered, in order. Partial for drops and timeouts.</summary>
        public List<PhaseTiming> phases = new();

        /// <summary>Combined judgement. Only meaningful when kind == Completed.</summary>
        public int overallQuality;

        /// <summary>Phase the player bailed out of, for drops and timeouts. Empty otherwise.</summary>
        public string endedAtPhase = "";

        public bool Completed => kind == OutcomeKind.Completed;
    }

    /// <summary>Timing window for one judged phase.</summary>
    [Serializable]
    public class PhaseWindow
    {
        public string phaseId;
        public float min;
        public float perfect;
        public float max;
        public float margin;
    }

    /// <summary>
    /// A snapshot of the timing tolerances in force when an attempt was recorded.
    ///
    /// This exists because the windows are not constant: they get retuned during balancing, and
    /// practice mode deliberately scales them per drill. Without recording which windows applied,
    /// an attempt judged Perfect under a 0.2s margin averages together with one judged under a
    /// 0.1s margin and every statistic quietly becomes meaningless. Configs are stored once and
    /// referenced by id.
    /// </summary>
    [Serializable]
    public class WindowConfig
    {
        public int id;
        public int stitchType;
        public List<PhaseWindow> phases = new();

        /// <summary>
        /// Value identity used to deduplicate configs. Rounded to 4dp so float noise from scaling
        /// arithmetic doesn't spawn a new config every drill.
        /// </summary>
        public string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(stitchType).Append('|');
            foreach (var p in phases)
            {
                sb.Append(p.phaseId).Append(':')
                  .Append(p.min.ToString("F4")).Append(',')
                  .Append(p.perfect.ToString("F4")).Append(',')
                  .Append(p.max.ToString("F4")).Append(',')
                  .Append(p.margin.ToString("F4")).Append(';');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// One recorded attempt. Flat enough for JsonUtility (no dictionaries, no polymorphism) while
    /// still carrying the per-phase detail the analyzer needs.
    /// </summary>
    [Serializable]
    public class StitchAttempt
    {
        public long timestampUtc;
        public string mode = "story";     // "story" | "practice"
        public string drillId = "";       // empty outside practice mode

        public int stitchType;
        public int kind;                  // cast from OutcomeKind
        public int overallQuality;
        public string endedAtPhase = "";

        public List<PhaseTiming> phases = new();

        /// <summary>Index into RecordFile.configs. -1 means unknown (should not happen).</summary>
        public int windowConfigId = -1;

        /// <summary>
        /// Average frame time during the session, in milliseconds. Input is polled once per frame,
        /// so measurement jitter is bounded by frame time - data captured at 30fps is not strictly
        /// comparable to data captured at 144fps. Recording it means the discrepancy is at least
        /// detectable rather than invisible.
        /// </summary>
        public float sessionFrameMs;
    }

    /// <summary>
    /// Root of records.json.
    ///
    /// schemaVersion is checked on load and mismatches are discarded rather than migrated. That is
    /// deliberate: JsonUtility silently defaults fields it cannot find, so loading an old file
    /// after a field rename produces attempts full of plausible-looking zeros - "0.0s, Perfect" -
    /// which poison every average with no visible error.
    /// </summary>
    [Serializable]
    public class RecordFile
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<WindowConfig> configs = new();
        public List<StitchAttempt> attempts = new();
    }
}
