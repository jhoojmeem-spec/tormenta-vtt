using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TormentaVTT.Services
{
    public sealed class DiceRollResult
    {
        public int Total { get; set; }
        public string Breakdown { get; set; } = string.Empty;
        public List<int> Rolls { get; } = new();
    }

    public sealed class DiceParser
    {
        private static readonly Regex TermPattern = new(@"([+-]?)(\d*d\d+(?:kh\d+|kl\d+)?|[A-Za-zÀ-ÿ]+|\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Random _random = new();

        public DiceParser()
        {
            GD.Randomize();
        }

        public DiceRollResult Evaluate(string expression, IReadOnlyDictionary<string, int>? variables = null)
        {
            var result = new DiceRollResult();
            var normalized = expression.Replace(" ", string.Empty);
            var matches = TermPattern.Matches(normalized);
            var total = 0;
            var breakdownParts = new List<string>();

            foreach (Match match in matches)
            {
                var sign = match.Groups[1].Value == "-" ? -1 : 1;
                var token = match.Groups[2].Value;
                var value = 0;
                var detail = token;

                if (token.Contains("d", StringComparison.OrdinalIgnoreCase))
                {
                    var diceMatch = Regex.Match(token, @"(\d*)d(\d+)(kh(\d+)|kl(\d+))?", RegexOptions.IgnoreCase);
                    if (diceMatch.Success)
                    {
                        var count = string.IsNullOrEmpty(diceMatch.Groups[1].Value) ? 1 : int.Parse(diceMatch.Groups[1].Value);
                        var faces = int.Parse(diceMatch.Groups[2].Value);
                        var keepHigh = diceMatch.Groups[4].Success;
                        var keepLow = diceMatch.Groups[5].Success;
                        var keepCount = 0;

                        if (keepHigh)
                        {
                            keepCount = int.Parse(diceMatch.Groups[4].Value);
                        }
                        else if (keepLow)
                        {
                            keepCount = int.Parse(diceMatch.Groups[5].Value);
                        }

                        value = RollDice(count, faces, keepHigh, keepLow, keepCount, result.Rolls, out var rollsText);
                        detail = rollsText;
                    }
                }
                else if (int.TryParse(token, out var constant))
                {
                    value = constant;
                }
                else if (variables != null && variables.TryGetValue(token, out var variableValue))
                {
                    value = variableValue;
                    detail = $"{token}({variableValue})";
                }
                else
                {
                    detail = $"{token}(0)";
                }

                total += sign * value;
                breakdownParts.Add((sign < 0 ? "-" : "+") + detail);
            }

            result.Total = total;
            result.Breakdown = string.Join(" ", breakdownParts).TrimStart('+');
            return result;
        }

        private int RollDice(int count, int faces, bool keepHigh, bool keepLow, int keepCount, List<int> rollStorage, out string detail)
        {
            var rolls = new List<int>();
            for (var i = 0; i < Math.Max(1, count); i++)
            {
                var roll = _random.Next(1, faces + 1);
                rolls.Add(roll);
                rollStorage.Add(roll);
            }

            if (keepHigh || keepLow)
            {
                keepCount = Math.Clamp(keepCount, 1, rolls.Count);
                rolls.Sort();
                if (keepHigh)
                {
                    rolls.Reverse();
                }

                var kept = rolls.GetRange(0, keepCount);
                detail = $"{count}d{faces}{(keepHigh ? "kh" : "kl")}{keepCount}: [{string.Join(",", kept)}]";
                return kept.Sum();
            }

            detail = $"{count}d{faces}: [{string.Join(",", rolls)}]";
            return rolls.Sum();
        }
    }
}
