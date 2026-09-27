using System.Collections;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Orchestrates keyword -> image -> chart -> editor. Owns no UI widgets directly; the panel
    /// calls Submit() and subscribes to the state events, so the UI can be replaced (or driven by
    /// tests) without touching this logic.
    ///
    /// Flow: validate keyword -> wrap in the icon prompt -> ask the image source -> run
    /// ImageToChart -> hand the chart to ChartEditor as one undoable step.
    ///
    /// The generated chart lands in the editor rather than being presented as final output. That
    /// is the single most important product decision here: generation gets the player most of the
    /// way, and letting them fix the rest by hand turns model imperfection from a failure into a
    /// starting point.
    /// </summary>
    public class ChartGenerationController : MonoBehaviour
    {
        public enum State { Idle, Generating, Done, Error }

        [Header("Wiring")]
        public ChartEditor editor;

        [Header("Conversion")]
        public ImageToChart.Settings conversion = new ImageToChart.Settings();

        [Tooltip("Keep the last generated source image around for debugging the pipeline.")]
        public bool keepLastSourceImage = true;

        public State CurrentState { get; private set; } = State.Idle;
        public Texture2D LastSourceImage { get; private set; }

        /// <summary>Fired on every state change with a message suitable for display.</summary>
        public event System.Action<State, string> OnStateChanged;

        private IChartImageSource imageSource;
        private PlaceholderImageSource placeholder;   // non-null only while using the placeholder
        private Coroutine running;

        void Awake()
        {
            // Default to the placeholder so the scene is fully playable with no model installed.
            placeholder = new PlaceholderImageSource();
            imageSource = placeholder;
        }

        /// <summary>Swap in a different source (e.g. local Stable Diffusion) at runtime.</summary>
        public void SetImageSource(IChartImageSource source)
        {
            if (source == null) return;
            imageSource = source;
            placeholder = source as PlaceholderImageSource;
        }

        public string SourceName => imageSource != null ? imageSource.DisplayName : "none";

        public void Submit(string rawKeyword)
        {
            if (CurrentState == State.Generating) return;

            var validation = KeywordFilter.Validate(rawKeyword);
            if (!validation.ok)
            {
                SetState(State.Error, validation.error);
                return;
            }

            if (editor == null || editor.Chart == null)
            {
                SetState(State.Error, "No chart editor connected.");
                return;
            }

            running = StartCoroutine(GenerateRoutine(validation.cleaned));
        }

        public void Cancel()
        {
            if (CurrentState != State.Generating) return;

            if (placeholder != null) placeholder.CancelRequested = true;

            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }
            SetState(State.Idle, "Cancelled.");
        }

        IEnumerator GenerateRoutine(string keyword)
        {
            SetState(State.Generating, $"Generating \"{keyword}\" via {SourceName}...");

            ImageResult result = default;
            bool received = false;

            // Sources receive the validated keyword and apply PromptBuilder themselves - a model
            // backend needs the wrapped icon prompt, while the placeholder has no prompt at all.
            yield return StartCoroutine(imageSource.Generate(keyword, r => { result = r; received = true; }));

            running = null;

            if (!received)
            {
                SetState(State.Error, "Image source returned nothing.");
                yield break;
            }

            if (result.cancelled)
            {
                SetState(State.Idle, "Cancelled.");
                yield break;
            }

            if (!result.success || result.texture == null)
            {
                SetState(State.Error, string.IsNullOrEmpty(result.message) ? "Generation failed." : result.message);
                yield break;
            }

            // Match the generated chart to the editor's current width so the player's canvas size
            // is respected rather than being silently replaced by the model's aspect.
            conversion.targetColumns = editor.Chart.Width;

            // No `yield` inside this try/catch: C# forbids yielding from a try block with a catch
            // clause, so the error path is handled after the block.
            ColorworkChart chart = null;
            try
            {
                chart = ImageToChart.Convert(result.texture, editor.palette, conversion);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            if (chart == null)
            {
                SetState(State.Error, "Could not convert that image into a chart.");
                yield break;
            }

            editor.LoadChart(chart);

            if (keepLastSourceImage)
            {
                if (LastSourceImage != null && LastSourceImage != result.texture) Destroy(LastSourceImage);
                LastSourceImage = result.texture;
            }
            else
            {
                Destroy(result.texture);
            }

            SetState(State.Done, result.message);
        }

        void SetState(State state, string message)
        {
            CurrentState = state;
            OnStateChanged?.Invoke(state, message);
        }

        void OnDestroy()
        {
            if (LastSourceImage != null) Destroy(LastSourceImage);
        }
    }
}
