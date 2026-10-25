using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Scene names used by the tabs, mode buttons and dialogs. Defaults match the scenes in
    /// knit_sprite; every scene listed here must also be in File > Build Profiles (Scene List).
    /// </summary>
    [Serializable]
    public class HtkScenes
    {
        public string title = "TitleScene";
        public string lessonSelect = "LessonSelectScene";
        public string creative = "creativeModeScene";
        public string practice = "PracticeModeScene";

        public static void Load(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return;
            if (SceneManager.GetActiveScene().name == scene) return;
            SceneManager.LoadScene(scene);
        }
    }

    /// <summary>
    /// The shared "website" frame: graph-paper page, browser toolbar (window dots, back / forward /
    /// reload drawn as plain icons, address bar) and, on the in-game screens, the folder tabs
    /// About · Tutorials · Create · Practice.
    ///
    /// The title screen has no tab row. Toolbar 80px + tab row 84px, so page content starts at
    /// y = 164 either way (the title screen keeps its spacing).
    /// </summary>
    public static class HtkBrowserChrome
    {
        public const float ToolbarHeight = 80f;
        public const float TabRowHeight = 84f;
        public const float ContentTop = ToolbarHeight + TabRowHeight;

        public enum Tab { None = -1, About = 0, Tutorials = 1, Create = 2, Practice = 3 }

        static readonly string[] TabNames = { "About", "Tutorials", "Create", "Practice" };

        /// <param name="screen">The 1920x1080 rect from HtkUI.Canvas.</param>
        /// <param name="urlPath">Grey part after the domain, e.g. "/tutorials/cast-on".</param>
        /// <param name="active">Highlighted tab. Tab.None with showTabs = false for the title screen.</param>
        /// <param name="scenes">Where Tutorials / Create / Practice go.</param>
        /// <param name="onAbout">About opens the "back to title?" dialog - pass its Show method.</param>
        public static void Build(RectTransform screen, string urlPath, Tab active, HtkScenes scenes,
                                 Action onAbout, bool showTabs = true)
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

            // Back / forward / reload are decoration only - icons, not buttons.
            float x = 28f + 3 * 26f + 14f;
            HtkUI.Icon(bar, "icon_arrow_left", x + 13f, 27f, 26f, HtkTheme.Ink);  x += 66f;
            HtkUI.Icon(bar, "icon_arrow_right", x + 13f, 27f, 26f, HtkTheme.Ink); x += 66f;

            float urlW = W - x - 28f - 52f - 14f;
            var url = HtkUI.Img(bar, "AddressBar", HtkTheme.UI("url_bar"), x, 16f, urlW, 48f);
            HtkUI.Icon(url.transform, "icon_lock", 24f, 14f, 20f, HtkTheme.Ink);
            var domain = HtkUI.Label(url.transform, "https://www.howtoknit.love", HtkTheme.Pixel, 20,
                                     HtkTheme.Ink, 56f, 0f, 380f, 48f);
            HtkUI.Label(url.transform, urlPath, HtkTheme.Pixel, 20, HtkTheme.Muted,
                        56f + domain.preferredWidth, 0f, 600f, 48f);

            HtkUI.Icon(bar, "icon_reload", W - 28f - 52f + 13f, 27f, 26f, HtkTheme.Ink);

            if (!showTabs) return;

            // ---- tab row ----
            var row = HtkUI.Box(screen, "Tabs", 0, ToolbarHeight, W, TabRowHeight);
            HtkUI.Fill(row, "TabRule", 0, TabRowHeight - 2f, W, 2f, HtkTheme.Ink);

            float tx = 60f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tabId = (Tab)i;
                bool on = tabId == active;
                Font f = on ? HtkTheme.PixelBold : HtkTheme.Pixel;
                float w = TextWidth(TabNames[i], f, 22) + (on ? 68f : 56f);
                float h = on ? 56f : 54f;
                var tab = HtkUI.Box(row, "Tab_" + TabNames[i], tx, TabRowHeight - (on ? 0f : 2f) - h, w, h);

                if (on)
                {
                    // The active tab covers the rule beneath it, joining it to the page.
                    HtkUI.GraphPaper(HtkUI.Box(tab, "Paper", 2f, 2f, w - 4f, 56f), w - 4f, 56f);
                    HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Outline", tab)), HtkTheme.UI("tab_active"));
                }
                else
                {
                    var bg = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Hover", tab)), HtkTheme.UI("tab_hover"));
                    tab.gameObject.AddComponent<HtkHoverShow>().target = bg;
                }

                HtkUI.Label(tab, TabNames[i], f, 22, on ? HtkTheme.Ink : HtkTheme.Muted, 0, 0, w, h, TextAnchor.MiddleCenter);

                var hit = tab.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                HtkUI.MakeButton(tab.gameObject, () =>
                {
                    switch (tabId)
                    {
                        case Tab.About:     onAbout?.Invoke(); break;
                        case Tab.Tutorials: if (!on) HtkScenes.Load(scenes?.lessonSelect); break;
                        case Tab.Create:    if (!on) HtkScenes.Load(scenes?.creative); break;
                        case Tab.Practice:  if (!on) HtkScenes.Load(scenes?.practice); break;
                    }
                });

                tx += w + 6f;
            }
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

    /// <summary>
    /// "Back to the title screen?" - opened by the About tab on every in-game screen.
    /// </summary>
    public static class HtkAboutDialog
    {
        /// Builds the dialog (hidden). Show it with SetActive(true).
        public static GameObject Build(RectTransform screen, HtkScenes scenes)
        {
            var root = HtkUI.Stretch(HtkUI.Node("AboutDialog", screen));
            HtkUI.AddImage(root, HtkTheme.UI("pixel"), HtkTheme.WithAlpha(HtkTheme.Paper, 0.86f)).raycastTarget = true;

            const float w = 600f, h = 330f;
            float x = (1920f - w) / 2f, y = (1080f - h) / 2f;
            HtkUI.Shadow(root, x, y, w, h, 0.2f, 30f, 64f);
            var card = HtkUI.Img(root, "Dialog", HtkTheme.UI("card"), x, y, w, h).rectTransform;

            var chip = HtkUI.Img(card, "Chip", HtkTheme.UI("pill"), 48f, 44f, 100f, 34f);
            HtkUI.Label(chip.transform, "About", HtkTheme.Pixel, 16, HtkTheme.Ink, 0, 0, 100f, 34f, TextAnchor.MiddleCenter);
            HtkUI.Label(card, "Back to the title screen?", HtkTheme.PixelBold, 36, HtkTheme.Ink, 48f, 94f, w - 96f, 48f);
            HtkUI.Label(card, "You'll leave this page and return to the start. Anything you haven't finished here won't be kept.",
                        HtkTheme.Mono, 19, HtkTheme.Ink, 48f, 154f, w - 96f, 60f, TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.2f);

            var go = HtkUI.Img(card, "GoToTitle", HtkTheme.UI("button_dark"), 48f, 236f, 220f, 64f);
            HtkUI.Label(go.transform, "Go to title", HtkTheme.Pixel, 19, HtkTheme.Card, 0, 0, 220f, 64f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(go.gameObject, () => HtkScenes.Load(scenes?.title));

            var stay = HtkUI.Img(card, "Stay", HtkTheme.UI("button_light"), 282f, 236f, 200f, 64f);
            HtkUI.Label(stay.transform, "Stay here", HtkTheme.Pixel, 19, HtkTheme.Ink, 0, 0, 200f, 64f, TextAnchor.MiddleCenter);
            GameObject dialog = root.gameObject;
            HtkUI.MakeButton(stay.gameObject, () => dialog.SetActive(false));

            dialog.SetActive(false);
            return dialog;
        }
    }
}
