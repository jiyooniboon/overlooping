using System;
using System.Collections;

namespace KnitSprite.CreativeMode.AI
{
    public struct LlmResult
    {
        public bool ok;
        public string json;     // the model's reply: a JSON document matching the schema
        public string error;
        public float seconds;   // wall-clock time of the request, for the model comparison

        public static LlmResult Ok(string json, float seconds) =>
            new LlmResult { ok = true, json = json, seconds = seconds };

        public static LlmResult Fail(string error, float seconds = 0f) =>
            new LlmResult { ok = false, error = error, seconds = seconds };
    }

    /// <summary>
    /// Everything that talks to a language model (ShapeSource, practice coaching) goes through
    /// this, never directly to Ollama. Moving to an in-build model later (LLMUnity, plan §6) is
    /// then one new class and no changes anywhere else.
    /// </summary>
    public interface ILlmClient
    {
        /// <summary>Shown in the UI, e.g. "Ollama (qwen3.5:4b)".</summary>
        string DisplayName { get; }

        /// <summary>Short model name for status messages, e.g. "qwen3.5:4b".</summary>
        string ModelName { get; }

        /// <summary>
        /// Asks for a JSON reply that matches jsonSchema (a raw JSON Schema string).
        /// Run as a coroutine. Always calls onDone exactly once, including on failure or Abort.
        /// </summary>
        IEnumerator CompleteJson(string prompt, string jsonSchema, float timeoutSeconds, Action<LlmResult> onDone);

        /// <summary>Stops every request in flight. Their onDone still fires (with a failure).</summary>
        void Abort();
    }
}
