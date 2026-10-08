using System;
using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// The shared "website" frame: graph-paper page, browser toolbar (window dots, back / forward,
    /// address bar, reload) and the folder tabs About / Tutorials / Tips &amp; Tricks.
    ///
    /// Heights match the canvas: toolbar 80px, tab row 84px, so page content starts at y = 164.
    /// </summary>
    public static class HtkBrowserChrome
    {
        public const float ToolbarHeight = 80f;
        public const float TabRowHeight = 84f;
        public const float ContentTop = ToolbarHeight + TabRowHeight;

        public enum Tab { About = 0, Tutorials = 1, TipsAndTricks = 2 }

        static readonly string[] TabNames = { "About", "Tutorials", "Tips & Tricks" };

        /// <param name="screen">The 1920x1080 rect from HtkUI.Canvas.</param>
        /// <param name="urlPath">Grey part after the domain, e.g. "/tutorials/cast-on".</param>
        /// <param name="onTab">Called with the tab clicked (null = tabs do nothing).</param>
        public static void Build(RectTransform screen, string urlPath, Tab active,
                                 Action<Tab> onTab = null, Action onBack = null, Action onForward = null)
        {
            var W = HtkTheme.Reference.x;
            var H = HtkTheme.Reference.y;

            // Page: graph paper over the whole screen.
            HtkUI.GraphPaper(HtkUI.Box(screen, "GraphPaper", 0, 0, W, H), W, H);

            // ---- toolbar ----
            var bar = HtkUI.Box(screen, "Toolbar", 0, 0, W, ToolbarHeight);
            HtkUI.Fill(bar, "ToolbarFill", 0, 0, W, ToolbarHeight, HtkTheme.Card);
            HtkUI.Fill(bar, "ToolbarRule", 0, ToolbarHeight - 2f, W, 2f, HtkTheme.Ink);

            for (int i = 0; i < 3; i++)
                HtkUI.Img(bar, "WindowDot", HtkTheme.UI("window_dot"), 28f + i * 26f, 32f, 16f, 16f);

            float x = 28f + 3 * 26f + 14f;
            ChromeButton(bar, "Back", "icon_arrow_left", x, onBack);       x += 66f;
            ChromeButton(bar, "Forward", "icon_arrow_right", x, onForward); x += 66f;

            float urlW = W - x - 28f - 52f - 14f;
            var url = HtkUI.Img(bar, "AddressBar", HtkTheme.UI("url_bar"), x, 16f, urlW, 48f);
            HtkUI.Icon(url.transform, "icon_lock", 24f, 14f, 20f, HtkTheme.Ink);
            var domain = HtkUI.Label(url.transform, "https://www.howtoknit.love", HtkTheme.Pixel, 20,
                                     HtkTheme.Ink, 56f, 0f, 380f, 48f);
            float domainW = domain.preferredWidth;
            HtkUI.Label(url.transform, urlPath, HtkTheme.Pixel, 20, HtkTheme.Muted,
                        56f + domainW, 0f, 600f, 48f);

            ChromeButton(bar, "Reload", "icon_reload", W - 28f - 52f, null);

            // ---- tab row ----
            var row = HtkUI.Box(screen, "Tabs", 0, ToolbarHeight, W, TabRowHeight);
            HtkUI.Fill(row, "TabRule", 0, TabRowHeight - 2f, W, 2f, HtkTheme.Ink);

            float tx = 60f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == (int)active;
                Font f = on ? HtkTheme.PixelBold : HtkTheme.Pixel;
                float textW = TextWidth(TabNames[i], f, 22);
                float w = textW + (on ? 68f : 56f);
                var tab = HtkUI.Box(row, "Tab_" + TabNames[i], tx, on ? TabRowHeight - 56f : TabRowHeight - 2f - 54f,
                                    w, on ? 56f : 54f);

                Image bg;
                if (on)
                {
                    // The active tab covers the rule beneath it, joining it to the page.
                    var paperRt = HtkUI.Box(tab, "Paper", 2f, 2f, w - 4f, 56f);
                    HtkUI.GraphPaper(paperRt, w - 4f, 56f);
                    bg = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Outline", tab)), HtkTheme.UI("tab_active"));
                    bg.color = Color.white;
                }
                else
                {
                    bg = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Hover", tab)), HtkTheme.UI("tab_hover"));
                    var hover = tab.gameObject.AddComponent<HtkHoverShow>();
                    hover.target = bg;
                }

                HtkUI.Label(tab, TabNames[i], f, 22, on ? HtkTheme.Ink : HtkTheme.Muted, 0, 0, w, on ? 56f : 54f,
                            TextAnchor.MiddleCenter);

                var hit = tab.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                Tab captured = (Tab)i;
                HtkUI.MakeButton(tab.gameObject, onTab == null ? (Action)null : () => onTab(captured));

                tx += w + 6f;
            }
        }

        static void ChromeButton(RectTransform bar, string name, string icon, float x, Action onClick)
        {
            var b = HtkUI.Box(bar, name, x, 14f, 52f, 52f);
            var hover = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Hover", b)), HtkTheme.UI("chrome_button_hover"));
            HtkUI.Icon(b, icon, 13f, 13f, 26f, HtkTheme.Ink);
            b.gameObject.AddComponent<HtkHoverShow>().target = hover;
            var hit = b.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            HtkUI.MakeButton(b.gameObject, onClick);
        }

        public static float TextWidth(string text, Font font, int size)
        {
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = size, scaleFactor = 1f, color = Color.white,
                horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(2000f, 200f), pivot = Vector2.zero, richText = false,
                textAnchor = TextAnchor.MiddleLeft, lineSpacing = 1f, updateBounds = true,
                generateOutOfBounds = true, resizeTextForBestFit = false
            };
            return new TextGenerator().GetPreferredWidth(text, settings);
        }
    }
}
