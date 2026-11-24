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
    public sealed class OrderPettingAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OrderPettingAction());
        }
        public const string ActionId = "OrderPetting";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (entity.IsDead
                    || !entity.TryGetComponent<PettableComp>(out var pettable)
                    || !entity.Species.IsPet
                    || pettable.HasGoal) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, pettable), Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!OrderPettingAll(beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), cnt), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, PettableComp pettable) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CHeart, T.OrderPetting, () => {
                if (!pettable.TryOrderPetting()) {
                    UISounds.PlayActionDenied();
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            UIUtils.AddTipFunc(l, widget, () => pettable.LastPetTooltip());
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CHeart, T.OrderPetting,
                () => OrderPettingAll(beings, false, out _));
            return widget;
        }

        private static bool OrderPettingAll(List<Being> beings, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var being in beings) {
                if (being.IsDead
                        || !being.TryGetComponent<PettableComp>(out var pettable)
                        || !being.Species.IsPet
                        || pettable.HasGoal) {
                    continue;
                }
                c++;
                if (!dryRun) {
                    pettable.TryOrderPetting();
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
