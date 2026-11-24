using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using KL.Utils;

namespace Game.Systems.Inventory {
    // Loose piles of InventorySys by material type, with a running total per type. Piles can
    // outnumber every other storage many times over (a broken up derelict leaves thousands),
    // so the queries that run every tick look at the piles of the type they ask about
    // instead of walking all of them. See Docs/Architecture/Systems/PileScaling.md
    public sealed class PileIndex {
        private readonly Dictionary<MatType, List<UnstoredMatComp>> byType = new();
        private readonly Dictionary<MatType, int> totals = new();
        // The part of totals in forbidden piles: counted as owned, but nobody may use it
        // (Docs/Architecture/Systems/Forbidding.md)
        private readonly Dictionary<MatType, int> forbidden = new();
        // Every type that ever had a pile, for walks over all piles in a stable order
        public readonly List<MatType> Types = new();

        public Dictionary<MatType, int> Totals => totals;

        public int ForbiddenOf(MatType type) {
            return forbidden.TryGetValue(type, out var count) ? count : 0;
        }

        public List<UnstoredMatComp> Of(MatType type) {
            return byType.TryGetValue(type, out var list) ? list : null;
        }

        public void Add(UnstoredMatComp pile) {
            var type = pile.Type;
            if (!byType.TryGetValue(type, out var list)) {
                list = new List<UnstoredMatComp>();
                byType.Add(type, list);
                Types.Add(type);
            }
            pile.IndexInType = list.Count;
            list.Add(pile);
            OnStackChanged(pile, pile.StackSize);
        }

        // Swaps the last pile of the type into the gap: order within a type is not kept
        public bool Remove(UnstoredMatComp pile) {
            var type = pile.Type;
            if (!byType.TryGetValue(type, out var list)) { return false; }
            var idx = pile.IndexInType;
            if (idx < 0) { return false; }
            if (idx >= list.Count || list[idx] != pile) {
                D.Err("Pile index out of sync, searching: {0} at {1}", pile, idx);
                idx = list.IndexOf(pile);
                if (idx < 0) { return false; }
            }
            var last = list.Count - 1;
            if (idx != last) {
                var moved = list[last];
                list[idx] = moved;
                moved.IndexInType = idx;
            }
            list.RemoveAt(last);
            pile.IndexInType = -1;
            OnStackChanged(pile, -pile.StackSize);
            return true;
        }

        public void OnStackChanged(UnstoredMatComp pile, int delta) {
            if (delta == 0) { return; }
            Change(totals, pile.Type, delta);
            if (pile.IsForbidden) {
                Change(forbidden, pile.Type, delta);
            }
        }

        // The pile's flag has just flipped
        public void OnForbidChanged(UnstoredMatComp pile) {
            Change(forbidden, pile.Type, pile.IsForbidden ? pile.StackSize : -pile.StackSize);
        }

        private static void Change(Dictionary<MatType, int> counts, MatType type, int delta) {
            if (delta == 0) { return; }
            counts.TryGetValue(type, out var total);
            counts[type] = total + delta;
        }

        public void Clear() {
            byType.Clear();
            totals.Clear();
            forbidden.Clear();
            Types.Clear();
        }
    }
}
