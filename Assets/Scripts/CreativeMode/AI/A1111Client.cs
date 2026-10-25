using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>
    /// IImageModel for the AUTOMATIC1111 web API (/sdapi/v1/txt2img and /img2img), as served by
    /// Draw Things on Mac (Settings > API Server, HTTP, port 7860). Also works unchanged with
    /// AUTOMATIC1111 or Forge Neo on an NVIDIA PC.
    ///
    /// Fields left at 0 in ImageRequest are not sent, so Draw Things uses whatever model, steps
    /// and sampler are selected in the app. That keeps model choice in one place (the app) while
    /// tuning.
    /// </summary>
    public class A1111Client : IImageModel
    {
        public string baseUrl = "http://127.0.0.1:7860";
        public string displayName = "Draw Things";

        public string DisplayName => displayName;

        [Serializable] class ImagesResponse { public string[] images; public string error; public string detail; }

        readonly HashSet<UnityWebRequest> inFlight = new HashSet<UnityWebRequest>();

        public IEnumerator Txt2Img(ImageRequest request, Action<ImageResult> onDone)
        {
            return Send("/sdapi/v1/txt2img", BuildBody(request, null), request.timeoutSeconds, onDone);
        }

        public IEnumerator Img2Img(Texture2D init, ImageRequest request, Action<ImageResult> onDone)
        {
            if (init == null)
            {
                onDone(ImageResult.Fail("Img2Img needs an input image."));
                return Empty();
            }
            string b64;
            try { b64 = Convert.ToBase64String(init.EncodeToPNG()); }
            catch (Exception e)
            {
                // EncodeToPNG fails on non-readable or compressed textures.
                Debug.LogException(e);
                onDone(ImageResult.Fail("Couldn't encode the input image."));
                return Empty();
            }
            return Send("/sdapi/v1/img2img", BuildBody(request, b64), request.timeoutSeconds, onDone);
        }

        static IEnumerator Empty() { yield break; }

        public void Abort()
        {
            foreach (var www in new List<UnityWebRequest>(inFlight))
            {
                try { www.Abort(); } catch (Exception) { /* already disposed */ }
            }
            inFlight.Clear();
        }

        static string BuildBody(ImageRequest r, string initImageB64)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"prompt\":").Append(Json.Quote(r.prompt ?? ""));
            if (!string.IsNullOrEmpty(r.negativePrompt)) sb.Append(",\"negative_prompt\":").Append(Json.Quote(r.negativePrompt));
            sb.Append(",\"width\":").Append(r.width).Append(",\"height\":").Append(r.height);
            sb.Append(",\"seed\":").Append(r.seed);
            if (r.steps > 0) sb.Append(",\"steps\":").Append(r.steps);
            if (r.cfgScale > 0f) sb.Append(",\"cfg_scale\":").Append(r.cfgScale.ToString(inv));
            if (initImageB64 != null)
            {
                sb.Append(",\"init_images\":[\"").Append(initImageB64).Append("\"]");
                sb.Append(",\"denoising_strength\":").Append(Mathf.Clamp01(r.denoisingStrength).ToString(inv));
            }
            return sb.Append('}').ToString();
        }

        IEnumerator Send(string path, string body, float timeoutSeconds, Action<ImageResult> onDone)
        {
            float started = Time.realtimeSinceStartup;
            using var www = new UnityWebRequest(baseUrl.TrimEnd('/') + path, "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

            inFlight.Add(www);
            yield return www.SendWebRequest();
            inFlight.Remove(www);
            float seconds = Time.realtimeSinceStartup - started;
            string text = www.downloadHandler != null ? www.downloadHandler.text : null;

            if (www.result != UnityWebRequest.Result.Success)
            {
                onDone(ImageResult.Fail(Describe(www, path, text)));
                yield break;
            }

            // No yield inside try/catch, so the outcome is carried out of the block.
            Texture2D tex = null;
            string error = null;
            try
            {
                var r = JsonUtility.FromJson<ImagesResponse>(text);
                if (r?.images == null || r.images.Length == 0 || string.IsNullOrEmpty(r.images[0]))
                {
                    error = $"{displayName} returned no image" + (string.IsNullOrEmpty(r?.error) ? "." : ": " + r.error);
                }
                else
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(Convert.FromBase64String(StripDataPrefix(r.images[0]))))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                        error = $"{displayName} sent an image Unity couldn't read.";
                    }
                    else
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        tex.wrapMode = TextureWrapMode.Clamp;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = $"Couldn't read {displayName}'s reply.";
            }

            if (tex == null) { onDone(ImageResult.Fail(error)); yield break; }
            onDone(ImageResult.Ok(tex, $"{displayName} made the image in {seconds:0.0}s."));
        }

        static string StripDataPrefix(string b64)
        {
            int comma = b64.StartsWith("data:", StringComparison.Ordinal) ? b64.IndexOf(',') : -1;
            return comma >= 0 ? b64.Substring(comma + 1) : b64;
        }

        string Describe(UnityWebRequest www, string path, string body)
        {
            string err = www.error ?? "unknown error";
            if (www.responseCode == 404)
                return $"{displayName} has no {path}. Turn on its HTTP API server (not gRPC).";
            if (!string.IsNullOrEmpty(body) && body.Length < 400 && www.responseCode >= 400)
                return $"{displayName}: {body.Trim()}";
            if (err.IndexOf("insecure", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HTTP to localhost is blocked. Set Player Settings > Allow downloads over HTTP.";
            if (err.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return $"{displayName} took longer than {www.timeout}s (the first image loads the model; try again).";
            if (www.result == UnityWebRequest.Result.ConnectionError && err.IndexOf("abort", StringComparison.OrdinalIgnoreCase) < 0)
                return $"Can't reach {displayName} at {baseUrl}. Is the app open with its API server on?";
            return $"{displayName}: {err}";
        }
    }
}
