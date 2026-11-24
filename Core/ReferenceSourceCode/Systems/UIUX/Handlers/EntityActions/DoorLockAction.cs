using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DoorLockAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DoorLockAction());
        }

        public const string ActionId = "DoorLock";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.Lock} / {T.Unlock}";
        public override string HotkeyId => "ToggleDoorLock";
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanLock(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile.GetComponent<DoorComp>()), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DoorComp door) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CLocked, T.Lock, () => {
                if (door.SetLockedByUI(true)) {
                    A.S.UIQuery.RebuildEntityActions.Ask();
                }
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CLocked, T.Lock,
                () => LockAll(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!LockAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));
            return true;
        }

        public static bool LockAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanLock(p)) {
                    c++;
                    if (!dryRun) {
                        var door = p.GetComponent<DoorComp>();
                        door.SetLockedByUI(true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanLock(Tile tile) {
            if (!tile.IsActive || tile.ENode?.IsReachable != true) {
                return false;
            }
            if (!tile.TryGetComponent<DoorComp>(out var door)) {
                return false;
            }
            return !door.IsLocked;
        }
    }
}
