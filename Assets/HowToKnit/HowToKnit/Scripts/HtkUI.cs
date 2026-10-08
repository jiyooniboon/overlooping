using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Small uGUI factory used by the page builders.
    ///
    /// Positions are given the way the design canvas measures them: x/y from the TOP-LEFT of the
    /// parent, in 1920x1080 reference pixels. That keeps every number in the builders identical to
    /// the spec, so a layout tweak is a one-number change.
    /// </summary>
    public static class HtkUI
    {
        // ---- canvas -------------------------------------------------------------------------

        public static RectTransform Canvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = HtkTheme.Reference;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f; // sprites are 200 PPU @2x -> drawn at 1x

            // A fixed 1920x1080 "screen" in the middle of the canvas, so top-left coordinates
            // from the spec hold at any aspect ratio (extra space shows the page colour).
            var screen = Node("Screen", go.transform);
            screen.anchorMin = screen.anchorMax = new Vector2(0.5f, 0.5f);
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.sizeDelta = HtkTheme.Reference;
            screen.anchoredPosition = Vector2.zero;
            return screen;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ---- rects --------------------------------------------------------------------------

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// Top-left placement in parent space.
        public static RectTransform At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        /// Placement by centre point (use for anything that rotates or scales around its middle).
        public static RectTransform AtCentre(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, -cy);
            return rt;
        }

        /// Rotate like CSS rotate(deg): around the centre, clockwise-positive.
        public static RectTransform RotateCss(RectTransform rt, float cssDegrees)
        {
            Vector2 size = rt.sizeDelta;
            Vector2 shift = new Vector2((0.5f - rt.pivot.x) * size.x, (0.5f - rt.pivot.y) * size.y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition += shift;
            rt.localEulerAngles = new Vector3(0f, 0f, -cssDegrees);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
            => At(Node(name, parent), x, y, w, h);

        // ---- graphics -----------------------------------------------------------------------

        public static Image Img(Transform parent, string name, Sprite sprite,
                                float x, float y, float w, float h, Color? tint = null)
        {
            var rt = Box(parent, name, x, y, w, h);
            return AddImage(rt, sprite, tint);
        }

        public static Image AddImage(RectTransform rt, Sprite sprite, Color? tint = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint ?? Color.white;
            img.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero)
                img.type = Image.Type.Sliced;
            return img;
        }

        /// Solid rectangle (rules, fills) from the white pixel sprite.
        public static Image Fill(Transform parent, string name, float x, float y, float w, float h, Color color)
            => Img(parent, name, HtkTheme.UI("pixel"), x, y, w, h, color);

        public static Text Label(Transform parent, string text, Font font, int size, Color color,
                                 float x, float y, float w, float h,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false,
                                 float lineSpacing = 1f)
        {
            var rt = Box(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.alignment = anchor;
            t.lineSpacing = lineSpacing;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// The pink/blue graph-paper background, tiled at 24px.
        public static RawImage GraphPaper(RectTransform rt, float w, float h)
        {
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = HtkTheme.GraphPaperTile;
            raw.uvRect = new Rect(0f, 0f, w / 24f, h / 24f);
            raw.raycastTarget = false;
            if (raw.texture == null) raw.color = HtkTheme.Paper;
            return raw;
        }

        /// Soft drop shadow for a box at (x, y, w, h). Add it BEFORE the box so it sits behind.
        public static Image Shadow(Transform parent, float x, float y, float w, float h,
                                   float alpha = 0.3f, float dropY = 18f, float spread = 52f)
        {
            return Img(parent, "Shadow", HtkTheme.UI("shadow_soft"),
                       x - spread, y - spread + dropY, w + spread * 2f, h + spread * 2f,
                       HtkTheme.WithAlpha(Color.black, alpha));
        }

        public static Image Icon(Transform parent, string icon, float x, float y, float size, Color color)
            => Img(parent, icon, HtkTheme.Icon(icon), x, y, size, size, color);

        // ---- images inside cards ------------------------------------------------------------

        /// <summary>
        /// Places artwork inside a box. contain = whole image visible (CSS object-fit: contain),
        /// otherwise cover = fills the box and crops (object-fit: cover, with focus picking which
        /// part survives: 0 = left/top, 1 = right/bottom).
        /// scale and offsetY match the CSS scale() / translateY() tweaks used on the canvas.
        /// </summary>
        public static Image FitArt(Transform parent, Sprite art, float boxX, float boxY, float boxW, float boxH,
                                   bool contain, float scale = 1f, float offsetY = 0f,
                                   float focusX = 0.5f, float focusY = 0.5f)
        {
            float aspect = art != null ? art.rect.width / art.rect.height : 16f / 9f;
            float w, h;
            bool widthLimited = boxW / boxH < aspect;
            if (contain == widthLimited) { w = boxW; h = boxW / aspect; }
            else                         { h = boxH; w = boxH * aspect; }
            w *= scale; h *= scale;

            float x = boxX - (w - boxW) * focusX;
            float y = boxY - (h - boxH) * focusY + offsetY;
            var img = Img(parent, "Art", art, x, y, w, h);
            img.type = Image.Type.Simple;
            return img;
        }

        /// A rounded card that clips its children to the rounded shape, with the ink outline
        /// drawn on top. Returns the content parent (put art inside it).
        public static RectTransform MaskedCard(Transform parent, string name, float x, float y, float w, float h,
                                               Color fill, string outlineSprite = "card_outline",
                                               bool centrePivot = false)
        {
            var root = centrePivot ? AtCentre(Node(name, parent), x + w / 2f, y + h / 2f, w, h)
                                   : Box(parent, name, x, y, w, h);

            var maskRt = Stretch(Node("Mask", root));
            AddImage(maskRt, HtkTheme.UI("card_fill"), fill);
            var mask = maskRt.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var content = Stretch(Node("Content", maskRt));

            var outline = Stretch(Node("Outline", root));
            AddImage(outline, HtkTheme.UI(outlineSprite));
            if (outlineSprite == "card_dashed") outline.GetComponent<Image>().type = Image.Type.Tiled;

            return content;
        }

        // ---- interaction --------------------------------------------------------------------

        public static Button MakeButton(GameObject go, Action onClick)
        {
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = graphic;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }
    }

    /// Lifts a button up-left by 3px and shows a hard ink shadow on hover (the canvas .btn:hover).
    public class HtkHoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RectTransform face;
        public Graphic hardShadow;
        Vector2 rest;
        bool captured;

        void Start()
        {
            if (face != null) { rest = face.anchoredPosition; captured = true; }
            if (hardShadow != null) hardShadow.enabled = false;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (captured) face.anchoredPosition = rest + new Vector2(-3f, 3f);
            if (hardShadow != null) hardShadow.enabled = true;
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (captured) face.anchoredPosition = rest;
            if (hardShadow != null) hardShadow.enabled = false;
        }
    }

    /// Shows a background graphic only while hovered (tabs and browser buttons).
    public class HtkHoverShow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Graphic target;
        void Start() { if (target != null) target.enabled = false; }
        public void OnPointerEnter(PointerEventData e) { if (target != null) target.enabled = true; }
        public void OnPointerExit(PointerEventData e)  { if (target != null) target.enabled = false; }
    }
}
