using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Bridges ProgressManager's state machine to the stitch art. This is the only script that
/// touches the sprites - ProgressManager never knows animations exist.
///
/// The art is one sprite sheet PER STITCH (cast on: 25 sheets x 21 frames). Every sheet has the
/// same hand motion; only the number of loops on the needle grows. So instead of an Animator with
/// 25 x 5 clips, this script picks the sheet from how many stitches are done, and the frame range
/// from the current step:
///
///   step           frames   key
///   Idle           0        -
///   ReadyingYarn   0-4      hold A
///   MovedToA       5-9      press Y
///   MovedToB       10-14    press I   (5 frames @ 6 fps = ProgressManager.moveToBDuration)
///   Tightening     15-20    let go of A
///
/// Each range plays once at framesPerSecond and holds its last frame (tick "loop" to repeat it).
/// Idle for stitch N shows frame 0 of sheet N+1, i.e. the needle with N loops on it. Once every
/// sheet is used up, Idle holds the last frame of the last sheet (the full needle).
///
/// Setup: put this on the object with the SpriteRenderer, set Sheet Folder, and the sheets fill in
/// automatically in the editor (or right-click the component > Reload sheets from folder).
/// For knit / purl, point Sheet Folder at that stitch's folder - no clips needed.
/// </summary>
[DefaultExecutionOrder(-50)] // set the first sprite before the page scripts frame their capture camera
[RequireComponent(typeof(SpriteRenderer))]
public class ProgresstoAnimation : MonoBehaviour
{
    [Serializable]
    public class StitchSheet
    {
        public string name;
        public Sprite[] frames;
    }

    [Serializable]
    public struct FrameRange
    {
        public int first;
        public int last;
        [Tooltip("Repeat the range while the step lasts, instead of holding the last frame.")]
        public bool loop;

        public FrameRange(int first, int last, bool loop = false)
        {
            this.first = first; this.last = last; this.loop = loop;
        }
    }

    [Tooltip("The FSM driving knitting input. Found automatically if left empty.")]
    public ProgressManager progressManager;

    [Header("Art")]
    [Tooltip("Folder holding one sliced sprite sheet per stitch, numbered 1, 2, 3...")]
    public string sheetFolder = "Assets/Sprites/1 Cast on spritesheet";
    [Tooltip("One entry per stitch, in order. Filled from Sheet Folder in the editor.")]
    public List<StitchSheet> sheets = new List<StitchSheet>();
    public float framesPerSecond = 6f;
    [Tooltip("After the last sheet, start again from sheet 1 (e.g. for endless practice). " +
             "Off = stay on the full needle.")]
    public bool wrapSheets = false;

    [Header("Frames per step (same for every sheet)")]
    public FrameRange idle = new FrameRange(0, 0);
    public FrameRange readyingYarn = new FrameRange(0, 4);
    public FrameRange movedToA = new FrameRange(5, 9);
    public FrameRange movedToB = new FrameRange(10, 14);
    public FrameRange tightening = new FrameRange(15, 20);

    /// Stitches completed since the last ResetStitchArt() - which sheet is showing.
    public int StitchesDone => (progressManager != null ? progressManager.CompletedStitches : 0) - baseline;

    SpriteRenderer spriteRenderer;
    ProgressManager.StitchStep currentStep = ProgressManager.StitchStep.Idle;
    FrameRange currentRange;
    float timer;
    int baseline;
    Sprite shown;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (progressManager == null) progressManager = FindFirstObjectByType<ProgressManager>();
        if (progressManager == null)
            Debug.LogWarning("ProgresstoAnimation: no ProgressManager found - the art will stay on the first frame.", this);
        if (sheets.Count == 0)
            Debug.LogWarning($"ProgresstoAnimation: no sprite sheets assigned (folder \"{sheetFolder}\").", this);

        // An Animator left on this object from the old setup would overwrite the sprite every frame.
        var animator = GetComponent<Animator>();
        if (animator != null && animator.enabled)
        {
            animator.enabled = false;
            Debug.Log("ProgresstoAnimation: disabled the old Animator on this object - it's no longer used and can be removed.", this);
        }

