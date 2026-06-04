using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TormentaVTT.Models;

namespace TormentaVTT.Services
{
    public sealed class TextContentParser
    {
        public record ParsedOutput(
        List<ClassDefinition> Classes, 
        List<RaceDefinition> Races, 
        List<PowerDefinition> Powers, 
        List<SpellDefinition> Spells, 
        List<ConditionDefinition> Conditions,
        List<ThreatDefinition> Threats);

        public ParsedOutput Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new ParsedOutput(
                    new List<ClassDefinition>(), 
                    new List<RaceDefinition>(),
                    new List<PowerDefinition>(),
                    new List<SpellDefinition>(), 
                    new List<ConditionDefinition>(),
                    new List<ThreatDefinition>());

            // Normalize line endings
            raw = raw.Replace("\r\n", "\n");

            // Split by double newlines into blocks
            var blocks = raw.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(b => b.Trim()).ToList();

            // Identify headings: block whose first line is all uppercase and reasonably short
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = "general";
            sections[currentSection] = new List<string>();

            foreach (var block in blocks)
            {
                var firstLine = block.Split('\n')[0].Trim();
                if (IsHeading(firstLine))
                {
                    currentSection = NormalizeHeading(firstLine);
                    if (!sections.ContainsKey(currentSection))
                        sections[currentSection] = new List<string>();
                    var remainder = block.Substring(firstLine.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(remainder))
                        sections[currentSection].Add(remainder);
                }
                else
                {
                    sections[currentSection].Add(block);
                }
            }

            var classes = new List<ClassDefinition>();
            var races = new List<RaceDefinition>();
            var powers = new List<PowerDefinition>();
            var spells = new List<SpellDefinition>();
            var conditions = new List<ConditionDefinition>();
            var threats = new List<ThreatDefinition>();

            foreach (var kv in sections)
            {
                var key = kv.Key.ToLowerInvariant();
                var items = SplitItems(kv.Value);
                
                if (key.Contains("class") || key.Contains("classe") || key.Contains("classes"))
                {
                    classes.AddRange(ParseClasses(items));
                }
                else if (key.Contains("race") || key.Contains("raça") || key.Contains("raças") || key.Contains("origin"))
                {
                    races.AddRange(ParseRaces(items));
                }
                else if (key.Contains("power") || key.Contains("poder") || key.Contains("poderes") || key.Contains("ability") || key.Contains("habilidade"))
                {
                    powers.AddRange(ParsePowers(items));
                }
                else if (key.Contains("mag") || key.Contains("spell") || key.Contains("magia"))
                {
                    spells.AddRange(ParseSpells(items));
                }
                else if (key.Contains("condi") || key.Contains("condition"))
                {
                    conditions.AddRange(ParseConditions(items));
                }
                else if (key.Contains("threat") || key.Contains("ameaça") || key.Contains("inimigo") || key.Contains("criatura") || key.Contains("monstro"))
                {
                    threats.AddRange(ParseThreats(items));
                }
                else
                {
                    // try to heuristically detect entries inside the block
                    var heuristic = HeuristicDetect(items);
                    classes.AddRange(heuristic.Classes);
                    races.AddRange(heuristic.Races);
                    powers.AddRange(heuristic.Powers);
                    spells.AddRange(heuristic.Spells);
                    conditions.AddRange(heuristic.Conditions);
                    threats.AddRange(heuristic.Threats);
                }
            }

            return new ParsedOutput(classes, races, powers, spells, conditions, threats);
        }

