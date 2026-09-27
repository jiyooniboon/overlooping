using System;
using System.Collections.Generic;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Pure data model for a colorwork chart. Knows nothing about rendering, input, or Unity
    /// scene objects - it is just a grid of palette indices plus the operations that mutate it.
    /// ChartRenderer draws it; ChartEditor mutates it.
    ///
    /// Storage is a flat int[] rather than int[,] for two reasons: Unity's JsonUtility cannot
    /// serialize multidimensional arrays, and flat iteration is faster. Index with Index(x, y).
    ///
    /// Coordinate convention: (0,0) is the BOTTOM-LEFT cell. This matches both Unity's texture
    /// origin and knitting chart convention (row 1 is read at the bottom), so no Y-flipping is
    /// needed anywhere between the data and the texture.
    ///
    /// Cell values are indices into a YarnPalette, never colors. Keeping indices means recoloring
    /// the palette instantly recolors every existing chart, and it keeps saved charts small.
    /// </summary>
    [Serializable]
    public class ColorworkChart
    {
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int[] cells;

        public int Width => width;
        public int Height => height;
        public int CellCount => cells.Length;

        /// <summary>Raw backing array. Exposed for fast bulk reads (rendering); do not resize it.</summary>
        public int[] Cells => cells;

        public ColorworkChart(int width, int height, int fillIndex = 0)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
            cells = new int[this.width * this.height];
            if (fillIndex != 0) Clear(fillIndex);
        }

        public int Index(int x, int y) => y * width + x;

        public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

        public int Get(int x, int y) => cells[Index(x, y)];

        /// <summary>Returns true if the value actually changed, so callers can skip redundant work.</summary>
        public bool Set(int x, int y, int paletteIndex)
        {
            int i = Index(x, y);
            if (cells[i] == paletteIndex) return false;
            cells[i] = paletteIndex;
            return true;
        }

        public void Clear(int paletteIndex = 0)
        {
            for (int i = 0; i < cells.Length; i++) cells[i] = paletteIndex;
        }

        /// <summary>
        /// 4-way flood fill from (startX, startY). Iterative rather than recursive on purpose:
        /// a recursive fill on a large single-color chart will blow the stack.
        /// Appends every changed cell to <paramref name="changed"/> so the caller can build an
        /// undo entry and repaint only what moved.
        /// </summary>
        public void FloodFill(int startX, int startY, int paletteIndex, List<CellEdit> changed)
        {
            if (!InBounds(startX, startY)) return;

            int target = Get(startX, startY);
            if (target == paletteIndex) return; // already that color - nothing to do

            var stack = new Stack<int>();
            stack.Push(Index(startX, startY));

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (cells[i] != target) continue; // already handled via another path

                changed.Add(new CellEdit(i, cells[i], paletteIndex));
                cells[i] = paletteIndex;

                int x = i % width;
                int y = i / width;

                if (x > 0)          stack.Push(i - 1);
                if (x < width - 1)  stack.Push(i + 1);
                if (y > 0)          stack.Push(i - width);
                if (y < height - 1) stack.Push(i + width);
            }
        }

        /// <summary>
        /// Resizes the chart, preserving the overlapping bottom-left region. Used when the player
        /// changes chart dimensions, and later when the generated-image pipeline hands back a
        /// chart at a different resolution.
        /// </summary>
        public void Resize(int newWidth, int newHeight, int fillIndex = 0)
        {
            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);
            if (newWidth == width && newHeight == height) return;

            var next = new int[newWidth * newHeight];
            if (fillIndex != 0)
                for (int i = 0; i < next.Length; i++) next[i] = fillIndex;

            int copyW = Mathf.Min(width, newWidth);
            int copyH = Mathf.Min(height, newHeight);
            for (int y = 0; y < copyH; y++)
                for (int x = 0; x < copyW; x++)
                    next[y * newWidth + x] = cells[y * width + x];

            width = newWidth;
            height = newHeight;
            cells = next;
        }

        public ColorworkChart Clone()
        {
            var copy = new ColorworkChart(width, height);
            Array.Copy(cells, copy.cells, cells.Length);
            return copy;
        }

        public string ToJson() => JsonUtility.ToJson(this);

        public static ColorworkChart FromJson(string json) => JsonUtility.FromJson<ColorworkChart>(json);
    }

    /// <summary>
    /// A single cell mutation, stored flat-indexed. Undo stores these rather than whole-chart
    /// snapshots - a 100x125 chart is 12,500 ints, so snapshotting every brush stroke would
    /// churn a lot of memory for no benefit.
    /// </summary>
    public struct CellEdit
    {
        public readonly int index;
        public readonly int previous;
        public readonly int next;

        public CellEdit(int index, int previous, int next)
        {
            this.index = index;
            this.previous = previous;
            this.next = next;
        }
    }
}
