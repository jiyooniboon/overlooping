using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>
    /// Keyword -> flat icon from an image model (FLUX.2 Klein in Draw Things) -> ImageToChart.
    ///
    /// Added after the shape test (plan v2 §2.3): local 2-9B LLMs knew which parts a subject has
    /// but couldn't place them (recognizability 1.0-1.5 out of 3). Diffusion models are good at
    /// exactly that, and ImageToChart was built for flat-icon images, so this is now the first
    /// source; ShapeSource stays as the fallback when no image model is running.
    ///
    /// The prompt lives in Assets/Resources/AI/ImagePrompt.txt (shared with
    /// AITools/diffusion_test.py) so it can be tuned without recompiling. Placeholders:
    /// {keyword}, {background} (yarn 1), {outline} (yarn 2), {fills} (yarns 3+), {colors} (all).
    /// </summary>
    public class DiffusionSource : IChartImageSource, ICancellableSource
    {
        public const string PromptResource = "AI/ImagePrompt";

        public int size = 512;
        /// <summary>0 = use the steps set in the app (Draw Things). FLUX.2 Klein needs about 4.</summary>
        public int steps = 0;
        /// <summary>-1 = a new design every time; any other value repeats the same image per keyword.</summary>
        public int seed = -1;
        public float timeoutSeconds = 90f;

        /// <summary>If set, every image is also saved here as PNG (for tuning and the writeup).</summary>
        public string saveImagesTo;

        public string LastPrompt { get; private set; }

        readonly IImageModel model;
        readonly YarnPalette palette;
        string template;
        int runId;

        public DiffusionSource(IImageModel model, YarnPalette palette)
        {
            this.model = model;
            this.palette = palette;
        }

        public string DisplayName => $"AI image ({model.DisplayName})";

        public void Abort()
        {
            runId++;
            model.Abort();
        }

        public string BuildPrompt(string keyword)
        {
            if (template == null)
            {
                var t = Resources.Load<TextAsset>(PromptResource);
                template = t != null ? t.text.Trim() : null;
            }
            if (template == null) return PromptBuilder.BuildPositive(keyword, palette); // file missing

            var names = new List<string>();
            for (int i = 0; i < palette.Count; i++) names.Add(palette.GetName(i).Trim().ToLowerInvariant());

            // {outline} = yarn 2 (index 1), {fills} = the yarns after it. With fewer than 3 yarns
            // there's nothing left to fill with, so the outline yarn fills too.
            string outline = names.Count > 1 ? names[1] : names[0];
            var fills = names.Count > 2 ? names.GetRange(2, names.Count - 2) : new List<string> { outline };

            return template
                .Replace("{keyword}", keyword)
                .Replace("{background}", names[0])
                .Replace("{outline}", outline)
                .Replace("{fills}", JoinAnd(fills))
                .Replace("{colors}", string.Join(", ", names));
        }

        /// "blue", "blue and pink", "blue, pink and green"
        static string JoinAnd(List<string> items)
        {
            if (items.Count <= 1) return items.Count == 1 ? items[0] : "";
            return string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
        }

        public IEnumerator Generate(string keyword, Action<ImageResult> onComplete)
        {
            int myRun = ++runId;
            LastPrompt = BuildPrompt(keyword);

            var req = new ImageRequest
            {
                prompt = LastPrompt,
                negativePrompt = PromptBuilder.NegativePrompt, // FLUX ignores it; SDXL uses it
                width = size, height = size,
                steps = steps, seed = seed,
                timeoutSeconds = timeoutSeconds
            };

            ImageResult r = default;
            bool got = false;
            yield return model.Txt2Img(req, x => { r = x; got = true; });

            if (myRun != runId)
            {
                if (r.texture != null) UnityEngine.Object.Destroy(r.texture);
                onComplete(ImageResult.Cancelled());
                yield break;
            }
            if (!got) { onComplete(ImageResult.Fail($"{model.DisplayName} returned nothing.")); yield break; }
            if (!r.success) { onComplete(r); yield break; }

            if (!string.IsNullOrEmpty(saveImagesTo)) Save(r.texture, keyword);

            r.message = $"{r.message.TrimEnd('.')} for \"{keyword}\". Edit it by hand from here.";
            onComplete(r);
        }

        void Save(Texture2D tex, string keyword)
        {
            try
            {
                Directory.CreateDirectory(saveImagesTo);
                string safe = string.Join("_", keyword.Split(Path.GetInvalidFileNameChars()));
                string file = Path.Combine(saveImagesTo, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safe}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Debug.Log($"[DiffusionSource] saved {file}");
            }
            catch (Exception e) { Debug.LogWarning($"[DiffusionSource] couldn't save image: {e.Message}"); }
        }
    }
}
