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
    public sealed class CleanNowAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new CleanNowAction());
        }

        public const string ActionId = "CleanNow";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.CleanNow;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (entity.IsDead
                    || !entity.AIAgent.DirectControl.IsControllable
                    || !entity.TryGetComponent<CleaningBotComp>(out var comp)
                    || comp.HasActiveCleaningGoal) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, comp));
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!CleanNowAll(beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), cnt));
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, CleaningBotComp comp) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CMop, T.CleanNow, () => {
                if (!comp.TrySearchForDirt()) {
                    WPopup.Create(false, IconId.CMop, T.NothingToClean, T.NothingToCleanInfo).Spawn();
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CMop, T.CleanNow,
                () => CleanNowAll(beings, false, out _));
            return widget;
        }

        private static bool CleanNowAll(List<Being> beings, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var being in beings) {
                if (being.IsDead
                        || !being.AIAgent.DirectControl.IsControllable
                        || !being.TryGetComponent<CleaningBotComp>(out var comp)
                        || comp.HasActiveCleaningGoal) { continue; }
                c++;
                if (!dryRun) { comp.TrySearchForDirt(); }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }
    }
}
