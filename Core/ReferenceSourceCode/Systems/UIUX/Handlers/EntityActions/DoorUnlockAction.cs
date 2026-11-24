using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DoorUnlockAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DoorUnlockAction());
        }

        public const string ActionId = "DoorUnlock";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.Lock} / {T.Unlock}";
        public override string HotkeyId => "ToggleDoorLock";
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanUnlock(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile.GetComponent<DoorComp>()), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DoorComp door) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CUnlocked, T.Unlock, () => {
                if (door.SetLockedByUI(false)) {
                    A.S.UIQuery.RebuildEntityActions.Ask();
                }
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CUnlocked, T.Unlock,
                () => UnlockAll(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!UnlockAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));
            return true;
        }

        public static bool UnlockAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanUnlock(p)) {
                    c++;
                    if (!dryRun) {
                        var door = p.GetComponent<DoorComp>();
                        door.SetLockedByUI(false);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanUnlock(Tile tile) {
            if (!tile.IsActive || tile.ENode?.IsReachable != true) {
                return false;
            }
            if (!tile.TryGetComponent<DoorComp>(out var door)) {
                return false;
            }
            return door.IsLocked;
        }
    }
}
