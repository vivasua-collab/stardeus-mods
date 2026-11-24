using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class BeingPettableSubTab : BaseBeingSubTab {
        public BeingPettableSubTab(EntityOverviewContext ctx) : base(ctx) { }

        public override bool Spawn(RectTransform container, int siblingIdx) {
            if (being.Being == null) { return false; }
            if (!being.Being.TryGetComponent<PettableComp>(out var pettable)) { return false; }
            if (being.Being.IsDead) { return false; }
            if (!being.Species.IsPet) { return false; }

            pettable.StateChangedB.Bind(lifetime, () => Reload(container, siblingIdx, pettable), false);

            SpawnRow(container, siblingIdx, pettable);
            return true;
        }

        private void SpawnRow(RectTransform container, int siblingIdx, PettableComp pettable) {
            if (pettable.HasGoal) {
                var row = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, container, siblingIdx);
                row.Load(IconId.CHeart, T.WillBePettedSoon);
                UIUtils.AddTipFunc(lifetime, row, () => pettable.LastPetTooltip());
            } else {
                var btn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, container, siblingIdx);
                btn.Load(IconId.CHeart, T.OrderPetting, null, () => {
                    if (!pettable.TryOrderPetting()) {
                        UISounds.PlayActionDenied();
                    }
                });
                UIUtils.AddTipFunc(lifetime, btn, () => pettable.LastPetTooltip());
            }
        }

        private void Reload(RectTransform container, int siblingIdx, PettableComp pettable) {
            lifetime.Dispose();
            lifetime = ctx.Lifetime.CreateChild($"BeingSubTab_{GetType().Name}");
            SpawnRow(container, siblingIdx, pettable);
        }
    }
}
