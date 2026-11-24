using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OrderCleaningAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OrderCleaningAction());
        }
        public const string ActionId = "OrderCleaning";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.OrderCleaningNow;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            if (!tile.TryGetComponent<CleanableComp>(out var cleanable) || !cleanable.CanOrderCleaning) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, cleanable), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, CleanableComp cleanable) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CMop, T.OrderCleaningNow, () => {
                cleanable.CreateCleaningGoal(true);
                A.S.Query.DeselectAny.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CMop, T.OrderCleaningNow,
                () => OrderCleaning(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!OrderCleaning(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));
            return true;
        }

        private bool OrderCleaning(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.IsActive && p.TryGetComponent<CleanableComp>(out var cleanable)
                        && cleanable.CanOrderCleaning) {
                    c++;
                    if (!dryRun) {
                        cleanable.CreateCleaningGoal(true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.Query.DeselectAny.Ask();
            }
            return cnt > 0;
        }
    }
}
