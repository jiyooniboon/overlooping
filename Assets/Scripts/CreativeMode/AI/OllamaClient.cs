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
    /// ILlmClient for a local Ollama server (`ollama serve`, default port 11434), using
    /// /api/generate with a JSON Schema in "format" so the reply is always valid JSON.
    ///
    /// Needs Project Settings > Player > Other Settings > Allow downloads over HTTP set to
    /// "Always allowed" (or "Allowed in development builds"), or every request fails with an
    /// "insecure connection" error.
    /// </summary>
    public class OllamaClient : ILlmClient
    {
        public string baseUrl = "http://localhost:11434";
        public string model = "qwen3.5:4b";

        /// <summary>
        /// Sends "think": false. Thinking mode (Qwen3.5 and others) is slow and can get in the
        /// way of schema output. Turn this off for a model that rejects the field.
        /// </summary>
        public bool disableThinking = true;

        public float temperature = 0.6f;

        /// <summary>How long Ollama keeps the weights loaded after a request.</summary>
        public string keepAlive = "30m";

        public string DisplayName => $"Ollama ({model})";
        public string ModelName => model;

        [Serializable] class GenerateResponse { public string response; public string error; }
        [Serializable] class ErrorResponse { public string error; }

        // A set, not one field: the warm-up request and a real request can overlap.
        readonly HashSet<UnityWebRequest> inFlight = new HashSet<UnityWebRequest>();

        public IEnumerator CompleteJson(string prompt, string jsonSchema, float timeoutSeconds, Action<LlmResult> onDone)
        {
            LlmResult result = default;
            yield return Send(prompt, jsonSchema, timeoutSeconds, r => result = r);

            // Models without a thinking switch (e.g. llama3.2) may reject "think". Drop the field
            // for this client from now on and try once more, so switching models in the
            // Inspector just works.
            if (!result.ok && disableThinking && result.error != null &&
                result.error.IndexOf("think", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log($"[AI] {model} doesn't take the thinking switch; retrying without it.");
                disableThinking = false;
                yield return Send(prompt, jsonSchema, timeoutSeconds, r => result = r);
            }
            onDone(result);
        }

        IEnumerator Send(string prompt, string jsonSchema, float timeoutSeconds, Action<LlmResult> onDone)
        {
            string body =
                "{\"model\":" + Json.Quote(model) +
                ",\"prompt\":" + Json.Quote(prompt) +
                (string.IsNullOrEmpty(jsonSchema) ? "" : ",\"format\":" + jsonSchema) +
                ",\"stream\":false" +
                (disableThinking ? ",\"think\":false" : "") +
                ",\"keep_alive\":" + Json.Quote(keepAlive) +
                ",\"options\":{\"temperature\":" + temperature.ToString(CultureInfo.InvariantCulture) + "}}";

            float started = Time.realtimeSinceStartup;
            using var www = new UnityWebRequest(baseUrl.TrimEnd('/') + "/api/generate", "POST");
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
                onDone(LlmResult.Fail(Describe(www, text), seconds));
                yield break;
            }

            // Parse twice: the outer reply carries the model's JSON as a *string* in "response".
            GenerateResponse r = null;
            try { r = JsonUtility.FromJson<GenerateResponse>(text); }
            catch (Exception e) { Debug.LogException(e); }

            if (r == null || string.IsNullOrEmpty(r.response))
            {
                onDone(LlmResult.Fail(!string.IsNullOrEmpty(r?.error) ? "Ollama: " + r.error : "Empty reply from Ollama.", seconds));
                yield break;
            }

            onDone(LlmResult.Ok(r.response, seconds));
        }

        public void Abort()
        {
            // Copy first: Abort can complete the request synchronously and the coroutine that
            // owns it removes it from the set.
            foreach (var www in new List<UnityWebRequest>(inFlight))
            {
                try { www.Abort(); } catch (Exception) { /* already disposed */ }
            }
            inFlight.Clear();
        }

        string Describe(UnityWebRequest www, string body)
        {
            // Ollama answers e.g. 404 {"error":"model 'x' not found"}: show that, it's the useful part.
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var e = JsonUtility.FromJson<ErrorResponse>(body);
                    if (e != null && !string.IsNullOrEmpty(e.error))
                    {
                        if (e.error.Contains("not found"))
                            return $"Ollama: {e.error}. Run `ollama pull {model}`.";
                        return "Ollama: " + e.error;
                    }
                }
                catch (Exception) { /* not JSON */ }
            }

            string err = www.error ?? "unknown error";
            if (err.IndexOf("insecure", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HTTP to localhost is blocked. Set Player Settings > Allow downloads over HTTP.";
            if (err.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return $"{model} took longer than {www.timeout}s (first run loads the model; try again).";
            if (www.result == UnityWebRequest.Result.ConnectionError && err.IndexOf("abort", StringComparison.OrdinalIgnoreCase) < 0)
                return $"Can't reach Ollama ({err}). Is `ollama serve` running?";
            return "Ollama: " + err;
        }
    }
}
