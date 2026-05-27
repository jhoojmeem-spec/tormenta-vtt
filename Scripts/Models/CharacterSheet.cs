using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace TormentaVTT.Models
{
    public sealed class CharacterSheet
    {
        public string Name { get; set; } = "Personagem";
        public int HP { get; set; } = 20;
        public int PM { get; set; } = 10;
        public int Defense { get; set; } = 12;
        public int Initiative { get; set; } = 0;
        public Godot.Collections.Dictionary<string, int> Attributes { get; set; } = new()
        {
            ["Força"] = 0,
            ["Destreza"] = 0,
            ["Constituição"] = 0,
            ["Inteligência"] = 0,
            ["Sabedoria"] = 0,
            ["Carisma"] = 0
        };
        public Godot.Collections.Dictionary<string, int> Skills { get; set; } = new();
        public List<string> Conditions { get; set; } = new();

        public Godot.Collections.Dictionary ToDictionary()
        {
            var attributes = new Godot.Collections.Dictionary<string, int>();
            foreach (var attribute in Attributes)
            {
                attributes[attribute.Key] = attribute.Value;
            }

            var skills = new Godot.Collections.Dictionary<string, int>();
            foreach (var skill in Skills)
            {
                skills[skill.Key] = skill.Value;
            }

            var conditionsArray = new Godot.Collections.Array();
            foreach (var condition in Conditions)
            {
                conditionsArray.Add(condition);
            }

            return new Godot.Collections.Dictionary
            {
                ["name"] = Name,
                ["hp"] = HP,
                ["pm"] = PM,
                ["defense"] = Defense,
                ["initiative"] = Initiative,
                ["attributes"] = attributes,
                ["skills"] = skills,
                ["conditions"] = conditionsArray
            };
        }

        public static CharacterSheet FromDictionary(Godot.Collections.Dictionary data)
        {
            var sheet = new CharacterSheet
            {
                Name = data.GetValueOrDefault("name", "Personagem").ToString(),
                HP = data.TryGetValue("hp", out var hpRaw) ? hpRaw.AsInt32() : 20,
                PM = data.TryGetValue("pm", out var pmRaw) ? pmRaw.AsInt32() : 10,
                Defense = data.TryGetValue("defense", out var defenseRaw) ? defenseRaw.AsInt32() : 12,
                Initiative = data.TryGetValue("initiative", out var initiativeRaw) ? initiativeRaw.AsInt32() : 0
            };

            if (data.TryGetValue("attributes", out var attributesRaw))
            {
                var attributesDict = attributesRaw.AsGodotDictionary<string, int>();
                sheet.Attributes = new Godot.Collections.Dictionary<string, int>();
                foreach (var entry in attributesDict)
                {
                    sheet.Attributes[entry.Key] = entry.Value;
                }
            }

            if (data.TryGetValue("skills", out var skillsRaw))
            {
                var skillsDict = skillsRaw.AsGodotDictionary<string, int>();
                sheet.Skills = new Godot.Collections.Dictionary<string, int>();
                foreach (var entry in skillsDict)
                {
                    sheet.Skills[entry.Key] = entry.Value;
                }
            }

            if (data.TryGetValue("conditions", out var conditionsRaw))
            {
                var conditionsArray = conditionsRaw.AsGodotArray();
                sheet.Conditions = new List<string>();
                foreach (var condition in conditionsArray)
                {
                    sheet.Conditions.Add(condition.AsString());
                }
            }

            return sheet;
        }

        public Godot.Collections.Dictionary<string, int> GetAttributeTable()
        {
            var result = new Godot.Collections.Dictionary<string, int>();
            foreach (var entry in Attributes)
            {
                result[entry.Key] = entry.Value;
            }

            result["PV"] = HP;
            result["PM"] = PM;
            result["Defesa"] = Defense;
            result["Iniciativa"] = Initiative;
            return result;
        }
    }
}
