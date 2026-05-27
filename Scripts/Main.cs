using Godot;
using System;
using TormentaVTT.Models;
using TormentaVTT.Services;

namespace TormentaVTT.UI
{
    public partial class Main : Control
    {
        private MapController _mapController = null!;
        private ChatController _chatController = null!;
        private Campaign _currentCampaign = Campaign.CreateDefault();
        private DiceParser _diceParser = new();

        public override void _Ready()
        {
            _mapController = GetNode<MapController>("MapPanel");
            _chatController = GetNode<ChatController>("ChatPanel");

            GetNode<Button>("Toolbar/TopButtons/NewCampaignButton").Pressed += OnNewCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/LoadCampaignButton").Pressed += OnLoadCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/SaveCampaignButton").Pressed += OnSaveCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/ImportMapButton").Pressed += OnImportMapPressed;
            GetNode<Button>("Toolbar/TopButtons/ToggleGridButton").Pressed += OnToggleGridPressed;
            GetNode<Button>("Toolbar/TopButtons/SpawnTokenButton").Pressed += OnSpawnTokenPressed;
            GetNode<Button>("Toolbar/TopButtons/ImportTokenButton").Pressed += OnImportTokenPressed;
            GetNode<Button>("Toolbar/TopButtons/RollInitButton").Pressed += OnRollInitiativePressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/ApplyStatsButton").Pressed += OnApplyStatsPressed;

            GetNode<FileDialog>("MapFileDialog").FileSelected += OnMapFileSelected;
            GetNode<FileDialog>("TokenFileDialog").FileSelected += OnTokenFileSelected;
            GetNode<FileDialog>("CampaignOpenDialog").FileSelected += OnCampaignOpenSelected;
            GetNode<FileDialog>("CampaignSaveDialog").FileSelected += OnCampaignSaveSelected;

            _mapController.SelectedTokenChanged += OnSelectedTokenChanged;
            _chatController.SystemMessage("Bem-vindo ao Tormenta VTT. Use /roll para rolar dados.");

            LoadCampaign(_currentCampaign);
        }

        private void OnNewCampaignPressed()
        {
            _currentCampaign = Campaign.CreateDefault();
            LoadCampaign(_currentCampaign);
            _chatController.SystemMessage("Nova campanha criada.");
        }

        private void OnLoadCampaignPressed()
        {
            GetNode<FileDialog>("CampaignOpenDialog").PopupCenteredRatio();
        }

        private void OnSaveCampaignPressed()
        {
            GetNode<FileDialog>("CampaignSaveDialog").PopupCenteredRatio();
        }

        private void OnImportMapPressed()
        {
            GetNode<FileDialog>("MapFileDialog").PopupCenteredRatio();
        }

        private void OnToggleGridPressed()
        {
            _mapController.ToggleGrid();
        }

        private void OnSpawnTokenPressed()
        {
            var token = TokenData.Create("Novo Token", string.Empty);
            token.Sheet.Name = "NPC"
                ;
            token.Position = new Vector2(150, 150);
            _currentCampaign.Tokens.Add(token);
            _mapController.AddToken(token);
            _chatController.SystemMessage($"Token '{token.Name}' criado.");
        }

        private void OnImportTokenPressed()
        {
            GetNode<FileDialog>("TokenFileDialog").PopupCenteredRatio();
        }

        private void OnRollInitiativePressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected is null)
            {
                _chatController.SystemMessage("Selecione um token para rolar iniciativa.");
                return;
            }

            var expression = $"1d20+{selected.Sheet.Initiative}";
            var result = _diceParser.Evaluate(expression, selected.Sheet.GetAttributeTable());
            _chatController.AddSystemMessage($"{selected.Name} rolou iniciativa: {result.Total} [{result.Breakdown}]");
        }

        private void OnApplyStatsPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Nenhum token selecionado.");
                return;
            }

            token.Sheet.HP = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/HPInput").Value;
            token.Sheet.PM = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/PMInput").Value;
            token.Sheet.Defense = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/DefenseInput").Value;
            token.Sheet.Initiative = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/InitiativeInput").Value;
            UpdateSelectionPanel(token);
            _chatController.SystemMessage($"Estatísticas de {token.Name} atualizadas.");
        }

        private void OnMapFileSelected(string path)
        {
            _currentCampaign.MapImagePath = path;
            _mapController.LoadMap(path);
            _chatController.SystemMessage($"Mapa importado: {System.IO.Path.GetFileName(path)}");
        }

        private void OnTokenFileSelected(string path)
        {
            var token = TokenData.Create("Token Importado", path);
            token.Position = new Vector2(200, 200);
            _currentCampaign.Tokens.Add(token);
            _mapController.AddToken(token);
            _chatController.SystemMessage($"Token importado: {System.IO.Path.GetFileName(path)}");
        }

        private void OnCampaignOpenSelected(string path)
        {
            var campaign = PersistenceService.LoadCampaign(path);
            if (campaign == null)
            {
                _chatController.SystemMessage("Falha ao carregar campanha.");
                return;
            }

            _currentCampaign = campaign;
            LoadCampaign(_currentCampaign);
            _chatController.SystemMessage($"Campanha carregada: {_currentCampaign.Name}");
        }

        private void OnCampaignSaveSelected(string path)
        {
            var actualPath = path.EndsWith(".json") ? path : path + ".json";
            _currentCampaign.Name = GetNode<LineEdit>("Toolbar/TopButtons/CampaignName")?.Text ?? _currentCampaign.Name;
            if (PersistenceService.SaveCampaign(_currentCampaign, actualPath))
            {
                _chatController.SystemMessage($"Campanha salva em: {actualPath}");
            }
            else
            {
                _chatController.SystemMessage("Falha ao salvar campanha.");
            }
        }

        private void OnSelectedTokenChanged(TokenData? token)
        {
            UpdateSelectionPanel(token);
        }

        private void LoadCampaign(Campaign campaign)
        {
            _currentCampaign = campaign;
            _mapController.LoadCampaign(campaign);
            _chatController.SystemMessage($"Campanha ativa: {campaign.Name}");
            UpdateSelectionPanel(_mapController.SelectedToken);
        }

        private void UpdateSelectionPanel(TokenData? token)
        {
            var selectedName = GetNode<Label>("SidebarPanel/SidebarVBox/SelectedName");
            var selectedStats = GetNode<Label>("SidebarPanel/SidebarVBox/SelectedStats");
            var hpInput = GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/HPInput");
            var pmInput = GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/PMInput");
            var defenseInput = GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/DefenseInput");
            var initiativeInput = GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/InitiativeInput");

            if (token == null)
            {
                selectedName.Text = "Nenhum token selecionado";
                selectedStats.Text = "Selecione um token no mapa.";
                hpInput.Value = 0;
                pmInput.Value = 0;
                defenseInput.Value = 0;
                initiativeInput.Value = 0;
                return;
            }

            selectedName.Text = token.Name;
            selectedStats.Text = $"PV: {token.Sheet.HP} / PM: {token.Sheet.PM}\nDefesa: {token.Sheet.Defense} \nIniciativa: {token.Sheet.Initiative}\nCondições: {string.Join(", ", token.Sheet.Conditions)}";
            hpInput.Value = token.Sheet.HP;
            pmInput.Value = token.Sheet.PM;
            defenseInput.Value = token.Sheet.Defense;
            initiativeInput.Value = token.Sheet.Initiative;
        }
    }
}
