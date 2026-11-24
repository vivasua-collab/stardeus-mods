using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DeactivateSirenAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DeactivateSirenAction());
        }

        public const string ActionId = "DeactivateSiren";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.ActivateSiren} / {T.DeactivateSiren}";
        public override string HotkeyId => "ToggleSiren";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!tile.IsConstructed || tile.ENode?.IsReachable == false) {
                data = EntityActionData.Unavailable;
                return false;
            }
            if (!tile.TryGetComponent<EmergencySirenComp>(out var siren) || !siren.IsAlertActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, siren));
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, EmergencySirenComp siren) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CSiren, T.DeactivateSiren, () => {
                siren.RequestDeactivate();
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!DeactivateAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CSiren, T.DeactivateSiren, () => DeactivateAll(tiles, false, out _));
            return widget;
        }

        public static bool DeactivateAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (!p.IsActive || !p.IsConstructed || p.ENode?.IsReachable == false) { continue; }
                if (!p.TryGetComponent<EmergencySirenComp>(out var siren) || !siren.IsAlertActive) { continue; }
                c++;
                if (!dryRun) { siren.RequestDeactivate(); }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) { A.S.UIQuery.RebuildEntityActions.Ask(); }
            return cnt > 0;
        }
    }
}
