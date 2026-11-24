using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SwitchOnAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new SwitchOnAction());
        }
        public const string ActionId = "SwitchOn";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.SwitchOn} / {T.SwitchOff}";
        public override string HotkeyId => "ToggleSwitch";
        public override string HotkeyDefault => "<Keyboard>/z";
        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (tile.ENode == null || !tile.ENode.IsReachable || !tile.ENode.IsSwitchable || tile.ENode.IsSwitchedOn) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile.ENode),
                Priority.Low, 3);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, ElectricNodeComp con) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CTurnOn, T.SwitchOn, () => {
                con.Switch(true);
                A.S.UIQuery.ReloadCurrentEntity.Ask();
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CTurnOn, T.SwitchOn,
                () => SwitchAll(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!SwitchAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));

            return true;
        }

        public static bool SwitchAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.IsActive && p.ENode?.IsReachable == true && p.ENode.IsSwitchable && !p.ENode.IsSwitchedOn) {
                    c++;
                    if (!dryRun) {
                        p.ENode.Switch(true);
                    }
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

    }
}
