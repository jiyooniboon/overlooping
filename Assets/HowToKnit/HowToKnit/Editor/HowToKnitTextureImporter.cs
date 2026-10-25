using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KnitSprite.UI.HowToKnit.EditorTools
{
    /// <summary>
    /// Sets import settings for everything under a "HowToKnit/Resources/HowToKnit/" folder, so the
    /// exported UI kit is ready to use the moment it lands in Assets - no manual Inspector work.
    ///
    ///  UI/ and Icons/   Sprite, drawn at 2x  -> Pixels Per Unit 200 (shows at 1x on a 100-PPU canvas),
    ///                   Full Rect mesh (needed for Sliced/Tiled), 9-slice borders from the table below.
    ///  Art/             Sprite, Pixels Per Unit 100, Full Rect mesh.
    ///  Textures/        Default texture, Repeat wrap (the graph-paper tile).
    ///
    /// It only runs on import. If you change a value here, right-click the folder > Reimport.
    /// Remove this file whenever you'd rather manage the settings yourself.
    /// </summary>
    public class HowToKnitTextureImporter : AssetPostprocessor
    {
        const string Root = "/HowToKnit/Resources/HowToKnit/";

        // 9-slice borders in texture pixels (x = left, y = bottom, z = right, w = top).
        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "bubble", new Vector4(48, 48, 48, 48) },
            { "button_dark", new Vector4(36, 36, 36, 36) },
            { "button_dashed", new Vector4(36, 36, 36, 36) },
            { "button_light", new Vector4(36, 36, 36, 36) },
            { "button_selected", new Vector4(36, 36, 36, 36) },
            { "card", new Vector4(48, 48, 48, 48) },
            { "card_dashed", new Vector4(48, 48, 48, 48) },
            { "card_fill", new Vector4(48, 48, 48, 48) },
            { "card_outline", new Vector4(48, 48, 48, 48) },
            { "card_outline_selected", new Vector4(48, 48, 48, 48) },
            { "card_selected", new Vector4(48, 48, 48, 48) },
            { "card_white", new Vector4(48, 48, 48, 48) },
            { "chrome_button_hover", new Vector4(26, 26, 26, 26) },
            { "dot", new Vector4(0, 0, 0, 0) },
            { "keycap_down", new Vector4(34, 34, 34, 48) },
            { "keycap_small", new Vector4(24, 32, 24, 24) },
            { "keycap_up", new Vector4(34, 48, 34, 34) },
            { "meter_track", new Vector4(18, 18, 18, 18) },
            { "pill", new Vector4(34, 32, 34, 32) },
            { "pill_light", new Vector4(48, 46, 48, 46) },
            { "pill_outline", new Vector4(34, 32, 34, 32) },
            { "pill_selected", new Vector4(48, 46, 48, 46) },
            { "pixel", new Vector4(2, 2, 2, 2) },
            { "progress_fill", new Vector4(10, 6, 10, 6) },
            { "progress_track", new Vector4(14, 10, 14, 10) },
            { "response_box", new Vector4(32, 32, 32, 32) },
            { "row_highlight", new Vector4(26, 26, 26, 26) },
            { "shadow_soft", new Vector4(196, 196, 196, 196) },
            { "step_box", new Vector4(22, 22, 22, 22) },
            { "step_box_done", new Vector4(22, 22, 22, 22) },
            { "swatch_fill", new Vector4(22, 22, 22, 22) },
            { "swatch_outline", new Vector4(22, 22, 22, 22) },
            { "tab_active", new Vector4(36, 4, 36, 36) },
            { "tab_hover", new Vector4(32, 4, 32, 32) },
            { "tick_empty", new Vector4(12, 12, 12, 12) },
            { "tick_filled", new Vector4(12, 12, 12, 12) },
            { "typing_dot", new Vector4(0, 0, 0, 0) },
            { "url_bar", new Vector4(50, 46, 50, 46) },
            { "window_dot", new Vector4(0, 0, 0, 0) },
        };

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            int i = path.IndexOf(Root);
            if (i < 0) return;

            string rel = path.Substring(i + Root.Length);          // e.g. "UI/card.png"
            string folder = rel.Contains("/") ? rel.Substring(0, rel.IndexOf('/')) : "";
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            var importer = (TextureImporter)assetImporter;

            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            if (folder == "Textures")
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsPerUnit = (folder == "UI" || folder == "Icons") ? 200f : 100f;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);

            if (folder == "UI" && Borders.TryGetValue(name, out Vector4 border))
                importer.spriteBorder = border;
        }
    }
}
