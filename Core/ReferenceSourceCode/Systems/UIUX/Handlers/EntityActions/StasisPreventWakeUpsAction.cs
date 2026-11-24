#if false
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StasisPreventWakeUpsAction : BaseEntityAction {
        public const string ActionIdEnable = "StasisPreventWakeUps_Enable";
        public const string ActionIdDisable = "StasisPreventWakeUps_Disable";

        private enum Kind {
            Enable,
            Disable,
        }

        private readonly Kind kind;

        [SelfInit]
        private static void Register() {
            RegisterAction(new StasisPreventWakeUpsAction(Kind.Enable));
            RegisterAction(new StasisPreventWakeUpsAction(Kind.Disable));
        }

        private StasisPreventWakeUpsAction(Kind kind) {
            this.kind = kind;
        }

        public override string Id => kind == Kind.Enable ? ActionIdEnable : ActionIdDisable;

        private string Icon => IconId.CCross;
        private string Label => kind == Kind.Enable
            ? $"{T.StasisPreventWakeUps}: {T.On}"
            : $"{T.StasisPreventWakeUps}: {T.Off}";

        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanApply(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, tile.GetComponent<StasisArrayComp>()),
                Priority.Low);
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
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                StasisArrayComp stasis) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(Icon, Label, () => {
                stasis.PreventWakeUps = !stasis.PreventWakeUps;
                A.S.UIQuery.RebuildEntityActions.Ask();
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
                        var stasis = p.GetComponent<StasisArrayComp>();
                        stasis.PreventWakeUps = !stasis.PreventWakeUps;
                    }
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private bool CanApply(Tile tile) {
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent<StasisArrayComp>(out var stasis)) {
                return false;
            }
            if (stasis.IsElysium || !stasis.IsALSOn) {
                return false;
            }
            return kind == Kind.Enable ? !stasis.PreventWakeUps : stasis.PreventWakeUps;
        }
    }
}
#endif
