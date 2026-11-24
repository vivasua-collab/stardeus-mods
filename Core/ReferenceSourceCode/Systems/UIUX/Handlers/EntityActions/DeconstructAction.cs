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
    public sealed class DeconstructAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DeconstructAction());
        }
        public const string ActionId = "Deconstruct";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.Deconstruct;
        public override string HotkeyDefault => "<Keyboard>/x";
        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            var con = tile.Constructable;
            if (!con.CanDeconstruct()) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, con),
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, ConstructableComp con) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CDemolition, T.Deconstruct, () => {
                if (!con.Tile.IsActive) {
                    widget.Dispose();
                    return;
                }
                // The order may come later from a confirmation popup, when this widget can
                // already be back in the pool, so it is not touched there
                DeconstructConfirm.OrderOrConfirm(con.Tile, () => {
                    if (con.TryOrderDeconstruct()) {
                        UIUtils.RefreshSelectedEntityUI();
                    }
                });
            });
            UIUtils.AddTipFunc(l, widget, con.OutputTip);
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CDemolition, T.Deconstruct,
                () => Deconstruct(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!Deconstruct(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));

            return true;
        }

        public static bool Deconstruct(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.IsActive && p.Constructable.CanDeconstruct()) {
                    c++;
                    if (!dryRun) {
                        p.Constructable.CreateDeconstructGoal();
                    }
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

    }
}
