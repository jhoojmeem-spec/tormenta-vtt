using Godot;

namespace TormentaVTT.UI
{
    public partial class GridOverlay : Control
    {
        [Export]
        public int CellSize { get; set; } = 64;

        public override void _Ready()
        {
            Visible = false;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (!Visible)
                return;

            var size = Size;
            var color = new Color(1.0f, 1.0f, 1.0f, 0.18f);

            for (var x = 0; x < size.X; x += CellSize)
            {
                DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), color, 1);
            }

            for (var y = 0; y < size.Y; y += CellSize)
            {
                DrawLine(new Vector2(0, y), new Vector2(size.X, y), color, 1);
            }
        }

        public void SetGridEnabled(bool enabled)
        {
            Visible = enabled;
            QueueRedraw();
        }
    }
}
