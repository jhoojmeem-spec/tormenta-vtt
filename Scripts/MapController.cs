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
        private FogOfWarLayer _fogLayer = null!;
        private bool _isPanning;
        private Vector2 _panStart;
        private Vector2 _mapStart;
        private readonly List<TokenControl> _tokenNodes = new();

        /// <summary>When false, GM-only tokens are hidden (player view).</summary>
        public bool IsGMMode { get; set; } = true;

        public TokenData? SelectedToken { get; private set; }
        public event Action<TokenData?>? SelectedTokenChanged;
        public event Action<TokenData>? TokenAdded;
        public event Action<TokenData>? TokenRemoved;
        /// <summary>Fires once when the user drops a token — for network sync.</summary>
        public event Action<TokenData>? TokenDropped;
        /// <summary>Fires when fog state changes via the paint tool.</summary>
        public event Action<List<string>, bool>? FogChanged;

        public float CurrentZoom  => _mapTexture.Scale.X;
        public bool  IsGridEnabled => _gridOverlay.Visible;
        public FogOfWarLayer FogLayer => _fogLayer;

        public override void _Ready()
        {
            _mapTexture  = GetNode<TextureRect>("MapTexture");
            _tokenLayer  = GetNode<Control>("MapTexture/TokenLayer");
            _gridOverlay = GetNode<GridOverlay>("MapTexture/GridOverlay");
            _gridOverlay.Visible = false;
            _mapTexture.MouseFilter = MouseFilterEnum.Pass;

            _fogLayer = new FogOfWarLayer();
            _fogLayer.Name = "FogOfWarLayer";
            _fogLayer.MouseFilter = MouseFilterEnum.Pass;
            _fogLayer.FogEnabled  = false;
            _fogLayer.IsGMMode    = true;
            _fogLayer.FogChanged += (cells, reveal) => FogChanged?.Invoke(cells, reveal);
            _mapTexture.AddChild(_fogLayer);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton button)
            {
                if (button.ButtonIndex == MouseButton.Right)
                {
                    _isPanning = button.Pressed;
                    _panStart  = button.Position;
                    _mapStart  = _mapTexture.Position;
                }
            }

            if (@event is InputEventMouseMotion motion && _isPanning)
                _mapTexture.Position = _mapStart + motion.Position - _panStart;

            if (@event is InputEventMouseButton wheel && wheel.ButtonIndex == MouseButton.WheelUp && wheel.Pressed)
                SetZoom(_mapTexture.Scale * 1.1f);
            else if (@event is InputEventMouseButton wheelDown && wheelDown.ButtonIndex == MouseButton.WheelDown && wheelDown.Pressed)
                SetZoom(_mapTexture.Scale * 0.9f);
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
            _mapTexture.Texture  = texture;
            _mapTexture.Size     = image.GetSize();
            _mapTexture.Position = Vector2.Zero;
            _mapTexture.Scale    = Vector2.One;
            _tokenLayer.Position = Vector2.Zero;
            _gridOverlay.Size    = _mapTexture.Size;
            _gridOverlay.QueueRedraw();
            _fogLayer.Size = _mapTexture.Size;
            _fogLayer.QueueRedraw();
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

            foreach (var node in _tokenNodes) node.QueueFree();
            _tokenNodes.Clear();
            SelectedToken = null;

            foreach (var token in campaign.Tokens)
                AddToken(token);

            _fogLayer.FogEnabled = campaign.FogEnabled;
            _fogLayer.SetFullState(campaign.FogRevealedCells);
            _fogLayer.QueueRedraw();
        }

        public void AddToken(TokenData token)
        {
            if (token.IsGMOnly && !IsGMMode) return;

            var tc = new TokenControl(token);
            tc.Position  = token.Position;
            tc.Selected += OnTokenSelected;
            tc.Dragged  += OnTokenDragged;
            tc.Dropped  += OnTokenDropped;
            _tokenLayer.AddChild(tc);
            _tokenNodes.Add(tc);
            UpdateSelectedToken(tc, false);
            TokenAdded?.Invoke(token);
        }

        public void SelectToken(TokenData token)
        {
            var tc = _tokenNodes.FirstOrDefault(n => n.Data == token);
            if (tc != null) UpdateSelectedToken(tc, true);
        }

        public void ToggleGrid()
        {
            _gridOverlay.Visible = !_gridOverlay.Visible;
            _gridOverlay.QueueRedraw();
        }

        // ── Fog helpers ───────────────────────────────────────────────────────
        public void SetFogEnabled(bool enabled)
        {
            _fogLayer.FogEnabled = enabled;
            _fogLayer.QueueRedraw();
        }

        public void SetFogGMMode(bool gm) => _fogLayer.IsGMMode = gm;

        public void SetFogToolActive(bool active, bool revealMode)
        {
            _fogLayer.ToolActive  = active;
            _fogLayer.RevealTool  = revealMode;
            _fogLayer.MouseFilter = active ? MouseFilterEnum.Stop : MouseFilterEnum.Pass;
        }

        // ── Visibility ────────────────────────────────────────────────────────
        public void ApplyVisibilityMode(bool isGM)
        {
            IsGMMode            = isGM;
            _fogLayer.IsGMMode  = isGM;
            foreach (var n in _tokenNodes)
                if (n.Data.IsGMOnly) n.Visible = isGM;
            _fogLayer.QueueRedraw();
        }

        // ── Remote sync helpers ───────────────────────────────────────────────
        public void RemoteUpdateTokenPosition(string tokenId, float x, float y)
        {
            var n = _tokenNodes.FirstOrDefault(t => t.Data.Id == tokenId);
            if (n == null) return;
            n.Position       = new Vector2(x, y);
            n.Data.Position  = n.Position;
        }

        public void RemoteUpdateTokenStats(string tokenId, int hp, int pm)
        {
            var token = _tokenNodes.FirstOrDefault(n => n.Data.Id == tokenId)?.Data;
            if (token == null) return;
            token.Sheet.HP = hp;
            token.Sheet.PM = pm;
        }

        // ── Private ───────────────────────────────────────────────────────────
        private void SetZoom(Vector2 scale)
        {
            var s = new Vector2(Mathf.Clamp(scale.X, 0.4f, 4f), Mathf.Clamp(scale.Y, 0.4f, 4f));
            _mapTexture.Scale  = s;
            _gridOverlay.Scale = s;
            _tokenLayer.Scale  = s;
        }

        private void OnTokenSelected(TokenControl tc)   => UpdateSelectedToken(tc, true);

        private void OnTokenDragged(TokenControl tc)
        {
            tc.Position    = SnapToGrid(tc.Position);
            tc.Data.Position = tc.Position;
        }

        private void OnTokenDropped(TokenControl tc)
        {
            tc.Position      = SnapToGrid(tc.Position);
            tc.Data.Position = tc.Position;
            TokenDropped?.Invoke(tc.Data);
        }

        public void RemoveToken(TokenData token)
        {
            var n = _tokenNodes.FirstOrDefault(x => x.Data == token);
            if (n == null) return;
            TokenRemoved?.Invoke(token);
            n.QueueFree();
            _tokenNodes.Remove(n);
            if (SelectedToken == token) { SelectedToken = null; SelectedTokenChanged?.Invoke(null); }
        }

        private Vector2 SnapToGrid(Vector2 position)
        {
            if (!_gridOverlay.Visible || _gridOverlay.CellSize <= 0) return position;
            var x = Mathf.Round(position.X / _gridOverlay.CellSize) * _gridOverlay.CellSize;
            var y = Mathf.Round(position.Y / _gridOverlay.CellSize) * _gridOverlay.CellSize;
            return new Vector2(x, y);
        }

        private void UpdateSelectedToken(TokenControl tc, bool notify)
        {
            foreach (var n in _tokenNodes) n.SetSelected(n == tc);
            SelectedToken = tc.Data;
            if (notify) SelectedTokenChanged?.Invoke(SelectedToken);
        }

        public Vector2 GetViewportCenterMapPosition()
        {
            var viewportCenter = Size / 2f;
            return (viewportCenter - _mapTexture.Position) / _mapTexture.Scale;
        }
    }
}
