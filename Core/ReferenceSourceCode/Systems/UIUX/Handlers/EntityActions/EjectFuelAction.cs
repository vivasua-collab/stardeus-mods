using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class EjectFuelAction : BaseEntityAction {
        public const string ActionId = "EjectFuel";

        [SelfInit]
        private static void Register() {
            RegisterAction(new EjectFuelAction());
        }

        public override string Id => ActionId;

        // Single device: the Refill tab has the same button, so the bar only carries it
        // for multi-selection, where no tab is shown
        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!EjectAll(tiles, true, out var cnt)) {
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
            widget.LoadMulti(cnt, IconId.CStop, T.Eject,
                () => EjectAll(tiles, false, out _));
            return widget;
        }

        private static bool EjectAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanEject(p, out var storage)) {
                    c++;
                    if (!dryRun) {
                        storage.Stop();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanEject(Tile tile, out RefillableStorageComp storage) {
            storage = null;
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent(out storage)) {
                return false;
            }
            return storage.IsConfigured && !storage.HasLockedMaterial;
        }
    }
}
