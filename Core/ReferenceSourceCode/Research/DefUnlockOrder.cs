using System;
using System.Collections.Generic;
using System.Text;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    // Orders defs the way a player unlocks them: research tier, then the research cost of
    // getting there (the unlocking nodes and all their ancestors). Codex lists use it so that
    // what can be built early comes first and a "+N" hides the late game, not the basics.
    // The rank reads only def JSON (tiers, datoid costs, datoid values), so the main menu
    // codex and the website export order lists the same way as a running game
    public static class DefUnlockOrder {
        private readonly struct Rank {
            public readonly int Tier;
            public readonly float Cost;

            public Rank(int tier, float cost) {
                Tier = tier;
                Cost = cost;
            }
        }

        private static readonly Dictionary<Def, Rank> ranks = new();
        private static readonly Dictionary<Def, float> nodeCosts = new();
        private static readonly HashSet<Def> closure = new();
        private static readonly Stack<Def> pending = new();
        private static readonly List<Def> buffer = new();
        private static readonly HashSet<string> shownNames = new(StringComparer.Ordinal);
        private static readonly Comparison<Def> comparison = Compare;

        [SelfInit]
        public static void Reset() {
            ranks.Clear();
            nodeCosts.Clear();
        }

        public static int Compare(Def a, Def b) {
            if (ReferenceEquals(a, b)) { return 0; }
            var ra = RankOf(a);
            var rb = RankOf(b);
            var c = ra.Tier.CompareTo(rb.Tier);
            if (c != 0) { return c; }
            c = ra.Cost.CompareTo(rb.Cost);
            if (c != 0) { return c; }
            c = string.Compare(a.NameT, b.NameT, StringComparison.CurrentCulture);
            if (c != 0) { return c; }
            return string.CompareOrdinal(a.Id, b.Id);
        }

        public static void Sort(List<Def> defs) {
            if (defs == null || defs.Count < 2) { return; }
            defs.Sort(comparison);
        }

        // Codex links in unlock order, ", +N" past the limit. A name already shown (several
        // floors are all "Floor") is folded into the +N too, because identical links read as a
        // mistake. The website export lists everything, each repeat links to its own page. The
        // defs list is copied, callers may pass lists other code owns
        public static void AppendLinks(StringBuilder sb, IReadOnlyList<Def> defs, int limit) {
            if (defs == null || defs.Count == 0) { return; }
            buffer.Clear();
            for (int i = 0; i < defs.Count; i++) {
                buffer.Add(defs[i]);
            }
            Sort(buffer);
            var isExport = CodexExportScope.IsActive;
            var cap = isExport ? int.MaxValue : limit;
            shownNames.Clear();
            var shown = 0;
            var hidden = 0;
            foreach (var def in buffer) {
                if (shown >= cap || (!isExport && !shownNames.Add(def.NameT))) {
                    hidden++;
                    continue;
                }
                if (shown > 0) {
                    sb.Append(", ");
                }
                sb.Append(def.CodexLink);
                shown++;
            }
            if (hidden > 0) {
                sb.Append($", +{hidden}");
            }
            shownNames.Clear();
            buffer.Clear();
        }

        private static Rank RankOf(Def def) {
            if (ranks.TryGetValue(def, out var rank)) {
                return rank;
            }
            var tier = 0;
            closure.Clear();
            pending.Clear();
            if (Researchable.TreeNode(def)) {
                tier = def.Tier;
                pending.Push(def);
            } else if (!Researchable.IsEmptyPrereq(def)) {
                foreach (var id in def.Researchable.Prerequisites) {
                    var node = The.Defs.TryGet(id);
                    if (node == null) { continue; }
                    tier = Math.Max(tier, node.Tier);
                    pending.Push(node);
                }
            }
            var cost = 0f;
            while (pending.Count > 0) {
                var node = pending.Pop();
                if (!closure.Add(node)) { continue; }
                cost += NodeCost(node);
                if (Researchable.IsEmptyPrereq(node)) { continue; }
                foreach (var id in node.Researchable.Prerequisites) {
                    var parent = The.Defs.TryGet(id);
                    if (parent != null && !closure.Contains(parent)) {
                        pending.Push(parent);
                    }
                }
            }
            closure.Clear();
            rank = new Rank(tier, cost);
            ranks[def] = rank;
            return rank;
        }

        // Same formula as ResearchTree.Balance's UnlockCost, which is only filled in a
        // running game
        private static float NodeCost(Def node) {
            if (nodeCosts.TryGetValue(node, out var cost)) {
                return cost;
            }
            cost = 0f;
            var costs = node.Researchable?.DatoidCosts;
            if (costs != null) {
                foreach (var d in costs) {
                    if (d.Type == null) { continue; }
                    cost += d.Type.InternalValue * d.StackSize;
                }
            }
            nodeCosts[node] = cost;
            return cost;
        }
    }
}
