using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TormentaVTT.Importers;
using TormentaVTT.Models;
using TormentaVTT.Network;
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
        private ContentService _contentService = new();
        private NetworkService _networkService = new();
        private PdfImportService _pdfImportService = new();
        private TextContentParser _textContentParser = new();
        private DocumentImporter _documentImporter = new();

        // ── Multiplayer / session ─────────────────────────────────────────────
        private SyncService _syncService = null!;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private string _localPlayerName = "Mestre";
        private RoleType _localRole = RoleType.GM;
        private readonly List<PlayerSession> _connectedPlayers = new();
        private bool _isSyncing = false; // guard: don't re-broadcast incoming changes

        // ── Session / network UI ──────────────────────────────────────────────
        private Label? _sessionStatusLabel;
        private Label? _sessionPlayersLabel;
        private LineEdit? _playerNameInput;

        // ── Journal / Handout UI ──────────────────────────────────────────────
        private Window? _journalWindow;
        private ItemList? _journalList;
        private TextEdit? _journalContent;
        private LineEdit? _journalTitle;
        private OptionButton? _journalCategory;
        private CheckButton? _journalVisibleToggle;
        private int _selectedJournalIndex = -1;

        // ── Fog of war UI ─────────────────────────────────────────────────────
        private Button? _fogToggleButton;
        private Button? _fogRevealButton;
        private Button? _fogHideButton;
        private Button? _fogRevealAllButton;
        private Button? _fogHideAllButton;
        private bool _fogToolActive = false;
        private bool _nextSpawnGMOnly = false;  // GM-only flag for next encounter spawn

        // ── Lobby UI ─────────────────────────────────────────────────────────
        private Control? _lobbyOverlay;
        private Label?   _lobbyHostIpLabel;
        private Label?   _lobbyStatusMsg;

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

        private ItemList? _contentClassesList;
        private ItemList? _contentRacesList;
        private ItemList? _contentPowersList;
        private ItemList? _contentSpellsList;
        private ItemList? _contentConditionsList;
        private ItemList? _contentThreatsList;
        private LineEdit? _spellTargetInput;
        private LineEdit? _conditionDurationInput;
        private LineEdit? _threatNameInput;
        private Button? _applySelectedClassButton;
        private Button? _applySelectedRaceButton;
        private Button? _useSelectedPowerButton;
        private Button? _castSelectedSpellButton;
        private Button? _applySelectedConditionButton;
        private Button? _removeSelectedConditionButton;
        private Button? _spawnThreatButton;
        // Sheet editor UI
        private AcceptDialog? _sheetEditorDialog;
        private LineEdit? _sheetEditorName;
        private LineEdit? _sheetEditorClass;
        private LineEdit? _sheetEditorHP;
        private LineEdit? _sheetEditorPM;
        private Dictionary<string, LineEdit> _sheetEditorAttributes = new();
        // Network UI
        private Button? _hostButton;
        private Button? _joinButton;
        private AcceptDialog? _connectDialog;
        private LineEdit? _connectHostInput;
        private LineEdit? _connectPortInput;

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
            GetNode<Button>("SidebarPanel/SidebarVBox/ConditionRow/AddConditionButton").Pressed += OnAddConditionPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/ConditionRow/RemoveConditionButton").Pressed += OnRemoveConditionPressed;
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
            // Add network buttons dynamically
            var topButtons = GetNode<HBoxContainer>("Toolbar/TopButtons");
            _hostButton = new Button { Text = "Hospedar" };
            _joinButton = new Button { Text = "Conectar" };
            topButtons.AddChild(_hostButton);
            topButtons.AddChild(_joinButton);
            _hostButton.Pressed += OnHostPressed;
            _joinButton.Pressed += OnJoinPressed;
            GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList").ItemSelected += OnInitiativeSelected;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeMoveButtons/MoveUpButton").Pressed += OnMoveUpPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeMoveButtons/MoveDownButton").Pressed += OnMoveDownPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/DamageButton").Pressed += OnDamageButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/AttackButton").Pressed += OnAttackButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/RerollButton").Pressed += OnRerollButtonPressed;
            GetNode<Button>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeActions/EndCombatButton").Pressed += OnEndCombatPressed;
            // TokenAdded/TokenRemoved wiring is in WireSyncOutbound() (called later in _Ready)
            _chatController.SystemMessage("Bem-vindo ao Tormenta VTT. Use /roll para rolar dados.");

            LoadCampaign(_currentCampaign);
            UpdateAssetList();
            CreatePercentUi();
            _contentService.LoadDefinitions();
            CreateContentBrowserUi();
            CreateSheetEditorUi();
            CreateNetworkUi();

            // ── Multiplayer setup ─────────────────────────────────────────────
            _syncService = new SyncService(_networkService, _mainThreadQueue);
            WireSyncOutbound();
            WireSyncInbound();
            CreateFogUi();
            CreateJournalUi();
            CreateSessionUi();

            _chatController.SystemMessage($"Conteúdo carregado: {_contentService.ClassCount} classes, {_contentService.RaceCount} raças, {_contentService.PowerCount} poderes, {_contentService.SpellCount} magias, {_contentService.ConditionCount} condições, {_contentService.ThreatCount} ameaças.");

            // ── Lobby must be created LAST so it renders on top ───────────────
            CreateLobbyUi();
        }

        // ── Main thread queue (pumped every frame for thread-safe network ops) ──
        public override void _Process(double delta)
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { GD.PrintErr($"[Sync] {e.Message}"); }
            }
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
                _chatController.SystemMessage("Comandos disponíveis: /attack [alvo] [dano tipo], /damage [alvo] <expressão> [tipo], /damageaoe <raio> <expressão> [tipo], /cast <magia> [alvo], /sheet [token], /tokens, /select <token>, /target <token>, /rename [token] <novo_nome>, /delete [token], /classes, /class <nome>, /applyclass <nome>, /spawnclass <nome> [nome_do_token], /spells, /spell <nome>, /conditions, /savecampaign <arquivo.json>, /loadcampaign <arquivo.json>, /startcombat, /endcombat, /order, /next, /prev, /resist [token] add|remove|list, /vuln [token] add|remove|list, /resistpct [token] add|remove|list, /vulnpct [token] add|remove|list, /condition [token] add <nome> [turnos]|remove <nome>|list, /heal [token] <expr>, /init [all], /skill <nome>, /test <atributo>, /importmodelagem, /documentimport");
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

            if (parts[0].Equals("/classes", StringComparison.OrdinalIgnoreCase))
            {
                if (_contentService.Classes.Count == 0)
                {
                    _chatController.SystemMessage("Nenhuma classe carregada.");
                }
                else
                {
                    _chatController.SystemMessage($"Classes disponíveis: {string.Join(", ", _contentService.Classes.Select(c => c.Name))}");
                }
                return true;
            }

            if (parts[0].Equals("/class", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var className = string.Join(' ', parts[1..]);
                var classDef = _contentService.Classes.FirstOrDefault(c => c.Name.Equals(className, StringComparison.OrdinalIgnoreCase) || c.Id.Equals(className, StringComparison.OrdinalIgnoreCase));
                if (classDef == null)
                {
                    _chatController.SystemMessage($"Classe '{className}' não encontrada.");
                    return true;
                }

                _chatController.SystemMessage($"Classe: {classDef.Name} / {classDef.Description} / Hit Die: d{classDef.HitDie} / Magia: {(classDef.Spellcasting ? "Sim" : "Não")}");
                return true;
            }

            if (parts[0].Equals("/applyclass", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var className = string.Join(' ', parts[1..]);
                var classDef = FindClassByName(className);
                var selected = _mapController.SelectedToken;
                if (selected == null)
                {
                    _chatController.SystemMessage("Selecione um token para aplicar a classe.");
                    return true;
                }

                if (classDef == null)
                {
                    _chatController.SystemMessage($"Classe '{className}' não encontrada.");
                    return true;
                }

                ApplyClassDefinition(selected, classDef);
                UpdateSelectionPanel(selected);
                _chatController.SystemMessage($"Classe '{classDef.Name}' aplicada a {selected.Name}.");
                return true;
            }

            if (parts[0].Equals("/spawnclass", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var className = parts[1];
                var tokenName = parts.Length > 2 ? string.Join(' ', parts[2..]) : $"Sem Nome {className}";
                var classDef = FindClassByName(className);
                if (classDef == null)
                {
                    _chatController.SystemMessage($"Classe '{className}' não encontrada.");
                    return true;
                }

                var token = TokenData.Create(tokenName, string.Empty);
                token.Position = _mapController.GetViewportCenterMapPosition();
                token.Sheet.CharacterClass = classDef.Name;
                token.Sheet.Level = 1;
                ApplyClassDefinition(token, classDef);
                _currentCampaign.Tokens.Add(token);
                _mapController.AddToken(token);
                UpdateAssetList();
                UpdateSelectionPanel(token);
                _chatController.SystemMessage($"Token '{token.Name}' criado com a classe {classDef.Name}.");
                return true;
            }

            if (parts[0].Equals("/spells", StringComparison.OrdinalIgnoreCase))
            {
                if (_contentService.Spells.Count == 0)
                {
                    _chatController.SystemMessage("Nenhuma magia carregada.");
                }
                else
                {
                    _chatController.SystemMessage($"Magias disponíveis: {string.Join(", ", _contentService.Spells.Select(s => s.Name))}");
                }
                return true;
            }

            if (parts[0].Equals("/importmodelagem", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("/documentimport", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = System.IO.Directory.GetCurrentDirectory();
                var inPath = System.IO.Path.Combine(basePath, "modelagem_vtt.txt");
                var contentDir = System.IO.Path.Combine(basePath, "Content");
                if (_documentImporter.TryImportDocument(inPath, contentDir, out var err))
                {
                    _chatController.SystemMessage(err);
                }
                else
                {
                    _chatController.SystemMessage($"Falha na importação: {err}");
                }
                return true;
            }

            if (parts[0].Equals("/parsemodelagem", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = System.IO.Directory.GetCurrentDirectory();
                var rawText = string.Empty;
                var importedJson = System.IO.Path.Combine(basePath, "Content", "imported_modelagem.json");
                if (System.IO.File.Exists(importedJson))
                {
                    try
                    {
                        var j = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(importedJson));
                        if (j.RootElement.TryGetProperty("raw", out var raw))
                            rawText = raw.GetString() ?? string.Empty;
                    }
                    catch { rawText = System.IO.File.ReadAllText(System.IO.Path.Combine(basePath, "modelagem_vtt.txt")); }
                }
                else if (System.IO.File.Exists(System.IO.Path.Combine(basePath, "modelagem_vtt.txt")))
                {
                    rawText = System.IO.File.ReadAllText(System.IO.Path.Combine(basePath, "modelagem_vtt.txt"));
                }

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    _chatController.SystemMessage("Nenhum texto de modelagem encontrado (modelagem_vtt.txt ou Content/imported_modelagem.json).");
                    return true;
                }

                var parsed = _textContentParser.Parse(rawText);
                _textContentParser.SaveParsedOutput(parsed, System.IO.Path.Combine(basePath, "Content"));
                _chatController.SystemMessage($"Parser executado: gerados Content/classes_parsed.json, Content/spells_parsed.json, Content/conditions_parsed.json");
                return true;
            }

            if (parts[0].Equals("/applyparsed", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = System.IO.Directory.GetCurrentDirectory();
                var contentDir = System.IO.Path.Combine(basePath, "Content");
                var mapped = new (string parsed, string canonical)[]
                {
                    (System.IO.Path.Combine(contentDir, "classes_parsed.json"), System.IO.Path.Combine(contentDir, "classes.json")),
                    (System.IO.Path.Combine(contentDir, "races_parsed.json"), System.IO.Path.Combine(contentDir, "races.json")),
                    (System.IO.Path.Combine(contentDir, "powers_parsed.json"), System.IO.Path.Combine(contentDir, "powers.json")),
                    (System.IO.Path.Combine(contentDir, "spells_parsed.json"), System.IO.Path.Combine(contentDir, "spells.json")),
                    (System.IO.Path.Combine(contentDir, "conditions_parsed.json"), System.IO.Path.Combine(contentDir, "conditions.json")),
                    (System.IO.Path.Combine(contentDir, "threats_parsed.json"), System.IO.Path.Combine(contentDir, "threats.json"))
                };

                var any = false;
                foreach (var (p, c) in mapped)
                {
                    if (System.IO.File.Exists(p))
                    {
                        try
                        {
                            // If this is the parsed conditions file, normalize modifier keys first
                            if (p.EndsWith("conditions_parsed.json", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var raw = System.IO.File.ReadAllText(p);
                                    var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<TormentaVTT.Models.ConditionDefinition>>(raw);
                                    if (list != null)
                                    {
                                        foreach (var cond in list)
                                        {
                                            if (cond.Modifiers == null) continue;
                                            var normalized = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                            foreach (var kv in cond.Modifiers)
                                            {
                                                var key = kv.Key?.Trim().ToLowerInvariant() ?? string.Empty;
                                                var val = kv.Value;
                                                string canonical;
                                                if (key.Contains("def") || key.Contains("defesa")) canonical = "defense";
                                                else if (key.Contains("attack") || key.Contains("atk") || key.Contains("ataque") || key.Contains("attackroll")) canonical = "attack";
                                                else if (key.Contains("check") || key.Contains("penalidade") || key.Contains("penalty")) canonical = "check";
                                                else if (key.Contains("resist")) canonical = "resistance";
                                                else if (key.Contains("vuln")) canonical = "vulnerability";
                                                else if (key.Contains("percent") || key.Contains("pct")) canonical = "percent";
                                                else canonical = key;

                                                // accumulate
                                                if (normalized.ContainsKey(canonical)) normalized[canonical] += val;
                                                else normalized[canonical] = val;
                                            }
                                            cond.Modifiers = normalized;
                                        }

                                        var outJson = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                        System.IO.File.WriteAllText(p, outJson);
                                    }
                                }
                                catch { /* swallow errors and fallback to direct copy */ }
                            }

                            System.IO.File.Copy(p, c, true);
                            any = true;
                        }
                        catch (Exception ex)
                        {
                            _chatController.SystemMessage($"Falha ao aplicar {p} -> {c}: {ex.Message}");
                            return true;
                        }
                    }
                }

                if (!any)
                {
                    _chatController.SystemMessage("Nenhum arquivo parsed encontrado em Content/*. Parsed: classes_parsed.json, races_parsed.json, powers_parsed.json, spells_parsed.json, conditions_parsed.json, threats_parsed.json");
                    return true;
                }

                // Reload definitions and refresh UI lists
                _contentService.LoadDefinitions();
                RefreshContentLists();
                _chatController.SystemMessage("Arquivos parsed aplicados e conteúdo recarregado.");
                return true;
            }

            if (parts[0].Equals("/spell", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var spellName = string.Join(' ', parts[1..]);
                var spellDef = _contentService.Spells.FirstOrDefault(s => s.Name.Equals(spellName, StringComparison.OrdinalIgnoreCase) || s.Id.Equals(spellName, StringComparison.OrdinalIgnoreCase));
                if (spellDef == null)
                {
                    _chatController.SystemMessage($"Magia '{spellName}' não encontrada.");
                    return true;
                }

                _chatController.SystemMessage($"Magia: {spellDef.Name} / Escola: {spellDef.School} / Círculo: {spellDef.Circle} / Custo PM: {spellDef.CostPM} / Alcance: {spellDef.Range} / Duração: {spellDef.Duration} / Alvo: {spellDef.TargetType} / Dano: {spellDef.DamageExpression} / Efeito: {spellDef.EffectType}");
                return true;
            }

            if (parts[0].Equals("/conditions", StringComparison.OrdinalIgnoreCase))
            {
                if (_contentService.Conditions.Count == 0)
                {
                    _chatController.SystemMessage("Nenhuma condição carregada.");
                }
                else
                {
                    _chatController.SystemMessage($"Condições disponíveis: {string.Join(", ", _contentService.Conditions.Select(c => c.Name))}");
                }
                return true;
            }

            if (parts[0].Equals("/savecampaign", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var pathIndex = 1;
                var pathText = ExtractQuotedName(parts, ref pathIndex);
                if (string.IsNullOrWhiteSpace(pathText))
                {
                    pathText = string.Join(' ', parts[pathIndex..]);
                }

                if (string.IsNullOrWhiteSpace(pathText))
                {
                    _chatController.SystemMessage("Uso: /savecampaign <arquivo.json>");
                    return true;
                }

                var actualPath = System.IO.Path.GetFullPath(pathText.EndsWith(".json") ? pathText : pathText + ".json");
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

                return true;
            }

            if (parts[0].Equals("/loadcampaign", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var pathIndex = 1;
                var pathText = ExtractQuotedName(parts, ref pathIndex);
                if (string.IsNullOrWhiteSpace(pathText))
                {
                    pathText = string.Join(' ', parts[pathIndex..]);
                }

                if (string.IsNullOrWhiteSpace(pathText))
                {
                    _chatController.SystemMessage("Uso: /loadcampaign <arquivo.json>");
                    return true;
                }

                var actualPath = System.IO.Path.GetFullPath(pathText);
                var campaign = PersistenceService.LoadCampaign(actualPath);
                if (campaign == null)
                {
                    _chatController.SystemMessage("Falha ao carregar campanha.");
                    return true;
                }

                _currentCampaign = campaign;
                LoadCampaign(_currentCampaign);
                UpdateAssetList();
                _chatController.SystemMessage($"Campanha carregada: {_currentCampaign.Name}");
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

            if (parts[0].Equals("/cast", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                var startIdx = 1;
                var spellName = ExtractQuotedName(parts, ref startIdx);
                if (string.IsNullOrWhiteSpace(spellName))
                {
                    spellName = string.Join(' ', parts[startIdx..]);
                    startIdx = parts.Length;
                }

                var spell = FindSpellByName(spellName);
                if (spell == null)
                {
                    _chatController.SystemMessage($"Magia '{spellName}' não encontrada.");
                    return true;
                }

                var caster = _mapController.SelectedToken;
                if (caster == null)
                {
                    _chatController.SystemMessage("Selecione um token para lançar a magia.");
                    return true;
                }

                if (caster.Sheet.PM < spell.CostPM)
                {
                    _chatController.SystemMessage($"{caster.Name} não tem PM suficiente para lançar {spell.Name} ({spell.CostPM} PM).");
                    return true;
                }

                caster.Sheet.PM -= spell.CostPM;
                TokenData? target = null;

                if (spell.TargetType.Equals("self", StringComparison.OrdinalIgnoreCase))
                {
                    target = caster;
                }
                else
                {
                    target = ResolveTargetToken(parts, ref startIdx) ?? caster;
                }

                if (target == null)
                {
                    _chatController.SystemMessage("Nenhum alvo disponível para a magia.");
                    return true;
                }

                var roll = _diceParser.Evaluate(spell.DamageExpression);
                if (spell.IsHealing)
                {
                    target.Sheet.HP += roll.Total;
                    _chatController.AddSystemMessage($"{caster.Name} lança {spell.Name} em {target.Name}: +{roll.Total} PV ({roll.Breakdown}). PV atuais: {target.Sheet.HP}.");
                }
                else
                {
                    var damage = target.Sheet.GetDamageAfterTypeModifiers(roll.Total, string.Empty);
                    OnApplyDamage(target.Id, damage);
                    _chatController.AddSystemMessage($"{caster.Name} lança {spell.Name} em {target.Name}: {damage} dano ({roll.Total} base, {roll.Breakdown}).");
                }

                UpdateSelectionPanel(caster);
                if (target.Id != caster.Id)
                {
                    UpdateSelectionPanel(target);
                }
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
                if (_networkService.IsConnected && !_isSyncing) _syncService.SyncCombatEnded();
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
            if (_networkService.IsConnected && !_isSyncing) SyncCombatStarted();
        }

        private void OnNextTurnPressed()
        {
            _combatController.AdvanceTurn();
            UpdateInitiativePanel();
            if (_networkService.IsConnected && !_isSyncing) SyncCombatAdvanced();
        }

        private void OnPrevTurnPressed()
        {
            _combatController.RetreatTurn();
            UpdateInitiativePanel();
            if (_networkService.IsConnected && !_isSyncing) SyncCombatAdvanced();
        }

        private void UpdateInitiativePanel()
        {
            var list   = GetNode<ItemList>("SidebarPanel/SidebarVBox/InitiativeVBox/InitiativeOrderList");
            var status = GetNode<Label>("SidebarPanel/SidebarVBox/InitiativeVBox/CombatStatusLabel");
            list.Clear();

            if (!_combatController.InCombat)
            {
                status.Text = "Combate inativo";
                return;
            }

            var current = _combatController.Current;
            status.Text = current != null
                ? $"⚔ Vez de: {current.Name}  PV {current.Sheet.HP}"
                : "Aguardando...";

            var order = _combatController.GetOrder();
            for (int i = 0; i < order.Count; i++)
            {
                var entry   = order[i];
                var token   = entry.Token;
                var isCurrent = current != null && current.Id == token.Id;

                var hpBar    = BuildHpBar(token.Sheet.HP, 20);   // simple text bar
                var conds    = token.Sheet.Conditions.Count > 0
                    ? $"  [{string.Join(",", token.Sheet.Conditions.Select(c => c.Name))}]"
                    : "";
                var marker   = isCurrent ? "▶ " : $"{i + 1}. ";
                var label    = $"{marker}{token.Name}  Init:{entry.InitiativeRoll}  PV:{token.Sheet.HP}{hpBar}{conds}";

                var itemIdx  = list.AddItem(label);
                list.SetItemMetadata(itemIdx, token.Id);

                if (isCurrent)
                    list.SetItemCustomBgColor(itemIdx, new Color(1.0f, 0.85f, 0.2f, 0.35f));
                else if (token.Sheet.HP <= 0)
                    list.SetItemCustomBgColor(itemIdx, new Color(0.6f, 0.0f, 0.0f, 0.30f));
                else
                    list.SetItemCustomBgColor(itemIdx, Colors.Transparent);
            }
        }

        private static string BuildHpBar(int hp, int max)
        {
            if (max <= 0) return "";
            var pct   = Math.Clamp((float)hp / max, 0f, 1f);
            var filled = (int)(pct * 8);
            return " [" + new string('█', filled) + new string('░', 8 - filled) + "]";
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

        private string ExtractQuotedName(string[] parts, ref int index)
        {
            if (index >= parts.Length)
                return string.Empty;

            if (parts[index].StartsWith('"'))
            {
                var collected = new List<string> { parts[index] };
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
                        index = i + 1;
                        return string.Join(' ', collected).Trim('"');
                    }
                }
            }

            return string.Empty;
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

        private ClassDefinition? FindClassByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _contentService.Classes.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || c.Id.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private SpellDefinition? FindSpellByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _contentService.Spells.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || s.Id.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyClassDefinition(TokenData token, ClassDefinition classDef)
        {
            token.Sheet.CharacterClass = classDef.Name;
            token.Sheet.HP = Math.Max(token.Sheet.HP, classDef.HitDie + token.Sheet.GetAttributeModifier("Constituição") + token.Sheet.Level);
            if (classDef.Spellcasting && token.Sheet.PM <= 0)
            {
                token.Sheet.PM = 10 + token.Sheet.GetAttributeModifier("Inteligência");
            }
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
                // Sync removal before removing
                if (_networkService.IsConnected && !_isSyncing)
                    _syncService.SyncTokenRemoved(token.Id);
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
                // Sync HP change
                if (_networkService.IsConnected && !_isSyncing)
                    _syncService.SyncDamage(token.Id, token.Sheet.HP);
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

            // Ownership check — players can only edit their own tokens
            if (!CanControlToken(token))
            {
                _chatController.SystemMessage("Você não tem permissão para editar este token.");
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

            // Sync HP/PM changes to all connected clients
            SyncTokenStatsIfConnected(token);
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
                _chatController.SystemMessage($"Condição '{conditionName}' atualizada em {token.Name}.");
            else
            {
                var durationText = duration > 0 ? $" por {duration} turnos" : string.Empty;
                _chatController.SystemMessage($"Condição '{conditionName}' adicionada a {token.Name}{durationText}.");
            }

            GetNode<LineEdit>("SidebarPanel/SidebarVBox/ConditionRow/ConditionInput").Text = string.Empty;
            UpdateSelectionPanel(token);
            UpdateInitiativePanel();

            // Sync: broadcast condition change as a combined stats + chat message
            SyncTokenStatsIfConnected(token);
            if (_networkService.IsConnected && !_isSyncing)
            {
                var msg = $"[Condição] {token.Name} → +{conditionName}";
                _syncService.SyncChat("Sistema", msg, "System");
            }
        }

        private void OnRemoveConditionPressed()
        {
            var token = _mapController.SelectedToken;
            if (token == null)
            {
                _chatController.SystemMessage("Selecione um token para remover condição.");
                return;
            }

            var list     = GetNode<ItemList>("SidebarPanel/SidebarVBox/ConditionsList");
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
            UpdateInitiativePanel();

            SyncTokenStatsIfConnected(token);
            if (_networkService.IsConnected && !_isSyncing)
            {
                var msg = $"[Condição] {token.Name} → -{condition}";
                _syncService.SyncChat("Sistema", msg, "System");
            }
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

        private void CreateContentBrowserUi()
        {
            var sidebar = GetNode<VBoxContainer>("SidebarPanel/SidebarVBox");

            var classesLabel = new Label { Text = "Classes carregadas" };
            _contentClassesList = new ItemList
            {
                Name = "ContentClassesList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 80)
            };
            _applySelectedClassButton = new Button { Text = "Aplicar classe" };
            var classButtons = new HBoxContainer();
            var reloadContentButton = new Button { Text = "↻ Recarregar" };
            var importModelButton = new Button { Text = "📥 Importar" };
            var openLibraryButton = new Button { Text = "📖 Biblioteca..." };
            classButtons.AddChild(reloadContentButton);
            classButtons.AddChild(importModelButton);
            classButtons.AddChild(openLibraryButton);
            sidebar.AddChild(classesLabel);
            sidebar.AddChild(_contentClassesList);
            sidebar.AddChild(_applySelectedClassButton);
            sidebar.AddChild(classButtons);

            var racesLabel = new Label { Text = "Raças carregadas" };
            _contentRacesList = new ItemList
            {
                Name = "ContentRacesList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 70)
            };
            _applySelectedRaceButton = new Button { Text = "Aplicar raça" };
            sidebar.AddChild(racesLabel);
            sidebar.AddChild(_contentRacesList);
            sidebar.AddChild(_applySelectedRaceButton);

            var powersLabel = new Label { Text = "Poderes carregados" };
            _contentPowersList = new ItemList
            {
                Name = "ContentPowersList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 70)
            };
            _useSelectedPowerButton = new Button { Text = "Usar poder" };
            sidebar.AddChild(powersLabel);
            sidebar.AddChild(_contentPowersList);
            sidebar.AddChild(_useSelectedPowerButton);

            var spellsLabel = new Label { Text = "Magias carregadas" };
            _contentSpellsList = new ItemList
            {
                Name = "ContentSpellsList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 80)
            };
            var spellRow = new HBoxContainer();
            _spellTargetInput = new LineEdit { Name = "SpellTargetInput", PlaceholderText = "alvo (opcional)" };
            _castSelectedSpellButton = new Button { Text = "Lançar magia" };
            spellRow.AddChild(_spellTargetInput);
            spellRow.AddChild(_castSelectedSpellButton);

            sidebar.AddChild(spellsLabel);
            sidebar.AddChild(_contentSpellsList);
            sidebar.AddChild(spellRow);

            if (_applySelectedClassButton != null)
                _applySelectedClassButton.Pressed += OnApplySelectedClassPressed;
            if (_castSelectedSpellButton != null)
                _castSelectedSpellButton.Pressed += OnCastSelectedSpellPressed;
            if (_contentClassesList != null)
                _contentClassesList.ItemSelected += OnContentClassSelected;
            if (_contentSpellsList != null)
                _contentSpellsList.ItemSelected += OnContentSpellSelected;
            if (_applySelectedRaceButton != null)
                _applySelectedRaceButton.Pressed += OnApplySelectedRacePressed;
            if (_contentRacesList != null)
                _contentRacesList.ItemSelected += OnContentRaceSelected;
            if (_useSelectedPowerButton != null)
                _useSelectedPowerButton.Pressed += OnUseSelectedPowerPressed;
            if (_contentPowersList != null)
                _contentPowersList.ItemSelected += OnContentPowerSelected;

            reloadContentButton.Pressed += () => {
                _contentService.LoadDefinitions();
                RefreshContentLists();
                _chatController.SystemMessage("Conteúdo recarregado.");
            };
            importModelButton.Pressed += () => {
                var basePath = System.IO.Directory.GetCurrentDirectory();
                var inPath = System.IO.Path.Combine(basePath, "modelagem_vtt.txt");
                var contentDir = System.IO.Path.Combine(basePath, "Content");
                if (_documentImporter.TryImportDocument(inPath, contentDir, out var err))
                {
                    _contentService.LoadDefinitions();
                    RefreshContentLists();
                    _chatController.SystemMessage(err);
                }
                else _chatController.SystemMessage($"Falha na importação: {err}");
            };

            // Open full library window
            openLibraryButton.Pressed += () => OpenLibraryWindow();

            var conditionsLabel = new Label { Text = "Condições carregadas" };
            _contentConditionsList = new ItemList
            {
                Name = "ContentConditionsList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 80)
            };
            var conditionRow = new HBoxContainer();
            _conditionDurationInput = new LineEdit { Name = "ConditionDurationInput", PlaceholderText = "turnos (opcional)" };
            _applySelectedConditionButton = new Button { Text = "Aplicar" };
            _removeSelectedConditionButton = new Button { Text = "Remover" };
            conditionRow.AddChild(_conditionDurationInput);
            conditionRow.AddChild(_applySelectedConditionButton);
            conditionRow.AddChild(_removeSelectedConditionButton);

            sidebar.AddChild(conditionsLabel);
            sidebar.AddChild(_contentConditionsList);
            sidebar.AddChild(conditionRow);

            if (_contentConditionsList != null)
                _contentConditionsList.ItemSelected += OnContentConditionSelected;
            if (_applySelectedConditionButton != null)
                _applySelectedConditionButton.Pressed += OnApplySelectedConditionPressed;
            if (_removeSelectedConditionButton != null)
                _removeSelectedConditionButton.Pressed += OnRemoveSelectedConditionPressed;

            var threatsLabel = new Label { Text = "Ameaças carregadas" };
            _contentThreatsList = new ItemList
            {
                Name = "ContentThreatsList",
                SelectMode = ItemList.SelectModeEnum.Single,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 100)
            };
            var threatRow = new HBoxContainer();
            _threatNameInput = new LineEdit { Name = "ThreatNameInput", PlaceholderText = "nome (opcional)" };
            _spawnThreatButton = new Button { Text = "Spawnar" };
            var spawnCombatButton = new Button { Text = "⚔ Spawn+Combate" };
            var gmOnlyCheck = new CheckButton { Text = "GM-only" };
            threatRow.AddChild(_threatNameInput);
            threatRow.AddChild(_spawnThreatButton);
            threatRow.AddChild(spawnCombatButton);
            threatRow.AddChild(gmOnlyCheck);

            sidebar.AddChild(threatsLabel);
            sidebar.AddChild(_contentThreatsList);
            sidebar.AddChild(threatRow);

            if (_contentThreatsList != null)
                _contentThreatsList.ItemSelected += OnContentThreatSelected;
            if (_spawnThreatButton != null)
                _spawnThreatButton.Pressed += OnSpawnThreatPressed;

            spawnCombatButton.Pressed += () =>
            {
                OnSpawnThreatPressed();
                var newest = _currentCampaign.Tokens.LastOrDefault();
                if (newest != null && !_combatController.InCombat)
                {
                    _combatController.StartCombat(_currentCampaign.Tokens, true);
                    _chatController.SystemMessage($"⚔ Combate iniciado com {newest.Name}.");
                    SyncCombatStarted();
                }
                else if (newest != null && _combatController.InCombat)
                {
                    _combatController.AddTokenToOrder(newest, true);
                    _chatController.SystemMessage($"{newest.Name} adicionado ao combate.");
                    SyncCombatStarted();
                }
                UpdateInitiativePanel();
            };

            // GM-only check: marking the NEXT spawn as GM-only
            gmOnlyCheck.Toggled += on =>
            {
                // Stored as a field for OnSpawnThreatPressed to read
                _nextSpawnGMOnly = on;
            };

            // Sheet import/export
            var sheetRow = new HBoxContainer();
            var exportBtn = new Button { Text = "Exportar ficha" };
            var importBtn = new Button { Text = "Importar ficha" };
            var editBtn = new Button { Text = "Editar ficha" };
            sheetRow.AddChild(exportBtn);
            sheetRow.AddChild(importBtn);
            sheetRow.AddChild(editBtn);
            sidebar.AddChild(sheetRow);

            exportBtn.Pressed += () => ShowSheetSaveDialog();
            importBtn.Pressed += () => ShowSheetOpenDialog();
            editBtn.Pressed += () => ShowSheetEditor();


            _contentClassesList?.Clear();
            if (_contentClassesList != null)
            {
                foreach (var classDef in _contentService.Classes)
                {
                    var index = _contentClassesList.AddItem(classDef.Name);
                    _contentClassesList.SetItemMetadata(index, classDef.Name);
                }
            }

            _contentRacesList?.Clear();
            if (_contentRacesList != null)
            {
                foreach (var raceDef in _contentService.Races)
                {
                    var index = _contentRacesList.AddItem(raceDef.Name);
                    _contentRacesList.SetItemMetadata(index, raceDef.Name);
                }
            }

            _contentPowersList?.Clear();
            if (_contentPowersList != null)
            {
                foreach (var powerDef in _contentService.Powers)
                {
                    var index = _contentPowersList.AddItem(powerDef.Name);
                    _contentPowersList.SetItemMetadata(index, powerDef.Name);
                }
            }

            _contentSpellsList?.Clear();
            if (_contentSpellsList != null)
            {
                foreach (var spellDef in _contentService.Spells)
                {
                    var index = _contentSpellsList.AddItem(spellDef.Name);
                    _contentSpellsList.SetItemMetadata(index, spellDef.Name);
                }
            }

            _contentConditionsList?.Clear();
            if (_contentConditionsList != null)
            {
                foreach (var conditionDef in _contentService.Conditions)
                {
                    var index = _contentConditionsList.AddItem(conditionDef.Name);
                    _contentConditionsList.SetItemMetadata(index, conditionDef.Name);
                }
            }

            _contentThreatsList?.Clear();
            if (_contentThreatsList != null)
            {
                foreach (var threatDef in _contentService.Threats)
                {
                    var index = _contentThreatsList.AddItem(threatDef.Name);
                    _contentThreatsList.SetItemMetadata(index, threatDef.Name);
                }
            }
        }

        private void OnContentClassSelected(long index)
        {
            if (_contentClassesList == null || index < 0 || index >= _contentClassesList.ItemCount)
                return;

            var className = _contentClassesList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(className))
                return;

            var classDef = FindClassByName(className);
            if (classDef != null)
            {
                _chatController.SystemMessage($"Classe selecionada: {classDef.Name} / {classDef.Description} / Hit Die: d{classDef.HitDie} / Magia: {(classDef.Spellcasting ? "Sim" : "Não")}");
            }
        }

        private void OnContentSpellSelected(long index)
        {
            if (_contentSpellsList == null || index < 0 || index >= _contentSpellsList.ItemCount)
                return;

            var spellName = _contentSpellsList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(spellName))
                return;

            var spellDef = FindSpellByName(spellName);
            if (spellDef != null)
            {
                _chatController.SystemMessage($"Magia selecionada: {spellDef.Name} / {spellDef.School} círculo {spellDef.Circle} / Custo PM: {spellDef.CostPM} / Alcance: {spellDef.Range}");
            }
        }

        private void OnContentConditionSelected(long index)
        {
            if (_contentConditionsList == null || index < 0 || index >= _contentConditionsList.ItemCount)
                return;

            var conditionName = _contentConditionsList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(conditionName))
                return;

            var conditionDef = _contentService.Conditions.FirstOrDefault(c => c.Name.Equals(conditionName, StringComparison.OrdinalIgnoreCase) || c.Id.Equals(conditionName, StringComparison.OrdinalIgnoreCase));
            if (conditionDef != null)
            {
                _chatController.SystemMessage($"Condição selecionada: {conditionDef.Name} / {conditionDef.Description}");
            }
        }

        private void OnApplySelectedConditionPressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para aplicar a condição.");
                return;
            }

            if (_contentConditionsList == null)
            {
                _chatController.SystemMessage("Lista de condições não está disponível.");
                return;
            }

            var selectedItems = _contentConditionsList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma condição na lista.");
                return;
            }

            var conditionName = _contentConditionsList.GetItemMetadata(selectedItems[0]).ToString();
            var conditionDef = _contentService.Conditions.FirstOrDefault(c => c.Name.Equals(conditionName, StringComparison.OrdinalIgnoreCase) || c.Id.Equals(conditionName, StringComparison.OrdinalIgnoreCase));
            if (conditionDef == null)
            {
                _chatController.SystemMessage("Condição selecionada não encontrada.");
                return;
            }

            var duration = -1;
            if (int.TryParse(_conditionDurationInput?.Text, out var parsedDuration) && parsedDuration > 0)
                duration = parsedDuration;

            selected.Sheet.AddConditionFromDefinition(conditionDef, duration);
            UpdateSelectionPanel(selected);
            UpdateConditionList(selected);
            var durationText = duration > 0 ? $" por {duration} turnos" : string.Empty;
            _chatController.SystemMessage($"Condição '{conditionDef.Name}' aplicada a {selected.Name}{durationText}.");
        }

        private void OnRemoveSelectedConditionPressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para remover a condição.");
                return;
            }

            if (_contentConditionsList == null)
            {
                _chatController.SystemMessage("Lista de condições não está disponível.");
                return;
            }

            var selectedItems = _contentConditionsList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma condição na lista.");
                return;
            }

            var conditionName = _contentConditionsList.GetItemMetadata(selectedItems[0]).ToString();
            selected.Sheet.RemoveCondition(conditionName ?? string.Empty);
            UpdateSelectionPanel(selected);
            UpdateConditionList(selected);
            _chatController.SystemMessage($"Condição '{conditionName}' removida de {selected.Name}.");
        }

        private void OnContentRaceSelected(long index)
        {
            if (_contentRacesList == null || index < 0 || index >= _contentRacesList.ItemCount)
                return;

            var raceName = _contentRacesList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(raceName))
                return;

            var raceDef = _contentService.Races.FirstOrDefault(r => r.Name.Equals(raceName, StringComparison.OrdinalIgnoreCase) || r.Id.Equals(raceName, StringComparison.OrdinalIgnoreCase));
            if (raceDef != null)
            {
                var bonus = string.Join(", ", raceDef.AttributeBonus.Select(kv => $"{kv.Key}:{kv.Value}"));
                _chatController.SystemMessage($"Raça selecionada: {raceDef.Name} / Bônus: {bonus} / Velocidade: {raceDef.MovementSpeed}m / Idiomas: {string.Join(", ", raceDef.Languages)}");
            }
        }

        private void OnApplySelectedRacePressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para aplicar a raça.");
                return;
            }

            if (_contentRacesList == null)
            {
                _chatController.SystemMessage("Lista de raças não está disponível.");
                return;
            }

            var selectedItems = _contentRacesList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma raça na lista.");
                return;
            }

            var raceName = _contentRacesList.GetItemMetadata(selectedItems[0]).ToString();
            var raceDef = _contentService.Races.FirstOrDefault(r => r.Name.Equals(raceName, StringComparison.OrdinalIgnoreCase) || r.Id.Equals(raceName, StringComparison.OrdinalIgnoreCase));
            if (raceDef == null)
            {
                _chatController.SystemMessage("Raça selecionada não encontrada.");
                return;
            }

            selected.Sheet.Race = raceDef.Name;
            foreach (var bonus in raceDef.AttributeBonus)
            {
                if (selected.Sheet.Attributes.ContainsKey(bonus.Key))
                    selected.Sheet.Attributes[bonus.Key] += bonus.Value;
            }
            UpdateSelectionPanel(selected);
            _chatController.SystemMessage($"Raça '{raceDef.Name}' aplicada a {selected.Name}.");
        }

        private void OnContentPowerSelected(long index)
        {
            if (_contentPowersList == null || index < 0 || index >= _contentPowersList.ItemCount)
                return;

            var powerName = _contentPowersList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(powerName))
                return;

            var powerDef = _contentService.Powers.FirstOrDefault(p => p.Name.Equals(powerName, StringComparison.OrdinalIgnoreCase) || p.Id.Equals(powerName, StringComparison.OrdinalIgnoreCase));
            if (powerDef != null)
            {
                _chatController.SystemMessage($"Poder selecionado: {powerDef.Name} / Tipo: {powerDef.Type} / {powerDef.Description}");
            }
        }

        private void OnUseSelectedPowerPressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para usar o poder.");
                return;
            }

            if (_contentPowersList == null)
            {
                _chatController.SystemMessage("Lista de poderes não está disponível.");
                return;
            }

            var selectedItems = _contentPowersList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione um poder na lista.");
                return;
            }

            var powerName = _contentPowersList.GetItemMetadata(selectedItems[0]).ToString();
            var powerDef = _contentService.Powers.FirstOrDefault(p => p.Name.Equals(powerName, StringComparison.OrdinalIgnoreCase) || p.Id.Equals(powerName, StringComparison.OrdinalIgnoreCase));
            if (powerDef == null)
            {
                _chatController.SystemMessage("Poder selecionado não encontrado.");
                return;
            }

            _chatController.SystemMessage($"{selected.Name} usa {powerDef.Name}: {string.Join(", ", powerDef.Effects)}");
        }

        private void OnContentThreatSelected(long index)
        {
            if (_contentThreatsList == null || index < 0 || index >= _contentThreatsList.ItemCount)
                return;

            var threatName = _contentThreatsList.GetItemMetadata((int)index).ToString();
            if (string.IsNullOrEmpty(threatName))
                return;

            var threatDef = _contentService.Threats.FirstOrDefault(t => t.Name.Equals(threatName, StringComparison.OrdinalIgnoreCase) || t.Id.Equals(threatName, StringComparison.OrdinalIgnoreCase));
            if (threatDef != null)
            {
                _chatController.SystemMessage($"Ameaça: {threatDef.Name} / Tipo: {threatDef.Type} / Nível: {threatDef.Level} / PV: {threatDef.HP} / Defesa: {threatDef.Defense}");
            }
        }

        private void OnSpawnThreatPressed()
        {
            if (_contentThreatsList == null)
            {
                _chatController.SystemMessage("Lista de ameaças não está disponível.");
                return;
            }

            var selectedItems = _contentThreatsList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma ameaça na lista.");
                return;
            }

            var threatName = _contentThreatsList.GetItemMetadata(selectedItems[0]).ToString();
            var threatDef = _contentService.Threats.FirstOrDefault(t =>
                t.Name.Equals(threatName, StringComparison.OrdinalIgnoreCase) ||
                t.Id.Equals(threatName, StringComparison.OrdinalIgnoreCase));
            if (threatDef == null)
            {
                _chatController.SystemMessage("Ameaça selecionada não encontrada.");
                return;
            }

            var tokenName = _threatNameInput?.Text?.Trim();
            if (string.IsNullOrEmpty(tokenName)) tokenName = threatDef.Name;

            var token = TokenData.Create(tokenName, string.Empty);
            token.Sheet.Name           = tokenName;
            token.Sheet.CharacterClass = threatDef.Type;
            token.Sheet.Level          = threatDef.Level;
            token.Sheet.HP             = threatDef.HP;
            token.Sheet.Defense        = threatDef.Defense;
            token.IsGMOnly             = _nextSpawnGMOnly;

            foreach (var attr in threatDef.Attributes)
                if (token.Sheet.Attributes.ContainsKey(attr.Key))
                    token.Sheet.Attributes[attr.Key] = attr.Value;

            token.Position = _mapController.GetViewportCenterMapPosition();
            _currentCampaign.Tokens.Add(token);
            _mapController.AddToken(token);
            UpdateAssetList();

            var gmOnlyNote = _nextSpawnGMOnly ? " [GM-only]" : "";
            _chatController.SystemMessage($"Ameaça '{tokenName}' spawnada no mapa{gmOnlyNote}.");
        }

        private void CreateSheetEditorUi()
        {
            _sheetEditorDialog = new AcceptDialog { Name = "SheetEditorDialog", Title = "Editor de Ficha", Visible = false };
            var vbox = new VBoxContainer();

            _sheetEditorName = new LineEdit { PlaceholderText = "Nome" };
            _sheetEditorClass = new LineEdit { PlaceholderText = "Classe" };
            _sheetEditorHP = new LineEdit { PlaceholderText = "PV" };
            _sheetEditorPM = new LineEdit { PlaceholderText = "PM" };

            vbox.AddChild(new Label { Text = "Nome" });
            vbox.AddChild(_sheetEditorName);
            vbox.AddChild(new Label { Text = "Classe" });
            vbox.AddChild(_sheetEditorClass);
            vbox.AddChild(new Label { Text = "PV" });
            vbox.AddChild(_sheetEditorHP);
            vbox.AddChild(new Label { Text = "PM" });
            vbox.AddChild(_sheetEditorPM);

            vbox.AddChild(new Label { Text = "Atributos" });
            var grid = new GridContainer { Columns = 2 };
            foreach (var attr in new[] { "Força", "Destreza", "Constituição", "Inteligência", "Sabedoria", "Carisma" })
            {
                var lbl = new Label { Text = attr };
                var le = new LineEdit { PlaceholderText = "0" };
                _sheetEditorAttributes[attr] = le;
                grid.AddChild(lbl);
                grid.AddChild(le);
            }
            vbox.AddChild(grid);

            var btnRow = new HBoxContainer();
            var saveTemplate = new Button { Text = "Salvar como template" };
            var applyBtn = new Button { Text = "Aplicar à seleção" };
            btnRow.AddChild(saveTemplate);
            btnRow.AddChild(applyBtn);
            vbox.AddChild(btnRow);

            _sheetEditorDialog.AddChild(vbox);
            AddChild(_sheetEditorDialog);

            saveTemplate.Pressed += OnSheetEditorSavePressed;
            applyBtn.Pressed += OnSheetEditorApplyPressed;
        }

        private void RefreshContentLists()
        {
            _contentClassesList?.Clear();
            if (_contentClassesList != null)
            {
                foreach (var classDef in _contentService.Classes)
                {
                    var index = _contentClassesList.AddItem(classDef.Name);
                    _contentClassesList.SetItemMetadata(index, classDef.Name);
                }
            }

            _contentSpellsList?.Clear();
            if (_contentSpellsList != null)
            {
                foreach (var spellDef in _contentService.Spells)
                {
                    var index = _contentSpellsList.AddItem(spellDef.Name);
                    _contentSpellsList.SetItemMetadata(index, spellDef.Name);
                }
            }

            _contentConditionsList?.Clear();
            if (_contentConditionsList != null)
            {
                foreach (var conditionDef in _contentService.Conditions)
                {
                    var index = _contentConditionsList.AddItem(conditionDef.Name);
                    _contentConditionsList.SetItemMetadata(index, conditionDef.Name);
                }
            }
        }

        private void CreateNetworkUi()
        {
            // Player name field added to toolbar
            var topButtons = GetNode<HBoxContainer>("Toolbar/TopButtons");
            _playerNameInput = new LineEdit
            {
                PlaceholderText = "Seu nome",
                Text = "Mestre",
                CustomMinimumSize = new Vector2(110, 0)
            };
            topButtons.AddChild(_playerNameInput);

            // Connect dialog
            _connectDialog = new AcceptDialog { Title = "Conectar a uma Mesa" };
            var v = new VBoxContainer();
            _connectHostInput = new LineEdit { PlaceholderText = "IP do host (ex: 192.168.1.10)" };
            _connectPortInput = new LineEdit { PlaceholderText = "Porta (padrão: 12345)", Text = "12345" };
            v.AddChild(new Label { Text = "Nome do jogador:" });
            v.AddChild(new Label { Text = "(use o campo 'Seu nome' da toolbar)" });
            v.AddChild(new Label { Text = "IP do host:" });
            v.AddChild(_connectHostInput);
            v.AddChild(new Label { Text = "Porta:" });
            v.AddChild(_connectPortInput);
            _connectDialog.AddChild(v);
            AddChild(_connectDialog);

            _connectDialog.Confirmed += () =>
            {
                var host     = _connectHostInput?.Text.Trim() ?? "127.0.0.1";
                var portText = _connectPortInput?.Text.Trim() ?? "12345";
                if (!int.TryParse(portText, out var port)) port = 12345;

                var name = _playerNameInput?.Text.Trim();
                if (!string.IsNullOrEmpty(name)) _localPlayerName = name;
                _localRole = RoleType.Player;

                var ok = _networkService.Join(host, port);
                if (!ok)
                {
                    _chatController.SystemMessage($"Falha ao conectar a {host}:{port}.");
                    return;
                }

                _chatController.SystemMessage($"Conectando a {host}:{port} como '{_localPlayerName}'...");
                _mapController.ApplyVisibilityMode(false); // player view: no GM tokens
                // Introduce ourselves and request full state
                _syncService.RequestFullState(_localPlayerName);
            };
        }

        private void ShowSheetEditor()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token antes de editar a ficha.");
                return;
            }

            // Populate
            _sheetEditorName!.Text = selected.Sheet.Name ?? string.Empty;
            _sheetEditorClass!.Text = selected.Sheet.CharacterClass ?? string.Empty;
            _sheetEditorHP!.Text = selected.Sheet.HP.ToString();
            _sheetEditorPM!.Text = selected.Sheet.PM.ToString();
            foreach (var attr in selected.Sheet.Attributes)
            {
                if (_sheetEditorAttributes.TryGetValue(attr.Key, out var le))
                    le.Text = attr.Value.ToString();
            }

            _sheetEditorDialog!.PopupCenteredRatio();
        }

        private void OnSheetEditorApplyPressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para aplicar a ficha.");
                return;
            }

            ApplyEditorToSelected(selected);
            _sheetEditorDialog!.Hide();
            UpdateSelectionPanel(selected);
            _chatController.SystemMessage($"Ficha aplicada a {selected.Name}.");
        }

        private void OnSheetEditorSavePressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para salvar a ficha como template.");
                return;
            }

            ApplyEditorToSelected(selected);

            var fileName = string.IsNullOrWhiteSpace(selected.Sheet.Name) ? selected.Name : selected.Sheet.Name;
            var sanitized = string.Join("_", fileName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var templatesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Content", "Sheets");
            if (!System.IO.Directory.Exists(templatesDir))
                System.IO.Directory.CreateDirectory(templatesDir);

            var filePath = System.IO.Path.Combine(templatesDir, sanitized + ".json");
            var dict = selected.Sheet.ToDictionary();
            var plain = ConvertGodotToPlainObject(dict);
            var json = System.Text.Json.JsonSerializer.Serialize(plain, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            try
            {
                System.IO.File.WriteAllText(filePath, json);
                _chatController.SystemMessage($"Template salvo: {filePath}");
            }
            catch (Exception ex)
            {
                _chatController.SystemMessage($"Falha ao salvar template: {ex.Message}");
            }
        }

        private void ApplyEditorToSelected(TokenData selected)
        {
            if (selected == null)
                return;

            if (_sheetEditorName != null) selected.Sheet.Name = _sheetEditorName.Text;
            if (_sheetEditorClass != null) selected.Sheet.CharacterClass = _sheetEditorClass.Text;
            if (_sheetEditorHP != null && int.TryParse(_sheetEditorHP.Text, out var hp)) selected.Sheet.HP = hp;
            if (_sheetEditorPM != null && int.TryParse(_sheetEditorPM.Text, out var pm)) selected.Sheet.PM = pm;

            foreach (var kv in _sheetEditorAttributes)
            {
                if (int.TryParse(kv.Value.Text, out var v))
                    selected.Sheet.Attributes[kv.Key] = v;
            }
        }

        private void OnHostPressed()
        {
            var port = 12345;
            var name = _playerNameInput?.Text.Trim();
            if (!string.IsNullOrEmpty(name)) _localPlayerName = name;
            _localRole = RoleType.GM;

            var ok = _networkService.StartHost(port);
            if (!ok)
            {
                _chatController.SystemMessage("Falha ao iniciar host. Porta em uso?");
                return;
            }

            // Hook host events (guard against double-subscription)
            _networkService.ClientConnected    -= OnClientConnected;
            _networkService.ClientDisconnected -= OnClientDisconnected;
            _networkService.ClientConnected    += OnClientConnected;
            _networkService.ClientDisconnected += OnClientDisconnected;

            _chatController.PlayerName = _localPlayerName;
            _mapController.ApplyVisibilityMode(true);
            _chatController.SystemMessage($"✅ Hospedando na porta {port} como '{_localPlayerName}'. IP: {GetLocalIP()}");
            UpdateSessionUI();

            var gmSession = new PlayerSession
            {
                Id = _networkService.LocalId, DisplayName = _localPlayerName,
                Role = RoleType.GM, IsConnected = true
            };
            _connectedPlayers.Clear();
            _connectedPlayers.Add(gmSession);
            UpdateSessionUI();
        }

        private void OnJoinPressed()
        {
            _connectDialog?.PopupCenteredRatio();
        }

        private void ShowSheetSaveDialog()
        {
            var dlg = GetNodeOrNull<FileDialog>("CampaignSaveDialog");
            if (dlg != null)
            {
                dlg.FileSelected -= OnSheetExportSelected;
                dlg.FileSelected += OnSheetExportSelected;
                dlg.PopupCenteredRatio();
                return;
            }

            var fallback = new FileDialog();
            fallback.Access = FileDialog.AccessEnum.Filesystem;
            AddChild(fallback);
            fallback.FileSelected += OnSheetExportSelected;
            fallback.PopupCenteredRatio();
        }

        private void ShowSheetOpenDialog()
        {
            var dlg = GetNodeOrNull<FileDialog>("CampaignOpenDialog");
            if (dlg != null)
            {
                dlg.FileSelected -= OnSheetImportSelected;
                dlg.FileSelected += OnSheetImportSelected;
                dlg.PopupCenteredRatio();
                return;
            }

            var fallback = new FileDialog();
            fallback.Access = FileDialog.AccessEnum.Filesystem;
            AddChild(fallback);
            fallback.FileSelected += OnSheetImportSelected;
            fallback.PopupCenteredRatio();
        }

        private void OnSheetExportSelected(string path)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para exportar a ficha.");
                return;
            }

            var actualPath = path.EndsWith(".json") ? path : path + ".json";
            var dict = selected.Sheet.ToDictionary();
            var plain = ConvertGodotToPlainObject(dict);
            var json = System.Text.Json.JsonSerializer.Serialize(plain, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.GetFullPath(actualPath), json);
                _chatController.SystemMessage($"Ficha de {selected.Name} exportada para: {actualPath}");
            }
            catch (Exception ex)
            {
                _chatController.SystemMessage($"Falha ao exportar ficha: {ex.Message}");
            }
        }

        private void OnSheetImportSelected(string path)
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para importar a ficha.");
                return;
            }

            try
            {
                var raw = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(path));
                var sheet = CharacterSheet.FromJson(raw);
                selected.Sheet = sheet;
                UpdateSelectionPanel(selected);
                _chatController.SystemMessage($"Ficha importada para {selected.Name} a partir de: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                _chatController.SystemMessage($"Falha ao importar ficha: {ex.Message}");
            }
        }

        private void OnApplySelectedClassPressed()
        {
            var selected = _mapController.SelectedToken;
            if (selected == null)
            {
                _chatController.SystemMessage("Selecione um token para aplicar a classe.");
                return;
            }

            if (_contentClassesList == null)
            {
                _chatController.SystemMessage("Lista de classes não está disponível.");
                return;
            }

            var selectedItems = _contentClassesList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma classe na lista.");
                return;
            }

            var className = _contentClassesList.GetItemMetadata(selectedItems[0]).ToString();
            var classDef = FindClassByName(className);
            if (classDef == null)
            {
                _chatController.SystemMessage("Classe selecionada não encontrada.");
                return;
            }

            ApplyClassDefinition(selected, classDef);
            UpdateSelectionPanel(selected);
            _chatController.SystemMessage($"Classe '{classDef.Name}' aplicada a {selected.Name}.");
        }

        private void OnCastSelectedSpellPressed()
        {
            if (_contentSpellsList == null)
            {
                _chatController.SystemMessage("Lista de magias não está disponível.");
                return;
            }

            var selectedItems = _contentSpellsList.GetSelectedItems();
            if (selectedItems.Length == 0)
            {
                _chatController.SystemMessage("Selecione uma magia na lista.");
                return;
            }

            var spellName = _contentSpellsList.GetItemMetadata(selectedItems[0]).ToString();
            var spellDef = FindSpellByName(spellName);
            if (spellDef == null)
            {
                _chatController.SystemMessage("Magia selecionada não encontrada.");
                return;
            }

            var caster = _mapController.SelectedToken;
            if (caster == null)
            {
                _chatController.SystemMessage("Selecione um token para lançar a magia.");
                return;
            }

            if (caster.Sheet.PM < spellDef.CostPM)
            {
                _chatController.SystemMessage($"{caster.Name} não tem PM suficiente para lançar {spellDef.Name} ({spellDef.CostPM} PM).\nPM atuais: {caster.Sheet.PM}.");
                return;
            }

            TokenData? target = null;
            if (spellDef.TargetType.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                target = caster;
            }
            else
            {
                var targetText = _spellTargetInput?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(targetText))
                    target = FindTokenByName(targetText);
                target ??= _mapController.SelectedToken != caster ? _mapController.SelectedToken : caster;
            }

            if (target == null)
            {
                _chatController.SystemMessage("Nenhum alvo disponível para a magia.");
                return;
            }

            caster.Sheet.PM -= spellDef.CostPM;
            var roll = _diceParser.Evaluate(spellDef.DamageExpression);
            if (spellDef.IsHealing)
            {
                target.Sheet.HP += roll.Total;
                _chatController.AddSystemMessage($"{caster.Name} lança {spellDef.Name} em {target.Name}: +{roll.Total} PV ({roll.Breakdown}). PV atuais: {target.Sheet.HP}.");
            }
            else
            {
                var damage = target.Sheet.GetDamageAfterTypeModifiers(roll.Total, string.Empty);
                OnApplyDamage(target.Id, damage);
                _chatController.AddSystemMessage($"{caster.Name} lança {spellDef.Name} em {target.Name}: {damage} dano ({roll.Total} base, {roll.Breakdown}).");
            }

            UpdateSelectionPanel(caster);
            if (target.Id != caster.Id)
                UpdateSelectionPanel(target);
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

        private object ConvertGodotToPlainObject(object value)
        {
            if (value == null)
                return null!;

            if (value is Godot.Collections.Dictionary gdDict)
            {
                var result = new Dictionary<string, object?>();
                foreach (var kv in gdDict)
                {
                    result[kv.Key.ToString()] = ConvertGodotToPlainObject(kv.Value);
                }
                return result;
            }

            if (value is Godot.Collections.Array gdArr)
            {
                var list = new List<object?>();
                foreach (var v in gdArr)
                    list.Add(ConvertGodotToPlainObject(v));
                return list;
            }

            return value;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MULTIPLAYER — Outbound sync wiring
        // ═══════════════════════════════════════════════════════════════════════

        private void WireSyncOutbound()
        {
            // Token dropped (final position after drag)
            _mapController.TokenDropped += token =>
            {
                if (!_networkService.IsConnected || _isSyncing) return;
                if (!CanControlToken(token)) return;
                _syncService.SyncTokenMoved(token.Id, token.Position.X, token.Position.Y);
            };

            // Token added to map
            _mapController.TokenAdded += token =>
            {
                UpdateAssetList();
                if (_combatController.InCombat)
                {
                    _combatController.AddTokenToOrder(token, true);
                    UpdateInitiativePanel();
                }
                ApplyTokenInteractivity(token);

                if (!_networkService.IsConnected || _isSyncing || token.IsGMOnly) return;

                // Serialize token with explicit flat fields — avoids Godot Vector2 serialization issues
                var payload = new
                {
                    id         = token.Id,
                    name       = token.Name,
                    imagePath  = token.ImagePath,
                    posX       = token.Position.X,
                    posY       = token.Position.Y,
                    ownerId    = token.OwnerId,
                    isGMOnly   = token.IsGMOnly,
                    hp         = token.Sheet.HP,
                    pm         = token.Sheet.PM,
                    charClass  = token.Sheet.CharacterClass,
                    race       = token.Sheet.Race,
                    level      = token.Sheet.Level,
                    defense    = token.Sheet.Defense,
                    initiative = token.Sheet.Initiative,
                    forca      = token.Sheet.GetAttributeValue("Força"),
                    destreza   = token.Sheet.GetAttributeValue("Destreza"),
                    const_     = token.Sheet.GetAttributeValue("Constituição"),
                    intel      = token.Sheet.GetAttributeValue("Inteligência"),
                    sab        = token.Sheet.GetAttributeValue("Sabedoria"),
                    carisma    = token.Sheet.GetAttributeValue("Carisma")
                };
                _syncService.SyncTokenSpawned(System.Text.Json.JsonSerializer.Serialize(payload));
            };

            // Token removed from map
            _mapController.TokenRemoved += token =>
            {
                UpdateAssetList();
                if (_combatController.InCombat)
                {
                    _combatController.RemoveTokenFromOrder(token.Id);
                    UpdateInitiativePanel();
                }
                if (!_networkService.IsConnected || _isSyncing) return;
                _syncService.SyncTokenRemoved(token.Id);
            };

            // Fog changed
            _mapController.FogChanged += (cells, reveal) =>
            {
                // Update campaign state
                if (reveal) _currentCampaign.FogRevealedCells.AddRange(
                    cells.Where(c => !_currentCampaign.FogRevealedCells.Contains(c)));
                else         _currentCampaign.FogRevealedCells.RemoveAll(cells.Contains);

                if (!_networkService.IsConnected || _isSyncing) return;
                _syncService.SyncFogUpdate(cells, reveal);
            };

            // Chat message sent by local user
            _chatController.MessageSent += msg =>
            {
                if (!_networkService.IsConnected) return;
                _syncService.SyncChat(msg.Sender, msg.Text, msg.Type.ToString());
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MULTIPLAYER — Inbound sync wiring
        // ═══════════════════════════════════════════════════════════════════════

        private void WireSyncInbound()
        {
            // ── Chat ────────────────────────────────────────────────────────────
            _syncService.RemoteChatReceived += (sender, text, type) =>
            {
                _isSyncing = true;
                var msgType = type switch
                {
                    "Roll"    => ChatMessageType.Roll,
                    "Whisper" => ChatMessageType.Whisper,
                    "System"  => ChatMessageType.System,
                    _         => ChatMessageType.Chat
                };
                // Display with the real sender name, not "Sistema"
                _chatController.AddMessage(new ChatMessage(sender, text, msgType));
                _isSyncing = false;
            };

            // ── Tokens ──────────────────────────────────────────────────────────
            _syncService.RemoteTokenMoved += (id, x, y) =>
            {
                _isSyncing = true;
                _mapController.RemoteUpdateTokenPosition(id, x, y);
                var token = _currentCampaign.Tokens.FirstOrDefault(t => t.Id == id);
                if (token != null) token.Position = new Vector2(x, y);
                _isSyncing = false;
            };

            _syncService.RemoteTokenSpawned += tokenJson =>
            {
                _isSyncing = true;
                try
                {
                    var j = System.Text.Json.JsonDocument.Parse(tokenJson).RootElement;
                    var token = TokenData.Create(JStr(j, "name", "Token"), JStr(j, "imagePath", ""));
                    token.Id       = JStr(j, "id", token.Id);
                    token.OwnerId  = JStr(j, "ownerId", "");
                    token.IsGMOnly = JBool(j, "isGMOnly");
                    token.Position = new Vector2(JFloat(j, "posX"), JFloat(j, "posY"));

                    token.Sheet.HP               = JInt(j, "hp", 10);
                    token.Sheet.PM               = JInt(j, "pm", 10);
                    token.Sheet.CharacterClass   = JStr(j, "charClass", "Guerreiro");
                    token.Sheet.Race             = JStr(j, "race", "Humano");
                    token.Sheet.Level            = JInt(j, "level", 1);
                    token.Sheet.Defense          = JInt(j, "defense", 12);
                    token.Sheet.Initiative       = JInt(j, "initiative", 0);
                    token.Sheet.SetAttributeValue("Força",         JInt(j, "forca",    10));
                    token.Sheet.SetAttributeValue("Destreza",      JInt(j, "destreza", 10));
                    token.Sheet.SetAttributeValue("Constituição",  JInt(j, "const_",   10));
                    token.Sheet.SetAttributeValue("Inteligência",  JInt(j, "intel",    10));
                    token.Sheet.SetAttributeValue("Sabedoria",     JInt(j, "sab",      10));
                    token.Sheet.SetAttributeValue("Carisma",       JInt(j, "carisma",  10));

                    // Don't show GM-only tokens to players
                    if (token.IsGMOnly && _localRole != RoleType.GM) { _isSyncing = false; return; }

                    _currentCampaign.Tokens.Add(token);
                    _mapController.AddToken(token);
                    _chatController.SystemMessage($"Token '{token.Name}' adicionado à mesa.");
                }
                catch (Exception e) { GD.PrintErr($"[RemoteTokenSpawned] {e.Message}"); }
                _isSyncing = false;
            };

            _syncService.RemoteTokenRemoved += id =>
            {
                _isSyncing = true;
                var token = _currentCampaign.Tokens.FirstOrDefault(t => t.Id == id);
                if (token != null)
                {
                    _currentCampaign.Tokens.Remove(token);
                    _mapController.RemoveToken(token);
                }
                _isSyncing = false;
            };

            _syncService.RemoteTokenStats += (id, hp, pm) =>
            {
                _isSyncing = true;
                var token = _currentCampaign.Tokens.FirstOrDefault(t => t.Id == id);
                if (token != null)
                {
                    token.Sheet.HP = hp;
                    token.Sheet.PM = pm;
                    _mapController.RemoteUpdateTokenStats(id, hp, pm);
                    if (_mapController.SelectedToken?.Id == id) UpdateSelectionPanel(token);
                }
                _isSyncing = false;
            };

            _syncService.RemoteDamageApplied += (id, newHP) =>
            {
                _isSyncing = true;
                var token = _currentCampaign.Tokens.FirstOrDefault(t => t.Id == id);
                if (token != null)
                {
                    token.Sheet.HP = newHP;
                    if (_mapController.SelectedToken?.Id == id) UpdateSelectionPanel(token);
                    _chatController.SystemMessage($"{token.Name}: PV → {newHP}");
                }
                _isSyncing = false;
            };

            // ── Combat ──────────────────────────────────────────────────────────
            _syncService.RemoteCombatStarted += (orderIds, rolls, current) =>
            {
                _isSyncing = true;
                _combatController.LoadCombatState(
                    _currentCampaign.Tokens, orderIds, rolls, current, true);
                UpdateInitiativePanel();
                _chatController.SystemMessage("⚔️ Combate iniciado remotamente.");
                _isSyncing = false;
            };

            _syncService.RemoteCombatAdvanced += current =>
            {
                _isSyncing = true;
                _combatController.SetCurrentIndex(current);
                UpdateInitiativePanel();
                var name = _combatController.Current?.Name ?? "?";
                _chatController.SystemMessage($"▶ Vez de: {name}");
                _isSyncing = false;
            };

            _syncService.RemoteCombatEnded += () =>
            {
                _isSyncing = true;
                _combatController.EndCombat();
                UpdateInitiativePanel();
                _chatController.SystemMessage("🏁 Combate encerrado.");
                _isSyncing = false;
            };

            // ── Fog ─────────────────────────────────────────────────────────────
            _syncService.RemoteFogUpdate += (cells, reveal) =>
            {
                _isSyncing = true;
                _mapController.FogLayer.ApplyCells(cells, reveal);
                if (reveal) _currentCampaign.FogRevealedCells.AddRange(
                    cells.Where(c => !_currentCampaign.FogRevealedCells.Contains(c)));
                else         _currentCampaign.FogRevealedCells.RemoveAll(cells.Contains);
                _isSyncing = false;
            };

            _syncService.RemoteFogReset += cells =>
            {
                _isSyncing = true;
                _mapController.FogLayer.SetFullState(cells);
                _currentCampaign.FogRevealedCells = new List<string>(cells);
                _isSyncing = false;
            };

            // ── Journals shared ─────────────────────────────────────────────────
            _syncService.RemoteJournalShared += (id, title, content) =>
            {
                _isSyncing = true;
                _chatController.SystemMessage($"📜 Handout compartilhado: '{title}'");
                // Add to local journal list as player-visible entry
                var existing = _currentCampaign.Journals.FirstOrDefault(j => j.Id == id);
                if (existing != null) { existing.Content = content; existing.Title = title; }
                else _currentCampaign.Journals.Add(new JournalEntry
                {
                    Id = id, Title = title, Content = content,
                    IsVisibleToPlayers = true, Category = "Handout"
                });
                RefreshJournalList();
                _isSyncing = false;
            };

            // ── Full state sync (client receives on join) ─────────────────────
            _syncService.RemoteRoleAssigned += (role, ownedCsv, myId) =>
            {
                _localRole = role == "GM" ? RoleType.GM : RoleType.Player;
                _mapController.ApplyVisibilityMode(_localRole == RoleType.GM);
                var ownedIds = ownedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                var me = new PlayerSession
                {
                    Id = myId, DisplayName = _localPlayerName,
                    Role = _localRole, OwnedTokenIds = ownedIds
                };
                if (!_connectedPlayers.Any(p => p.Id == myId)) _connectedPlayers.Add(me);
                _chatController.PlayerName = _localPlayerName;

                // Refresh interactivity on all existing tokens
                foreach (var t in _currentCampaign.Tokens)
                    ApplyTokenInteractivity(t);

                UpdateSessionUI();
                _chatController.SystemMessage($"Entrou como: {_localRole} — {_localPlayerName}");
            };

            _syncService.RemoteFullStateSync += campaignJson =>
            {
                _isSyncing = true;
                try
                {
                    var parse = Godot.Json.ParseString(campaignJson);
                    if (parse.VariantType == Variant.Type.Dictionary)
                    {
                        var campaign = Campaign.FromDictionary(parse.AsGodotDictionary());
                        // Players don't see GM-only tokens — already handled by AddToken
                        LoadCampaign(campaign);
                        _chatController.SystemMessage("✅ Estado da mesa sincronizado.");
                    }
                }
                catch (Exception e) { GD.PrintErr($"[FullStateSync] {e.Message}"); }
                _isSyncing = false;
            };

            // ── Ownership assignment (host assigns token to player) ────────────
            _syncService.RemoteOwnershipChanged += (tokenId, ownerId) =>
            {
                // Update local session
                var me = _connectedPlayers.FirstOrDefault(p => p.Id == _networkService.LocalId);
                if (me != null && ownerId == _networkService.LocalId)
                {
                    if (!me.OwnedTokenIds.Contains(tokenId)) me.OwnedTokenIds.Add(tokenId);
                    _chatController.SystemMessage($"✅ Você foi atribuído como dono do token '{tokenId}'.");
                }
                // Refresh token interactivity for affected token
                var token = _currentCampaign.Tokens.FirstOrDefault(t => t.Id == tokenId);
                if (token != null)
                {
                    token.OwnerId = ownerId;
                    ApplyTokenInteractivity(token);
                }
            };

            // ── Session events ────────────────────────────────────────────────
            _syncService.RemotePlayerJoined += (id, name, role) =>
            {
                if (_connectedPlayers.Any(p => p.Id == id)) return;
                var session = new PlayerSession
                {
                    Id = id, DisplayName = name, IsConnected = true,
                    Role = role == "GM" ? RoleType.GM : RoleType.Player
                };
                _connectedPlayers.Add(session);
                UpdateSessionUI();
                _chatController.SystemMessage($"🎲 {name} entrou na mesa como {role}.");

                // Host sends full state to the new joiner
                if (_networkService.IsHost)
                {
                    var dict  = _currentCampaign.ToDictionary();
                    var json  = Godot.Json.Stringify(dict);
                    _syncService.SendFullStateTo(id, json, RoleType.Player, id, new List<string>());

                    // Announce to all that someone joined
                    var joinMsg = NetMsg.Encode(NetMsgType.PlayerJoined, _networkService.LocalId,
                        new PlayerJoinedPayload { Id = id, Name = name, Role = role });
                    _ = _networkService.BroadcastAsync(joinMsg);
                }
            };

            _syncService.RemotePlayerLeft += id =>
            {
                var session = _connectedPlayers.FirstOrDefault(p => p.Id == id);
                if (session != null)
                {
                    session.IsConnected = false;
                    _chatController.SystemMessage($"👋 {session.DisplayName} saiu da mesa.");
                    _connectedPlayers.Remove(session);
                    UpdateSessionUI();
                }
            };

            // Host-side: wire NetworkService client events
            _networkService.ClientDisconnected += clientId =>
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    var session = _connectedPlayers.FirstOrDefault(p => p.Id == clientId);
                    if (session != null)
                    {
                        _chatController.SystemMessage($"👋 {session.DisplayName} desconectou.");
                        _connectedPlayers.Remove(session);
                        UpdateSessionUI();
                    }
                    // Broadcast disconnect to others
                    if (_networkService.IsHost)
                    {
                        var msg = NetMsg.Encode(NetMsgType.PlayerLeft, _networkService.LocalId,
                            new PlayerLeftPayload { Id = clientId });
                        _ = _networkService.BroadcastAsync(msg);
                    }
                });
            };
        }

        // Host client connection handler (fires on background thread → queued)
        private void OnClientConnected(string clientId)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                _chatController.SystemMessage($"⏳ Novo jogador conectando ({clientId})...");
            });
        }

        private void OnClientDisconnected(string clientId)
        {
            // Already handled in WireSyncInbound
        }

        // ── Sync helpers ──────────────────────────────────────────────────────
        private bool CanControlToken(TokenData token)
        {
            if (_localRole == RoleType.GM) return true;
            var me = _connectedPlayers.FirstOrDefault(p => p.Id == _networkService.LocalId);
            return me?.CanControlToken(token.Id) ?? false;
        }

        /// <summary>Syncs token stats after any stats change on the host side.</summary>
        private void SyncTokenStatsIfConnected(TokenData token)
        {
            if (!_networkService.IsConnected || _isSyncing) return;
            _syncService.SyncTokenStats(token.Id, token.Sheet.HP, token.Sheet.PM);
        }

        // Also sync combat events — wire into existing handlers
        private void SyncCombatStarted()
        {
            if (!_networkService.IsConnected) return;
            _syncService.SyncCombatStarted(
                _combatController.GetOrderIds(),
                _combatController.GetOrderRolls(),
                _combatController.GetCurrentIndex());
        }

        private void SyncCombatAdvanced()
        {
            if (!_networkService.IsConnected) return;
            _syncService.SyncCombatAdvanced(_combatController.GetCurrentIndex());
        }

        // ═══════════════════════════════════════════════════════════════════════
        // FOG OF WAR UI
        // ═══════════════════════════════════════════════════════════════════════

        private void CreateFogUi()
        {
            var toolbar = GetNode<HBoxContainer>("Toolbar/TopButtons");

            toolbar.AddChild(new VSeparator());

            // Library button (quick access from toolbar)
            var libBtn = new Button { Text = "📖" };
            libBtn.TooltipText = "Biblioteca de Conteúdo";
            libBtn.Pressed += OpenLibraryWindow;
            toolbar.AddChild(libBtn);

            // Owner button
            var ownBtn = new Button { Text = "👤" };
            ownBtn.TooltipText = "Atribuir token a jogador";
            ownBtn.Pressed += OpenOwnershipDialog;
            toolbar.AddChild(ownBtn);

            toolbar.AddChild(new VSeparator());

            _fogToggleButton = new Button { Text = "Névoa OFF", ToggleMode = true };
            _fogRevealButton = new Button { Text = "🔦 Revelar", ToggleMode = true };
            _fogHideButton   = new Button { Text = "🌑 Esconder", ToggleMode = true };
            _fogRevealAllButton = new Button { Text = "Revelar Tudo" };
            _fogHideAllButton   = new Button { Text = "Esconder Tudo" };

            toolbar.AddChild(_fogToggleButton);
            toolbar.AddChild(_fogRevealButton);
            toolbar.AddChild(_fogHideButton);
            toolbar.AddChild(_fogRevealAllButton);
            toolbar.AddChild(_fogHideAllButton);

            _fogToggleButton.Toggled += on =>
            {
                _currentCampaign.FogEnabled = on;
                _mapController.SetFogEnabled(on);
                _fogToggleButton.Text = on ? "Névoa ON" : "Névoa OFF";
                _chatController.SystemMessage($"Névoa de guerra: {(on ? "ativada" : "desativada")}.");
            };

            _fogRevealButton.Toggled += on =>
            {
                if (on) _fogHideButton!.ButtonPressed = false;
                _fogToolActive = on;
                _mapController.SetFogToolActive(on, true);
                if (on) _chatController.SystemMessage("🔦 Ferramenta: REVELAR (clique no mapa).");
            };

            _fogHideButton.Toggled += on =>
            {
                if (on) _fogRevealButton!.ButtonPressed = false;
                _fogToolActive = on;
                _mapController.SetFogToolActive(on, false);
                if (on) _chatController.SystemMessage("🌑 Ferramenta: ESCONDER (clique no mapa).");
            };

            _fogRevealAllButton.Pressed += () =>
            {
                _mapController.FogLayer.RevealAll();
                _currentCampaign.FogRevealedCells = _mapController.FogLayer.GetRevealedCells();
                if (_networkService.IsConnected)
                    _syncService.SyncFogReset(_currentCampaign.FogRevealedCells);
                _chatController.SystemMessage("Névoa removida de todo o mapa.");
            };

            _fogHideAllButton.Pressed += () =>
            {
                _mapController.FogLayer.HideAll();
                _currentCampaign.FogRevealedCells.Clear();
                if (_networkService.IsConnected)
                    _syncService.SyncFogReset(new List<string>());
                _chatController.SystemMessage("Névoa aplicada a todo o mapa.");
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SESSION PANEL
        // ═══════════════════════════════════════════════════════════════════════

        private void CreateSessionUi()
        {
            var sidebar = GetNode<VBoxContainer>("SidebarPanel/SidebarVBox");

            var sep = new HSeparator();
            sidebar.AddChild(sep);

            var title = new Label { Text = "━ Sessão Online ━" };
            sidebar.AddChild(title);

            _sessionStatusLabel = new Label { Text = "Offline (local)", AutowrapMode = TextServer.AutowrapMode.Word };
            sidebar.AddChild(_sessionStatusLabel);

            _sessionPlayersLabel = new Label
            {
                Text = "",
                AutowrapMode = TextServer.AutowrapMode.Word,
                CustomMinimumSize = new Vector2(0, 60)
            };
            sidebar.AddChild(_sessionPlayersLabel);

            var sessionBtns = new HBoxContainer();

            var assignOwnerBtn = new Button { Text = "👤 Atribuir Token" };
            assignOwnerBtn.Pressed += OpenOwnershipDialog;

            var libraryBtn = new Button { Text = "📖 Biblioteca" };
            libraryBtn.Pressed += OpenLibraryWindow;

            var disconnectBtn = new Button { Text = "⏏ Sair" };
            disconnectBtn.Pressed += () =>
            {
                _networkService.Stop();
                _connectedPlayers.Clear();
                UpdateSessionUI();
                _chatController.SystemMessage("Desconectado da sessão.");
                _lobbyOverlay!.Visible = true;
            };

            sessionBtns.AddChild(assignOwnerBtn);
            sessionBtns.AddChild(libraryBtn);
            sessionBtns.AddChild(disconnectBtn);
            sidebar.AddChild(sessionBtns);
        }

        private void UpdateSessionUI()
        {
            if (_sessionStatusLabel == null) return;

            if (!_networkService.IsConnected)
            {
                _sessionStatusLabel.Text = "Offline (local)";
                _sessionPlayersLabel!.Text = "";
                return;
            }

            if (_networkService.IsHost)
                _sessionStatusLabel.Text = $"🟢 Host ativo — {_networkService.ClientCount} jogador(es)";
            else
                _sessionStatusLabel.Text = "🟢 Conectado como jogador";

            var lines = _connectedPlayers
                .Select(p => $"  {(p.IsGM ? "👑" : "🎲")} {p.DisplayName}{(p.IsConnected ? "" : " (off)"})")
                .ToList();
            _sessionPlayersLabel!.Text = string.Join("\n", lines);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // JOURNAL / HANDOUT UI
        // ═══════════════════════════════════════════════════════════════════════

        private void CreateJournalUi()
        {
            // Add Journal button to toolbar
            var toolbar = GetNode<HBoxContainer>("Toolbar/TopButtons");
            var journalBtn = new Button { Text = "📔 Diário" };
            toolbar.AddChild(journalBtn);
            journalBtn.Pressed += OpenJournalWindow;

            // Build the journal window
            _journalWindow = new Window
            {
                Title = "Diário & Handouts",
                Size  = new Vector2I(700, 500),
                Visible = false
            };
            AddChild(_journalWindow);

            var root = new HBoxContainer();
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 8);
            _journalWindow.AddChild(root);

            // Left panel — list + controls
            var leftVBox = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
            root.AddChild(leftVBox);

            leftVBox.AddChild(new Label { Text = "Anotações" });

            _journalList = new ItemList { CustomMinimumSize = new Vector2(0, 320), SizeFlagsVertical = (int)Control.SizeFlags.ExpandFill };
            leftVBox.AddChild(_journalList);

            var listBtns = new HBoxContainer();
            var newBtn  = new Button { Text = "Novo", SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            var delBtn  = new Button { Text = "Del"  };
            listBtns.AddChild(newBtn);
            listBtns.AddChild(delBtn);
            leftVBox.AddChild(listBtns);

            var shareBtn = new Button { Text = "📤 Compartilhar" };
            leftVBox.AddChild(shareBtn);

            // Right panel — editor
            var rightVBox = new VBoxContainer { SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            root.AddChild(rightVBox);

            var titleRow = new HBoxContainer();
            titleRow.AddChild(new Label { Text = "Título:" });
            _journalTitle = new LineEdit { PlaceholderText = "Título da anotação", SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            titleRow.AddChild(_journalTitle);
            rightVBox.AddChild(titleRow);

            var catRow = new HBoxContainer();
            catRow.AddChild(new Label { Text = "Categoria:" });
            _journalCategory = new OptionButton();
            foreach (var cat in new[] { "Geral", "NPC", "Local", "Lore", "Sessão", "Handout" })
                _journalCategory.AddItem(cat);
            catRow.AddChild(_journalCategory);
            _journalVisibleToggle = new CheckButton { Text = "Visível p/ jogadores" };
            catRow.AddChild(_journalVisibleToggle);
            rightVBox.AddChild(catRow);

            _journalContent = new TextEdit
            {
                PlaceholderText = "Escreva sua anotação aqui...",
                SizeFlagsVertical = (int)Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 340),
                WrapMode = TextEdit.LineWrappingMode.Boundary
            };
            rightVBox.AddChild(_journalContent);

            var saveBtnRow = new HBoxContainer();
            var saveBtn = new Button { Text = "💾 Salvar", SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            saveBtnRow.AddChild(saveBtn);
            rightVBox.AddChild(saveBtnRow);

            // ── Event wiring ─────────────────────────────────────────────────
            newBtn.Pressed += () =>
            {
                var entry = new JournalEntry { Title = "Nova Anotação" };
                _currentCampaign.Journals.Add(entry);
                RefreshJournalList();
                _journalList!.Select(_currentCampaign.Journals.Count - 1);
                _selectedJournalIndex = _currentCampaign.Journals.Count - 1;
                LoadJournalEntry(entry);
            };

            delBtn.Pressed += () =>
            {
                if (_selectedJournalIndex < 0 || _selectedJournalIndex >= _currentCampaign.Journals.Count) return;
                _currentCampaign.Journals.RemoveAt(_selectedJournalIndex);
                _selectedJournalIndex = -1;
                RefreshJournalList();
                ClearJournalEditor();
            };

            saveBtn.Pressed += SaveSelectedJournalEntry;

            shareBtn.Pressed += () =>
            {
                if (_selectedJournalIndex < 0 || _selectedJournalIndex >= _currentCampaign.Journals.Count) return;
                SaveSelectedJournalEntry();
                var entry = _currentCampaign.Journals[_selectedJournalIndex];
                entry.IsVisibleToPlayers = true;
                if (_networkService.IsConnected)
                    _syncService.SyncJournalShared(entry.Id, entry.Title, entry.Content);
                _chatController.SystemMessage($"📜 Handout '{entry.Title}' compartilhado com jogadores.");
            };

            _journalList.ItemSelected += idx =>
            {
                _selectedJournalIndex = (int)idx;
                if (_selectedJournalIndex >= 0 && _selectedJournalIndex < _currentCampaign.Journals.Count)
                    LoadJournalEntry(_currentCampaign.Journals[_selectedJournalIndex]);
            };

            _journalWindow.CloseRequested += () => _journalWindow.Visible = false;

            RefreshJournalList();
        }

        private void OpenJournalWindow()
        {
            if (_journalWindow == null) return;
            RefreshJournalList();
            _journalWindow.Visible = true;
            _journalWindow.GrabFocus();
        }

        private void RefreshJournalList()
        {
            if (_journalList == null) return;
            _journalList.Clear();
            foreach (var j in _currentCampaign.Journals)
            {
                var icon = j.IsVisibleToPlayers ? "📤" : "🔒";
                _journalList.AddItem($"{icon} [{j.Category}] {j.Title}");
            }
        }

        private void LoadJournalEntry(JournalEntry entry)
        {
            if (_journalTitle != null)   _journalTitle.Text = entry.Title;
            if (_journalContent != null) _journalContent.Text = entry.Content;
            if (_journalCategory != null)
            {
                var cats = new[] { "Geral", "NPC", "Local", "Lore", "Sessão", "Handout" };
                var idx  = Array.IndexOf(cats, entry.Category);
                _journalCategory.Selected = idx >= 0 ? idx : 0;
            }
            if (_journalVisibleToggle != null)
                _journalVisibleToggle.ButtonPressed = entry.IsVisibleToPlayers;
        }

        private void SaveSelectedJournalEntry()
        {
            if (_selectedJournalIndex < 0 || _selectedJournalIndex >= _currentCampaign.Journals.Count) return;
            var entry = _currentCampaign.Journals[_selectedJournalIndex];
            if (_journalTitle != null)         entry.Title   = _journalTitle.Text;
            if (_journalContent != null)       entry.Content = _journalContent.Text;
            if (_journalCategory != null)      entry.Category = _journalCategory.GetItemText(_journalCategory.Selected);
            if (_journalVisibleToggle != null)  entry.IsVisibleToPlayers = _journalVisibleToggle.ButtonPressed;
            RefreshJournalList();
        }

        private void ClearJournalEditor()
        {
            if (_journalTitle   != null) _journalTitle.Text   = "";
            if (_journalContent != null) _journalContent.Text = "";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Helper: convert plain dict back to Godot Dictionary (for token sync)
        // ═══════════════════════════════════════════════════════════════════════

        private Godot.Collections.Dictionary ConvertPlainToGodotDict(
            System.Collections.Generic.Dictionary<string, object> plain)
        {
            var gd = new Godot.Collections.Dictionary();
            foreach (var kv in plain)
            {
                gd[kv.Key] = kv.Value switch
                {
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                        => je.GetString() ?? "",
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number
                        => je.TryGetInt32(out var i) ? (Variant)i : (Variant)je.GetDouble(),
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.True  => true,
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.False => false,
                    _ => kv.Value?.ToString() ?? ""
                };
            }
            return gd;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // JSON ELEMENT HELPERS (for flat token deserialization)
        // ═══════════════════════════════════════════════════════════════════════
        private static string JStr(System.Text.Json.JsonElement j, string key, string def = "")
            => j.TryGetProperty(key, out var v) ? v.GetString() ?? def : def;
        private static int JInt(System.Text.Json.JsonElement j, string key, int def = 0)
            => j.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;
        private static float JFloat(System.Text.Json.JsonElement j, string key, float def = 0f)
            => j.TryGetProperty(key, out var v) ? (float)v.GetDouble() : def;
        private static bool JBool(System.Text.Json.JsonElement j, string key, bool def = false)
            => j.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;

        // ═══════════════════════════════════════════════════════════════════════
        // OWNERSHIP — set token interactivity based on role
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets CanInteract on the TokenControl that corresponds to this token.
        /// GM can always interact; players only interact with their own tokens.
        /// </summary>
        private void ApplyTokenInteractivity(TokenData token)
        {
            // MapController doesn't expose the token nodes directly,
            // so we apply via a method on MapController
            _mapController.SetTokenInteractivity(token.Id, CanControlToken(token));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LOCAL IP HELPER
        // ═══════════════════════════════════════════════════════════════════════
        private static string GetLocalIP()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return "127.0.0.1";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LIBRARY WINDOW  (ETAPA 9 — Biblioteca de Conteúdo com busca/filtro)
        // ═══════════════════════════════════════════════════════════════════════

        private Window? _libraryWindow;
        private LineEdit? _librarySearch;
        private TabContainer? _libraryTabs;

        private void OpenLibraryWindow()
        {
            if (_libraryWindow == null) BuildLibraryWindow();
            _libraryWindow!.Visible = true;
            _libraryWindow.GrabFocus();
            FilterLibrary(_librarySearch?.Text ?? "");
        }

        private void BuildLibraryWindow()
        {
            _libraryWindow = new Window
            {
                Title   = "📖 Biblioteca de Conteúdo Tormenta20",
                Size    = new Vector2I(700, 560),
                Visible = false
            };
            AddChild(_libraryWindow);

            var vbox = new VBoxContainer();
            vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 8);
            _libraryWindow.AddChild(vbox);

            // Search bar
            var searchRow = new HBoxContainer();
            searchRow.AddChild(new Label { Text = "🔍 Buscar:" });
            _librarySearch = new LineEdit
            {
                PlaceholderText     = "Digite para filtrar…",
                SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill
            };
            searchRow.AddChild(_librarySearch);
            vbox.AddChild(searchRow);

            _librarySearch.TextChanged += _ => FilterLibrary(_librarySearch.Text);

            // Tabs
            _libraryTabs = new TabContainer { SizeFlagsVertical = (int)Control.SizeFlags.ExpandFill };
            vbox.AddChild(_libraryTabs);

            void AddTab(string name, IEnumerable<string> items, Action<string> onApply, string applyLabel)
            {
                var tab  = new VBoxContainer { Name = name };
                var list = new ItemList
                {
                    Name = $"Lib{name}List",
                    CustomMinimumSize = new Vector2(0, 380),
                    SizeFlagsVertical = (int)Control.SizeFlags.ExpandFill
                };
                foreach (var item in items) list.AddItem(item);

                var info = new Label { AutowrapMode = TextServer.AutowrapMode.Word, Text = "" };
                var btn  = new Button { Text = applyLabel };
                btn.Pressed += () =>
                {
                    var sel = list.GetSelectedItems();
                    if (sel.Length == 0) return;
                    onApply(list.GetItemText(sel[0]));
                };
                list.ItemSelected += idx => info.Text = list.GetItemText((int)idx);
                tab.AddChild(list);
                tab.AddChild(info);
                tab.AddChild(btn);
                _libraryTabs.AddChild(tab);
            }

            AddTab("Classes",   _contentService.Classes.Select(c => $"{c.Name}  —  d{c.HitDie}  {c.Description}"),
                name => OnApplyContentByName("class", name.Split("  —  ")[0]), "Aplicar ao token selecionado");

            AddTab("Raças",     _contentService.Races.Select(r => $"{r.Name}  —  {r.Description}"),
                name => OnApplyContentByName("race",  name.Split("  —  ")[0]), "Aplicar ao token selecionado");

            AddTab("Poderes",   _contentService.Powers.Select(p => $"{p.Name}  ({p.Type})  —  {p.Description}"),
                name => OnApplyContentByName("power", name.Split("  —  ")[0]), "Usar poder");

            AddTab("Magias",    _contentService.Spells.Select(s => $"{s.Name}  Nv{s.Level} [{s.School}]  —  {s.Description}"),
                name => OnApplyContentByName("spell", name.Split("  —  ")[0]), "Lançar magia");

            AddTab("Condições", _contentService.Conditions.Select(c => $"{c.Name}  —  {c.Description}"),
                name => OnApplyContentByName("condition", name.Split("  —  ")[0]), "Aplicar ao token selecionado");

            AddTab("Ameaças",   _contentService.Threats.Select(t => $"{t.Name}  Nv{t.Level}  PV:{t.HP}  DEF:{t.Defense}"),
                name => OnApplyContentByName("threat", name.Split("  ")[0]), "Spawnar no mapa");

            _libraryWindow.CloseRequested += () => _libraryWindow.Visible = false;
        }

        private void FilterLibrary(string term)
        {
            if (_libraryTabs == null) return;
            term = term.Trim().ToLowerInvariant();

            for (int t = 0; t < _libraryTabs.GetTabCount(); t++)
            {
                var tab = _libraryTabs.GetTabControl(t);
                var list = tab.GetChildren().OfType<ItemList>().FirstOrDefault();
                if (list == null) continue;

                for (int i = 0; i < list.ItemCount; i++)
                {
                    var text    = list.GetItemText(i).ToLowerInvariant();
                    var visible = string.IsNullOrEmpty(term) || text.Contains(term);
                    list.SetItemDisabled(i, !visible);
                    // Godot 4: hide via custom colour — dimmed when not matching
                    list.SetItemCustomFgColor(i, visible ? Colors.White : new Color(1, 1, 1, 0.2f));
                }
            }
        }

        private void OnApplyContentByName(string kind, string name)
        {
            var token = _mapController.SelectedToken;
            switch (kind)
            {
                case "class":
                    var cls = FindClassByName(name);
                    if (cls != null && token != null)
                    {
                        ApplyClassDefinition(token, cls);
                        UpdateSelectionPanel(token);
                        SyncTokenStatsIfConnected(token);
                        _chatController.SystemMessage($"Classe '{cls.Name}' aplicada a {token.Name}.");
                    }
                    else _chatController.SystemMessage("Selecione um token e verifique se a classe existe.");
                    break;

                case "race":
                    var rce = _contentService.Races.FirstOrDefault(r =>
                        r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (rce != null && token != null)
                    {
                        token.Sheet.Race = rce.Name;
                        if (rce.AttributeBonus != null)
                            foreach (var kv in rce.AttributeBonus)
                                if (token.Sheet.Attributes.ContainsKey(kv.Key))
                                    token.Sheet.Attributes[kv.Key] += kv.Value;
                        UpdateSelectionPanel(token);
                        SyncTokenStatsIfConnected(token);
                        _chatController.SystemMessage($"Raça '{rce.Name}' aplicada a {token.Name}.");
                    }
                    else _chatController.SystemMessage("Selecione um token e verifique se a raça existe.");
                    break;

                case "power":
                    var pwr = _contentService.Powers.FirstOrDefault(p =>
                        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (pwr != null && token != null)
                    {
                        _chatController.SystemMessage($"Poder '{pwr.Name}': {pwr.Description}");
                    }
                    else _chatController.SystemMessage("Selecione um token.");
                    break;

                case "spell":
                    var spl = FindSpellByName(name);
                    if (spl == null) { _chatController.SystemMessage($"Magia '{name}' não encontrada."); break; }
                    if (token == null) { _chatController.SystemMessage("Selecione um token para lançar a magia."); break; }
                    if (token.Sheet.PM < spl.CostPM)
                    {
                        _chatController.SystemMessage($"PM insuficiente ({token.Sheet.PM}/{spl.CostPM}).");
                        break;
                    }
                    token.Sheet.PM -= spl.CostPM;
                    _chatController.SystemMessage($"✨ {token.Name} lança {spl.Name} (custos {spl.CostPM} PM). Efeito: {spl.Effect}");
                    UpdateSelectionPanel(token);
                    SyncTokenStatsIfConnected(token);
                    break;

                case "condition":
                    var cond = _contentService.Conditions.FirstOrDefault(c =>
                        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (cond != null && token != null)
                    {
                        token.Sheet.AddCondition(cond.Name, -1);
                        UpdateSelectionPanel(token);
                        SyncTokenStatsIfConnected(token);
                        if (_networkService.IsConnected && !_isSyncing)
                            _syncService.SyncChat("Sistema", $"[Condição] {token.Name} → +{cond.Name}", "System");
                        _chatController.SystemMessage($"Condição '{cond.Name}' aplicada a {token.Name}.");
                    }
                    else _chatController.SystemMessage("Selecione um token.");
                    break;

                case "threat":
                    _chatController.SystemMessage($"Selecione '{name}' na lista de ameaças e clique Spawnar.");
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TOKEN OWNERSHIP ASSIGNMENT  (ETAPA 4)
        // ═══════════════════════════════════════════════════════════════════════

        private void OpenOwnershipDialog()
        {
            var token = _mapController.SelectedToken;
            if (token == null) { _chatController.SystemMessage("Selecione um token primeiro."); return; }
            if (_localRole != RoleType.GM) { _chatController.SystemMessage("Apenas o GM pode atribuir ownership."); return; }
            if (_connectedPlayers.Count <= 1) { _chatController.SystemMessage("Nenhum jogador conectado."); return; }

            var dlg = new AcceptDialog { Title = $"Atribuir owner: {token.Name}" };
            var vb  = new VBoxContainer();
            dlg.AddChild(vb);

            vb.AddChild(new Label { Text = "Selecione o jogador responsável:" });
            var opts = new OptionButton();
            opts.AddItem("(sem dono — GM controla)");
            foreach (var p in _connectedPlayers.Where(p => !p.IsGM))
                opts.AddItem($"{p.DisplayName}  [{p.Id}]");
            vb.AddChild(opts);

            var gmOnlyRow = new HBoxContainer();
            var gmOnly = new CheckButton { Text = "Visível apenas para o GM" };
            gmOnly.ButtonPressed = token.IsGMOnly;
            gmOnlyRow.AddChild(gmOnly);
            vb.AddChild(gmOnlyRow);

            AddChild(dlg);
            dlg.PopupCentered();

            dlg.Confirmed += () =>
            {
                // Apply GM-only flag
                token.IsGMOnly = gmOnly.ButtonPressed;

                // Apply owner
                if (opts.Selected <= 0)
                {
                    token.OwnerId = "";
                    _chatController.SystemMessage($"{token.Name}: sem dono atribuído.");
                }
                else
                {
                    var label    = opts.GetItemText(opts.Selected);
                    var playerId = label.Split('[', ']')[1];
                    token.OwnerId = playerId;

                    var player = _connectedPlayers.FirstOrDefault(p => p.Id == playerId);
                    if (player != null && !player.OwnedTokenIds.Contains(token.Id))
                        player.OwnedTokenIds.Add(token.Id);

                    _chatController.SystemMessage($"{token.Name}: atribuído a {player?.DisplayName ?? playerId}.");

                    // Notify the player of their new ownership via network
                    if (_networkService.IsConnected)
                    {
                        var ownMsg = NetMsg.Encode(NetMsgType.TokenOwnership, _networkService.LocalId,
                            new TokenOwnershipPayload { TokenId = token.Id, OwnerId = playerId });
                        _ = _networkService.SendAsync(ownMsg);
                    }
                }

                // Update visibility on map
                _mapController.ApplyVisibilityMode(_localRole == RoleType.GM);
                ApplyTokenInteractivity(token);
                dlg.QueueFree();
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LOBBY SCREEN
        // ═══════════════════════════════════════════════════════════════════════
        private void CreateLobbyUi()
        {
            // Full-screen overlay — added last so it renders on top
            _lobbyOverlay = new ColorRect
            {
                Color             = new Color(0.08f, 0.08f, 0.12f, 0.97f),
                AnchorRight       = 1f,
                AnchorBottom      = 1f,
                MouseFilter       = MouseFilterEnum.Stop
            };
            AddChild(_lobbyOverlay);

            // Centred card
            var card = new PanelContainer();
            card.SetAnchorsPreset(Control.LayoutPreset.Center);
            card.CustomMinimumSize = new Vector2(480, 480);
            _lobbyOverlay.AddChild(card);

            var vbox = new VBoxContainer();
            vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 20);
            card.AddChild(vbox);

            // ── Title ─────────────────────────────────────────────────────────
            var title = new Label
            {
                Text                = "⚔  TORMENTA VTT",
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize   = new Vector2(0, 48)
            };
            title.AddThemeFontSizeOverride("font_size", 28);
            vbox.AddChild(title);

            var subtitle = new Label
            {
                Text                = "Virtual Tabletop para Tormenta20",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            vbox.AddChild(subtitle);
            vbox.AddChild(new HSeparator());

            // ── Status message ────────────────────────────────────────────────
            _lobbyStatusMsg = new Label
            {
                Text                = "",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.Word
            };
            vbox.AddChild(_lobbyStatusMsg);

            // ── Main menu buttons ─────────────────────────────────────────────
            var mainMenu = new VBoxContainer { Name = "LobbyMainMenu" };
            vbox.AddChild(mainMenu);

            Button MakeBtn(string text) => new Button
            {
                Text = text, CustomMinimumSize = new Vector2(0, 40)
            };

            var btnNova      = MakeBtn("📄  Nova Campanha");
            var btnCarregar  = MakeBtn("📂  Carregar Campanha");
            var btnHost      = MakeBtn("🌐  Hospedar Mesa");
            var btnJoin      = MakeBtn("🔗  Entrar em Mesa");
            var btnSair      = MakeBtn("❌  Sair");

            mainMenu.AddChild(btnNova);
            mainMenu.AddChild(btnCarregar);
            mainMenu.AddChild(new HSeparator());
            mainMenu.AddChild(btnHost);
            mainMenu.AddChild(btnJoin);
            mainMenu.AddChild(new HSeparator());
            mainMenu.AddChild(btnSair);

            // ── Host sub-panel ────────────────────────────────────────────────
            var hostPanel = new VBoxContainer { Name = "LobbyHostPanel", Visible = false };
            vbox.AddChild(hostPanel);

            hostPanel.AddChild(new Label { Text = "━━  Hospedar Mesa  ━━",
                HorizontalAlignment = HorizontalAlignment.Center });

            var localIp = GetLocalIP();
            _lobbyHostIpLabel = new Label { Text = $"Seu IP: {localIp}" };
            hostPanel.AddChild(_lobbyHostIpLabel);

            var portRow  = new HBoxContainer();
            portRow.AddChild(new Label { Text = "Porta:" });
            var portEdit = new LineEdit { Text = "12345", CustomMinimumSize = new Vector2(100, 0) };
            portRow.AddChild(portEdit);
            hostPanel.AddChild(portRow);

            var nameRowH = new HBoxContainer();
            nameRowH.AddChild(new Label { Text = "Seu nome:" });
            var nameEditH = new LineEdit { Text = "Mestre", CustomMinimumSize = new Vector2(180, 0) };
            nameRowH.AddChild(nameEditH);
            hostPanel.AddChild(nameRowH);

            var hBtnRow  = new HBoxContainer();
            var btnIniciar = new Button { Text = "▶  Iniciar Mesa",
                SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            var btnVoltar1 = new Button { Text = "← Voltar" };
            hBtnRow.AddChild(btnIniciar);
            hBtnRow.AddChild(btnVoltar1);
            hostPanel.AddChild(hBtnRow);

            // ── Join sub-panel ────────────────────────────────────────────────
            var joinPanel = new VBoxContainer { Name = "LobbyJoinPanel", Visible = false };
            vbox.AddChild(joinPanel);

            joinPanel.AddChild(new Label { Text = "━━  Entrar em Mesa  ━━",
                HorizontalAlignment = HorizontalAlignment.Center });

            var ipRow   = new HBoxContainer();
            ipRow.AddChild(new Label { Text = "IP do Host:" });
            var ipEdit  = new LineEdit { PlaceholderText = "ex: 192.168.1.10",
                CustomMinimumSize = new Vector2(200, 0) };
            ipRow.AddChild(ipEdit);
            joinPanel.AddChild(ipRow);

            var jPortRow  = new HBoxContainer();
            jPortRow.AddChild(new Label { Text = "Porta:" });
            var jPortEdit = new LineEdit { Text = "12345", CustomMinimumSize = new Vector2(100, 0) };
            jPortRow.AddChild(jPortEdit);
            joinPanel.AddChild(jPortRow);

            var nameRowJ   = new HBoxContainer();
            nameRowJ.AddChild(new Label { Text = "Seu nome:" });
            var nameEditJ  = new LineEdit { Text = "Jogador",
                CustomMinimumSize = new Vector2(180, 0) };
            nameRowJ.AddChild(nameEditJ);
            joinPanel.AddChild(nameRowJ);

            var jBtnRow  = new HBoxContainer();
            var btnConectar = new Button { Text = "🔗  Conectar",
                SizeFlagsHorizontal = (int)Control.SizeFlags.ExpandFill };
            var btnVoltar2  = new Button { Text = "← Voltar" };
            jBtnRow.AddChild(btnConectar);
            jBtnRow.AddChild(btnVoltar2);
            joinPanel.AddChild(jBtnRow);

            // ── Wiring ────────────────────────────────────────────────────────
            void ShowMain()
            {
                mainMenu.Visible  = true;
                hostPanel.Visible = false;
                joinPanel.Visible = false;
                _lobbyStatusMsg!.Text = "";
            }

            btnNova.Pressed += () =>
            {
                _currentCampaign = Campaign.CreateDefault();
                LoadCampaign(_currentCampaign);
                _lobbyOverlay!.Visible = false;
            };

            btnCarregar.Pressed += () =>
            {
                _lobbyOverlay!.Visible = false;
                GetNode<FileDialog>("CampaignOpenDialog").PopupCenteredRatio();
            };

            btnSair.Pressed += () => GetTree().Quit();

            btnHost.Pressed += () =>
            {
                mainMenu.Visible  = false;
                hostPanel.Visible = true;
                _lobbyHostIpLabel!.Text = $"Seu IP: {GetLocalIP()}";
            };

            btnJoin.Pressed += () =>
            {
                mainMenu.Visible  = false;
                joinPanel.Visible = true;
            };

            btnVoltar1.Pressed += () => ShowMain();
            btnVoltar2.Pressed += () => ShowMain();

            btnIniciar.Pressed += () =>
            {
                var name = nameEditH.Text.Trim();
                if (!string.IsNullOrEmpty(name)) _localPlayerName = name;
                if (_playerNameInput != null) _playerNameInput.Text = _localPlayerName;
                _localRole = RoleType.GM;
                _chatController.PlayerName = _localPlayerName;

                if (!int.TryParse(portEdit.Text.Trim(), out var port)) port = 12345;
                var ok = _networkService.StartHost(port);
                if (!ok)
                {
                    _lobbyStatusMsg!.Text = "❌ Falha ao iniciar — porta em uso?";
                    return;
                }

                _networkService.ClientConnected    -= OnClientConnected;
                _networkService.ClientDisconnected -= OnClientDisconnected;
                _networkService.ClientConnected    += OnClientConnected;
                _networkService.ClientDisconnected += OnClientDisconnected;

                _mapController.ApplyVisibilityMode(true);
                _connectedPlayers.Clear();
                _connectedPlayers.Add(new PlayerSession
                {
                    Id = _networkService.LocalId, DisplayName = _localPlayerName,
                    Role = RoleType.GM, IsConnected = true
                });
                UpdateSessionUI();
                _lobbyOverlay!.Visible = false;
                _chatController.SystemMessage(
                    $"✅ Mesa aberta na porta {port}. Seu IP: {GetLocalIP()} — compartilhe com os jogadores.");
            };

            btnConectar.Pressed += () =>
            {
                var ip   = ipEdit.Text.Trim();
                var name = nameEditJ.Text.Trim();
                if (string.IsNullOrEmpty(ip)) { _lobbyStatusMsg!.Text = "Digite o IP do host."; return; }
                if (!string.IsNullOrEmpty(name)) _localPlayerName = name;
                if (_playerNameInput != null) _playerNameInput.Text = _localPlayerName;
                _localRole = RoleType.Player;
                _chatController.PlayerName = _localPlayerName;

                if (!int.TryParse(jPortEdit.Text.Trim(), out var port)) port = 12345;
                _lobbyStatusMsg!.Text = $"Conectando a {ip}:{port}…";

                var ok = _networkService.Join(ip, port);
                if (!ok)
                {
                    _lobbyStatusMsg.Text = $"❌ Falha ao conectar a {ip}:{port}";
                    return;
                }

                _mapController.ApplyVisibilityMode(false);
                _lobbyOverlay!.Visible = false;
                _syncService.RequestFullState(_localPlayerName);
                _chatController.SystemMessage($"🔗 Conectado como '{_localPlayerName}'. Aguardando estado da mesa…");
            };
        }

    }
}
