using Godot;
using System;
using System.Collections.Generic;
using TormentaVTT.Models;

namespace TormentaVTT.UI
{
    public partial class MapController : Panel
    {
        private TextureRect _mapTexture = null!;
        private Control _tokenLayer = null!;
        private GridOverlay _gridOverlay = null!;
        private bool _isPanning;
        private Vector2 _panStart;
        private Vector2 _mapStart;
        private readonly List<TokenControl> _tokenNodes = new();

        public TokenData? SelectedToken { get; private set; }
        public event Action<TokenData?>? SelectedTokenChanged;

        public override void _Ready()
        {
            _mapTexture = GetNode<TextureRect>("MapTexture");
            _tokenLayer = GetNode<Control>("MapTexture/TokenLayer");
            _gridOverlay = GetNode<GridOverlay>("MapTexture/GridOverlay");
            _gridOverlay.Visible = false;
            _mapTexture.MouseFilter = MouseFilterEnum.Pass;
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton button)
            {
                if (button.ButtonIndex == MouseButton.Right)
                {
                    _isPanning = button.Pressed;
                    _panStart = button.Position;
                    _mapStart = _mapTexture.Position;
                }
            }

            if (@event is InputEventMouseMotion motion && _isPanning)
            {
                _mapTexture.Position = _mapStart + motion.Position - _panStart;
            }

            if (@event is InputEventMouseButton wheel && wheel.ButtonIndex == MouseButton.WheelUp && wheel.Pressed)
            {
                SetZoom(_mapTexture.Scale * 1.1f);
            }
            else if (@event is InputEventMouseButton wheelDown && wheelDown.ButtonIndex == MouseButton.WheelDown && wheelDown.Pressed)
            {
                SetZoom(_mapTexture.Scale * 0.9f);
            }
        }

        public void LoadMap(string path)
        {
            var image = new Image();
            var error = image.Load(path);
            if (error != Error.Ok)
            {
                GD.PrintErr($"Falha ao carregar imagem do mapa: {path}");
                return;
            }

            var texture = ImageTexture.CreateFromImage(image);
            _mapTexture.Texture = texture;
            _mapTexture.Size = image.GetSize();
            _mapTexture.Position = Vector2.Zero;
            _mapTexture.Scale = Vector2.One;
            _tokenLayer.Position = Vector2.Zero;
            _gridOverlay.Size = _mapTexture.Size;
            _gridOverlay.QueueRedraw();
        }

        public void LoadCampaign(Campaign campaign)
        {
            if (!string.IsNullOrEmpty(campaign.MapImagePath) && System.IO.File.Exists(campaign.MapImagePath))
            {
                LoadMap(campaign.MapImagePath);
            }

            foreach (var node in _tokenNodes)
            {
                node.QueueFree();
            }

            _tokenNodes.Clear();
            SelectedToken = null;

            foreach (var token in campaign.Tokens)
            {
                AddToken(token);
            }
        }

        public void AddToken(TokenData token)
        {
            var tokenControl = new TokenControl(token);
            tokenControl.Position = token.Position;
            tokenControl.Selected += OnTokenSelected;
            tokenControl.Dragged += OnTokenDragged;
            _tokenLayer.AddChild(tokenControl);
            _tokenNodes.Add(tokenControl);
            UpdateSelectedToken(tokenControl, false);
        }

        public void ToggleGrid()
        {
            _gridOverlay.Visible = !_gridOverlay.Visible;
            _gridOverlay.QueueRedraw();
        }

        private void SetZoom(Vector2 scale)
        {
            var newScale = new Vector2(
                Mathf.Clamp(scale.X, 0.4f, 4f),
                Mathf.Clamp(scale.Y, 0.4f, 4f)
            );

            _mapTexture.Scale = newScale;
            _gridOverlay.Scale = newScale;
            _tokenLayer.Scale = newScale;
        }

        private void OnTokenSelected(TokenControl tokenControl)
        {
            UpdateSelectedToken(tokenControl, true);
        }

        private void OnTokenDragged(TokenControl tokenControl)
        {
            tokenControl.Data.Position = tokenControl.Position;
        }

        private void UpdateSelectedToken(TokenControl tokenControl, bool notify)
        {
            foreach (var node in _tokenNodes)
            {
                node.SetSelected(node == tokenControl);
            }

            SelectedToken = tokenControl.Data;
            if (notify)
            {
                SelectedTokenChanged?.Invoke(SelectedToken);
            }
        }
    }
}