        EnterStep(progressManager != null ? progressManager.CurrentStep : ProgressManager.StitchStep.Idle);
    }

    void OnEnable()
    {
        if (progressManager != null) progressManager.OnStepChanged += EnterStep;
    }

    void OnDisable()
    {
        if (progressManager != null) progressManager.OnStepChanged -= EnterStep;
    }

    void Update()
    {
        int length = currentRange.last - currentRange.first + 1;
        if (length <= 1) return;

        timer += Time.unscaledDeltaTime; // same clock ProgressManager judges timing with
        int i = Mathf.FloorToInt(timer * framesPerSecond);
        i = currentRange.loop ? i % length : Mathf.Min(i, length - 1);
        ShowFrame(currentRange.first + i);
    }

    /// <summary>Start the art over from sheet 1 (e.g. "Cast on again"), without reloading the scene.</summary>
    public void ResetStitchArt()
    {
        baseline = progressManager != null ? progressManager.CompletedStitches : 0;
        EnterStep(currentStep);
    }

    void EnterStep(ProgressManager.StitchStep step)
    {
        currentStep = step;
        currentRange = RangeFor(step);
        timer = 0f;
        ShowFrame(currentRange.first);
    }

    FrameRange RangeFor(ProgressManager.StitchStep step)
    {
        switch (step)
        {
            case ProgressManager.StitchStep.ReadyingYarn: return readyingYarn;
            case ProgressManager.StitchStep.MovedToA: return movedToA;
            case ProgressManager.StitchStep.MovedToB: return movedToB;
            case ProgressManager.StitchStep.Tightening: return tightening;
            default: return idle;
        }
    }

    void ShowFrame(int frame)
    {
        if (sheets.Count == 0 || spriteRenderer == null) return;

        int done = Mathf.Max(0, StitchesDone);
        int sheetIndex;
        if (wrapSheets)
        {
            sheetIndex = done % sheets.Count;
        }
        else if (done >= sheets.Count)
        {
            // Every stitch is made: rest on the finished needle instead of re-showing the last stitch's start.
            sheetIndex = sheets.Count - 1;
            if (currentStep == ProgressManager.StitchStep.Idle) frame = int.MaxValue;
        }
        else
        {
            sheetIndex = done;
        }

        var frames = sheets[sheetIndex].frames;
        if (frames == null || frames.Length == 0) return;

        var sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
        if (sprite == shown) return;
        shown = sprite;
        spriteRenderer.sprite = sprite;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this)) return;

        // Loading assets or touching other components inside OnValidate is unsafe; wait until the editor is idle.
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (sheets.Count == 0 && !string.IsNullOrEmpty(sheetFolder) && AssetDatabase.IsValidFolder(sheetFolder))
                LoadSheetsFromFolder();
            else
                ShowFirstFrameInEditor();
        };
    }

    [ContextMenu("Reload sheets from folder")]
    void LoadSheetsFromFolder()
    {
        if (!AssetDatabase.IsValidFolder(sheetFolder))
        {
            Debug.LogWarning($"ProgresstoAnimation: \"{sheetFolder}\" isn't a folder in the project.", this);
            return;
        }

        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { sheetFolder })
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .Distinct()
            .OrderBy(p => TrailingNumber(System.IO.Path.GetFileNameWithoutExtension(p)))
            .ToList();

        Undo.RecordObject(this, "Reload stitch sheets");
        sheets.Clear();
        foreach (var path in paths)
        {
            var frames = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(s => TrailingNumber(s.name))
                .ToArray();
            if (frames.Length == 0)
            {
                Debug.LogWarning($"ProgresstoAnimation: {path} has no sprites - set Sprite Mode to Multiple and slice it.", this);
                continue;
            }
            sheets.Add(new StitchSheet { name = System.IO.Path.GetFileNameWithoutExtension(path), frames = frames });
        }

        int needed = Mathf.Max(idle.last, readyingYarn.last, movedToA.last, movedToB.last, tightening.last) + 1;
        foreach (var s in sheets.Where(s => s.frames.Length < needed))
            Debug.LogWarning($"ProgresstoAnimation: sheet \"{s.name}\" has {s.frames.Length} frames but the step ranges use {needed}.", this);

        Debug.Log($"ProgresstoAnimation: loaded {sheets.Count} sheets from {sheetFolder}.", this);
        ShowFirstFrameInEditor();
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    void ShowFirstFrameInEditor()
    {
        // So the scene view and the pages' auto-framing see the real sprite size before Play.
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || sheets.Count == 0 || sheets[0].frames == null || sheets[0].frames.Length == 0) return;
        var first = sheets[0].frames[Mathf.Clamp(idle.first, 0, sheets[0].frames.Length - 1)];
        if (sr.sprite != first)
        {
            sr.sprite = first;
            EditorUtility.SetDirty(sr);
        }
    }

    static int TrailingNumber(string s)
    {
        var m = Regex.Match(s, @"(\d+)\D*$");
        return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
    }
#endif
}
