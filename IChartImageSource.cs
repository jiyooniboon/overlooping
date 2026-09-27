using System;
using System.Collections;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Supplies a source image for a keyword. This is the single seam between Creative Mode and
    /// any AI model: everything downstream (ImageToChart, the editor, the UI) only ever sees a
    /// Texture2D, so swapping the placeholder for Stable Diffusion is a one-class change with no
    /// ripple effects.
    ///
    /// Implementations must never throw into the caller and must always invoke onComplete exactly
    /// once, including on failure. A generation step that silently never returns leaves the UI
    /// stuck in a loading state, which in a live demo is indistinguishable from a crash.
    /// </summary>
    public interface IChartImageSource
    {
        /// <summary>Human-readable name shown in the UI, e.g. "Placeholder" or "Stable Diffusion (local)".</summary>
        string DisplayName { get; }

        /// <summary>
        /// Produces an image for the (already validated and prompt-wrapped) keyword.
        /// Run as a coroutine. Always calls onComplete once.
        /// </summary>
        IEnumerator Generate(string keyword, Action<ImageResult> onComplete);
    }

    public struct ImageResult
    {
        public bool success;
        public Texture2D texture;
        public string message;   // shown to the player: either a status note or an error
        public bool cancelled;

        public static ImageResult Ok(Texture2D tex, string message) =>
            new ImageResult { success = true, texture = tex, message = message };

        public static ImageResult Fail(string message) =>
            new ImageResult { success = false, texture = null, message = message };

        public static ImageResult Cancelled() =>
            new ImageResult { success = false, texture = null, cancelled = true, message = "Cancelled." };
    }

    /// <summary>
    /// Builds the actual text prompt sent to an image model. Kept separate from the model call so
    /// the wording can be iterated on without touching networking code.
    ///
    /// The wrapper matters more than any downstream image processing: a bare noun like
    /// "christmas" makes a general model paint a detailed *scene* - gradients, soft shadows,
    /// background clutter, dozens of near-identical greens - and almost none of that survives
    /// being squeezed into a 40-cell grid. Asking for a flat icon instead is what makes the
    /// result chartable.
    /// </summary>
    public static class PromptBuilder
    {
        public const string PositiveTemplate =
            "a simple flat icon of {0}, solid plain background, bold flat colors, thick outlines, " +
            "no gradient, no shading, no shadow, centered, minimalist vector illustration";

        public const string NegativePrompt =
            "photorealistic, texture, detailed background, gradient, soft shading, blurry, noise, " +
            "text, watermark, signature, 3d render";

        public static string BuildPositive(string keyword) => string.Format(PositiveTemplate, keyword);
    }
}
