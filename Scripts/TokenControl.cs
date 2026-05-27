using Godot;
using System;
using TormentaVTT.Models;

namespace TormentaVTT.UI
{
    public partial class TokenControl : Button
    {
        public TokenData Data { get; }
        private bool _dragging;
        private Vector2 _dragOffset;

        public event Action<TokenControl>? Selected;
        public event Action<TokenControl>? Dragged;

        public TokenControl(TokenData data)
        {
            Data = data;
            Text = data.Name;
            TooltipText = data.Name;
            CustomMinimumSize = new Vector2(72, 72);
            Theme = null;
            FocusMode = FocusModeEnum.Click;
            Pressed += OnPressed;
            AddThemeColorOverride("font_color", Colors.Black);
            AddThemeColorOverride("font_color_hover", Colors.Black);
            AddThemeColorOverride("font_color_pressed", Colors.Black);
        }

        public override void _Ready()
        {
            Size = new Vector2(88, 88);
            SetSelected(false);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
                    _dragging = true;
                    _dragOffset = button.Position;
                }
                else
                {
                    _dragging = false;
                    Dragged?.Invoke(this);
                }
            }

            if (@event is InputEventMouseMotion motion && _dragging)
            {
                Position += motion.Relative;
                Dragged?.Invoke(this);
            }
        }

        public void SetSelected(bool selected)
        {
            Modulate = selected ? new Color(1.0f, 0.9f, 0.4f) : Colors.White;
        }

        private void OnPressed()
        {
            Selected?.Invoke(this);
        }
    }
}
