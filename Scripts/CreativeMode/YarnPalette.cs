using System;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// The set of yarn colors available in Creative Mode. Charts store indices into this palette,
    /// never raw colors, so editing this asset recolors every existing chart at once.
    ///
    /// Create one via Assets > Create > Knit > Yarn Palette. If ChartEditor has no palette
    /// assigned it falls back to CreateDefault() so the scene still runs - that fallback exists
    /// purely so nothing is blocked on final art, and should be replaced with a real asset.
    ///
    /// Index 0 is treated as the "background"/unworked stitch by the editor's eraser. Keep it as
    /// your fabric base color.
    ///
    /// Colors are also what the generated-image pipeline will snap to later (nearest color in
    /// CIE Lab space), which is what keeps AI-generated charts visually consistent with
    /// hand-drawn ones instead of introducing arbitrary new colors.
    /// </summary>
    [CreateAssetMenu(menuName = "Knit/Yarn Palette", fileName = "YarnPalette")]
    public class YarnPalette : ScriptableObject
    {
        [Serializable]
        public class Yarn
        {
            public string displayName = "Yarn";
            public Color32 color = new Color32(255, 255, 255, 255);
        }

        [Tooltip("Index 0 is the background / base fabric color used by the eraser.")]
        public Yarn[] yarns = Array.Empty<Yarn>();

        public int Count => yarns == null ? 0 : yarns.Length;

        public Color32 GetColor(int index)
        {
            if (yarns == null || yarns.Length == 0) return new Color32(255, 0, 255, 255); // magenta = misconfigured
            return yarns[Mathf.Clamp(index, 0, yarns.Length - 1)].color;
        }

        public string GetName(int index)
        {
            if (yarns == null || yarns.Length == 0) return "?";
            return yarns[Mathf.Clamp(index, 0, yarns.Length - 1)].displayName;
        }

        /// <summary>
        /// Placeholder palette so Creative Mode is runnable before final colors are chosen.
        /// Six colors is a realistic upper bound for stranded colorwork - beyond that the floats
        /// on the back of the work become impractical, which is also why the generation pipeline
        /// quantizes to a small k.
        /// </summary>
        public static YarnPalette CreateDefault()
        {
            var p = CreateInstance<YarnPalette>();
            p.name = "YarnPalette (placeholder)";
            p.yarns = new[]
            {
                new Yarn { displayName = "Cream",    color = new Color32(245, 240, 228, 255) },
                new Yarn { displayName = "Charcoal", color = new Color32( 54,  54,  60, 255) },
                new Yarn { displayName = "Rust",     color = new Color32(190,  92,  58, 255) },
                new Yarn { displayName = "Sage",     color = new Color32(126, 150, 112, 255) },
                new Yarn { displayName = "Mustard",  color = new Color32(222, 168,  70, 255) },
                new Yarn { displayName = "Plum",     color = new Color32(108,  71, 100, 255) },
            };
            return p;
        }
    }
}
