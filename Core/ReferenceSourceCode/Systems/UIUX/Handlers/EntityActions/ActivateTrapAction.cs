using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ActivateTrapAction : BaseEntityAction {
        public const string ActionId = "ActivateTrap";

        [SelfInit]
        private static void Register() {
            RegisterAction(new ActivateTrapAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.StructureFloorsTrapfloor} - {T.Activate}";

        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanActivate(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, tile.GetComponent<TrapFloorComp>()),
                Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!ActivateAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, TrapFloorComp trap) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CCombat, T.Activate, () => {
                trap.TriggerTrap(false);
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CCombat, T.Activate,
                () => ActivateAll(tiles, false, out _));
            return widget;
        }

        private static bool ActivateAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanActivate(p)) {
                    c++;
                    if (!dryRun) {
                        p.GetComponent<TrapFloorComp>().TriggerTrap(false);
                    }
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

        private static bool CanActivate(Tile tile) {
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent<TrapFloorComp>(out var trap)) {
                return false;
            }
            return trap.IsTrapReady && !trap.IsClosed;
        }
    }
}
