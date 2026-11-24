using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // The Fire at Will toggle on drafted units. One button that shows what a click does:
    // Hold Fire while the unit fires at will, Fire at Will while it holds fire.
    public sealed class FireAtWillAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new FireAtWillAction());
        }
        public const string ActionId = "FireAtWill";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.VerbFireAtWill} / {T.VerbHoldFire}";

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!TryGetDrafted(entity, out var control)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, control),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DirectControlComp control) {
            var widget = SpawnButton(l, true, p, i);
            var holdNext = control.FireAtWill;
            widget.Load(IconFor(holdNext), LabelFor(holdNext), () => {
                control.SetFireAtWill(!holdNext);
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings,
                out EntityActionData data) {
            int cnt = 0;
            bool anyFiresAtWill = false;
            foreach (var being in beings) {
                if (TryGetDrafted(being, out var control)) {
                    cnt++;
                    anyFiresAtWill |= control.FireAtWill;
                }
            }
            if (cnt == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            // any unit still firing at will: the click makes the whole group hold fire
            var holdNext = anyFiresAtWill;
            var list = new List<Being>(beings);
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, list, cnt, holdNext), Priority.Normal);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Being> beings,
                int cnt, bool holdNext) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconFor(holdNext), LabelFor(holdNext), () => {
                foreach (var being in beings) {
                    if (TryGetDrafted(being, out var control)) {
                        control.SetFireAtWill(!holdNext);
                    }
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        private static string IconFor(bool holdNext) {
            return holdNext ? IconId.CStop : IconId.CTarget;
        }

        private static string LabelFor(bool holdNext) {
            return holdNext ? T.VerbHoldFire : T.VerbFireAtWill;
        }

        private static bool TryGetDrafted(Being being, out DirectControlComp control) {
            control = null;
            if (!being.IsActive || !being.TryGetComponent(out DirectControlComp dc)) {
                return false;
            }
            if (!dc.IsUnderDirectControl) { return false; }
            // the toggle shows only with a ranged weapon in hand: without one there is
            // nothing to hold, a drafted unit only hits what it can reach either way
            if (being.AIAgent?.CombatUnit?.HasUsableRangedAttack() != true) { return false; }
            control = dc;
            return true;
        }
    }
}
