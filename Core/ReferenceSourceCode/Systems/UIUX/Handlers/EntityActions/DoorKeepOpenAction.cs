using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DoorKeepOpenAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DoorKeepOpenAction());
        }

        public const string ActionId = "DoorKeepOpen";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.KeepOpen} / {T.AutoClose}";
        public override string HotkeyId => "ToggleDoorKeepOpen";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanKeepOpen(tile) && !IsLockedDoor(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile.GetComponent<DoorComp>()), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DoorComp door) {
            if (door.IsLocked) {
                return CreateLocked(l, p, i);
            }
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.COpenDoor, T.KeepOpen, (System.Action)(() => {
                if (door.ForceOpen(true)) {
                    A.S.UIQuery.RebuildEntityActions.Ask();
                }
            }));
            return widget;
        }

        // A locked door cannot be held open, but the button keeps its place: locking used to
        // take it off the centered bar, every button moved half a slot, and the next click on
        // the same spot hit Disconnect (STA-1756)
        private WBase CreateLocked(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, false, p, i);
            widget.Load(IconId.COpenDoor, Texts.Strike(T.KeepOpen), () => {
                A.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.Locked}");
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.COpenDoor, T.KeepOpen,
                () => KeepOpenAll(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (KeepOpenAll(tiles, true, out var cnt)) {
                data = new EntityActionData(this,
                    (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));
                return true;
            }
            foreach (var t in tiles) {
                if (IsLockedDoor(EntityUtils.ParentObj(t))) {
                    data = new EntityActionData(this, (l, p, i) => CreateLocked(l, p, i));
                    return true;
                }
            }
            data = EntityActionData.Unavailable;
            return false;
        }

        public static bool KeepOpenAll(List<Tile> tiles, bool dryRun, out int cnt) {

            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanKeepOpen(p)) {
                    c++;
                    if (!dryRun) {
                        var door = p.GetComponent<DoorComp>();
                        door.ForceOpen(true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanKeepOpen(Tile tile) {
            if (!tile.IsActive || tile.ENode?.IsReachable != true) {
                return false;
            }
            if (!tile.TryGetComponent<DoorComp>(out var door)) {
                return false;
            }
            return !door.IsLocked && !door.IsForcedOpen;
        }

        private static bool IsLockedDoor(Tile tile) {
            if (!tile.IsActive || tile.ENode?.IsReachable != true) {
                return false;
            }
            return tile.TryGetComponent<DoorComp>(out var door) && door.IsLocked;
        }
    }
}
