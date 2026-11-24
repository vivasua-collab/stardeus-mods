using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Crafting;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StopProducingAction : BaseEntityAction {
        public const string ActionId = "StopProducing";

        [SelfInit]
        private static void Register() {
            RegisterAction(new StopProducingAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.StopProducing;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!StopAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CStop, T.StopProducing,
                () => StopAll(tiles, false, out _));
            return widget;
        }

        private static bool StopAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanStop(p)) {
                    c++;
                    if (!dryRun) {
                        p.GetComponent<MultiCrafterComp>().UI.StopAll();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanStop(Tile tile) {
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent<MultiCrafterComp>(out var crafter)) {
                return false;
            }
            return !Arrays.IsEmpty(crafter.Orders);
        }
    }
}
