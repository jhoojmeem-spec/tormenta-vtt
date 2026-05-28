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
        private RuleEngine _ruleEngine = new();
        private CombatController _combatController = new();

        private readonly (string NodeName, string AttributeName)[] _attributeInputs = new[]
        {
            ("ForcaInput", "Força"),
            ("DestrezaInput", "Destreza"),
            ("ConstituicaoInput", "Constituição"),
            ("InteligenciaInput", "Inteligência"),
            ("SabedoriaInput", "Sabedoria"),
            ("CarismaInput", "Carisma")
        };

        private readonly (string NodeName, string SkillName)[] _skillInputs = new[]
        {
            ("AtletismoInput", "Atletismo"),
            ("AcrobaciaInput", "Acrobacia"),
            ("FurtividadeInput", "Furtividade"),
            ("PercepcaoInput", "Percepção"),
            ("IntimidacaoInput", "Intimidação"),
            ("LidarComAnimaisInput", "Lidar com Animais"),
            ("PersuasaoInput", "Persuasão")
        };

        public override void _Ready()
        {
            _mapController = GetNode<MapController>("MapPanel");
            _chatController = GetNode<ChatController>("ChatPanel");
            _chatController.CommandTriggered += HandleChatCommand;

            GetNode<Button>("Toolbar/TopButtons/NewCampaignButton").Pressed += OnNewCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/LoadCampaignButton").Pressed += OnLoadCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/SaveCampaignButton").Pressed += OnSaveCampaignPressed;
            GetNode<Button>("Toolbar/TopButtons/ImportMapButton").Pressed += OnImportMapPressed;
            GetNode<Button>("Toolbar/TopButtons/ToggleGridButton").Pressed += OnToggleGridPressed;
            GetNode<Button>("Toolbar/TopButtons/SpawnTokenButton").Pressed += OnSpawnTokenPressed;
            GetNode<Button>("Toolbar/TopButtons/ImportTokenButton").Pressed += OnImportTokenPressed;
            GetNode<Button>("Toolbar/TopButtons/RollInitButton").Pressed += OnRollInitiativePressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/ApplyStatsButton").Pressed += OnApplyStatsPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/AddConditionButton").Pressed += OnAddConditionPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/RemoveConditionButton").Pressed += OnRemoveConditionPressed;
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
            CreatePercentUi();
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
            var selected = _mapController.SelectedToken;
            if (selected is null)
            {
                _chatController.SystemMessage("Selecione um token para rolar iniciativa.");
                return;
            }

            var result = _ruleEngine.RollInitiative(selected.Sheet);
            _chatController.AddSystemMessage($"{selected.Name} rolou iniciativa: {result.Total} [{result.Breakdown}]");
        }

        private bool HandleChatCommand(string text)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (parts[0].Equals("/test", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var attribute = parts[1];
                var selected = _mapController.SelectedToken;
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para testar um atributo.");
                    return true;
                }

                var result = _ruleEngine.RollAttributeCheck(selected.Sheet, attribute);
                _chatController.AddSystemMessage($"Teste de {attribute} para {selected.Name}: {result.Total} [{result.Breakdown}]");
                return true;
            }

            if (parts[0].Equals("/skill", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var skillName = string.Join(' ', parts[1..]);
                var selected = _mapController.SelectedToken;
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para testar uma perícia.");
                    return true;
                }

                var result = _ruleEngine.RollSkillCheck(selected.Sheet, skillName);
                _chatController.AddSystemMessage($"Teste de perícia {skillName} para {selected.Name}: {result.Total} [{result.Breakdown}]");
                return true;
            }

            if (parts[0].Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                _chatController.SystemMessage("Comandos disponíveis: /attack [alvo] [dano tipo], /damage [alvo] <expressão> [tipo], /damageaoe <raio> <expressão> [tipo], /sheet [token], /tokens, /select <token>, /target <token>, /rename [token] <novo_nome>, /delete [token], /startcombat, /endcombat, /order, /next, /prev, /resist [token] add|remove|list, /vuln [token] add|remove|list, /resistpct [token] add|remove|list, /vulnpct [token] add|remove|list, /condition [token] add|remove|list, /heal [token] <expr>, /init [all], /skill <nome>, /test <atributo>");
                return true;
            }

            if (parts[0].Equals("/tokens", StringComparison.OrdinalIgnoreCase))
            {
                if (_currentCampaign.Tokens.Count == 0)
                {
                    _chatController.SystemMessage("Nenhum token presente.");
                }
                else
                {
                    _chatController.SystemMessage($"Tokens: {string.Join(", ", _currentCampaign.Tokens.Select(t => t.Name))}");
                }
                return true;
            }

            if (parts[0].Equals("/select", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/target", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _chatController.SystemMessage("Uso: /select <nome do token> ou /target <nome do token>");
                    return true;
                }

                var idx = 1;
                var tokenName = ExtractTokenName(parts, ref idx);
                if (string.IsNullOrWhiteSpace(tokenName))
                {
                    _chatController.SystemMessage("Nome de token inválido ou não encontrado.");
                    return true;
                }

                var token = FindTokenByName(tokenName);
                if (token == null)
                {
                    _chatController.SystemMessage($"Token '{tokenName}' não encontrado.");
                    return true;
                }

                _mapController.SelectToken(token);
                UpdateSelectionPanel(token);
                _chatController.SystemMessage($"Token '{token.Name}' selecionado.");
                return true;
            }

            if (parts[0].Equals("/sheet", StringComparison.OrdinalIgnoreCase))
            {
                var idx = 1;
                var token = ResolveTargetToken(parts, ref idx);
                if (token == null)
                {
                    _chatController.SystemMessage("Nenhum token selecionado ou encontrado para mostrar ficha.");
                    return true;
                }

                var details = $"Ficha de {token.Name} / Classe: {token.Sheet.CharacterClass} / Raça: {token.Sheet.Race} / Nível: {token.Sheet.Level}\nPV: {token.Sheet.HP} / PM: {token.Sheet.PM} / Defesa: {token.Sheet.Defense} / Iniciativa: {token.Sheet.Initiative}\nAtributos: {string.Join(", ", _attributeInputs.Select(p => $"{p.AttributeName}:{token.Sheet.GetAttributeValue(p.AttributeName)}"))}\nPerícias: {string.Join(", ", _skillInputs.Select(p => $"{p.SkillName}:{token.Sheet.GetSkillBonus(p.SkillName)}"))}\nCondições: {token.Sheet.GetConditionSummary()}\nResistências: {token.Sheet.GetResistanceSummary()}\nResistências%: {token.Sheet.GetResistancePercentSummary()}\nVulnerabilidades: {token.Sheet.GetVulnerabilitySummary()}\nVulnerabilidades%: {token.Sheet.GetVulnerabilityPercentSummary()}";
                _chatController.SystemMessage(details);
                return true;
            }

            if (parts[0].Equals("/rename", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var token = ResolveTargetToken(parts, ref idx);
                if (token == null)
                {
                    _chatController.SystemMessage("Nenhum token selecionado ou encontrado para renomear.");
                    return true;
                }

                if (idx >= parts.Length)
                {
                    _chatController.SystemMessage("Uso: /rename [token] <novo_nome>");
                    return true;
                }

                var newName = string.Join(' ', parts[idx..]);
                var oldName = token.Name;
                token.Name = newName;
                UpdateAssetList();
                UpdateSelectionPanel(token);
                _chatController.SystemMessage($"Token '{oldName}' renomeado para '{newName}'.");
                return true;
            }

            if (parts[0].Equals("/delete", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var token = ResolveTargetToken(parts, ref idx);
                if (token == null)
                {
                    _chatController.SystemMessage("Nenhum token selecionado ou encontrado para excluir.");
                    return true;
                }

                DeleteToken(token);
                _chatController.SystemMessage($"Token '{token.Name}' excluído.");
                return true;
            }

            if (parts[0].Equals("/init", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length > 1 && parts[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var token in _currentCampaign.Tokens)
                    {
                        var initiativeResult = _ruleEngine.RollInitiative(token.Sheet);
                        _chatController.AddSystemMessage($"{token.Name} rolou iniciativa: {initiativeResult.Total} [{initiativeResult.Breakdown}]");
                    }
                    return true;
                }

                var selected = _mapController.SelectedToken;
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para rolar iniciativa.");
                    return true;
                }

                var result = _ruleEngine.RollInitiative(selected.Sheet);
                _chatController.AddSystemMessage($"{selected.Name} rolou iniciativa: {result.Total} [{result.Breakdown}]");
                return true;
            }

            if (parts[0].Equals("/startcombat", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/begincombat", StringComparison.OrdinalIgnoreCase))
            {
                if (_combatController.InCombat)
                {
                    _chatController.SystemMessage("O combate já está em andamento.");
                    return true;
                }

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
                return true;
            }

            if (parts[0].Equals("/endcombat", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/stopcombat", StringComparison.OrdinalIgnoreCase))
            {
                if (!_combatController.InCombat)
                {
                    _chatController.SystemMessage("Não há combate em andamento.");
                    return true;
                }

                _combatController.EndCombat();
                UpdateInitiativePanel();
                _chatController.SystemMessage("Combate encerrado.");
                return true;
            }

            if (parts[0].Equals("/order", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/initiativeorder", StringComparison.OrdinalIgnoreCase))
            {
                if (!_combatController.InCombat)
                {
                    _chatController.SystemMessage("Nenhum combate ativo.");
                    return true;
                }

                var order = _combatController.GetOrder();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Ordem de iniciativa:");
                for (var i = 0; i < order.Count; i++)
                {
                    var entry = order[i];
                    var currentMarker = _combatController.Current != null && _combatController.Current.Id == entry.Token.Id ? " <== vez atual" : string.Empty;
                    sb.AppendLine($"{i + 1}. {entry.Token.Name} ({entry.InitiativeRoll}){currentMarker}");
                }
                _chatController.AddSystemMessage(sb.ToString());
                return true;
            }

            if (parts[0].Equals("/next", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/pass", StringComparison.OrdinalIgnoreCase))
            {
                if (!_combatController.InCombat)
                {
                    _chatController.SystemMessage("Nenhum combate ativo.");
                    return true;
                }

                TickCurrentTokenConditions();
                _combatController.AdvanceTurn();
                UpdateInitiativePanel();
                _chatController.SystemMessage($"Vez de: {_combatController.Current?.Name ?? "Nenhum"}");
                return true;
            }

            if (parts[0].Equals("/prev", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/back", StringComparison.OrdinalIgnoreCase))
            {
                if (!_combatController.InCombat)
                {
                    _chatController.SystemMessage("Nenhum combate ativo.");
                    return true;
                }

                TickCurrentTokenConditions();
                _combatController.RetreatTurn();
                UpdateInitiativePanel();
                _chatController.SystemMessage($"Vez de: {_combatController.Current?.Name ?? "Nenhum"}");
                return true;
            }

            if (parts[0].Equals("/heal", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var startIdx = 1;
                var target = ResolveTargetToken(parts, ref startIdx);
                if (target == null)
                {
                    _chatController.SystemMessage("Selecione um token para curar.");
                    return true;
                }

                var expression = string.Join(' ', parts[startIdx..]);
                var healResult = _diceParser.Evaluate(expression);
                target.Sheet.HP += healResult.Total;
                _chatController.AddSystemMessage($"{target.Name} recupera {healResult.Total} PV ({healResult.Breakdown}). PV atuais: {target.Sheet.HP}.");
                UpdateSelectionPanel(target);
                return true;
            }

            if (parts[0].Equals("/condition", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var selected = ResolveTargetToken(parts, ref idx);
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para gerenciar condições.");
                    return true;
                }

                if (idx >= parts.Length)
                {
                    _chatController.SystemMessage("Uso: /condition [token] add <nome> [turnos] | /condition [token] remove <nome> | /condition [token] list");
                    return true;
                }

                var action = parts[idx].ToLowerInvariant();
                if (action == "add" && parts.Length > idx + 1)
                {
                    var rawConditionText = string.Join(' ', parts[(idx + 1)..]);
                    var (conditionName, duration) = ParseConditionText(rawConditionText);
                    var alreadyPresent = selected.Sheet.HasCondition(conditionName);
                    selected.Sheet.AddCondition(conditionName, duration);

                    if (alreadyPresent)
                    {
                        _chatController.SystemMessage($"Condição '{conditionName}' atualizada em {selected.Name}.");
                    }
                    else
                    {
                        var durationText = duration > 0 ? $" por {duration} turnos" : string.Empty;
                        _chatController.SystemMessage($"Condição '{conditionName}' adicionada a {selected.Name}{durationText}.");
                    }

                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (action == "remove" && parts.Length > idx + 1)
                {
                    var conditionName = string.Join(' ', parts[(idx + 1)..]);
                    selected.Sheet.RemoveCondition(conditionName);
                    _chatController.SystemMessage($"Condição '{conditionName}' removida de {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (action == "list")
                {
                    _chatController.SystemMessage($"Condições de {selected.Name}: {selected.Sheet.GetConditionSummary()}");
                    return true;
                }

                _chatController.SystemMessage("Uso: /condition [token] add <nome> [turnos] | /condition [token] remove <nome> | /condition [token] list");
                return true;
            }

            if (parts[0].Equals("/resist", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var selected = ResolveTargetToken(parts, ref idx);
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para gerenciar resistências.");
                    return true;
                }

                if (idx >= parts.Length)
                {
                    _chatController.SystemMessage("Uso: /resist [token] add <tipo> <valor> | /resist [token] remove <tipo> | /resist [token] list");
                    return true;
                }

                var subAction = parts[idx].ToLowerInvariant();
                if (subAction == "add" && parts.Length > idx + 2 && int.TryParse(parts[idx + 2], out var amount))
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetResistance(damageType, amount);
                    _chatController.SystemMessage($"Resistência {damageType}:{amount} adicionada a {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "remove" && parts.Length > idx + 1)
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetResistance(damageType, 0);
                    _chatController.SystemMessage($"Resistência {damageType} removida de {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "list")
                {
                    _chatController.SystemMessage($"Resistências de {selected.Name}: {selected.Sheet.GetResistanceSummary()}");
                    return true;
                }

                _chatController.SystemMessage("Uso: /resist [token] add <tipo> <valor> | /resist [token] remove <tipo> | /resist [token] list");
                return true;
            }

            if (parts[0].Equals("/resistpct", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var selected = ResolveTargetToken(parts, ref idx);
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para gerenciar resistências percentuais.");
                    return true;
                }

                if (idx >= parts.Length)
                {
                    _chatController.SystemMessage("Uso: /resistpct [token] add <tipo> <percent> | /resistpct [token] remove <tipo> | /resistpct [token] list");
                    return true;
                }

                var subAction = parts[idx].ToLowerInvariant();
                if (subAction == "add" && parts.Length > idx + 2 && int.TryParse(parts[idx + 2], out var pct))
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetResistancePercent(damageType, pct);
                    _chatController.SystemMessage($"Resistência% {damageType}:{pct}% adicionada a {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "remove" && parts.Length > idx + 1)
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetResistancePercent(damageType, 0);
                    _chatController.SystemMessage($"Resistência% {damageType} removida de {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "list")
                {
                    _chatController.SystemMessage($"Resistências% de {selected.Name}: {selected.Sheet.GetResistancePercentSummary()}");
                    return true;
                }

                _chatController.SystemMessage("Uso: /resistpct [token] add <tipo> <percent> | /resistpct [token] remove <tipo> | /resistpct [token] list");
                return true;
            }

            if (parts[0].Equals("/vulnpct", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var idx = 1;
                var selected = ResolveTargetToken(parts, ref idx);
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para gerenciar vulnerabilidades percentuais.");
                    return true;
                }

                if (idx >= parts.Length)
                {
                    _chatController.SystemMessage("Uso: /vulnpct [token] add <tipo> <percent> | /vulnpct [token] remove <tipo> | /vulnpct [token] list");
                    return true;
                }

                var subAction = parts[idx].ToLowerInvariant();
                if (subAction == "add" && parts.Length > idx + 2 && int.TryParse(parts[idx + 2], out var pct))
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetVulnerabilityPercent(damageType, pct);
                    _chatController.SystemMessage($"Vulnerabilidade% {damageType}:{pct}% adicionada a {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "remove" && parts.Length > idx + 1)
                {
                    var damageType = parts[idx + 1];
                    selected.Sheet.SetVulnerabilityPercent(damageType, 0);
                    _chatController.SystemMessage($"Vulnerabilidade% {damageType} removida de {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "list")
                {
                    _chatController.SystemMessage($"Vulnerabilidades% de {selected.Name}: {selected.Sheet.GetVulnerabilityPercentSummary()}");
                    return true;
                }

                _chatController.SystemMessage("Uso: /vulnpct [token] add <tipo> <percent> | /vulnpct [token] remove <tipo> | /vulnpct [token] list");
                return true;
            }

            if (parts[0].Equals("/vuln", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var selected = _mapController.SelectedToken;
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para gerenciar vulnerabilidades.");
                    return true;
                }

                var subAction = parts[1].ToLowerInvariant();
                if (subAction == "add" && parts.Length > 3 && int.TryParse(parts[3], out var amount))
                {
                    var damageType = parts[2];
                    selected.Sheet.SetVulnerability(damageType, amount);
                    _chatController.SystemMessage($"Vulnerabilidade {damageType}:{amount} adicionada a {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "remove" && parts.Length > 2)
                {
                    var damageType = parts[2];
                    selected.Sheet.SetVulnerability(damageType, 0);
                    _chatController.SystemMessage($"Vulnerabilidade {damageType} removida de {selected.Name}.");
                    UpdateSelectionPanel(selected);
                    return true;
                }

                if (subAction == "list")
                {
                    _chatController.SystemMessage($"Vulnerabilidades de {selected.Name}: {selected.Sheet.GetVulnerabilitySummary()}");
                    return true;
                }

                _chatController.SystemMessage("Uso: /vuln add <tipo> <valor> | /vuln remove <tipo> | /vuln list");
                return true;
            }

            if (parts[0].Equals("/damage", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                // /damage [targetName] <expression> [type]
                var attacker = _combatController.Current ?? _mapController.SelectedToken;
                TokenData? target = null;
                var startIdx = 1;

                // If first arg matches a token name, treat as explicit target
                var maybeTarget = _currentCampaign.Tokens.Find(t => t.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                if (maybeTarget != null)
                {
                    target = maybeTarget;
                    startIdx = 2;
                }

                if (target == null)
                {
                    target = _mapController.SelectedToken != attacker ? _mapController.SelectedToken : _currentCampaign.Tokens.Find(t => t.Id != attacker?.Id);
                }

                if (target == null)
                {
                    _chatController.SystemMessage("Nenhum alvo disponível para aplicar dano.");
                    return true;
                }

                var (expr, dtype) = ParseAttackDamage(parts, startIdx);
                var dmgRoll = _diceParser.Evaluate(expr);
                var adjusted = target.Sheet.GetDamageAfterTypeModifiers(dmgRoll.Total, dtype);

                OnApplyDamage(target.Id, adjusted);
                var typeText = string.IsNullOrEmpty(dtype) ? string.Empty : $" de {dtype}";
                _chatController.AddSystemMessage($"{target.Name} sofre {adjusted} dano ({dmgRoll.Total} base{typeText}). PV atuais: {Math.Max(0, target.Sheet.HP)}.");
                UpdateSelectionPanel(_mapController.SelectedToken);
                return true;
            }

            if (parts[0].Equals("/damageaoe", StringComparison.OrdinalIgnoreCase) && parts.Length > 2)
            {
                // /damageaoe <radius> <expression> [type]
                var centerToken = _mapController.SelectedToken;
                if (centerToken == null)
                {
                    _chatController.SystemMessage("Selecione um token como centro da área.");
                    return true;
                }

                if (!int.TryParse(parts[1], out var radius))
                {
                    _chatController.SystemMessage("Uso: /damageaoe <radius> <expression> [type] (radius em pixels)");
                    return true;
                }

                var (expr, dtype) = ParseAttackDamage(parts, 2);
                var roll = _diceParser.Evaluate(expr);
                var affected = _currentCampaign.Tokens.Where(t => t.Position.DistanceTo(centerToken.Position) <= radius).ToList();
                if (affected.Count == 0)
                {
                    _chatController.SystemMessage("Nenhum token na área.");
                    return true;
                }

                foreach (var tgt in affected)
                {
                    var adjusted = tgt.Sheet.GetDamageAfterTypeModifiers(roll.Total, dtype);
                    OnApplyDamage(tgt.Id, adjusted);
                    _chatController.AddSystemMessage($"{tgt.Name} sofre {adjusted} dano ({roll.Total} base{(string.IsNullOrEmpty(dtype) ? string.Empty : $" de {dtype}")}). PV atuais: {Math.Max(0, tgt.Sheet.HP)}.");
                }

                UpdateSelectionPanel(_mapController.SelectedToken);
                return true;
            }

            if (parts[0].Equals("/attack", StringComparison.OrdinalIgnoreCase))
            {
                var attacker = _combatController.Current ?? _mapController.SelectedToken;
                if (attacker == null)
                {
                    _chatController.SystemMessage("Selecione um token ou inicie o combate para atacar.");
                    return true;
                }

                TokenData? target = null;
                var damageExpression = "1d6+0";
                if (parts.Length > 1)
                {
                    var candidate = parts[1];
                    var maybeTarget = _currentCampaign.Tokens.Find(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                    if (maybeTarget != null)
                    {
                        target = maybeTarget;
                        if (parts.Length > 2)
                        {
                            damageExpression = string.Join(' ', parts[2..]);
                        }
                    }
                    else
                    {
                        damageExpression = string.Join(' ', parts[1..]);
                    }
                }

                if (target == null)
                {
                    target = _mapController.SelectedToken != attacker ? _mapController.SelectedToken : _currentCampaign.Tokens.Find(t => t.Id != attacker.Id);
                }

                if (target == null)
                {
                    _chatController.SystemMessage("Nenhum alvo disponível para atacar.");
                    return true;
                }

                var (resolvedExpression, resolvedType) = ParseAttackDamage(parts, 2);
                var targetNonNull = target!;
                var result = _ruleEngine.RollAttack(attacker, targetNonNull, resolvedExpression, resolvedType);
                var hitText = result.Hit ? "acerta" : "erra";
                var specialText = result.IsCritical ? " CRÍTICO!" : result.IsFumble ? " FUMBLE!" : string.Empty;
                var typeText = string.IsNullOrEmpty(resolvedType) ? string.Empty : $" de {resolvedType}";
                _chatController.AddSystemMessage($"{attacker.Name} ataca {targetNonNull.Name}{typeText} e {hitText} com {result.RollResult.Total} [{result.RollResult.Breakdown}] contra Defesa {targetNonNull.Sheet.GetEffectiveDefense()}.{specialText}");
                if (result.Hit)
                {
                    OnApplyDamage(targetNonNull.Id, result.Damage);
                }

                return true;
            }

            return false;
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

            var amount = Math.Max(1, (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/DamageInput").Value);
            var result = _ruleEngine.RollAttack(attacker, target!, amount);
            var hitText = result.Hit ? "acerta" : "erra";
            var specialText = result.IsCritical ? " CRÍTICO!" : result.IsFumble ? " FUMBLE!" : string.Empty;
            _chatController.AddSystemMessage($"{attacker.Name} ataca {target.Name} e {hitText} com {result.RollResult.Total} [{result.RollResult.Breakdown}] contra Defesa {target.Sheet.GetEffectiveDefense()}.{specialText}");
            if (result.Hit)
            {
                OnApplyDamage(targetId, result.Damage);
            }

            TickCurrentTokenConditions();
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

        private static (string ConditionName, int Duration) ParseConditionText(string rawConditionText)
        {
            if (string.IsNullOrWhiteSpace(rawConditionText))
                return (string.Empty, -1);

            var parts = rawConditionText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return (rawConditionText.Trim(), -1);

            if (int.TryParse(parts[^1], out var duration) && duration > 0)
            {
                var name = string.Join(' ', parts[..^1]).Trim();
                return (name, duration);
            }

            return (rawConditionText.Trim(), -1);
        }

        private TokenData? ResolveTargetToken(string[] parts, ref int index)
        {
            var originalIndex = index;
            var tokenName = ExtractTokenName(parts, ref index);
            if (!string.IsNullOrWhiteSpace(tokenName))
            {
                var maybeTarget = FindTokenByName(tokenName);
                if (maybeTarget != null)
                    return maybeTarget;

                index = originalIndex;
            }

            return _mapController.SelectedToken;
        }

        private string ExtractTokenName(string[] parts, ref int index)
        {
            if (index >= parts.Length)
                return string.Empty;

            if (parts[index].StartsWith('"'))
            {
                var collected = new List<string> { parts[index] };
                var endIndex = index;
                if (parts[index].EndsWith('"') && parts[index].Length > 1)
                {
                    index++;
                    return parts[index - 1].Trim('"');
                }

                for (var i = index + 1; i < parts.Length; i++)
                {
                    collected.Add(parts[i]);
                    if (parts[i].EndsWith('"'))
                    {
                        endIndex = i;
                        break;
                    }
                }

                if (endIndex > index)
                {
                    var joined = string.Join(' ', collected).Trim();
                    joined = joined.Trim('"');
                    index = endIndex + 1;
                    return joined;
                }
            }

            for (var length = parts.Length - index; length > 0; length--)
            {
                var candidate = string.Join(' ', parts[index..(index + length)]);
                if (FindTokenByName(candidate) != null)
                {
                    index += length;
                    return candidate;
                }
            }

            return string.Empty;
        }

        private TokenData? FindTokenByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _currentCampaign.Tokens.Find(t => t.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static (string DamageExpression, string DamageType) ParseAttackDamage(string[] parts, int startIndex)
        {
            if (parts == null || parts.Length <= startIndex)
                return ("1d6+0", string.Empty);

            var damageParts = parts[startIndex..];
            if (damageParts.Length == 0)
                return ("1d6+0", string.Empty);

            var lastPart = damageParts[^1];
            var isType = true;
            foreach (var c in lastPart)
            {
                if (!char.IsLetter(c) && c != '_')
                {
                    isType = false;
                    break;
                }
            }

            if (isType && damageParts.Length > 1)
            {
                var expression = string.Join(' ', damageParts[..^1]);
                return (expression, lastPart);
            }

            return (string.Join(' ', damageParts), string.Empty);
        }
        private void TickCurrentTokenConditions()
        {
            var current = _combatController.Current;
            if (current == null)
                return;

            var expired = current.Sheet.TickConditionDurations();
            if (expired.Count == 0)
                return;

            foreach (var condition in expired)
            {
                _chatController.SystemMessage($"{current.Name} não sofre mais de {condition}.");
            }

            if (_mapController.SelectedToken?.Id == current.Id)
            {
                UpdateSelectionPanel(current);
                UpdateConditionList(current);
            }
        }

        private void LoadSheetInputs(TokenData token)
        {
            foreach (var (nodeName, attributeName) in _attributeInputs)
            {
                GetNode<SpinBox>($"SidebarPanel/SidebarVBox/AttributeGrid/{nodeName}").Value = token.Sheet.GetAttributeValue(attributeName);
            }

            foreach (var (nodeName, skillName) in _skillInputs)
            {
                GetNode<SpinBox>($"SidebarPanel/SidebarVBox/SkillGrid/{nodeName}").Value = token.Sheet.GetSkillBonus(skillName);
            }
        }

        private void StoreSheetInputs(TokenData token)
        {
            foreach (var (nodeName, attributeName) in _attributeInputs)
            {
                token.Sheet.Attributes[attributeName] = (int)GetNode<SpinBox>($"SidebarPanel/SidebarVBox/AttributeGrid/{nodeName}").Value;
            }

            foreach (var (nodeName, skillName) in _skillInputs)
            {
                token.Sheet.Skills[skillName] = (int)GetNode<SpinBox>($"SidebarPanel/SidebarVBox/SkillGrid/{nodeName}").Value;
            }
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

            token.Sheet.CharacterClass = GetNode<LineEdit>("SidebarPanel/SidebarVBox/ClassInput").Text;
            token.Sheet.Race = GetNode<LineEdit>("SidebarPanel/SidebarVBox/RaceInput").Text;
            token.Sheet.Level = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/LevelInput").Value;
            token.Sheet.HP = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/HPInput").Value;
            token.Sheet.PM = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/PMInput").Value;
            token.Sheet.Defense = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/DefenseInput").Value;
            token.Sheet.Initiative = (int)GetNode<SpinBox>("SidebarPanel/SidebarVBox/StatsGrid/InitiativeInput").Value;
            StoreSheetInputs(token);
            UpdateSelectionPanel(token);
            _chatController.SystemMessage($"Ficha de {token.Name} atualizada.");
        }

        private void OnRemoveTokenPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Selecione um token para remover.");
                return;
            }

            DeleteToken(token);
            _chatController.SystemMessage($"Token '{token.Name}' removido.");
        }

        private void DeleteToken(TokenData token)
        {
            _currentCampaign.Tokens.Remove(token);
            _mapController.RemoveToken(token);
            UpdateAssetList();
            UpdateSelectionPanel(null);
        }

        private void OnAddConditionPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Selecione um token para adicionar condição.");
                return;
            }

            var conditionText = GetNode<LineEdit>("SidebarPanel/SidebarVBox/ConditionRow/ConditionInput").Text.Trim();
            if (string.IsNullOrEmpty(conditionText))
            {
                _chatController.SystemMessage("Digite uma condição para adicionar.");
                return;
            }

            var (conditionName, duration) = ParseConditionText(conditionText);
            var alreadyPresent = token.Sheet.HasCondition(conditionName);
            token.Sheet.AddCondition(conditionName, duration);

            if (alreadyPresent)
            {
                _chatController.SystemMessage($"Condição '{conditionName}' atualizada em {token.Name}.");
            }
            else
            {
                var durationText = duration > 0 ? $" por {duration} turnos" : string.Empty;
                _chatController.SystemMessage($"Condição '{conditionName}' adicionada a {token.Name}{durationText}.");
            }

            GetNode<LineEdit>("SidebarPanel/SidebarVBox/ConditionRow/ConditionInput").Text = string.Empty;
            UpdateSelectionPanel(token);
        }

        private void OnRemoveConditionPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Selecione um token para remover condição.");
                return;
            }

            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/ConditionsList");
            var selected = list.GetSelectedItems();
            if (selected.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma condição para remover.");
                return;
            }

            var condition = list.GetItemText((int)selected[0]);
            token.Sheet.RemoveCondition(condition);
            _chatController.SystemMessage($"Condição '{condition}' removida de {token.Name}.");
            UpdateSelectionPanel(token);
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

        private void UpdateConditionList(TokenData? token)
        {
            var list = GetNode<ItemList>("SidebarPanel/SidebarVBox/ConditionsList");
            list.Clear();
            if (token == null)
                return;

            foreach (var condition in token.Sheet.Conditions)
            {
                list.AddItem(condition.ToString());
            }
        }

        private void OnSelectedTokenChanged(TokenData? token)
        {
            UpdateSelectionPanel(token);
            UpdateConditionList(token);

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
            UpdateConditionList(_mapController.SelectedToken);
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

        private void CreatePercentUi()
        {
            var sidebar = GetNode<VBoxContainer>("SidebarPanel/SidebarVBox");

            var resPctLabel = new Label { Text = "Resistências (%)" };
            sidebar.AddChild(resPctLabel);

            var resPctHBox = new HBoxContainer();
            var resPctType = new LineEdit { Name = "ResPctTypeInput", PlaceholderText = "tipo" };
            var resPctAmount = new SpinBox { Name = "ResPctAmountInput", MinValue = 0, MaxValue = 100, Value = 0 };
            var resPctAdd = new Button { Text = "Add%" };
            var resPctRemove = new Button { Text = "Remove%" };
            var resPctList = new Button { Text = "List%" };
            resPctHBox.AddChild(resPctType);
            resPctHBox.AddChild(resPctAmount);
            resPctHBox.AddChild(resPctAdd);
            resPctHBox.AddChild(resPctRemove);
            resPctHBox.AddChild(resPctList);
            sidebar.AddChild(resPctHBox);

            resPctAdd.Pressed += () => {
                OnUiAddResistancePercent(resPctType.Text, (int)resPctAmount.Value);
            };
            resPctRemove.Pressed += () => { OnUiRemoveResistancePercent(resPctType.Text); };
            resPctList.Pressed += () => { OnUiListResistancePercent(); };

            var vulnPctLabel = new Label { Text = "Vulnerabilidades (%)" };
            sidebar.AddChild(vulnPctLabel);

            var vulnPctHBox = new HBoxContainer();
            var vulnPctType = new LineEdit { Name = "VulnPctTypeInput", PlaceholderText = "tipo" };
            var vulnPctAmount = new SpinBox { Name = "VulnPctAmountInput", MinValue = 0, MaxValue = 100, Value = 0 };
            var vulnPctAdd = new Button { Text = "Add%" };
            var vulnPctRemove = new Button { Text = "Remove%" };
            var vulnPctList = new Button { Text = "List%" };
            vulnPctHBox.AddChild(vulnPctType);
            vulnPctHBox.AddChild(vulnPctAmount);
            vulnPctHBox.AddChild(vulnPctAdd);
            vulnPctHBox.AddChild(vulnPctRemove);
            vulnPctHBox.AddChild(vulnPctList);
            sidebar.AddChild(vulnPctHBox);

            vulnPctAdd.Pressed += () => { OnUiAddVulnerabilityPercent(vulnPctType.Text, (int)vulnPctAmount.Value); };
            vulnPctRemove.Pressed += () => { OnUiRemoveVulnerabilityPercent(vulnPctType.Text); };
            vulnPctList.Pressed += () => { OnUiListVulnerabilityPercent(); };
        }

        private void OnUiAddResistancePercent(string type, int percent)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para adicionar resistência percentual.");
                return;
            }
            selected.Sheet.SetResistancePercent(type, percent);
            _chatController.SystemMessage($"Resistência% {type}:{percent}% adicionada a {selected.Name}.");
            UpdateSelectionPanel(selected);
        }

        private void OnUiRemoveResistancePercent(string type)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para remover resistência percentual.");
                return;
            }
            selected.Sheet.SetResistancePercent(type, 0);
            _chatController.SystemMessage($"Resistência% {type} removida de {selected.Name}.");
            UpdateSelectionPanel(selected);
        }

        private void OnUiListResistancePercent()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para listar resistências percentuais.");
                return;
            }
            _chatController.SystemMessage($"Resistências% de {selected.Name}: {selected.Sheet.GetResistancePercentSummary()}");
        }

        private void OnUiAddVulnerabilityPercent(string type, int percent)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para adicionar vulnerabilidade percentual.");
                return;
            }
            selected.Sheet.SetVulnerabilityPercent(type, percent);
            _chatController.SystemMessage($"Vulnerabilidade% {type}:{percent}% adicionada a {selected.Name}.");
            UpdateSelectionPanel(selected);
        }

        private void OnUiRemoveVulnerabilityPercent(string type)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para remover vulnerabilidade percentual.");
                return;
            }
            selected.Sheet.SetVulnerabilityPercent(type, 0);
            _chatController.SystemMessage($"Vulnerabilidade% {type} removida de {selected.Name}.");
            UpdateSelectionPanel(selected);
        }

        private void OnUiListVulnerabilityPercent()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para listar vulnerabilidades percentuais.");
                return;
            }
            _chatController.SystemMessage($"Vulnerabilidades% de {selected.Name}: {selected.Sheet.GetVulnerabilityPercentSummary()}");
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
                GetNode<LineEdit>("SidebarPanel/SidebarVBox/ClassInput").Text = string.Empty;
                GetNode<LineEdit>("SidebarPanel/SidebarVBox/RaceInput").Text = string.Empty;
                GetNode<SpinBox>("SidebarPanel/SidebarVBox/LevelInput").Value = 1;
                selectedStats.Text = "Selecione um token no mapa.";
                hpInput.Value = 0;
                pmInput.Value = 0;
                defenseInput.Value = 0;
                initiativeInput.Value = 0;
                foreach (var (nodeName, _) in _attributeInputs)
                {
                    GetNode<SpinBox>($"SidebarPanel/SidebarVBox/AttributeGrid/{nodeName}").Value = 10;
                }
                foreach (var (nodeName, _) in _skillInputs)
                {
                    GetNode<SpinBox>($"SidebarPanel/SidebarVBox/SkillGrid/{nodeName}").Value = 0;
                }
                return;
            }

            selectedName.Text = token.Name;
            GetNode<LineEdit>("SidebarPanel/SidebarVBox/ClassInput").Text = token.Sheet.CharacterClass;
            GetNode<LineEdit>("SidebarPanel/SidebarVBox/RaceInput").Text = token.Sheet.Race;
            GetNode<SpinBox>("SidebarPanel/SidebarVBox/LevelInput").Value = token.Sheet.Level;
            LoadSheetInputs(token);
            selectedStats.Text = $"Classe: {token.Sheet.CharacterClass} / Raça: {token.Sheet.Race} / Nível: {token.Sheet.Level}\nPV: {token.Sheet.HP} / PM: {token.Sheet.PM}\nDefesa: {token.Sheet.Defense} / Iniciativa: {token.Sheet.Initiative}\nAtributos: {string.Join(", ", _attributeInputs.Select(p => $"{p.AttributeName}:{token.Sheet.GetAttributeValue(p.AttributeName)}"))}\nPerícias: {string.Join(", ", _skillInputs.Select(p => $"{p.SkillName}:{token.Sheet.GetSkillBonus(p.SkillName)}"))}\nCondições: {token.Sheet.GetConditionSummary()}\nResistências: {token.Sheet.GetResistanceSummary()}\nResistências%: {token.Sheet.GetResistancePercentSummary()}\nVulnerabilidades: {token.Sheet.GetVulnerabilitySummary()}\nVulnerabilidades%: {token.Sheet.GetVulnerabilityPercentSummary()}";
            hpInput.Value = token.Sheet.HP;
            pmInput.Value = token.Sheet.PM;
            defenseInput.Value = token.Sheet.Defense;
            initiativeInput.Value = token.Sheet.Initiative;
        }
    }
}
