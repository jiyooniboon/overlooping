using System;
using System.Collections.Generic;
using UnityEngine;
using KnitSprite.Records;

/// <summary>
/// Owns the knitting input/timing state machine for a single stitch (cast-on level).
/// Knows nothing about sprites or the Animator - it only tracks input, judges timing,
/// and fires events. ProgresstoAnimation listens to those events and plays the matching clip.
///
/// Input sequence, matched to the clips as described:
///   1. Hold A                     -> ReadyingYarn   (clip: castOnAlt)
///   2. Press Y (while holding A)  -> MovedToA        (clip: castOnAlt2), starts the Y->I timing window
///   3. Press I (while holding A)  -> MovedToB         (clip: castOnAlt3), judges Y->I timing
///   4. After moveToBDuration      -> Tightening        (clip: castOnAlt4), still holding A
///   5. Release A                  -> judges tighten-hold duration, combines both judgments,
///                                    fires OnStitchComplete, resets to Idle
///
/// moveToBDuration is purely visual pacing (how long castOnAlt3 gets to play before the tighten
/// loop takes over) - it does NOT count against the tighten timing judgment. The tighten timer
/// only starts once the FSM actually enters Tightening, so min/perfect/maxTightenTime keep the
/// same meaning no matter how long the castOnAlt3 clip is.
/// </summary>
public class ProgressManager : MonoBehaviour
{
    public enum StitchStep { Idle, ReadyingYarn, MovedToA, MovedToB, Tightening }
    public enum StitchQuality { Miss, Good, Perfect } // ordered worst -> best, used by CombineQuality

    [Header("Identity")]
    [Tooltip("Which stitch this manager is working. Only CastOn is implemented; Knit and Purl " +
             "exist so recorded data stays meaningful once they do.")]
    public StitchType stitchType = StitchType.CastOn;

    [Header("Input")]
    public KeyCode readyKey = KeyCode.A;
    public KeyCode moveToAKey = KeyCode.Y;
    public KeyCode moveToBKey = KeyCode.I;

    [Header("Y -> I timing window (seconds after Y is pressed)")]
    public float minYIInterval = 0.3f;
    public float perfectYITime = 0.5f;
    public float maxYIInterval = 0.8f;
    public float perfectYIMargin = 0.1f;

    [Header("MovedToB (\"press I\" clip) - how long it plays before the tighten loop takes over")]
    public float moveToBDuration = 20f / 12f; // castOnAlt3: 20 frames at 12 samples/sec

    [Header("Tighten hold duration (seconds A is held after I is pressed, spanning MovedToB + Tightening)")]
    public float minTightenTime = 0.3f;
    public float perfectTightenTime = 0.6f;
    public float maxTightenTime = 1.2f;
    public float perfectTightenMargin = 0.15f;

    [Header("Debug")]
    public bool logStepChanges = true;

    /// Fires whenever the FSM moves to a new step - drives in-progress animations (ready/move/tighten poses).
    public event Action<StitchStep> OnStepChanged;

    /// Fires once per completed (or dropped) stitch with the final judged quality.
    public event Action<StitchQuality> OnStitchComplete;

    /// <summary>
    /// Fires once for EVERY way a stitch attempt can end - completed, dropped early, or timed out -
    /// carrying the raw phase timings rather than just a verdict.
    ///
    /// OnStitchComplete deliberately stays as it is so ProgresstoAnimation needs no changes. This
    /// event exists because the analyzer needs two things that verdict alone cannot provide:
    /// the raw elapsed times (so "0.14s late" is distinguishable from "0.02s late"), and the
    /// attempts that never completed at all. Previously a player who kept fumbling produced no
    /// events whatsoever, meaning the players who most need practice generated the least data.
    /// </summary>
    public event Action<StitchOutcome> OnStitchOutcome;

    public StitchStep CurrentStep => step;
    public int CompletedStitches { get; private set; }

    private StitchStep step = StitchStep.Idle;
    private float readyTimer;
    private float yTimer;
    private float tightenTimer;
    private float movedToBTimer;
    private StitchQuality yiQuality;

    // Elapsed times are frozen as each phase is left, so a later phase's timer can't overwrite
    // what an earlier one measured when an outcome is assembled.
    private float readyElapsed;
    private float yiElapsed;

