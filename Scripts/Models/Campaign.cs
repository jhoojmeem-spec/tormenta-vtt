using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace TormentaVTT.Models
{
    public sealed class Campaign
    {
        public string Name { get; set; } = "Nova Campanha";
        public string Description { get; set; } = "Campanha Tormenta20";
        public List<string> Players { get; set; } = new();
        public string GM { get; set; } = "Mestre";
        public string MapImagePath { get; set; } = string.Empty;
        public bool GridEnabled { get; set; } = true;
        public float Zoom { get; set; } = 1.0f;
        public List<TokenData> Tokens { get; set; } = new();
        public bool CombatActive { get; set; } = false;
        public List<string> CombatOrder { get; set; } = new();
        public System.Collections.Generic.Dictionary<string, int> CombatOrderRolls { get; set; } = new();
        public int CombatCurrentIndex { get; set; } = -1;

        public static Campaign CreateDefault()
        {
            return new Campaign();
        }

        public Godot.Collections.Dictionary ToDictionary()
        {
            var playersArray = new Godot.Collections.Array();
            foreach (var player in Players)
            {
                playersArray.Add(player);
            }

            var tokensArray = new Godot.Collections.Array();
            foreach (var token in Tokens)
            {
                tokensArray.Add(token.ToDictionary());
            }

            var combatOrderArray = new Godot.Collections.Array();
            foreach (var entry in CombatOrder)
            {
                combatOrderArray.Add(entry);
            }

            var combatRollsDict = new Godot.Collections.Dictionary();
            foreach (var rollEntry in CombatOrderRolls)
            {
                combatRollsDict[rollEntry.Key] = rollEntry.Value;
            }

            return new Godot.Collections.Dictionary
            {
                ["name"] = Name,
                ["description"] = Description,
                ["players"] = playersArray,
                ["gm"] = GM,
                ["map_image_path"] = MapImagePath,
                ["grid_enabled"] = GridEnabled,
                ["zoom"] = Zoom,
                ["tokens"] = tokensArray,
                ["combat_active"] = CombatActive,
                ["combat_order"] = combatOrderArray,
                ["combat_rolls"] = combatRollsDict,
                ["combat_current_index"] = CombatCurrentIndex
            };
        }

        public static Campaign FromDictionary(Godot.Collections.Dictionary data)
        {
            var campaign = CreateDefault();
            campaign.Name = data.GetValueOrDefault("name", campaign.Name).ToString();
            campaign.Description = data.GetValueOrDefault("description", campaign.Description).ToString();
            campaign.GM = data.GetValueOrDefault("gm", campaign.GM).ToString();
            campaign.MapImagePath = data.GetValueOrDefault("map_image_path", string.Empty).ToString();

            if (data.TryGetValue("grid_enabled", out var gridEnabledRaw))
            {
                campaign.GridEnabled = gridEnabledRaw.AsBool();
            }

            if (data.TryGetValue("zoom", out var zoomRaw))
            {
                campaign.Zoom = zoomRaw.AsSingle();
            }

            if (data.TryGetValue("players", out var playersRaw))
            {
                var playersArray = playersRaw.AsGodotArray();
                campaign.Players = new List<string>();
                foreach (var player in playersArray)
                {
                    campaign.Players.Add(player.AsString());
                }
            }

            if (data.TryGetValue("tokens", out var tokensRaw))
            {
                var tokensArray = tokensRaw.AsGodotArray();
                campaign.Tokens = new List<TokenData>();
                foreach (var tokenObject in tokensArray)
                {
                    var tokenDict = tokenObject.AsGodotDictionary();
                    if (tokenDict.Count > 0)
                    {
                        campaign.Tokens.Add(TokenData.FromDictionary(tokenDict));
                    }
                }
            }

            if (data.TryGetValue("combat_active", out var combatRaw))
            {
                campaign.CombatActive = combatRaw.AsBool();
            }

            if (data.TryGetValue("combat_order", out var orderRaw))
            {
                campaign.CombatOrder = new List<string>();
                var orderArray = orderRaw.AsGodotArray();
                foreach (var item in orderArray)
                {
                    campaign.CombatOrder.Add(item.ToString());
                }
            }

            if (data.TryGetValue("combat_rolls", out var rollsRaw))
            {
                campaign.CombatOrderRolls = new System.Collections.Generic.Dictionary<string, int>();
                var rollsDict = rollsRaw.AsGodotDictionary();
                foreach (var entry in rollsDict)
                {
                    campaign.CombatOrderRolls[entry.Key.ToString()] = entry.Value.AsInt32();
                }
            }

            if (data.TryGetValue("combat_current_index", out var currentRaw))
            {
                campaign.CombatCurrentIndex = currentRaw.AsInt32();
            }

            return campaign;
        }
    }
}
