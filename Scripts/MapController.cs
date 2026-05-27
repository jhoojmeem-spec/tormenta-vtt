using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public event Action<TokenData>? TokenAdded;
        public event Action<TokenData>? TokenRemoved;

        public float CurrentZoom => _mapTexture.Scale.X;
        public bool IsGridEnabled => _gridOverlay.Visible;

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
                SetZoom(new Vector2(campaign.Zoom, campaign.Zoom));
                _gridOverlay.Visible = campaign.GridEnabled;
                _gridOverlay.QueueRedraw();
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
            TokenAdded?.Invoke(token);
        }

        public void SelectToken(TokenData token)
        {
            var tokenControl = _tokenNodes.FirstOrDefault(node => node.Data == token);
            if (tokenControl == null)
                return;

            UpdateSelectedToken(tokenControl, true);
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
            tokenControl.Position = SnapToGrid(tokenControl.Position);
            tokenControl.Data.Position = tokenControl.Position;
        }

        public void RemoveToken(TokenData token)
        {
            var tokenNode = _tokenNodes.FirstOrDefault(node => node.Data == token);
            if (tokenNode == null)
                return;

            TokenRemoved?.Invoke(token);
            tokenNode.QueueFree();
            _tokenNodes.Remove(tokenNode);

            if (SelectedToken == token)
            {
                SelectedToken = null;
                SelectedTokenChanged?.Invoke(null);
            }
        }

        private Vector2 SnapToGrid(Vector2 position)
        {
            if (!_gridOverlay.Visible || _gridOverlay.CellSize <= 0)
                return position;

            var x = Mathf.Round(position.X / _gridOverlay.CellSize) * _gridOverlay.CellSize;
            var y = Mathf.Round(position.Y / _gridOverlay.CellSize) * _gridOverlay.CellSize;
            return new Vector2(x, y);
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

        public Vector2 GetViewportCenterMapPosition()
        {
            // Center of this control (the MapController) in local coords
            var viewportCenter = Size / 2f;
            // Convert from viewport (MapController local) to map texture local coordinates
            // Account for map texture position (panning) and scale (zoom)
            var mapPos = (viewportCenter - _mapTexture.Position) / _mapTexture.Scale;
            return mapPos;
        }
    }
}
