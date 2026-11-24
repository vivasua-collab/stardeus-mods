using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class PileAllowAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new PileAllowAction());
        }

        public const string ActionId = "PileAllow";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.VerbAllow} / {T.VerbForbid}";
        public override string HotkeyId => "ToggleAutoHaul";
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanAllow(obj, out var pile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, pile), Priority.Normal, -10);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!AllowAll(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Obj>(objs), cnt), Priority.Normal, -10);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, UnstoredMatComp pile) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CCheck, T.VerbAllow, () => {
                pile.IsForbidden = false;
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CCheck, T.VerbAllow,
                () => AllowAll(objs, false, out _));
            return widget;
        }

        public static bool AllowAll(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (CanAllow(obj, out var pile)) {
                    c++;
                    if (!dryRun) {
                        pile.IsForbidden = false;
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanAllow(Obj obj, out UnstoredMatComp pile) {
            pile = null;
            if (!obj.IsActive) {
                return false;
            }
            if (!obj.TryGetComponent(out pile)) {
                return false;
            }
            return !pile.TreatAsObj && pile.IsForbidden;
        }
    }
}
