using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// The keyword input UI: text field, Generate button, Cancel button, and a response area.
    /// Builds its own widgets at runtime (see CreativeModeBootstrap for why) and talks to
    /// ChartGenerationController purely through Submit/Cancel plus the OnStateChanged event.
    ///
    /// Uses legacy UnityEngine.UI.InputField/Text rather than TextMeshPro so Creative Mode has no
    /// extra package dependency. Swap for TMP later if the rest of the game standardizes on it.
    ///
    /// The two things this UI must get right, because they are what makes a demo feel broken:
    /// the player can always see that something is happening, and they can always back out of it.
    /// </summary>
    public class GenerationPanel : MonoBehaviour
    {
        public ChartGenerationController controller;

        private InputField keywordField;
        private Button generateButton;
        private Button cancelButton;
        private Text statusText;
        private Text responseText;
        private Font font;

        public void Build(RectTransform parent, ChartGenerationController controller)
        {
            this.controller = controller;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var panel = MakeRect("GenerationPanel", parent);
            panel.anchorMin = new Vector2(1f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-24f, -24f);
            panel.sizeDelta = new Vector2(420f, 300f);

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.85f);

            MakeLabel("Title", panel, "Generate a design", 22, FontStyle.Bold,
                      new Vector2(16f, -14f), new Vector2(388f, 30f));

            MakeLabel("Hint", panel, "Describe a motif, e.g. \"snowflake\" or \"bunny\"", 15,
                      FontStyle.Normal, new Vector2(16f, -46f), new Vector2(388f, 24f));

            keywordField = MakeInputField(panel, new Vector2(16f, -74f), new Vector2(388f, 40f));
            keywordField.onEndEdit.AddListener(OnFieldSubmit);

            generateButton = MakeButton(panel, "Generate", new Vector2(16f, -122f),
                                        new Vector2(190f, 40f), OnGenerateClicked);
            cancelButton = MakeButton(panel, "Cancel", new Vector2(214f, -122f),
                                      new Vector2(190f, 40f), OnCancelClicked);
            SetCancelVisible(false);

            statusText = MakeLabel("Status", panel, "", 15, FontStyle.Italic,
                                   new Vector2(16f, -170f), new Vector2(388f, 24f));

            responseText = MakeLabel("Response", panel, "AI response here", 15, FontStyle.Normal,
                                     new Vector2(16f, -198f), new Vector2(388f, 90f));
            responseText.color = new Color(0.25f, 0.25f, 0.3f);

            if (controller != null)
            {
                controller.OnStateChanged += HandleStateChanged;
                statusText.text = $"Source: {controller.SourceName}";
            }
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnStateChanged -= HandleStateChanged;
        }

        void OnFieldSubmit(string value)
        {
            // onEndEdit also fires when the field simply loses focus, so only treat an actual
            // Enter press as a submit - otherwise clicking elsewhere would fire a generation.
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            Submit();
        }

        void OnGenerateClicked() => Submit();

        void OnCancelClicked()
        {
            if (controller != null) controller.Cancel();
        }

        void Submit()
        {
            if (controller == null || keywordField == null) return;
            controller.Submit(keywordField.text);
        }

        void HandleStateChanged(ChartGenerationController.State state, string message)
        {
            bool busy = state == ChartGenerationController.State.Generating;

            SetCancelVisible(busy);
            if (generateButton != null) generateButton.interactable = !busy;
            if (keywordField != null) keywordField.interactable = !busy;

            switch (state)
            {
                case ChartGenerationController.State.Generating:
                    statusText.text = message;
                    statusText.color = new Color(0.2f, 0.3f, 0.5f);
                    break;

                case ChartGenerationController.State.Done:
                    statusText.text = "Done - edit it by hand from here.";
                    statusText.color = new Color(0.2f, 0.45f, 0.25f);
                    responseText.text = message;
                    break;

                case ChartGenerationController.State.Error:
                    statusText.text = message;
                    statusText.color = new Color(0.6f, 0.2f, 0.2f);
                    break;

                default:
                    statusText.text = message;
                    statusText.color = new Color(0.3f, 0.3f, 0.35f);
                    break;
            }
        }

        void SetCancelVisible(bool visible)
        {
            if (cancelButton != null) cancelButton.gameObject.SetActive(visible);
        }

        // ---- tiny uGUI builders -------------------------------------------------------

        RectTransform MakeRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        Text MakeLabel(string name, RectTransform parent, string content, int size, FontStyle style,
                       Vector2 topLeft, Vector2 size2)
        {
            var rt = MakeRect(name, parent);
            AnchorTopLeft(rt, topLeft, size2);

            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.text = content;
            t.color = new Color(0.15f, 0.15f, 0.18f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.alignment = TextAnchor.UpperLeft;
            return t;
        }

        InputField MakeInputField(RectTransform parent, Vector2 topLeft, Vector2 size)
        {
            var rt = MakeRect("KeywordField", parent);
            AnchorTopLeft(rt, topLeft, size);

            var image = rt.gameObject.AddComponent<Image>();
            image.color = Color.white;

            var field = rt.gameObject.AddComponent<InputField>();
            field.characterLimit = KeywordFilter.MaxLength;
            field.lineType = InputField.LineType.SingleLine;

            var placeholder = MakeLabel("Placeholder", rt, "type a keyword...", 16,
                                        FontStyle.Italic, new Vector2(10f, -10f),
                                        new Vector2(size.x - 20f, size.y - 16f));
            placeholder.color = new Color(0.6f, 0.6f, 0.65f);

            var text = MakeLabel("Text", rt, "", 16, FontStyle.Normal,
                                 new Vector2(10f, -10f), new Vector2(size.x - 20f, size.y - 16f));
            text.supportRichText = false;

            // Assign these last: InputField wires up its own caret when textComponent is set.
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = image;
            return field;
        }

        Button MakeButton(RectTransform parent, string label, Vector2 topLeft, Vector2 size,
                          UnityEngine.Events.UnityAction onClick)
        {
            var rt = MakeRect(label + "Button", parent);
            AnchorTopLeft(rt, topLeft, size);

            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0.86f, 0.86f, 0.9f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var t = MakeLabel("Label", rt, label, 18, FontStyle.Bold, Vector2.zero, size);
            t.alignment = TextAnchor.MiddleCenter;
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return button;
        }

        static void AnchorTopLeft(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }
    }
}
