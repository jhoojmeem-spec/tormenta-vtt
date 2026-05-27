using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TormentaVTT.Models
{
    public sealed class CharacterSheet
    {
        public string Name { get; set; } = "Personagem";
        public string CharacterClass { get; set; } = "Guerreiro";
        public string Race { get; set; } = "Humano";
        public int Level { get; set; } = 1;
        public int HP { get; set; } = 20;
        public int PM { get; set; } = 10;
        public int Defense { get; set; } = 12;
        public int Initiative { get; set; } = 0;
        public Godot.Collections.Dictionary<string, int> Attributes { get; set; } = new()
        {
            ["Força"] = 10,
            ["Destreza"] = 10,
            ["Constituição"] = 10,
            ["Inteligência"] = 10,
            ["Sabedoria"] = 10,
            ["Carisma"] = 10
        };
        public Godot.Collections.Dictionary<string, int> Skills { get; set; } = new()
        {
            ["Atletismo"] = 0,
            ["Acrobacia"] = 0,
            ["Furtividade"] = 0,
            ["Percepção"] = 0,
            ["Intimidação"] = 0,
            ["Lidar com Animais"] = 0,
            ["Persuasão"] = 0
        };
        public List<ConditionEntry> Conditions { get; set; } = new();
        public Godot.Collections.Dictionary<string, int> Resistances { get; set; } = new();
        public Godot.Collections.Dictionary<string, int> Vulnerabilities { get; set; } = new();

        public sealed class ConditionEntry
        {
            public string Name { get; set; } = string.Empty;
            public int RemainingTurns { get; set; } = -1;

            public Godot.Collections.Dictionary ToDictionary()
            {
                return new Godot.Collections.Dictionary
                {
                    ["name"] = Name,
                    ["remaining_turns"] = RemainingTurns
                };
            }

            public static ConditionEntry FromDictionary(Godot.Collections.Dictionary data)
            {
                var entry = new ConditionEntry
                {
                    Name = data.GetValueOrDefault("name", string.Empty).ToString(),
                    RemainingTurns = data.TryGetValue("remaining_turns", out var remaining) ? remaining.AsInt32() : -1
                };
                return entry;
            }

            public override string ToString()
            {
                return RemainingTurns > 0 ? $"{Name} ({RemainingTurns} turnos)" : Name;
            }
        }

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
                conditionsArray.Add(condition.ToDictionary());
            }

            return new Godot.Collections.Dictionary
            {
                ["name"] = Name,
                ["class"] = CharacterClass,
                ["race"] = Race,
                ["level"] = Level,
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

            if (data.TryGetValue("class", out var classRaw))
            {
                sheet.CharacterClass = classRaw.ToString();
            }
            if (data.TryGetValue("race", out var raceRaw))
            {
                sheet.Race = raceRaw.ToString();
            }
            if (data.TryGetValue("level", out var levelRaw))
            {
                sheet.Level = levelRaw.AsInt32();
            }

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
                sheet.Conditions = new List<ConditionEntry>();
                foreach (var condition in conditionsArray)
                {
                    object rawCondition = condition;
                    if (rawCondition is Godot.Collections.Dictionary conditionDict)
                    {
                        sheet.Conditions.Add(ConditionEntry.FromDictionary(conditionDict));
                    }
                    else
                    {
                        sheet.Conditions.Add(new ConditionEntry { Name = condition.ToString() });
                    }
                }
            }

            if (data.TryGetValue("resistances", out var resistancesRaw))
            {
                sheet.Resistances = new Godot.Collections.Dictionary<string, int>();
                var resistancesDict = resistancesRaw.AsGodotDictionary();
                foreach (var entry in resistancesDict)
                {
                    sheet.Resistances[entry.Key.ToString().ToLowerInvariant()] = entry.Value.AsInt32();
                }
            }

            if (data.TryGetValue("vulnerabilities", out var vulnerabilitiesRaw))
            {
                sheet.Vulnerabilities = new Godot.Collections.Dictionary<string, int>();
                var vulnerabilitiesDict = vulnerabilitiesRaw.AsGodotDictionary();
                foreach (var entry in vulnerabilitiesDict)
                {
                    sheet.Vulnerabilities[entry.Key.ToString().ToLowerInvariant()] = entry.Value.AsInt32();
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

        public int GetAttributeValue(string attributeName)
        {
            if (string.IsNullOrWhiteSpace(attributeName))
                return 0;

            var key = Attributes.Keys.FirstOrDefault(k => string.Equals(k, attributeName, StringComparison.OrdinalIgnoreCase));
            return key != null ? Attributes[key] : 0;
        }

        public int GetAttributeModifier(string attributeName)
        {
            var value = GetAttributeValue(attributeName);
            return (int)Math.Floor((value - 10) / 2.0);
        }

        public int GetInitiativeBonus()
        {
            var dexBonus = GetAttributeModifier("Destreza");
            return Initiative != 0 ? Initiative : dexBonus + (Level / 2);
        }

        public int GetAttackBonus()
        {
            var strBonus = GetAttributeModifier("Força");
            return strBonus + (Level / 2);
        }

        public int GetSkillBonus(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return 0;

            var key = Skills.Keys.FirstOrDefault(k => string.Equals(k, skillName, StringComparison.OrdinalIgnoreCase));
            return key != null ? Skills[key] : 0;
        }

        public bool HasCondition(string conditionName)
        {
            if (string.IsNullOrWhiteSpace(conditionName))
                return false;

            return Conditions.Any(c => string.Equals(c.Name.Trim(), conditionName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public int GetResistanceValue(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return 0;

            var key = damageType.Trim().ToLowerInvariant();
            return Resistances.TryGetValue(key, out var value) ? value : 0;
        }

        public int GetVulnerabilityValue(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return 0;

            var key = damageType.Trim().ToLowerInvariant();
            return Vulnerabilities.TryGetValue(key, out var value) ? value : 0;
        }

        public void SetResistance(string damageType, int amount)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return;

            var key = damageType.Trim().ToLowerInvariant();
            if (amount <= 0)
                Resistances.Remove(key);
            else
                Resistances[key] = amount;
        }

        public void SetVulnerability(string damageType, int amount)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return;

            var key = damageType.Trim().ToLowerInvariant();
            if (amount <= 0)
                Vulnerabilities.Remove(key);
            else
                Vulnerabilities[key] = amount;
        }

        public int GetDamageAfterTypeModifiers(int damage, string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return damage;

            var normalizedType = damageType.Trim().ToLowerInvariant();
            var resistance = GetResistanceValue(normalizedType);
            var vulnerability = GetVulnerabilityValue(normalizedType);
            var adjusted = damage - resistance + vulnerability;
            return Math.Max(0, adjusted);
        }

        public string GetResistanceSummary()
        {
            return Resistances.Count == 0
                ? "Nenhuma"
                : string.Join(", ", Resistances.Select(entry => $"{entry.Key}:{entry.Value}"));
        }

        public string GetVulnerabilitySummary()
        {
            return Vulnerabilities.Count == 0
                ? "Nenhuma"
                : string.Join(", ", Vulnerabilities.Select(entry => $"{entry.Key}:{entry.Value}"));
        }

        public void AddCondition(string conditionName, int remainingTurns = -1)
        {
            if (string.IsNullOrWhiteSpace(conditionName))
                return;

            var normalized = conditionName.Trim();
            var existing = Conditions.FirstOrDefault(c => string.Equals(c.Name.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (remainingTurns > 0)
                {
                    existing.RemainingTurns = remainingTurns;
                }
                return;
            }

            Conditions.Add(new ConditionEntry
            {
                Name = normalized,
                RemainingTurns = remainingTurns
            });
        }

        public void RemoveCondition(string conditionName)
        {
            if (string.IsNullOrWhiteSpace(conditionName))
                return;

            Conditions.RemoveAll(c => string.Equals(c.Name.Trim(), conditionName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public List<string> TickConditionDurations()
        {
            var expired = new List<string>();
            foreach (var condition in Conditions)
            {
                if (condition.RemainingTurns > 0)
                {
                    condition.RemainingTurns--;
                    if (condition.RemainingTurns == 0)
                    {
                        expired.Add(condition.Name);
                    }
                }
            }

            Conditions.RemoveAll(c => c.RemainingTurns == 0);
            return expired;
        }

        public int GetEffectiveDefense()
        {
            var defenseModifier = 0;
            if (HasCondition("Atordoado") || HasCondition("Paralisado"))
                defenseModifier -= 2;
            if (HasCondition("Exausto"))
                defenseModifier -= 1;
            if (HasCondition("Ameaçado"))
                defenseModifier += 2;
            return Defense + defenseModifier;
        }

        public string GetConditionSummary()
        {
            return Conditions.Count == 0 ? "Nenhuma" : string.Join(", ", Conditions.Select(c => c.ToString()));
        }

        public string GetSkillAttribute(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return "Força";

            // Simplistic mapping for core skills to attributes. Extend as needed.
            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("força") || normalized.Contains("musculatura")) return "Força";
            if (normalized.Contains("destreza") || normalized.Contains("furtividade") || normalized.Contains("acrobacia")) return "Destreza";
            if (normalized.Contains("consti") || normalized.Contains("fortitude")) return "Constituição";
            if (normalized.Contains("inteligência") || normalized.Contains("inteligencia")) return "Inteligência";
            if (normalized.Contains("sabedoria") || normalized.Contains("percepção")) return "Sabedoria";
            if (normalized.Contains("carisma") || normalized.Contains("atuação") || normalized.Contains("diplomacia")) return "Carisma";
            return "Força";
        }
    }
}
