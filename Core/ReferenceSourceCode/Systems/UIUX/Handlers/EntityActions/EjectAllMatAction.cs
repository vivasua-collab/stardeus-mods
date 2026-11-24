using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class EjectAllMatAction : BaseEntityAction {
        public const string ActionId = "EjectAllMat";

        [SelfInit]
        private static void Register() {
            RegisterAction(new EjectAllMatAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.StorageEjectAll;
        public override string HotkeyId => "EjectAll";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanEject(tile, out var storage)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                // Low: a housekeeping action, it sits after Relocate, Build a Copy and
                // Deconstruct and before Copy Config (Low, 1), not first on the bar
                (l, p, i) => Create(l, p, i, storage), Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!EjectAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                MatStorageComp storage) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.COpenPackage, T.StorageEjectAll, () => {
                storage.DumpContents(true);
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.COpenPackage, T.StorageEjectAll,
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
                        storage.DumpContents(true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanEject(Tile tile, out MatStorageComp storage) {
            storage = null;
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent(out storage)) {
                return false;
            }
            if (storage.StorageType == StorageType.TakeOnly) {
                return false;
            }
            if (storage.HideUI) {
                return false;
            }
            return !storage.IsEmpty;
        }
    }
}
