using System.Collections.Generic;
using UnityEngine;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Colours, fonts and sprite lookup for the "How to Knit" web-page UI.
    ///
    /// Everything is loaded from Resources/HowToKnit/, so the page builders need no Inspector
    /// wiring - drop the HowToKnit folder into Assets and the components find their art.
    /// Values match the design canvas (Spec/ui-spec.md).
    /// </summary>
    public static class HtkTheme
    {
        // ---- colour -------------------------------------------------------------------------
        public static readonly Color Ink      = Hex("#1a1c22"); // lines, text, dark buttons
        public static readonly Color Paper    = Hex("#f6f7fb"); // page background (under the grid)
        public static readonly Color Card     = Hex("#fbfcfe"); // cards, panels, browser chrome
        public static readonly Color White    = Color.white;    // game card, keycaps, bubbles
        public static readonly Color Chip     = Hex("#dfe8fb"); // pills, URL bar, current row
        public static readonly Color Hover    = Hex("#e9eefa"); // tab / chrome-button hover
        public static readonly Color Yarn     = Hex("#3b72d9"); // the one accent
        public static readonly Color Muted    = Hex("#4d5566"); // secondary text
        public static readonly Color Error    = Hex("#9e2b55"); // wrong-key hint text

        /// Reference resolution every layout number in this kit is written against.
        public static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        /// "#rrggbb" -> Color. Pure C# (no engine call), so it is safe in field initialisers,
        /// which Unity may run off the main thread while deserialising.
        public static Color Hex(string hex)
        {
            hex = hex.TrimStart('#');
            int v = System.Convert.ToInt32(hex.Substring(0, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        // ---- assets -------------------------------------------------------------------------
        const string Root = "HowToKnit/";
        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

        public static Sprite UI(string name)   => LoadSprite("UI/" + name);
        public static Sprite Icon(string name) => LoadSprite("Icons/" + name);
        public static Sprite Art(string name)  => LoadSprite("Art/" + name);

        public static Texture2D GraphPaperTile =>
            Resources.Load<Texture2D>(Root + "Textures/graph_paper_tile");

        /// Silkscreen - headings, buttons, labels.
        public static Font Pixel     => LoadFont("Silkscreen-Regular");
        public static Font PixelBold => LoadFont("Silkscreen-Bold");
        /// DM Mono - body copy, captions, chat messages.
        public static Font Mono      => LoadFont("DMMono-Regular");
        public static Font MonoBold  => LoadFont("DMMono-Medium");

        static Sprite LoadSprite(string path)
        {
            if (spriteCache.TryGetValue(path, out Sprite s)) return s;
            s = Resources.Load<Sprite>(Root + path);
            if (s == null)
                Debug.LogWarning($"HtkTheme: missing sprite Resources/{Root}{path}. Check the file " +
                                 "is imported as a Sprite (HowToKnitTextureImporter does this).");
            spriteCache[path] = s;
            return s;
        }

        static Font LoadFont(string file)
        {
            if (fontCache.TryGetValue(file, out Font f)) return f;
            f = Resources.Load<Font>(Root + "Fonts/" + file);
            if (f == null)
            {
                Debug.LogWarning($"HtkTheme: missing font Resources/{Root}Fonts/{file} - using the built-in font.");
                f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            fontCache[file] = f;
            return f;
        }
    }
}