        private bool IsHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.Length < 3) return false;
            // consider heading if most letters are uppercase or contains spaces and is short
            var letters = line.Where(char.IsLetter).ToArray();
            if (letters.Length == 0) return false;
            var upperCount = letters.Count(char.IsUpper);
            return upperCount >= (letters.Length * 0.6) || line.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || !char.IsLetter(c));
        }

        private string NormalizeHeading(string line)
        {
            return line.Trim().Replace("\n", " ").ToLowerInvariant();
        }

        private List<string> SplitItems(List<string> blocks)
        {
            var items = new List<string>();
            foreach (var b in blocks)
            {
                // if block contains lines starting with '-' or a numeric list, split by those
                var lines = b.Split('\n').Select(l => l.Trim()).ToList();
                var current = new List<string>();
                foreach (var l in lines)
                {
                    if (l.StartsWith("- ") || System.Text.RegularExpressions.Regex.IsMatch(l, "^\\d+\\."))
                    {
                        if (current.Count > 0)
                        {
                            items.Add(string.Join(' ', current));
                            current.Clear();
                        }
                        items.Add(l.Substring(2).Trim());
                    }
                    else
                    {
                        current.Add(l);
                    }
                }
                if (current.Count > 0)
                    items.Add(string.Join(' ', current));
            }
            return items.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        }

        private List<ClassDefinition> ParseClasses(List<string> items)
        {
            var outp = new List<ClassDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new ClassDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    Description = it,
                    HitDie = 8,
                    PrimaryAttributes = new List<string>(),
                    SavingThrows = new List<string>(),
                    Spellcasting = it.IndexOf("mag", StringComparison.OrdinalIgnoreCase) >= 0
                });
            }
            return outp;
        }

        private List<RaceDefinition> ParseRaces(List<string> items)
        {
            var outp = new List<RaceDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new RaceDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    Description = it,
                    AttributeBonus = new Dictionary<string, int>(),
                    Abilities = new List<string>(),
                    MovementSpeed = 9,
                    Languages = new List<string>()
                });
            }
            return outp;
        }

        private List<PowerDefinition> ParsePowers(List<string> items)
        {
            var outp = new List<PowerDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new PowerDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    Type = string.Empty,
                    Description = it,
                    Prerequisites = new List<string>(),
                    Effects = new List<string>()
                });
            }
            return outp;
        }

        private List<SpellDefinition> ParseSpells(List<string> items)
        {
            var outp = new List<SpellDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new SpellDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    School = string.Empty,
                    Circle = 0,
                    Range = string.Empty,
                    Duration = string.Empty,
                    CostPM = 0,
                    EffectType = "damage",
                    DamageExpression = string.Empty,
                    TargetType = "enemy",
                    Description = it
                });
            }
            return outp;
        }

        private List<ConditionDefinition> ParseConditions(List<string> items)
        {
            var outp = new List<ConditionDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new ConditionDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    Description = it,
                    Modifiers = new Dictionary<string, int>(),
                    IsNegative = true
                });
            }
            return outp;
        }

        private List<ThreatDefinition> ParseThreats(List<string> items)
        {
            var outp = new List<ThreatDefinition>();
            foreach (var it in items)
            {
                var name = ExtractName(it);
                outp.Add(new ThreatDefinition
                {
                    Id = Slugify(name),
                    Name = name,
                    Type = string.Empty,
                    Level = 1,
                    Description = it,
                    Attributes = new Dictionary<string, int>(),
                    HP = 20,
                    Defense = 12,
                    Abilities = new List<string>(),
                    Treasure = string.Empty
                });
            }
            return outp;
        }

        private (List<ClassDefinition> Classes, List<RaceDefinition> Races, List<PowerDefinition> Powers, List<SpellDefinition> Spells, List<ConditionDefinition> Conditions, List<ThreatDefinition> Threats) HeuristicDetect(List<string> items)
        {
            var classes = new List<ClassDefinition>();
            var races = new List<RaceDefinition>();
            var powers = new List<PowerDefinition>();
            var spells = new List<SpellDefinition>();
            var conditions = new List<ConditionDefinition>();
            var threats = new List<ThreatDefinition>();
            
            foreach (var it in items)
            {
                var low = it.ToLowerInvariant();
                if (low.Contains("mag") || low.Contains("magia") || low.Contains("círculo") || low.Contains("custo"))
                {
                    spells.AddRange(ParseSpells(new List<string> { it }));
                }
                else if (low.Contains("condi") || low.Contains("efeito") || low.Contains("modificador"))
                {
                    conditions.AddRange(ParseConditions(new List<string> { it }));
                }
                else if (low.Contains("classe") || low.Contains("hit die") || low.Contains("atributo"))
                {
                    classes.AddRange(ParseClasses(new List<string> { it }));
                }
                else if (low.Contains("raça") || low.Contains("origem") || low.Contains("idioma"))
                {
                    races.AddRange(ParseRaces(new List<string> { it }));
                }
                else if (low.Contains("poder") || low.Contains("habilidade") || low.Contains("pré-requisito"))
                {
                    powers.AddRange(ParsePowers(new List<string> { it }));
                }
                else if (low.Contains("ameaça") || low.Contains("monstro") || low.Contains("criatura") || low.Contains("inimigo"))
                {
                    threats.AddRange(ParseThreats(new List<string> { it }));
                }
                else
                {
                    // default: skip
                }
            }
            return (classes, races, powers, spells, conditions, threats);
        }

        private string ExtractName(string text)
        {
            // Take first line or first sentence as name
            var firstLine = text.Split('\n')[0].Trim();
            if (firstLine.Length <= 60 && firstLine.IndexOf(' ') >= 0 && firstLine.All(c => char.IsLetter(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c)))
                return Truncate(firstLine, 60);

            var dot = text.IndexOf('.');
            if (dot > 0 && dot < 60)
                return Truncate(text.Substring(0, dot), 60);

            return Truncate(firstLine, 60);
        }

        private string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max).Trim();
        }

        private string Slugify(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "item";
            var t = s.ToLowerInvariant();
            t = System.Text.RegularExpressions.Regex.Replace(t, "[^a-z0-9]+", "_");
            t = t.Trim('_');
            return string.IsNullOrEmpty(t) ? "item" : t;
        }

        public void SaveParsedOutput(ParsedOutput outp, string baseDir)
        {
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            var classesPath = Path.Combine(baseDir, "classes_parsed.json");
            var racesPath = Path.Combine(baseDir, "races_parsed.json");
            var powersPath = Path.Combine(baseDir, "powers_parsed.json");
            var spellsPath = Path.Combine(baseDir, "spells_parsed.json");
            var conditionsPath = Path.Combine(baseDir, "conditions_parsed.json");
            var threatsPath = Path.Combine(baseDir, "threats_parsed.json");

            File.WriteAllText(classesPath, JsonSerializer.Serialize(outp.Classes, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(racesPath, JsonSerializer.Serialize(outp.Races, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(powersPath, JsonSerializer.Serialize(outp.Powers, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(spellsPath, JsonSerializer.Serialize(outp.Spells, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(conditionsPath, JsonSerializer.Serialize(outp.Conditions, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(threatsPath, JsonSerializer.Serialize(outp.Threats, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
