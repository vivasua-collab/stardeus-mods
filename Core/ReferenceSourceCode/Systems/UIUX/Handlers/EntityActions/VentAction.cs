using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class VentAction : BaseEntityAction {
        public const string ActionIdOpen = "VentOpen";
        public const string ActionIdClose = "VentClose";
        // Floor vents share this binding, so one key toggles whichever vent is selected
        public const string ToggleVentHotkeyId = "ToggleVent";

        private enum Kind {
            Open,
            Close,
        }

        private readonly Kind kind;

        [SelfInit]
        private static void Register() {
            RegisterAction(new VentAction(Kind.Open));
            RegisterAction(new VentAction(Kind.Close));
        }

        private VentAction(Kind kind) {
            this.kind = kind;
        }

        public override string Id => kind == Kind.Open ? ActionIdOpen : ActionIdClose;
        public override string HotkeyLabel => $"{T.OpenVent} / {T.CloseVent}";
        public override string HotkeyId => ToggleVentHotkeyId;

        private string Icon => kind == Kind.Open ? IconId.CFlow : IconId.CNoFlow;
        private string Label => kind == Kind.Open ? T.OpenVent : T.CloseVent;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanApply(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, tile.GetComponent<VentComp>()),
                Priority.First);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!ToggleAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.First);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, VentComp vent) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(Icon, Label, () => {
                vent.ToggleVent();
                A.S.UIQuery.RebuildEntityActions.Ask();
                VentTab.ReloadIfShown();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, Icon, Label, () => ToggleAll(tiles, false, out _));
            return widget;
        }

        private bool ToggleAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanApply(p)) {
                    c++;
                    if (!dryRun) {
                        p.GetComponent<VentComp>().ToggleVent();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
                VentTab.ReloadIfShown();
            }
            return cnt > 0;
        }

        private bool CanApply(Tile tile) {
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent<VentComp>(out var vent)) {
                return false;
            }
            return kind == Kind.Open ? vent.IsClosed : !vent.IsClosed;
        }
    }
}
