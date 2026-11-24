using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class NodeDisconnectAction : BaseEntityAction {
        // Kept from when this class also had a Try Auto Connect button: saved key
        // overrides of Disconnect All are stored under this id
        public const string ActionId = "NodeDisconnect";

        [SelfInit]
        private static void Register() {
            RegisterAction(new NodeDisconnectAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.DisconnectAll;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanApply(tile, out var node)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, node),
                Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!ApplyAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, ElectricNodeComp node) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CNoEnergy, Label(node.Node.NumConnections), () => {
                Apply(node);
                Coroutines.OnFrame(1, () => {
                    A.S.UIQuery.RebuildEntityActions.Ask();
                    A.S.UIQuery.ReloadCurrentEntity.Ask();
                });
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CNoEnergy, T.DisconnectAll, () => ApplyAll(tiles, false, out _));
            return widget;
        }

        private bool ApplyAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            var visited = Caches.IntSet;
            visited.Clear();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p == null || !visited.Add(p.Id)) {
                    continue;
                }
                if (!CanApply(p, out var node)) {
                    continue;
                }

                c++;
                if (!dryRun) {
                    Apply(node);
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                Coroutines.OnFrame(1, () => A.S.UIQuery.RebuildEntityActions.Ask());
            }
            return cnt > 0;
        }

        private static void Apply(ElectricNodeComp node) {
            if (node.Node.NumConnections > 0) {
                node.S.Sys.Electricity.DisconnectAll(node);
            }
        }

        private static bool CanApply(Tile tile, out ElectricNodeComp node) {
            node = null;
            if (tile?.IsActive != true || !tile.IsConstructed) {
                return false;
            }

            node = tile.ENode;
            if (node == null || !node.Entity.IsActive) {
                return false;
            }

            var n = node.Node;
            return n.IsReachable && n.NumConnections > 0;
        }

        private static string Label(int count) {
            var txt = T.EnergyNodeDisconnect;
            if (count > 1) {
                txt = $"{txt} ({Units.Num(count)})";
            }
            return txt;
        }
    }
}
