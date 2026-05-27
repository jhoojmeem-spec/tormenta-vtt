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

            return new Godot.Collections.Dictionary
            {
                ["name"] = Name,
                ["description"] = Description,
                ["players"] = playersArray,
                ["gm"] = GM,
                ["map_image_path"] = MapImagePath,
                ["grid_enabled"] = GridEnabled,
                ["zoom"] = Zoom,
                ["tokens"] = tokensArray
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

            return campaign;
        }
    }
}
