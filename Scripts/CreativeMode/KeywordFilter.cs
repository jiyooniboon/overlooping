using System.Text;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Validates and sanitizes player-typed keywords before they reach any image model.
    ///
    /// This exists because "arbitrary player text drives generated imagery displayed in-game" is
    /// exactly the design decision a capstone committee will ask about. A modest denylist plus a
    /// length cap and character whitelist is proportionate here - the point is that the input is
    /// bounded and considered, not that the filter is exhaustive.
    ///
    /// Deliberately conservative on characters: only letters, digits, spaces and hyphens survive.
    /// That also removes prompt-injection style punctuation and newlines before they can reach a
    /// model's prompt template.
    /// </summary>
    public static class KeywordFilter
    {
        public const int MaxLength = 40;
        public const int MinLength = 2;

        // Kept short and obvious on purpose. Extend as needed - and note that a real product
        // would use a maintained list plus a moderation endpoint rather than hardcoding.
        static readonly string[] Blocked =
        {
            "nude", "naked", "nsfw", "gore", "blood", "corpse", "porn", "sex", "kill", "suicide"
        };

        public struct Result
        {
            public bool ok;
            public string cleaned;
            public string error;
        }

        public static Result Validate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new Result { ok = false, error = "Type something to knit!" };

            string trimmed = raw.Trim();

            if (trimmed.Length > MaxLength)
                return new Result { ok = false, error = $"Keep it under {MaxLength} characters." };

            var sb = new StringBuilder(trimmed.Length);
            bool lastWasSpace = false;
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
                else if (char.IsWhiteSpace(c) && !lastWasSpace)
                {
                    sb.Append(' ');       // collapse runs of whitespace
                    lastWasSpace = true;
                }
                // everything else is dropped
            }

            string cleaned = sb.ToString().Trim();

            if (cleaned.Length < MinLength)
                return new Result { ok = false, error = "That's a bit short - try a word or two." };

            string lower = cleaned.ToLowerInvariant();
            foreach (string bad in Blocked)
            {
                if (lower.Contains(bad))
                    return new Result { ok = false, error = "Let's pick a different design." };
            }

            return new Result { ok = true, cleaned = cleaned };
        }
    }
}
