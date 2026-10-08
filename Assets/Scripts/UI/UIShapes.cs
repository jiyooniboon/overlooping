using System;
using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.UI
{
    /// <summary>
    /// A rectangle drawn as an outline rather than a filled block.
    ///
    /// uGUI's Image can only draw filled quads (or a 9-sliced sprite, which needs an asset and
    /// whose border thickness drifts with scaling). This generates the four border quads directly,
    /// so line weight is exact at any rect size and no texture is needed.
    ///
    /// Set `thickness` to 0 and give `fillColor` an alpha to get a plain filled block - useful for
    /// thin solid elements like a playhead, where "outline" would be meaningless.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIOutlineBox : Graphic
    {
        [Flags]
        public enum Edges
        {
            None   = 0,
            Top    = 1 << 0,
            Bottom = 1 << 1,
            Left   = 1 << 2,
            Right  = 1 << 3,
            All    = Top | Bottom | Left | Right,
        }

        [Tooltip("Border line weight in UI units. 0 draws no border.")]
        public float thickness = 2f;

        [Tooltip("Which sides to draw. Use single edges for rules and separators.")]
        public Edges edges = Edges.All;

        [Tooltip("Interior colour. Leave alpha at 0 for a true outline.")]
        public Color fillColor = Color.clear;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = GetPixelAdjustedRect();

            // Clamp to half the smaller side. Past that the top and bottom bars would overlap
            // each other, which double-draws the middle and darkens it when the ink is
            // semi-transparent.
            float t = Mathf.Min(Mathf.Max(0f, thickness),
                                Mathf.Min(r.width, r.height) * 0.5f);
            Color32 line = color;

            if (fillColor.a > 0f)
                AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax, fillColor);

            if (t <= 0f || edges == Edges.None) return;

            // Corners are covered by the horizontal edges; the verticals are inset by `t` so the
            // corner pixels aren't drawn twice (which shows as a darker square when semi-opaque).
            if ((edges & Edges.Bottom) != 0) AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMin + t, line);
            if ((edges & Edges.Top) != 0)    AddQuad(vh, r.xMin, r.yMax - t, r.xMax, r.yMax, line);

            float innerMin = (edges & Edges.Bottom) != 0 ? r.yMin + t : r.yMin;
            float innerMax = (edges & Edges.Top) != 0 ? r.yMax - t : r.yMax;

            if ((edges & Edges.Left) != 0)  AddQuad(vh, r.xMin, innerMin, r.xMin + t, innerMax, line);
            if ((edges & Edges.Right) != 0) AddQuad(vh, r.xMax - t, innerMin, r.xMax, innerMax, line);
        }

        static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;

            v.position = new Vector3(x0, y0); vh.AddVert(v);
            v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y0); vh.AddVert(v);

            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    /// <summary>
    /// A ring. Used for the browser window dots and the record indicator, which read as hollow
    /// circles in a line-art style rather than solid blobs.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIOutlineCircle : Graphic
    {
        public float thickness = 2f;

        [Range(6, 64)]
        public int segments = 28;

        [Tooltip("Interior colour. Leave alpha at 0 for a hollow ring.")]
        public Color fillColor = Color.clear;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = GetPixelAdjustedRect();
            float outer = Mathf.Min(r.width, r.height) * 0.5f;
            if (outer <= 0f) return;

            float inner = Mathf.Max(0f, outer - Mathf.Max(0f, thickness));
            Vector2 c = r.center;
            var v = UIVertex.simpleVert;

            if (fillColor.a > 0f && inner > 0f)
            {
                v.color = fillColor;
                v.position = c; vh.AddVert(v);
                int centreIndex = vh.currentVertCount - 1;

                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    v.position = new Vector3(c.x + Mathf.Cos(a) * inner, c.y + Mathf.Sin(a) * inner);
                    vh.AddVert(v);
                }
                for (int s = 0; s < segments; s++)
                    vh.AddTriangle(centreIndex, centreIndex + 1 + s, centreIndex + 2 + s);
            }

            if (thickness <= 0f) return;

            v.color = color;
            int ringStart = vh.currentVertCount;

            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                v.position = new Vector3(c.x + cos * inner, c.y + sin * inner); vh.AddVert(v);
                v.position = new Vector3(c.x + cos * outer, c.y + sin * outer); vh.AddVert(v);
            }

            for (int s = 0; s < segments; s++)
            {
                int i = ringStart + s * 2;
                vh.AddTriangle(i, i + 1, i + 3);
                vh.AddTriangle(i, i + 3, i + 2);
            }
        }
    }
}
