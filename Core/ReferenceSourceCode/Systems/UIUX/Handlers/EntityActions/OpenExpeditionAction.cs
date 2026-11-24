using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OpenExpeditionAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OpenExpeditionAction());
        }

        public const string ActionId = "OpenExpedition";
        public override string Id => ActionId;

        public override bool ApplyForDetached(GameState s, BeingData being, out EntityActionData data) {
            if (!being.HasStateFlag(BeingState.Expedition)
                    || s.Sys.Expedition.FindForCrew(being) == null) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, being), Priority.High);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, BeingData being) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CPickaxe, T.Expedition, () => {
                // Resolved on click, the expedition can end while the panel stays open
                var exp = A.S.Sys.Expedition.FindForCrew(being);
                if (exp != null) {
                    A.S.UIQuery.ShowExpedition.Ask(exp);
                } else {
                    A.S.UIQuery.RebuildEntityActions.Ask();
                }
            });
            return widget;
        }
    }
}
