using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StopGrowingAction : BaseEntityAction {
        public const string ActionId = "StopGrowing";

        [SelfInit]
        private static void Register() {
            RegisterAction(new StopGrowingAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.StopGrowing;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanStop(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, tile), Priority.Normal);
            return true;
        }

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

        private WBase Create(Lifetime l, RectTransform p, int i, Tile tile) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CLeaf, T.StopGrowing, () => {
                tile.GetComponent<PlanterComp>().StopGrowing();
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CLeaf, T.StopGrowing,
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
                        p.GetComponent<PlanterComp>().StopGrowing();
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
            if (!tile.TryGetComponent<PlanterComp>(out var planter)) {
                return false;
            }
            return planter.IsConfigured;
        }
    }
}
