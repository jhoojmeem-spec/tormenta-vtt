using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace TormentaVTT.Models
{
    public sealed class TokenData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Token";
        public string ImagePath { get; set; } = string.Empty;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public CharacterSheet Sheet { get; set; } = new();
        public bool IsGM { get; set; }

        public static TokenData Create(string name, string imagePath)
        {
            return new TokenData
            {
                Name = name,
                ImagePath = imagePath,
                Position = Vector2.Zero,
                Sheet = new CharacterSheet { Name = name }
            };
        }

        public Godot.Collections.Dictionary ToDictionary()
        {
            return new Godot.Collections.Dictionary
            {
                ["id"] = Id,
                ["name"] = Name,
                ["image_path"] = ImagePath,
                ["position"] = Position,
                ["is_gm"] = IsGM,
                ["sheet"] = Sheet.ToDictionary()
            };
        }

        public static TokenData FromDictionary(Godot.Collections.Dictionary data)
        {
            var position = Vector2.Zero;
            if (data.TryGetValue("position", out var positionRaw))
            {
                position = positionRaw.AsVector2();
            }

            var isGm = false;
            if (data.TryGetValue("is_gm", out var isGmRaw))
            {
                isGm = isGmRaw.AsBool();
            }

            var sheet = new CharacterSheet();
            if (data.TryGetValue("sheet", out var sheetRaw))
            {
                var sheetDict = sheetRaw.AsGodotDictionary();
                if (sheetDict.Count > 0)
                {
                    sheet = CharacterSheet.FromDictionary(sheetDict);
                }
            }

            return new TokenData
            {
                Id = data.GetValueOrDefault("id", Guid.NewGuid().ToString()).ToString(),
                Name = data.GetValueOrDefault("name", "Token").ToString(),
                ImagePath = data.GetValueOrDefault("image_path", string.Empty).ToString(),
                Position = position,
                IsGM = isGm,
                Sheet = sheet
            };
        }
    }
}
