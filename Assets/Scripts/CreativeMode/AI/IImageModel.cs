using System;
using System.Collections;
using UnityEngine;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>Settings for one image request. Zero / empty means "use the backend's own setting".</summary>
    public class ImageRequest
    {
        public string prompt = "";
        public string negativePrompt = "";   // ignored by FLUX models
        public int width = 512, height = 512;
        public int steps = 0;                // 0 = backend default (Draw Things: the app's current setting)
        public int seed = -1;                // -1 = random
        public float cfgScale = 0f;          // 0 = backend default
        public float denoisingStrength = 0.55f; // img2img only: 0 keeps the input, 1 ignores it
        public float timeoutSeconds = 90f;
    }

    /// <summary>
    /// An image model (diffusion). Separate from IChartImageSource: a source decides *what* to
    /// ask for; this does the asking. A1111Client is the only implementation, but the A1111 API
    /// format is served by Draw Things (Mac), Forge Neo and AUTOMATIC1111 (NVIDIA PCs), so the
    /// same code runs on any of them.
    /// </summary>
    public interface IImageModel
    {
        string DisplayName { get; }

        /// <summary>Text to image. Always calls onDone exactly once.</summary>
        IEnumerator Txt2Img(ImageRequest request, Action<ImageResult> onDone);

        /// <summary>Image to image (redraws init guided by the prompt). Always calls onDone exactly once.</summary>
        IEnumerator Img2Img(Texture2D init, ImageRequest request, Action<ImageResult> onDone);

        /// <summary>Stops every request in flight. Their onDone still fires (with a failure).</summary>
        void Abort();
    }
}
