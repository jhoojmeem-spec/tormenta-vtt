using Godot;
using System;
using System.IO;
using TormentaVTT.Models;

namespace TormentaVTT.UI
{
    public partial class TokenControl : Button
    {
        public TokenData Data { get; }
        private bool _dragging;
        private Vector2 _dragOffset;
        private bool _isSelected;
        private StyleBoxFlat _outlineBox = null!;

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

            if (!string.IsNullOrEmpty(data.ImagePath) && File.Exists(data.ImagePath))
            {
                var image = new Image();
                var error = image.Load(data.ImagePath);
                if (error == Error.Ok)
                {
                    var texture = ImageTexture.CreateFromImage(image);
                    Icon = texture;
                    IconAlignment = HorizontalAlignment.Center;
                    VerticalIconAlignment = VerticalAlignment.Center;
                    ExpandIcon = true;
                    Text = string.Empty;
                }
            }

            _outlineBox = new StyleBoxFlat();
            _outlineBox.BorderWidthLeft = 3;
            _outlineBox.BorderWidthTop = 3;
            _outlineBox.BorderWidthRight = 3;
            _outlineBox.BorderWidthBottom = 3;
            _outlineBox.BorderColor = new Color(1.0f, 0.9f, 0.4f);
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
            _isSelected = selected;
            Modulate = selected ? new Color(1.0f, 0.9f, 0.4f) : Colors.White;
            QueueRedraw();
        }

        public override void _Draw()
        {
            base._Draw();
            if (_isSelected && _outlineBox != null)
            {
                DrawStyleBox(_outlineBox, new Rect2(Vector2.Zero, Size));
            }
        }

        private void OnPressed()
        {
            Selected?.Invoke(this);
        }
    }
}
