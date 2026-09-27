using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KnitSprite.Records;

/// <summary>
/// Temporary on-screen readout for story mode, standing in until the stitch animations can carry
/// the feedback themselves.
///
/// Drop this on any GameObject in a playable scene and press Play - it finds ProgressManager,
/// builds its own Canvas, and needs no wiring. Delete the component (or untick it) once the
/// animations communicate state on their own.
///
/// It is a pure observer: it subscribes to events and reads public properties, never touching the
/// FSM or the recorder. Nothing here can change how the game plays or what gets logged.
///
/// The live phase readout is the part worth keeping longest. Cast-on is a timing mechanic, so
/// without either animations or numbers there is no way to tell a near-miss from a wild miss,
/// which makes tuning the windows guesswork.
/// </summary>
public class StoryModeHud : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Leave empty to find a ProgressManager on this GameObject, then anywhere in the scene.")]
    public ProgressManager progressManager;

    [Header("Display")]
    [Tooltip("Draw a bar showing the current phase against its timing window. Untick for text only.")]
    public bool showTimingBar = true;

    public int fontSize = 22;
    public Vector2 screenOffset = new Vector2(24f, -24f);

    [Tooltip("Sits above other UI in the scene.")]
    public int sortingOrder = 500;

    // --- running tallies ---
    private int perfectCount, goodCount, missCount, dropCount, timeoutCount;
    private string lastStitchLine = "-";

    private Text statusText;
    private RectTransform barTrack, barFill, barPerfectZone;
    private Font font;
    private bool subscribed;

    void Awake()
    {
        if (progressManager == null) progressManager = GetComponent<ProgressManager>();
        if (progressManager == null) progressManager = FindFirstObjectByType<ProgressManager>();

        if (progressManager == null)
        {
            Debug.LogWarning("StoryModeHud: no ProgressManager found - HUD will show nothing.", this);
            return;
        }

        BuildUi();
    }

    void OnEnable()
    {
        if (progressManager == null || subscribed) return;
        progressManager.OnStitchOutcome += HandleOutcome;
        subscribed = true;
    }

    void OnDisable()
    {
        if (!subscribed || progressManager == null) return;
        progressManager.OnStitchOutcome -= HandleOutcome;
        subscribed = false;
    }

    void Update()
    {
        if (progressManager == null || statusText == null) return;

        bool active = progressManager.TryGetActivePhase(out string phaseId, out float elapsed,
                                                        out PhaseWindow window);

        statusText.text = BuildStatus(active, phaseId, elapsed, window);
        UpdateBar(active, elapsed, window);
    }

    string BuildStatus(bool active, string phaseId, float elapsed, PhaseWindow window)
    {
        var sb = new System.Text.StringBuilder(320);

        sb.Append("<b>").Append(progressManager.stitchType).Append("</b>   step: ")
          .Append(progressManager.CurrentStep).Append('\n');

        // Live phase timing - the most useful line while there are no animations.
        if (active && window != null)
        {
            sb.Append(phaseId).Append("  ")
              .Append(elapsed.ToString("0.00")).Append("s  /  target ")
              .Append(window.perfect.ToString("0.00")).Append("s   ")
              .Append(Verdict(elapsed, window)).Append('\n');
        }
        else if (active)
        {
            sb.Append(phaseId).Append("  ").Append(elapsed.ToString("0.00")).Append("s\n");
        }
        else
        {
            sb.Append('\n');
        }

        sb.Append("\nstitches ").Append(progressManager.CompletedStitches)
          .Append("    perfect ").Append(perfectCount)
          .Append("   good ").Append(goodCount)
          .Append("   miss ").Append(missCount).Append('\n');

        sb.Append("dropped ").Append(dropCount)
          .Append("   timed out ").Append(timeoutCount).Append('\n');

        sb.Append("\nlast: ").Append(lastStitchLine).Append('\n');
        sb.Append("\n<i>").Append(Hint(progressManager.CurrentStep)).Append("</i>");

        return sb.ToString();
    }

    /// <summary>Same thresholds ProgressManager judges by, so the readout can't disagree with the score.</summary>
    static string Verdict(float elapsed, PhaseWindow w)
    {
        if (elapsed < w.min) return "(too early)";
        if (elapsed > w.max) return "(too late)";
        return Mathf.Abs(elapsed - w.perfect) <= w.margin ? "(PERFECT)" : "(ok)";
    }

    static string Hint(ProgressManager.StitchStep step)
    {
        switch (step)
        {
            case ProgressManager.StitchStep.Idle:         return "hold A to start a stitch";
            case ProgressManager.StitchStep.ReadyingYarn: return "press Y (keep holding A)";
            case ProgressManager.StitchStep.MovedToA:     return "press I (keep holding A)";
            case ProgressManager.StitchStep.MovedToB:     return "keep holding A...";
            case ProgressManager.StitchStep.Tightening:   return "release A to finish";
            default:                                      return "";
        }
    }

    void HandleOutcome(StitchOutcome outcome)
    {
        if (outcome == null) return;

        switch (outcome.kind)
        {
            case OutcomeKind.DroppedEarly:
                dropCount++;
                lastStitchLine = $"dropped during {outcome.endedAtPhase}";
                return;

            case OutcomeKind.TimedOut:
                timeoutCount++;
                lastStitchLine = $"timed out during {outcome.endedAtPhase}";
                return;
        }

        var q = (ProgressManager.StitchQuality)outcome.overallQuality;
        if (q == ProgressManager.StitchQuality.Perfect) perfectCount++;
        else if (q == ProgressManager.StitchQuality.Good) goodCount++;
        else missCount++;

        float yi = FindPhase(outcome.phases, PhaseIds.YToI);
        float tighten = FindPhase(outcome.phases, PhaseIds.Tighten);
        lastStitchLine = $"{q}   (Y-I {yi:0.00}s, tighten {tighten:0.00}s)";
    }

    static float FindPhase(List<PhaseTiming> phases, string id)
    {
        if (phases == null) return 0f;
        for (int i = 0; i < phases.Count; i++)
            if (phases[i].phaseId == id) return phases[i].elapsed;
        return 0f;
    }

    [ContextMenu("Reset Counters")]
    public void ResetCounters()
    {
        perfectCount = goodCount = missCount = dropCount = timeoutCount = 0;
        lastStitchLine = "-";
    }

    // ---- UI construction -----------------------------------------------------------

    void BuildUi()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("StoryModeHudCanvas",
            typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        // No GraphicRaycaster and no EventSystem: this HUD is never interactive, and adding a
        // raycaster could steal clicks from gameplay or from other UI in the scene.

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = NewRect("Readout", (RectTransform)canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = screenOffset;
        panel.sizeDelta = new Vector2(620f, 280f);

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.72f);
        bg.raycastTarget = false;

        var textRect = NewRect("Status", panel);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(16f, 16f);
        textRect.offsetMax = new Vector2(-16f, -12f);

        statusText = textRect.gameObject.AddComponent<Text>();
        statusText.font = font;
        statusText.fontSize = fontSize;
        statusText.color = new Color(0.12f, 0.12f, 0.16f);
        statusText.alignment = TextAnchor.UpperLeft;
        statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;
        statusText.supportRichText = true;   // <b>/<i> used in BuildStatus
        statusText.raycastTarget = false;

        if (showTimingBar) BuildBar(panel);
    }

    void BuildBar(RectTransform parent)
    {
        // Anchors + sizeDelta only; mixing those with offsetMin/offsetMax on partially-stretched
        // rects is where runtime-built uGUI usually goes wrong.
        barTrack = NewRect("BarTrack", parent);
        barTrack.anchorMin = new Vector2(0f, 0f);
        barTrack.anchorMax = new Vector2(1f, 0f);
        barTrack.pivot = new Vector2(0.5f, 0f);
        barTrack.sizeDelta = new Vector2(-32f, 14f);   // parent width minus 16px each side
        barTrack.anchoredPosition = new Vector2(0f, 10f);

        var trackImg = barTrack.gameObject.AddComponent<Image>();
        trackImg.color = new Color(0f, 0f, 0f, 0.12f);
        trackImg.raycastTarget = false;

        // Perfect band is added before the fill so the fill draws on top of it.
        barPerfectZone = NewBarPart("PerfectZone", new Color(0.3f, 0.7f, 0.35f, 0.55f));
        barFill        = NewBarPart("Fill",        new Color(0.2f, 0.3f, 0.5f, 0.9f));
    }

    RectTransform NewBarPart(string name, Color color)
    {
        var rt = NewRect(name, barTrack);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);   // stretch vertically, position horizontally
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 0f);
        rt.anchoredPosition = Vector2.zero;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    void UpdateBar(bool active, float elapsed, PhaseWindow window)
    {
        if (barTrack == null) return;

        bool show = active && window != null && window.max > 0f;
        barTrack.gameObject.SetActive(show);
        if (!show) return;

        float trackWidth = barTrack.rect.width;

        float fill = Mathf.Clamp01(elapsed / window.max);
        barFill.sizeDelta = new Vector2(trackWidth * fill, 0f);

        float zoneStart = Mathf.Clamp01((window.perfect - window.margin) / window.max);
        float zoneEnd   = Mathf.Clamp01((window.perfect + window.margin) / window.max);
        barPerfectZone.anchoredPosition = new Vector2(trackWidth * zoneStart, 0f);
        barPerfectZone.sizeDelta = new Vector2(trackWidth * (zoneEnd - zoneStart), 0f);
    }

    static RectTransform NewRect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
