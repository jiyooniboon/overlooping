using System;
using System.Collections;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>
    /// Tries each source in order and returns the first success. The chain ends with
    /// PlaceholderImageSource, which always works, so Generate never leaves the player with nothing.
    ///
    /// A fallback is reported in the result message on purpose: during the demo it should be
    /// visible ("Fell back to ..."), never hidden.
    /// </summary>
    public class FallbackSource : IChartImageSource, ICancellableSource
    {
        readonly IChartImageSource[] chain;
        IChartImageSource current;
        int runId; // bumped on Abort, so a cancelled run never moves on to the next source

        public FallbackSource(params IChartImageSource[] chain)
        {
            if (chain == null || chain.Length == 0) throw new ArgumentException("FallbackSource needs at least one source.");
            this.chain = chain;
        }

        public string DisplayName => (current ?? chain[0]).DisplayName;

        public IEnumerator Generate(string keyword, Action<ImageResult> onComplete)
        {
            int myRun = ++runId;
            string firstError = null;
            IChartImageSource firstFailed = null;

            foreach (var source in chain)
            {
                current = source;
                ImageResult r = default;
                bool got = false;
                yield return source.Generate(keyword, x => { r = x; got = true; });

                if (myRun != runId || r.cancelled) { onComplete(ImageResult.Cancelled()); yield break; }

                if (got && r.success)
                {
                    if (firstError != null)
                        r.message += $"\n(Fell back to {source.DisplayName}: {firstFailed.DisplayName} failed: {firstError})";
                    onComplete(r);
                    yield break;
                }

                if (firstError == null)
                {
                    firstError = got ? r.message : "no response";
                    firstFailed = source;
                }
            }

            onComplete(ImageResult.Fail(firstError ?? "All sources failed."));
        }

        public void Abort()
        {
            runId++;
            (current as ICancellableSource)?.Abort();
        }
    }
}
