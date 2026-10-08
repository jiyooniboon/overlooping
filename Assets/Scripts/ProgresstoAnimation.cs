using UnityEngine;

/// <summary>
/// Bridges ProgressManager's state machine to the Animator. This is the only script that
/// touches the Animator - ProgressManager never knows animations exist.
///
/// Current clip mapping - all cut from "Cast On_WIP4_Sprite Sheet" (21 frames, 6 fps) and held
/// in the "Cast On_WIP4_Sprite Sheet_0" Animator Controller:
///   castOn_idle        frame 0       Idle
///   castOn_ready       frames 0-4    ReadyingYarn (A held)
///   castOn_needleUnder frames 5-9    MovedToA     (Y pressed)
///   castOn_needleOver  frames 10-14  MovedToB     (I pressed) - length = ProgressManager.moveToBDuration
///   castOn_tighten     frames 15-20  Tightening   (A held)
/// The frame split is a first guess; re-cut the clips if the artist intends different breaks.
///
/// Result states are blank on purpose: ProgressManager returns to Idle in the same frame a stitch
/// completes, so a result clip would be overwritten immediately. Leave them empty unless that changes.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ProgresstoAnimation : MonoBehaviour
{
    [Tooltip("The FSM driving knitting input. Drag the GameObject that holds ProgressManager here.")]
    public ProgressManager progressManager;

    [Header("In-progress states (play while a stitch is being worked)")]
    public string idleState = "castOn_idle";
    public string readyingYarnState = "castOn_ready";
    public string movedToAState = "castOn_needleUnder";
    public string movedToBState = "castOn_needleOver";
    public string tighteningState = "castOn_tighten";

    [Header("Result states (play once a stitch finishes) - see note above")]
    public string perfectStitchState = "";
    public string goodStitchState = "";
    public string missStitchState = "";

    private Animator animator;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (progressManager == null)
            Debug.LogWarning("ProgresstoAnimation: no ProgressManager assigned - it won't receive any events.", this);
    }

    void OnEnable()
    {
        if (progressManager == null) return;
        progressManager.OnStepChanged += HandleStepChanged;
        progressManager.OnStitchComplete += HandleStitchComplete;
    }

    void OnDisable()
    {
        if (progressManager == null) return;
        progressManager.OnStepChanged -= HandleStepChanged;
        progressManager.OnStitchComplete -= HandleStitchComplete;
    }

    void HandleStepChanged(ProgressManager.StitchStep step)
    {
        switch (step)
        {
            case ProgressManager.StitchStep.Idle: Play(idleState); break;
            case ProgressManager.StitchStep.ReadyingYarn: Play(readyingYarnState); break;
            case ProgressManager.StitchStep.MovedToA: Play(movedToAState); break;
            case ProgressManager.StitchStep.MovedToB: Play(movedToBState); break;
            case ProgressManager.StitchStep.Tightening: Play(tighteningState); break;
        }
    }

    void HandleStitchComplete(ProgressManager.StitchQuality quality)
    {
        switch (quality)
        {
            case ProgressManager.StitchQuality.Perfect: Play(perfectStitchState); break;
            case ProgressManager.StitchQuality.Good: Play(goodStitchState); break;
            case ProgressManager.StitchQuality.Miss: Play(missStitchState); break;
        }
    }

    void Play(string stateName)
    {
        if (string.IsNullOrEmpty(stateName)) return;

        if (!animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"ProgresstoAnimation: no Animator state named \"{stateName}\" yet - skipping. " +
                              "Add it to the Animator Controller once that clip exists.", this);
            return;
        }

        animator.Play(stateName, 0, 0f);
    }
}
