using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OrderRepairAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OrderRepairAction());
        }

        public const string ActionId = "OrderRepair";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.Repair;
        public override string HotkeyDefault => "<Keyboard>/r";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanOrderRepair(tile, out var damageable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, damageable),
                Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!OrderRepair(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DamageableComp damageable) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CWrench2, T.Repair, () => {
                if (!damageable.CreateRepairGoal()) {
                    UISounds.PlayActionDenied();
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CWrench2, T.Repair,
                () => OrderRepair(tiles, false, out _));
            return widget;
        }

        private bool OrderRepair(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            var visited = Caches.IntSet;
            visited.Clear();
            foreach (var tile in tiles) {
                var parent = EntityUtils.ParentObj(tile);
                if (parent == null || !visited.Add(parent.Id)) {
                    continue;
                }
                if (CanOrderRepair(parent, out var damageable)) {
                    c++;
                    if (!dryRun) {
                        damageable.CreateRepairGoal();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanOrderRepair(Tile tile, out DamageableComp damageable) {
            damageable = null;
            if (tile?.IsActive != true) {
                return false;
            }
            damageable = tile.Health;
            if (damageable == null || !damageable.IsDamaged) {
                return false;
            }
            if (tile.Constructable?.HasAnyGoal == true) {
                return false;
            }
            return true;
        }
    }
}
