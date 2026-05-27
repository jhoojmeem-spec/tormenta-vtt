using Godot;
using System;
using System.Linq;
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
        private CombatController _combatController = new();

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
            GetNode<Button>("SidebarPanel/SidebarVBox/RemoveTokenButton").Pressed += OnRemoveTokenPressed;
            var assets = GetNode<ItemList>("AssetsPanel/AssetsVBox/AssetList");
            assets.ItemSelected += index => OnAssetSelected(index);
            assets.ItemActivated += index => OnAssetActivated(index);

            GetNode<FileDialog>("MapFileDialog").FileSelected += OnMapFileSelected;
            GetNode<FileDialog>("TokenFileDialog").FileSelected += OnTokenFileSelected;
            GetNode<FileDialog>("CampaignOpenDialog").FileSelected += OnCampaignOpenSelected;
            GetNode<FileDialog>("CampaignSaveDialog").FileSelected += OnCampaignSaveSelected;

            _mapController.SelectedTokenChanged += OnSelectedTokenChanged;
            _combatController.CurrentTurnChanged += token =>
            {
                if (token == null)
                {
                    _chatController.SystemMessage("Combate finalizado.");
                    return;
                }

                _mapController.SelectToken(token);
                _chatController.SystemMessage($"Vez de: {token.Name}");
                UpdateInitiativePanel();
            };

            GetNode<Button>("Toolbar/TopButtons/StartCombatButton").Pressed += OnStartCombatPressed;
            GetNode<Button>("Toolbar/TopButtons/PrevTurnButton").Pressed += OnPrevTurnPressed;
            GetNode<Button>("Toolbar/TopButtons/NextTurnButton").Pressed += OnNextTurnPressed;
            GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList").ItemSelected += OnInitiativeSelected;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeMoveButtons/MoveUpButton").Pressed += OnMoveUpPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeMoveButtons/MoveDownButton").Pressed += OnMoveDownPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/DamageButton").Pressed += OnDamageButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/AttackButton").Pressed += OnAttackButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/RerollButton").Pressed += OnRerollButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/EndCombatButton").Pressed += OnEndCombatPressed;
            _mapController.TokenAdded += token =>
            {
                UpdateAssetList();
                if (_combatController.InCombat)
                {
                    _combatController.AddTokenToOrder(token, true);
                    UpdateInitiativePanel();
                }
            };
            _mapController.TokenRemoved += token =>
            {
                UpdateAssetList();
                if (_combatController.InCombat)
                {
                    _combatController.RemoveTokenFromOrder(token.Id);
                    UpdateInitiativePanel();
                }
            };
            _chatController.SystemMessage("Bem-vindo ao Tormenta VTT. Use /roll para rolar dados.");

            LoadCampaign(_currentCampaign);
            UpdateAssetList();
        }

        private void OnNewCampaignPressed()
        {
            _currentCampaign = Campaign.CreateDefault();
            LoadCampaign(_currentCampaign);
            UpdateAssetList();
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
            token.Sheet.Name = "NPC";
            token.Position = _mapController.GetViewportCenterMapPosition();
            _currentCampaign.Tokens.Add(token);
            _mapController.AddToken(token);
            UpdateAssetList();
            _chatController.SystemMessage($"Token '{token.Name}' criado.");
        }

        private void OnImportTokenPressed()
        {
            GetNode<FileDialog>("TokenFileDialog").PopupCenteredRatio();
        }

        private void OnRollInitiativePressed()
        {
            // Roll initiative for selected token only
            var selected = _mapController.SelectedToken;
            if (selected is null)
            {
                _chatController.SystemMessage("Selecione um token para rolar iniciativa.");
                return;
            }

            var roll = new Random().Next(1, 21) + selected.Sheet.Initiative;
            _chatController.AddSystemMessage($"{selected.Name} rolou iniciativa: {roll}");
        }

        private void OnStartCombatPressed()
        {
            if (_combatController.InCombat)
            {
                _combatController.EndCombat();
                UpdateInitiativePanel();
                return;
            }

            // Start combat and roll initiative for all tokens
            _combatController.StartCombat(_currentCampaign.Tokens, true);
            var order = _combatController.GetOrder();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Ordem de iniciativa:");
            foreach (var entry in order)
            {
                sb.AppendLine($"{entry.Token.Name}: {entry.InitiativeRoll}");
            }
            _chatController.AddSystemMessage(sb.ToString());
            UpdateInitiativePanel();
        }

        private void OnNextTurnPressed()
        {
            _combatController.AdvanceTurn();
            UpdateInitiativePanel();
        }

        private void OnPrevTurnPressed()
        {
            _combatController.RetreatTurn();
            UpdateInitiativePanel();
        }

        private void UpdateInitiativePanel()
        {
            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList");
            list.Clear();

            var status = GetNode<Label>("SidebarPanel/SidebarVBox/InitiativeVBox/CombatStatusLabel");
            if (!_combatController.InCombat)
            {
                status.Text = "Combate inativo";
                return;
            }

            status.Text = $"Turno atual: {_combatController.Current?.Name ?? "Nenhum"}";

            var order = _combatController.GetOrder();
            for (int i = 0; i < order.Count; i++)
            {
                var entry = order[i];
                var itemIndex = list.AddItem($"{i + 1}. {entry.Token.Name} ({entry.InitiativeRoll})");
                list.SetItemMetadata(itemIndex, entry.Token.Id);
                if (_combatController.Current != null && _combatController.Current.Id == entry.Token.Id)
                {
                    list.SetItemCustomBgColor(itemIndex, new Color(1.0f, 0.9f, 0.4f));
                }
                else
                {
                    list.SetItemCustomBgColor(itemIndex, Colors.Transparent);
                }
            }
        }

        private void OnInitiativeSelected(long index)
        {
            // No action needed for now, just maintain selection state.
        }

        private void OnMoveUpPressed()
        {
            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList");
            var selected = list.GetSelectedItems();
            if (selected.Length == 0)
                return;

            var index = selected[0];
            if (index <= 0)
                return;

            _combatController.ReorderTurn(index, index - 1);
            UpdateInitiativePanel();
            list.Select(index - 1);
        }

        private void OnMoveDownPressed()
        {
            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList");
            var selected = list.GetSelectedItems();
            if (selected.Length == 0)
                return;

            var index = selected[0];
            if (index < 0 || index >= list.ItemCount - 1)
                return;

            _combatController.ReorderTurn(index, index + 1);
            UpdateInitiativePanel();
            list.Select(index + 1);
        }

        private void OnDamageButtonPressed()
        {
            var tokenId = GetSelectedInitiativeTokenId();
            if (string.IsNullOrEmpty(tokenId))
                return;

            var amount = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/DamageInput").Value;
            OnApplyDamage(tokenId, amount);
        }

        private void OnAttackButtonPressed()
        {
            var attacker = _combatController.Current;
            if (attacker == null)
            {
                _chatController.SystemMessage("Nenhum atacante ativo.");
                return;
            }

            var targetId = GetSelectedInitiativeTokenId();
            if (string.IsNullOrEmpty(targetId) || targetId == attacker.Id)
            {
                var nextTarget = _combatController.GetOrder().FirstOrDefault(x => x.Token.Id != attacker.Id).Token;
                if (nextTarget == null)
                {
                    _chatController.SystemMessage("Nenhum alvo disponível.");
                    return;
                }
                targetId = nextTarget.Id;
            }

            var target = _currentCampaign.Tokens.Find(t => t.Id == targetId);
            if (target == null)
                return;

            var attackRoll = new Random().Next(1, 21) + attacker.Sheet.Initiative;
            var amount = Math.Max(1, (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/DamageInput").Value);
            if (attackRoll >= target.Sheet.Defense)
            {
                _chatController.AddSystemMessage($"{attacker.Name} ataca {target.Name} e acerta com {attackRoll} contra Defesa {target.Sheet.Defense}.");
                OnApplyDamage(targetId, amount);
            }
            else
            {
                _chatController.AddSystemMessage($"{attacker.Name} ataca {target.Name} e erra com {attackRoll} contra Defesa {target.Sheet.Defense}.");
            }

            _combatController.AdvanceTurn();
            UpdateInitiativePanel();
        }

        private void OnRerollButtonPressed()
        {
            var tokenId = GetSelectedInitiativeTokenId();
            if (string.IsNullOrEmpty(tokenId))
                return;
            _combatController.RerollToken(tokenId);
            _chatController.SystemMessage("Reroll de iniciativa aplicado.");
            UpdateInitiativePanel();
        }

        private void OnEndCombatPressed()
        {
            _combatController.EndCombat();
            UpdateInitiativePanel();
        }

        private string GetSelectedInitiativeTokenId()
        {
            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList");
            var selected = list.GetSelectedItems();
            if (selected.Length == 0)
                return string.Empty;

            return list.GetItemMetadata(selected[0]).ToString();
        }

        private void OnRerollInitiative(string tokenId)
        {
            _combatController.RerollToken(tokenId);
            _chatController.SystemMessage("Reroll de iniciativa aplicado.");
            UpdateInitiativePanel();
        }

        private void OnApplyDamage(string tokenId, int amount)
        {
            var token = _currentCampaign.Tokens.Find(t => t.Id == tokenId);
            if (token == null)
                return;

            token.Sheet.HP -= amount;
            if (token.Sheet.HP <= 0)
            {
                _chatController.SystemMessage($"{token.Name} sofreu {amount} de dano e morreu.");
                _currentCampaign.Tokens.Remove(token);
                _mapController.RemoveToken(token);
                _combatController.RemoveTokenFromOrder(token.Id);
                UpdateAssetList();
                UpdateSelectionPanel(null);
            }
            else
            {
                _chatController.SystemMessage($"{token.Name} sofreu {amount} de dano (PV restantes: {token.Sheet.HP}).");
                UpdateSelectionPanel(_mapController.SelectedToken);
            }

            UpdateInitiativePanel();
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

        private void OnRemoveTokenPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Selecione um token para remover.");
                return;
            }

            _currentCampaign.Tokens.Remove(token);
            _mapController.RemoveToken(token);
            UpdateAssetList();
            UpdateSelectionPanel(null);
            _chatController.SystemMessage($"Token '{token.Name}' removido.");
        }

        private void OnMapFileSelected(string path)
        {
            _currentCampaign.MapImagePath = path;
            _mapController.LoadMap(path);
            UpdateAssetList();
            _chatController.SystemMessage($"Mapa importado: {System.IO.Path.GetFileName(path)}");
        }

        private void OnTokenFileSelected(string path)
        {
            var token = TokenData.Create("Token Importado", path);
            token.Position = _mapController.GetViewportCenterMapPosition();
            _currentCampaign.Tokens.Add(token);
            _mapController.AddToken(token);
            UpdateAssetList();
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
            UpdateAssetList();
            _chatController.SystemMessage($"Campanha carregada: {_currentCampaign.Name}");
        }

        private void OnCampaignSaveSelected(string path)
        {
            var actualPath = path.EndsWith(".json") ? path : path + ".json";
            _currentCampaign.Name = GetNode<LineEdit>("Toolbar/TopButtons/CampaignName")?.Text ?? _currentCampaign.Name;
            _currentCampaign.Zoom = _mapController.CurrentZoom;
            _currentCampaign.GridEnabled = _mapController.IsGridEnabled;
            _currentCampaign.CombatActive = _combatController.InCombat;
            _currentCampaign.CombatOrder = _combatController.GetOrderIds();
            _currentCampaign.CombatOrderRolls = _combatController.GetOrderRolls();
            _currentCampaign.CombatCurrentIndex = _combatController.GetCurrentIndex();

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

            var assetList = GetNode<ItemList>("AssetsPanel/AssetsVBox/AssetList");
            // Clear custom colors and highlight the selected token in the asset list
            for (int i = 0; i < assetList.ItemCount; i++)
            {
                var meta = assetList.GetItemMetadata(i).ToString();
                if (token != null && meta == token.Id)
                {
                    assetList.SetItemCustomBgColor(i, new Color(1.0f, 0.9f, 0.4f));
                }
                else
                {
                    assetList.SetItemCustomBgColor(i, Colors.Transparent);
                }
            }
        }

        private void UpdateAssetList()
        {
            var assetList = GetNode<ItemList>("AssetsPanel/AssetsVBox/AssetList");
            assetList.Clear();

            foreach (var token in _currentCampaign.Tokens)
            {
                var itemIndex = assetList.AddItem(token.Name);
                assetList.SetItemMetadata(itemIndex, token.Id);
            }

            // Restore highlight for currently selected token, if any
            if (_mapController.SelectedToken != null)
            {
                OnSelectedTokenChanged(_mapController.SelectedToken);
            }
        }

        private void OnAssetSelected(long index)
        {
            var assetList = GetNode<ItemList>("AssetsPanel/AssetsVBox/AssetList");
            if (index < 0 || index >= assetList.ItemCount)
                return;

            var tokenId = assetList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(tokenId))
                return;

            var token = _currentCampaign.Tokens.Find(t => t.Id == tokenId);
            if (token != null)
            {
                _mapController.SelectToken(token);
            }
        }

        private void OnAssetActivated(long index)
        {
            var assetList = GetNode<ItemList>("AssetsPanel/AssetsVBox/AssetList");
            if (index < 0 || index >= assetList.ItemCount)
                return;

            var tokenId = assetList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(tokenId))
                return;

            var template = _currentCampaign.Tokens.Find(t => t.Id == tokenId);
            if (template == null)
                return;

            // Clone template into a new token instance
            var newToken = TokenData.Create(template.Name, template.ImagePath);
            newToken.Sheet = CharacterSheet.FromDictionary(template.Sheet.ToDictionary());
            newToken.Position = _mapController.GetViewportCenterMapPosition();

            _currentCampaign.Tokens.Add(newToken);
            _mapController.AddToken(newToken);
            UpdateAssetList();
            _chatController.SystemMessage($"Token '{newToken.Name}' spawnado a partir do asset.");
        }

        private void LoadCampaign(Campaign campaign)
        {
            _currentCampaign = campaign;
            _mapController.LoadCampaign(campaign);
            GetNode<LineEdit>("Toolbar/TopButtons/CampaignName").Text = campaign.Name;
            UpdateAssetList();
            if (campaign.CombatActive)
            {
                _combatController.LoadCombatState(
                    _currentCampaign.Tokens,
                    campaign.CombatOrder,
                    campaign.CombatOrderRolls,
                    campaign.CombatCurrentIndex,
                    campaign.CombatActive
                );
            }
            else
            {
                _combatController.EndCombat();
            }
            UpdateInitiativePanel();
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
