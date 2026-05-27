using System;
using System.Collections.Generic;
using TormentaVTT.Models;

namespace TormentaVTT.Services
{
    public sealed class AttackResult
    {
        public bool Hit { get; set; }
        public bool IsCritical { get; set; }
        public bool IsFumble { get; set; }
        public int Damage { get; set; }
        public DiceRollResult RollResult { get; set; } = new();
    }

    public sealed class RuleEngine
    {
        private readonly DiceParser _diceParser = new();
        private readonly Random _random = new();
        private static readonly string[] Attributes = { "Força", "Destreza", "Constituição", "Inteligência", "Sabedoria", "Carisma" };

        public DiceRollResult RollAttributeCheck(CharacterSheet sheet, string attributeName)
        {
            var value = sheet.GetAttributeValue(attributeName);
            var modifier = sheet.GetAttributeModifier(attributeName);
            var conditionMod = GetConditionCheckPenalty(sheet);
            var expression = $"1d20+{modifier}+{conditionMod}";
            var result = _diceParser.Evaluate(expression);
            result.Breakdown = $"{attributeName}({value}) mod {modifier} + condições({conditionMod}) : {result.Breakdown}";
            return result;
        }

        public DiceRollResult RollSkillCheck(CharacterSheet sheet, string skillName)
        {
            var skillBonus = sheet.GetSkillBonus(skillName);
            var attributeName = sheet.GetSkillAttribute(skillName);
            var attrMod = sheet.GetAttributeModifier(attributeName);
            var conditionMod = GetConditionCheckPenalty(sheet);
            var expression = $"1d20+{skillBonus}+{attrMod}+{conditionMod}";
            var result = _diceParser.Evaluate(expression);
            result.Breakdown = $"{skillName}({skillBonus}) + {attributeName} mod {attrMod} + condições({conditionMod}) : {result.Breakdown}";
            return result;
        }

        private int GetConditionCheckPenalty(CharacterSheet sheet)
        {
            var penalty = 0;
            if (sheet.HasCondition("Atordoado") || sheet.HasCondition("Paralisado"))
                penalty -= 2;
            if (sheet.HasCondition("Exausto"))
                penalty -= 1;
            if (sheet.HasCondition("Desarmado"))
                penalty -= 2;
            return penalty;
        }

        private int GetConditionAttackModifier(CharacterSheet sheet)
        {
            var modifier = 0;
            if (sheet.HasCondition("Atordoado") || sheet.HasCondition("Paralisado"))
                modifier -= 2;
            if (sheet.HasCondition("Exausto"))
                modifier -= 1;
            if (sheet.HasCondition("Ameaçado"))
                modifier += 2;
            return modifier;
        }

        public DiceRollResult RollInitiative(CharacterSheet sheet)
        {
            var initiativeBonus = sheet.GetInitiativeBonus();
            var expression = $"1d20+{initiativeBonus}";
            var result = _diceParser.Evaluate(expression);
            result.Breakdown = $"Iniciativa({initiativeBonus}) : {result.Breakdown}";
            return result;
        }

        public AttackResult RollAttack(TokenData attacker, TokenData target, int baseDamage)
        {
            return RollAttack(attacker, target, baseDamage.ToString());
        }

        public AttackResult RollAttack(TokenData attacker, TokenData target, string damageExpression, string damageType = "")
        {
            var attackBonus = attacker.Sheet.GetAttackBonus();
            var conditionAttackModifier = GetConditionAttackModifier(attacker.Sheet);
            var totalAttackBonus = attackBonus + conditionAttackModifier;
            var naturalRoll = _random.Next(1, 21);
            var totalRoll = naturalRoll + totalAttackBonus;
            var result = new DiceRollResult
            {
                Total = totalRoll
            };
            result.Rolls.Add(naturalRoll);

            var breakdown = $"1d20({naturalRoll}) + ataque({totalAttackBonus})";
            if (conditionAttackModifier != 0)
            {
                breakdown = $"Bônus de ataque({attackBonus}) + condições({conditionAttackModifier}) : {breakdown}";
            }
            else
            {
                breakdown = $"Bônus de ataque({attackBonus}) : {breakdown}";
            }

            var effectiveDefense = target.Sheet.GetEffectiveDefense();
            var isCritical = naturalRoll == 20;
            var isFumble = naturalRoll == 1;
            var hit = isCritical || (!isFumble && totalRoll >= effectiveDefense);
            if (isCritical)
            {
                breakdown += " CRÍTICO!";
            }
            else if (isFumble)
            {
                breakdown += " FUMBLE!";
            }

            var damage = 0;
            if (hit)
            {
                damage = EvaluateDamageExpression(damageExpression);
                if (isCritical)
                {
                    damage *= 2;
                }
                damage = target.Sheet.GetDamageAfterTypeModifiers(damage, damageType);
                damage = Math.Max(0, damage);
            }

            var typeText = string.IsNullOrEmpty(damageType) ? string.Empty : $" tipo {damageType}";
            result.Breakdown = $"{breakdown}{typeText} (Defesa alvo: {effectiveDefense})";

            return new AttackResult
            {
                Hit = hit,
                IsCritical = isCritical,
                IsFumble = isFumble,
                Damage = damage,
                RollResult = result
            };
        }

        private int EvaluateDamageExpression(string damageExpression)
        {
            if (string.IsNullOrWhiteSpace(damageExpression))
                return 1;

            var damageResult = _diceParser.Evaluate(damageExpression);
            return Math.Max(1, damageResult.Total);
        }
    }
}
