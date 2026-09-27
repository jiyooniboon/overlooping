using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Free-draw editing for a ColorworkChart: brush, flood fill, eraser, eyedropper, and undo.
    /// Owns the chart data and drives ChartRenderer; contains no AI and no network calls, so this
    /// is a complete, demoable Creative Mode on its own.
    ///
    /// Uses the legacy Input API to match ProgressManager. The project's Active Input Handling is
    /// set to "Both", so this compiles alongside the installed Input System package.
    ///
    /// Controls:
    ///   Left mouse       paint with the active tool
    ///   Right mouse      erase (paint palette index 0)
    ///   1-9              select yarn color
    ///   B / G / I        brush / fill (gap) / eyedropper
    ///   Ctrl+Z, Ctrl+Y   undo / redo
    ///   [ / ]            brush size down / up
    /// </summary>
    public class ChartEditor : MonoBehaviour
    {
        public enum Tool { Brush, Fill, Eyedropper }

        [Header("Chart")]
        public int chartWidth = 40;
        public int chartHeight = 50;

        [Tooltip("Leave empty to run with a placeholder palette (see YarnPalette.CreateDefault).")]
        public YarnPalette palette;

        [Header("Rendering")]
        public ChartRenderer chartRenderer;

        [Tooltip("Camera used for screen->cell conversion. Leave null for Screen Space Overlay canvases.")]
        public Camera uiCamera;

        [Header("Tools")]
        public Tool activeTool = Tool.Brush;
        [Range(1, 8)] public int brushSize = 1;
        public int activeYarnIndex = 1;

        [Header("Undo")]
        [Tooltip("Maximum number of undoable strokes held in memory.")]
        public int undoLimit = 64;

        public ColorworkChart Chart { get; private set; }

        // Each stroke (press -> release) collapses into a single undo entry, so one drag is one
        // Ctrl+Z rather than dozens.
        private readonly List<CellEdit> currentStroke = new();
        private readonly LinkedList<List<CellEdit>> undoStack = new();
        private readonly LinkedList<List<CellEdit>> redoStack = new();

        private bool painting;
        private bool strokeWasFill; // a fill is one-shot; holding the button must not then brush
        private int lastCellX = -1, lastCellY = -1;

        void Start()
        {
            if (palette == null)
            {
                palette = YarnPalette.CreateDefault();
                Debug.LogWarning("ChartEditor: no YarnPalette assigned - using placeholder colors. " +
                                 "Create one via Assets > Create > Knit > Yarn Palette.", this);
            }

            Chart = new ColorworkChart(chartWidth, chartHeight);

            if (chartRenderer == null)
            {
                Debug.LogError("ChartEditor: no ChartRenderer assigned - nothing will be drawn.", this);
                return;
            }

            chartRenderer.Bind(Chart, palette);
        }

        void Update()
        {
            if (Chart == null || chartRenderer == null) return;

            HandleHotkeys();
            HandlePainting();
        }

        void HandleHotkeys()
        {
            for (int i = 1; i <= 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) && i < palette.Count)
                    activeYarnIndex = i;
            }

            if (Input.GetKeyDown(KeyCode.B)) activeTool = Tool.Brush;
            if (Input.GetKeyDown(KeyCode.G)) activeTool = Tool.Fill;
            if (Input.GetKeyDown(KeyCode.I)) activeTool = Tool.Eyedropper;

            if (Input.GetKeyDown(KeyCode.LeftBracket))  brushSize = Mathf.Max(1, brushSize - 1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) brushSize = Mathf.Min(8, brushSize + 1);

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
        }

        void HandlePainting()
        {
            bool leftDown  = Input.GetMouseButton(0);
            bool rightDown = Input.GetMouseButton(1);
            bool anyDown = leftDown || rightDown;

            if (!anyDown)
            {
                if (painting) EndStroke();
                return;
            }

            // Don't paint through the generation panel or palette bar. The chart's own RawImage
            // has raycastTarget disabled, so it never counts as "over UI" here.
            if (!painting && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (!chartRenderer.ScreenToCell(Input.mousePosition, uiCamera, out int cx, out int cy))
                return;

            // Right mouse always erases to the background index regardless of active tool.
            int value = rightDown ? 0 : activeYarnIndex;

            if (activeTool == Tool.Eyedropper && leftDown)
            {
                activeYarnIndex = Chart.Get(cx, cy);
                return;
            }

            if (!painting)
            {
                painting = true;
                strokeWasFill = false;
                currentStroke.Clear();
                lastCellX = cx;
                lastCellY = cy;

                if (activeTool == Tool.Fill && leftDown)
                {
                    strokeWasFill = true;
                    Chart.FloodFill(cx, cy, value, currentStroke);
                    foreach (var e in currentStroke) chartRenderer.RepaintCellByIndex(e.index);
                    return;
                }
            }

            // A fill already happened for this press; ignore drag until the button is released.
            if (strokeWasFill) return;

            // Interpolate between the previous and current cell. A fast drag moves several cells
            // per frame, so painting only the current cell leaves visible gaps in the stroke.
            PaintLine(lastCellX, lastCellY, cx, cy, value);
            lastCellX = cx;
            lastCellY = cy;
        }

        void PaintLine(int x0, int y0, int x1, int y1, int value)
        {
            // Bresenham
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                PaintBrush(x0, y0, value);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        void PaintBrush(int cx, int cy, int value)
        {
            int r = brushSize - 1;
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!Chart.InBounds(x, y)) continue;

                    int i = Chart.Index(x, y);
                    int prev = Chart.Cells[i];
                    if (prev == value) continue;

                    Chart.Set(x, y, value);
                    currentStroke.Add(new CellEdit(i, prev, value));
                    chartRenderer.RepaintCell(x, y);
                }
            }
        }

        void EndStroke()
        {
            painting = false;
            if (currentStroke.Count == 0) return;

            PushUndo(new List<CellEdit>(currentStroke));
            currentStroke.Clear();
        }

        void PushUndo(List<CellEdit> stroke)
        {
            undoStack.AddLast(stroke);
            while (undoStack.Count > undoLimit) undoStack.RemoveFirst();
            redoStack.Clear(); // a new edit invalidates the redo branch
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;

            var stroke = undoStack.Last.Value;
            undoStack.RemoveLast();

            foreach (var e in stroke)
            {
                Chart.Cells[e.index] = e.previous;
                chartRenderer.RepaintCellByIndex(e.index);
            }

            redoStack.AddLast(stroke);
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;

            var stroke = redoStack.Last.Value;
            redoStack.RemoveLast();

            foreach (var e in stroke)
            {
                Chart.Cells[e.index] = e.next;
                chartRenderer.RepaintCellByIndex(e.index);
            }

            undoStack.AddLast(stroke);
        }

        /// <summary>Clears the chart to the background color as a single undoable action.</summary>
        public void ClearChart()
        {
            var stroke = new List<CellEdit>();
            for (int i = 0; i < Chart.Cells.Length; i++)
            {
                if (Chart.Cells[i] == 0) continue;
                stroke.Add(new CellEdit(i, Chart.Cells[i], 0));
                Chart.Cells[i] = 0;
            }

            if (stroke.Count == 0) return;
            PushUndo(stroke);
            chartRenderer.RepaintAll();
        }

        /// <summary>
        /// Replaces the whole chart in one undoable step. This is the seam the keyword-generation
        /// pipeline will plug into later: it produces a ColorworkChart, hands it here, and the
        /// player can then hand-edit the result with the same tools.
        /// </summary>
        public void LoadChart(ColorworkChart incoming)
        {
            if (incoming == null) return;

            if (incoming.Width != Chart.Width || incoming.Height != Chart.Height)
            {
                Chart = incoming;
                undoStack.Clear();
                redoStack.Clear();
                chartRenderer.Bind(Chart, palette);
                return;
            }

            var stroke = new List<CellEdit>();
            for (int i = 0; i < Chart.Cells.Length; i++)
            {
                if (Chart.Cells[i] == incoming.Cells[i]) continue;
                stroke.Add(new CellEdit(i, Chart.Cells[i], incoming.Cells[i]));
                Chart.Cells[i] = incoming.Cells[i];
            }

            if (stroke.Count > 0) PushUndo(stroke);
            chartRenderer.RepaintAll();
        }
    }
}