    void Update()
    {
        switch (step)
        {
            case StitchStep.Idle:
                if (Input.GetKeyDown(readyKey))
                {
                    readyTimer = 0f;
                    SetStep(StitchStep.ReadyingYarn);
                }
                break;

            case StitchStep.ReadyingYarn:
                if (Input.GetKeyUp(readyKey))
                {
                    ReportDropDuringReady();
                    SetStep(StitchStep.Idle); // released before pressing Y - dropped stitch
                    break;
                }
                readyTimer += Time.unscaledDeltaTime;
                if (Input.GetKeyDown(moveToAKey))
                {
                    readyElapsed = readyTimer; // freeze before the next phase starts measuring
                    yTimer = 0f;
                    SetStep(StitchStep.MovedToA);
                }
                break;

            case StitchStep.MovedToA:
                if (Input.GetKeyUp(readyKey))
                {
                    ReportDropDuringYToI();
                    SetStep(StitchStep.Idle); // released before pressing I - dropped stitch
                    break;
                }
                yTimer += Time.unscaledDeltaTime;
                if (Input.GetKeyDown(moveToBKey))
                {
                    yiElapsed = yTimer;
                    yiQuality = JudgeTiming(yTimer, minYIInterval, perfectYITime, maxYIInterval, perfectYIMargin);
                    tightenTimer = 0f;
                    movedToBTimer = 0f;
                    SetStep(StitchStep.MovedToB);
                }
                else if (yTimer > maxYIInterval)
                {
                    // "Hesitated past the window" is a specific, coachable failure - record it
                    // rather than letting the retry erase the evidence.
                    ReportTimeoutDuringYToI();
                    readyTimer = 0f; // the retry is a fresh ready phase, A is still held
                    SetStep(StitchStep.ReadyingYarn); // too slow to press I - retry from ready, A still held
                }
                break;

            case StitchStep.MovedToB:
                if (Input.GetKeyUp(readyKey))
                {
                    ReportDropBeforeTighten();
                    SetStep(StitchStep.Idle); // released before tightening ever started - just a reset, not a scored Miss
                    break;
                }
                movedToBTimer += Time.unscaledDeltaTime;
                if (movedToBTimer >= moveToBDuration)
                {
                    tightenTimer = 0f; // the real tighten judgment starts fresh here, not at the I press
                    SetStep(StitchStep.Tightening);
                }
                break;

            case StitchStep.Tightening:
                if (Input.GetKeyUp(readyKey))
                {
                    CompleteStitch(tightenTimer);
                    break;
                }
                tightenTimer += Time.unscaledDeltaTime;
                if (tightenTimer > maxTightenTime)
                {
                    CompleteStitch(tightenTimer); // held too long -> JudgeTiming scores it a Miss
                }
                break;
        }
    }

    void CompleteStitch(float tightenElapsed)
    {
        StitchQuality tightenQuality = JudgeTiming(
            tightenElapsed, minTightenTime, perfectTightenTime, maxTightenTime, perfectTightenMargin);

        StitchQuality overall = CombineQuality(yiQuality, tightenQuality);

        CompletedStitches++;
        if (logStepChanges) Debug.Log($"ProgressManager: stitch complete -> {overall} (Y-I: {yiQuality}, tighten: {tightenQuality})");

        ReportOutcome(OutcomeKind.Completed, "", overall, new List<PhaseTiming>
        {
            new PhaseTiming(PhaseIds.Ready,   readyElapsed),
            new PhaseTiming(PhaseIds.YToI,    yiElapsed,      true, (int)yiQuality),
            new PhaseTiming(PhaseIds.Tighten, tightenElapsed, true, (int)tightenQuality),
        });

        OnStitchComplete?.Invoke(overall);
        SetStep(StitchStep.Idle);
    }

    // ---- Instrumentation -------------------------------------------------------------
    // These only report; they never touch the state machine. Every path out of a stitch attempt
    // fires exactly one outcome, so the analyzer can count attempts without double-counting.

    void ReportOutcome(OutcomeKind kind, string endedAtPhase, StitchQuality overall,
                       List<PhaseTiming> phases)
    {
        if (OnStitchOutcome == null) return; // don't allocate when nothing is listening

        OnStitchOutcome.Invoke(new StitchOutcome
        {
            stitchType = stitchType,
            kind = kind,
            endedAtPhase = endedAtPhase,
            overallQuality = (int)overall,
            phases = phases,
        });
    }

    /// <summary>
    /// Released A before ever pressing Y. Often just a mis-press rather than a fumble, so the
    /// hold duration is recorded unjudged and the analyzer can filter very short taps out.
    /// </summary>
    void ReportDropDuringReady()
    {
        ReportOutcome(OutcomeKind.DroppedEarly, PhaseIds.Ready, StitchQuality.Miss,
            new List<PhaseTiming> { new PhaseTiming(PhaseIds.Ready, readyTimer) });
    }

