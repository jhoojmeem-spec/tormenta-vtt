using System;
using System.Collections.Generic;
using System.Linq;
using TormentaVTT.Models;

namespace TormentaVTT.UI
{
    public sealed class CombatController
    {
        private readonly Random _rand = new();
        private List<(TokenData Token, int InitiativeRoll)> _order = new();
        private int _currentIndex = -1;

        public bool InCombat { get; private set; }
        public TokenData? Current => (_currentIndex >= 0 && _currentIndex < _order.Count) ? _order[_currentIndex].Token : null;

        public event Action<TokenData?>? CurrentTurnChanged;

        public void StartCombat(IEnumerable<TokenData> tokens, bool rollAll = true)
        {
            var list = new List<(TokenData, int)>();
            foreach (var t in tokens)
            {
                var baseInit = t.Sheet.Initiative;
                var roll = rollAll ? _rand.Next(1, 21) + baseInit : baseInit;
                list.Add((t, roll));
            }

            // Sort by roll desc, tie-breaker random
            _order = list.OrderByDescending(x => x.Item2).ThenBy(x => _rand.Next()).ToList();
            _currentIndex = _order.Count > 0 ? 0 : -1;
            InCombat = _order.Count > 0;
            CurrentTurnChanged?.Invoke(Current);
        }

        public void RerollToken(string tokenId)
        {
            var idx = _order.FindIndex(x => x.Token.Id == tokenId);
            if (idx < 0)
                return;

            var entry = _order[idx];
            var baseInit = entry.Token.Sheet.Initiative;
            var newRoll = _rand.Next(1, 21) + baseInit;
            _order[idx] = (entry.Token, newRoll);
            // Re-sort
            _order = _order.OrderByDescending(x => x.InitiativeRoll).ThenBy(x => _rand.Next()).ToList();
            // ensure current index points to same token if possible
            if (Current != null)
            {
                var curId = Current.Id;
                _currentIndex = _order.FindIndex(x => x.Token.Id == curId);
            }
            CurrentTurnChanged?.Invoke(Current);
        }

        public void RemoveTokenFromOrder(string tokenId)
        {
            var idx = _order.FindIndex(x => x.Token.Id == tokenId);
            if (idx < 0)
                return;
            _order.RemoveAt(idx);
            if (_order.Count == 0)
            {
                EndCombat();
                return;
            }
            _currentIndex = Math.Clamp(_currentIndex, 0, _order.Count - 1);
            CurrentTurnChanged?.Invoke(Current);
        }

        public void AddTokenToOrder(TokenData token, bool rollInitiative = true)
        {
            var baseInit = token.Sheet.Initiative;
            var roll = rollInitiative ? _rand.Next(1, 21) + baseInit : baseInit;
            _order.Add((token, roll));
            _order = _order.OrderByDescending(x => x.InitiativeRoll).ThenBy(x => _rand.Next()).ToList();
            if (_currentIndex < 0 && _order.Count > 0)
                _currentIndex = 0;
            CurrentTurnChanged?.Invoke(Current);
        }

        public void ReorderTurn(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _order.Count || toIndex < 0 || toIndex >= _order.Count)
                return;

            var temp = _order[fromIndex];
            _order[fromIndex] = _order[toIndex];
            _order[toIndex] = temp;

            if (_currentIndex == fromIndex)
            {
                _currentIndex = toIndex;
            }
            else if (_currentIndex == toIndex)
            {
                _currentIndex = fromIndex;
            }

            CurrentTurnChanged?.Invoke(Current);
        }

        public List<string> GetOrderIds() => _order.Select(x => x.Token.Id).ToList();

        public Dictionary<string, int> GetOrderRolls() => _order.ToDictionary(x => x.Token.Id, x => x.InitiativeRoll);

        public int GetCurrentIndex() => _currentIndex;

        public void LoadCombatState(IEnumerable<TokenData> tokens, IReadOnlyList<string> orderIds, IReadOnlyDictionary<string, int> rolls, int currentIndex, bool active)
        {
            if (!active)
            {
                EndCombat();
                return;
            }

            var order = new List<(TokenData Token, int InitiativeRoll)>();
            foreach (var id in orderIds)
            {
                var token = tokens.FirstOrDefault(t => t.Id == id);
                if (token == null)
                    continue;

                var roll = rolls.TryGetValue(id, out var savedRoll) ? savedRoll : token.Sheet.Initiative;
                order.Add((token, roll));
            }

            _order = order;
            _currentIndex = order.Count > 0 ? Math.Clamp(currentIndex, 0, order.Count - 1) : -1;
            InCombat = _order.Count > 0;
            CurrentTurnChanged?.Invoke(Current);
        }

        public void EndCombat()
        {
            InCombat = false;
            _order.Clear();
            _currentIndex = -1;
            CurrentTurnChanged?.Invoke(null);
        }

        public void AdvanceTurn()
        {
            if (!InCombat || _order.Count == 0)
                return;
            _currentIndex = (_currentIndex + 1) % _order.Count;
            CurrentTurnChanged?.Invoke(Current);
        }

        public void RetreatTurn()
        {
            if (!InCombat || _order.Count == 0)
                return;
            _currentIndex = (_currentIndex - 1 + _order.Count) % _order.Count;
            CurrentTurnChanged?.Invoke(Current);
        }

        public IReadOnlyList<(TokenData Token, int InitiativeRoll)> GetOrder() => _order.AsReadOnly();
    }
}
