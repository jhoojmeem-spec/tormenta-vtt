using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using TormentaVTT.Models;
using TormentaVTT.Parsers;

namespace TormentaVTT.Services
{
    public sealed class ContentService
    {
        private const string ContentPath = "res://Content";

        public IReadOnlyList<ClassDefinition> Classes { get; private set; } = new List<ClassDefinition>();
        public IReadOnlyList<RaceDefinition> Races { get; private set; } = new List<RaceDefinition>();
        public IReadOnlyList<PowerDefinition> Powers { get; private set; } = new List<PowerDefinition>();
        public IReadOnlyList<ConditionDefinition> Conditions { get; private set; } = new List<ConditionDefinition>();
        public IReadOnlyList<SpellDefinition> Spells { get; private set; } = new List<SpellDefinition>();
        public IReadOnlyList<ThreatDefinition> Threats { get; private set; } = new List<ThreatDefinition>();

        public int ClassCount => Classes.Count;
        public int RaceCount => Races.Count;
        public int PowerCount => Powers.Count;
        public int ConditionCount => Conditions.Count;
        public int SpellCount => Spells.Count;
        public int ThreatCount => Threats.Count;

        public void LoadDefinitions()
        {
            Classes = LoadContentFile<ClassDefinition>("classes.json");
            Races = LoadContentFile<RaceDefinition>("races.json");
            Powers = LoadContentFile<PowerDefinition>("powers.json");
            Conditions = LoadContentFile<ConditionDefinition>("conditions.json");
            Spells = LoadContentFile<SpellDefinition>("spells.json");
            Threats = LoadContentFile<ThreatDefinition>("threats.json");
        }

        private IReadOnlyList<T> LoadContentFile<T>(string fileName)
        {
            var resourcePath = Path.Combine(ContentPath, fileName).Replace("\\", "/");
            if (!Godot.FileAccess.FileExists(resourcePath))
            {
                GD.PrintErr($"Content file não encontrado: {resourcePath}");
                return new List<T>();
            }

            using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
            var content = file.GetAsText();
            return JsonContentParser.ParseArray<T>(content) ?? new List<T>();
        }
    }
}
