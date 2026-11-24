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
    public sealed class QuestPackageAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new QuestPackageAction());
        }

        public const string ActionId = "QuestPackageOpen";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.OpenCapsule;
        public override string HotkeyId => OrderOpenCapsuleAction.OpenContainerHotkeyId;
        public override string HotkeyDefault => "<Keyboard>/o";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanOrderOpen(obj, out var package)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, package),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, QuestPackageComp package) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.COpenPackage, T.Open, () => {
                if (!package.TryOrderOpen()) {
                    UISounds.PlayActionDenied();
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private static bool CanOrderOpen(Obj obj, out QuestPackageComp package) {
            package = null;
            if (!obj.IsActive || !obj.TryGetComponent(out package)) {
                return false;
            }

            return !package.IsOpen && !package.HasGoal;
        }
    }
}