    /// <summary>Released A after Y but before I - the Y-to-I phase never completed.</summary>
    void ReportDropDuringYToI()
    {
        ReportOutcome(OutcomeKind.DroppedEarly, PhaseIds.YToI, StitchQuality.Miss,
            new List<PhaseTiming>
            {
                new PhaseTiming(PhaseIds.Ready, readyElapsed),
                new PhaseTiming(PhaseIds.YToI,  yTimer), // unjudged: I was never pressed
            });
    }

    /// <summary>Held past maxYIInterval without pressing I; the FSM resets to ReadyingYarn.</summary>
    void ReportTimeoutDuringYToI()
    {
        ReportOutcome(OutcomeKind.TimedOut, PhaseIds.YToI, StitchQuality.Miss,
            new List<PhaseTiming>
            {
                new PhaseTiming(PhaseIds.Ready, readyElapsed),
                new PhaseTiming(PhaseIds.YToI,  yTimer),
            });
    }

    /// <summary>Released A during the MovedToB pacing clip, so tightening never began.</summary>
    void ReportDropBeforeTighten()
    {
        ReportOutcome(OutcomeKind.DroppedEarly, PhaseIds.Tighten, StitchQuality.Miss,
            new List<PhaseTiming>
            {
                new PhaseTiming(PhaseIds.Ready,   readyElapsed),
                new PhaseTiming(PhaseIds.YToI,    yiElapsed, true, (int)yiQuality),
                new PhaseTiming(PhaseIds.Tighten, 0f),     // never reached
            });
    }

    /// <summary>
    /// Snapshot of the timing tolerances currently in force, for RecordLogger to store alongside
    /// attempts. Only judged phases appear - "ready" has no window.
    ///
    /// Practice mode will scale these fields per drill, which is exactly why each attempt has to
    /// record which values applied to it.
    /// </summary>
    public WindowConfig BuildWindowConfig()
    {
        return new WindowConfig
        {
            stitchType = (int)stitchType,
            phases = new List<PhaseWindow>
            {
                new PhaseWindow {
                    phaseId = PhaseIds.YToI,
                    min = minYIInterval, perfect = perfectYITime,
                    max = maxYIInterval, margin = perfectYIMargin
                },
                new PhaseWindow {
                    phaseId = PhaseIds.Tighten,
                    min = minTightenTime, perfect = perfectTightenTime,
                    max = maxTightenTime, margin = perfectTightenMargin
                },
            }
        };
    }

    // ---- Read-only live state (for HUDs and feedback; nothing here mutates the FSM) ----

    public float ReadyElapsed   => readyTimer;
    public float YToIElapsed    => yTimer;
    public float TightenElapsed => tightenTimer;

    /// <summary>
    /// The phase currently being measured, if any, with the window it will be judged against.
    /// Returns false in Idle (nothing started) and MovedToB (that step is visual pacing - the
    /// tighten timer hasn't begun yet, so showing it would be misleading).
    ///
    /// `window` is null for the "ready" phase, which is recorded but not judged.
    /// </summary>
    public bool TryGetActivePhase(out string phaseId, out float elapsed, out PhaseWindow window)
    {
        switch (step)
        {
            case StitchStep.ReadyingYarn:
                phaseId = PhaseIds.Ready; elapsed = readyTimer; window = null;
                return true;

            case StitchStep.MovedToA:
                phaseId = PhaseIds.YToI; elapsed = yTimer;
                window = new PhaseWindow {
                    phaseId = PhaseIds.YToI,
                    min = minYIInterval, perfect = perfectYITime,
                    max = maxYIInterval, margin = perfectYIMargin
                };
                return true;

            case StitchStep.Tightening:
                phaseId = PhaseIds.Tighten; elapsed = tightenTimer;
                window = new PhaseWindow {
                    phaseId = PhaseIds.Tighten,
                    min = minTightenTime, perfect = perfectTightenTime,
                    max = maxTightenTime, margin = perfectTightenMargin
                };
                return true;

            default:
                phaseId = null; elapsed = 0f; window = null;
                return false;
        }
    }

    static StitchQuality JudgeTiming(float elapsed, float min, float perfect, float max, float margin)
    {
        if (elapsed < min || elapsed > max) return StitchQuality.Miss;
        return Mathf.Abs(elapsed - perfect) <= margin ? StitchQuality.Perfect : StitchQuality.Good;
    }

    static StitchQuality CombineQuality(StitchQuality a, StitchQuality b)
    {
        // Enum values are ordered worst -> best, so the lower one is the worse result.
        return (StitchQuality)Mathf.Min((int)a, (int)b);
    }

    void SetStep(StitchStep newStep)
    {
        if (step == newStep) return;
        step = newStep;
        if (logStepChanges) Debug.Log($"ProgressManager: step -> {step}");
        OnStepChanged?.Invoke(step);
    }
}
