using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.Areas {
    /// <summary>
    /// Builds the itemized "you are about to abandon..." text shown before a hyperjump - the
    /// sections that would be cut loose (see AreaSys.CollectDerelictIslands), the loose items
    /// and material piles that would be lost with them (CmdAbandonSection destroys every Obj on
    /// a cut-loose section, and ShipSys.RemoveLooseObjects destroys every Obj drifting in open
    /// space), and the non-player beings standing on those sections.
    /// </summary>
    public static class AbandonPreview {
        [SelfInit]
        private static void Cleanup() {
            islandKeys.Clear();
            orphans.Clear();
            positions.Clear();
            objCounts.Clear();
            firstObjOf.Clear();
            beings.Clear();
            rows.Clear();
        }

        private const int MaxRows = 20;

        private static readonly List<int> islandKeys = new();
        private static readonly List<Tile> orphans = new();
        private static readonly HashSet<int> positions = new();
        private static readonly Dictionary<Def, int> objCounts = new();
        private static readonly Dictionary<Def, Obj> firstObjOf = new();
        private static readonly List<Being> beings = new();
        private static readonly List<string> rows = new();

        public static bool TryBuild(GameState s, out string text) {
            s.Sys.Areas.CollectDerelictIslands(islandKeys, out _);
            OrphanSpaceDevices.Collect(s, orphans);

            positions.Clear();
            foreach (var islandId in islandKeys) {
                if (s.Sys.Areas.Islands.TryGetValue(islandId, out var cells)) {
                    positions.UnionWith(cells);
                }
            }

            objCounts.Clear();
            firstObjOf.Clear();
            foreach (var o in s.Objs.All) {
                if (!o.IsActive || o.IsCarriedByAny) { continue; }
                if (!o.Transform.IsInSpace && !positions.Contains(o.PosIdx)) { continue; }
                if (!IsLoseable(o)) { continue; }
                var def = o.Definition;
                objCounts.TryGetValue(def, out var count);
                objCounts[def] = count + (o.Pile?.StackSize ?? 1);
                if (!firstObjOf.ContainsKey(def)) {
                    firstObjOf[def] = o;
                }
            }

            beings.Clear();
            foreach (var b in s.Beings.All) {
                if (!b.IsActive) { continue; }
                if (b.Data.Mind.Faction?.IsPlayer != false) { continue; }
                if (!positions.Contains(b.PosIdx)) { continue; }
                beings.Add(b);
            }

            if (islandKeys.Count == 0 && orphans.Count == 0 && objCounts.Count == 0
                    && beings.Count == 0) {
                text = null;
                return false;
            }

            rows.Clear();
            foreach (var id in islandKeys.OrderByDescending(k => s.Sys.Areas.Islands[k].Count)) {
                var cells = s.Sys.Areas.Islands[id];
                var rekt = Pos.Encapsulate(cells);
                rows.Add(string.Format("{0} ({1}: {2})",
                    Texts.LinkToArea(T.IslandNr_V1.F(id), rekt), T.Size, Units.Num(cells.Count)));
            }
            foreach (var t in orphans) {
                rows.Add(Texts.LinkToTile(t));
            }
            foreach (var kv in objCounts.OrderBy(kv => kv.Key.NameT)) {
                var first = firstObjOf[kv.Key];
                rows.Add(kv.Value > 1 ? $"{first.EntityName} x{kv.Value}" : first.EntityName);
            }
            foreach (var b in beings.OrderBy(b => b.Data.Mind.Name.ShortName)) {
                rows.Add($"{b.EntityName} {Texts.GreyParentheses(b.Data.Mind.Faction.NameT)}");
            }

            var sb = Caches.StringBuilder4;
            sb.Clear();
            var shown = rows.Count < MaxRows ? rows.Count : MaxRows;
            for (int i = 0; i < shown; i++) {
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, rows[i]);
            }
            if (rows.Count > shown) {
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, T.ListMore_V1.F(rows.Count - shown));
            }
            text = sb.ToString();
            return true;
        }

        private static bool IsLoseable(Obj o) {
            if (o.Pile == null && o.StorableObj == null) { return false; }
            if (o.Transform.SkipMoveLoose) { return false; }
            if (o.HasComponent<IWithTrajectory>()) { return false; }
            return true;
        }
    }
}
