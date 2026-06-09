using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TormentaVTT.UI
{
    /// <summary>
    /// Renders fog-of-war over the map using a grid-cell revealed state.
    ///
    /// - GM mode: fog is semi-transparent (GM can still see tokens through it).
    /// - Player mode: fog is fully opaque.
    /// - RevealToolActive: left-click reveals cells, right-click hides them.
    /// </summary>
    public partial class FogOfWarLayer : Control
    {
        private readonly HashSet<(int X, int Y)> _revealed = new();

        // ── Settings ─────────────────────────────────────────────────────────
        public int   CellSize      { get; set; } = 64;
        public bool  IsGMMode      { get; set; } = true;
        public bool  FogEnabled    { get; set; } = false;
        public bool  RevealTool    { get; set; } = false;  // true = reveal, false = hide
        public bool  ToolActive    { get; set; } = false;

        private readonly Color _playerFog = new(0f, 0f, 0f, 0.92f);
        private readonly Color _gmFog     = new(0f, 0f, 0.1f, 0.45f);

        // fires when the user modifies fog via the tool — (cells, isReveal)
        public event Action<List<string>, bool>? FogChanged;

        // ── Draw ─────────────────────────────────────────────────────────────
        public override void _Draw()
        {
            if (!FogEnabled || CellSize <= 0) return;

            var color = IsGMMode ? _gmFog : _playerFog;
            int cols  = (int)(Size.X / CellSize) + 2;
            int rows  = (int)(Size.Y / CellSize) + 2;

            for (int cx = 0; cx < cols; cx++)
                for (int cy = 0; cy < rows; cy++)
                    if (!_revealed.Contains((cx, cy)))
                        DrawRect(new Rect2(cx * CellSize, cy * CellSize, CellSize, CellSize), color);
        }

        // ── Painting tool ─────────────────────────────────────────────────────
        private bool _painting;

        public override void _GuiInput(InputEvent @event)
        {
            if (!ToolActive) return;

            if (@event is InputEventMouseButton btn)
            {
                if (btn.ButtonIndex == MouseButton.Left)
                {
                    _painting = btn.Pressed;
                    if (btn.Pressed) PaintAt(btn.Position);
                }
                else if (btn.ButtonIndex == MouseButton.Right && btn.Pressed)
                {
                    HideCellAt(btn.Position);
                }
            }

            if (@event is InputEventMouseMotion motion && _painting)
                PaintAt(motion.Position);
        }

        private void PaintAt(Vector2 pos)
        {
            var (cx, cy) = ToCell(pos);
            if (RevealTool) RevealCell(cx, cy, broadcast: true);
            else            HideCell(cx, cy, broadcast: true);
        }

        private void HideCellAt(Vector2 pos)
        {
            var (cx, cy) = ToCell(pos);
            HideCell(cx, cy, broadcast: true);
        }

        // ── Public API ───────────────────────────────────────────────────────
        public void RevealCell(int x, int y, bool broadcast = false)
        {
            if (_revealed.Add((x, y)))
            {
                QueueRedraw();
                if (broadcast) FogChanged?.Invoke(new List<string> { $"{x},{y}" }, true);
            }
        }

        public void HideCell(int x, int y, bool broadcast = false)
        {
            if (_revealed.Remove((x, y)))
            {
                QueueRedraw();
                if (broadcast) FogChanged?.Invoke(new List<string> { $"{x},{y}" }, false);
            }
        }

        public void RevealAll()
        {
            if (CellSize <= 0) return;
            int cols = (int)(Size.X / CellSize) + 2;
            int rows = (int)(Size.Y / CellSize) + 2;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    _revealed.Add((x, y));
            QueueRedraw();
        }

        public void HideAll()
        {
            _revealed.Clear();
            QueueRedraw();
        }

        /// <summary>Apply a batch of cells from a network sync.</summary>
        public void ApplyCells(IEnumerable<string> cells, bool reveal)
        {
            foreach (var cell in cells)
            {
                var parts = cell.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out var x)
                    && int.TryParse(parts[1], out var y))
                {
                    if (reveal) _revealed.Add((x, y));
                    else        _revealed.Remove((x, y));
                }
            }
            QueueRedraw();
        }

        /// <summary>Replace entire fog state from a list of "x,y" strings.</summary>
        public void SetFullState(IEnumerable<string> revealedCells)
        {
            _revealed.Clear();
            ApplyCells(revealedCells, true);
        }

        public List<string> GetRevealedCells() =>
            _revealed.Select(c => $"{c.X},{c.Y}").ToList();

        public bool IsCellRevealed(int x, int y) => _revealed.Contains((x, y));

        // ── Helpers ──────────────────────────────────────────────────────────
        private (int, int) ToCell(Vector2 pos) =>
            ((int)(pos.X / CellSize), (int)(pos.Y / CellSize));
    }
}
