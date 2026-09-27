using UnityEngine;

/// <summary>
/// Bridges ProgressManager's state machine to the Animator. This is the only script that
/// touches the Animator - ProgressManager never knows animations exist.
///
/// Current clip mapping (Assets/Animations, all consolidated into the
/// "image sequence alt_0" Animator Controller):
///   idle       - default/idle pose
///   castOnAlt  - ready yarn, plays while A is held (ReadyingYarn step)
///   castOnAlt2 - plays when Y is pressed (MovedToA step)
///   castOnAlt3 - plays when I is pressed (MovedToB step)
///   castOnAlt4 - tighten step, plays while A is held (Tightening step)
/// castOn / castOnAlt5 aren't wired to a step yet - castOn is still only used as the
/// perfect/good result clip below, and it isn't in the merged controller yet (see the
/// class comment on Play() - missing states just warn and skip, they won't error).
/// </summary>
[RequireComponent(typeof(Animator))]
public class ProgresstoAnimation : MonoBehaviour
{
    [Tooltip("The FSM driving knitting input. Drag the GameObject that holds ProgressManager here.")]
    public ProgressManager progressManager;

    [Header("In-progress states (play while a stitch is being worked)")]
    public string idleState = "idle";
    public string readyingYarnState = "castOnAlt";
    public string movedToAState = "castOnAlt2";
    public string movedToBState = "castOnAlt3";
    public string tighteningState = "castOnAlt4";

    [Header("Result states (play once a stitch finishes)")]
    public string perfectStitchState = "castOn";
    public string goodStitchState = "castOn";
    public string missStitchState = "castOnAlt";

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
