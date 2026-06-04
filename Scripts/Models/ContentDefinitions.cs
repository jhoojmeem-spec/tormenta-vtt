using System;
using System.Collections.Generic;

namespace TormentaVTT.Models
{
    public sealed class ClassDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int HitDie { get; set; } = 8;
        public List<string> PrimaryAttributes { get; set; } = new();
        public List<string> SavingThrows { get; set; } = new();
        public bool Spellcasting { get; set; }
    }

    public sealed class RaceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, int> AttributeBonus { get; set; } = new();
        public List<string> Abilities { get; set; } = new();
        public int MovementSpeed { get; set; } = 9;
        public List<string> Languages { get; set; } = new();
    }

    public sealed class PowerDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Prerequisites { get; set; } = new();
        public List<string> Effects { get; set; } = new();
    }

    public sealed class ConditionDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, int> Modifiers { get; set; } = new();
        public bool IsNegative { get; set; } = true;
    }

    public sealed class SpellDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string School { get; set; } = string.Empty;
        public int Circle { get; set; }
        public string Range { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public int CostPM { get; set; }
        public string EffectType { get; set; } = "damage";
        public string DamageExpression { get; set; } = string.Empty;
        public string TargetType { get; set; } = "enemy";
        public string Description { get; set; } = string.Empty;

        public bool IsHealing => EffectType.Equals("heal", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ThreatDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, int> Attributes { get; set; } = new();
        public int HP { get; set; } = 20;
        public int Defense { get; set; } = 12;
        public List<string> Abilities { get; set; } = new();
        public string Treasure { get; set; } = string.Empty;
    }
}
