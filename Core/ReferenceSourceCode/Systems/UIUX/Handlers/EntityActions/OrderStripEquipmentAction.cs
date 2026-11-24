using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OrderStripEquipmentAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OrderStripEquipmentAction());
        }
        public const string ActionId = "OrderStripEquipment";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.StripEquipment;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!CanStrip(entity, out var comp)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, comp), Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, InventoryComp comp) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CNoClothing, T.OrderStrip, () => {
                comp.OrderStripEquipment();
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CNoClothing, T.StripEquipment,
                () => StripAll(beings, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!StripAll(beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), cnt), Priority.Normal);
            return true;
        }

        public static bool StripAll(List<Being> beings, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var being in beings) {
                if (CanStrip(being, out var comp)) {
                    c++;
                    if (!dryRun) {
                        comp.OrderStripEquipment();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanStrip(Being being, out InventoryComp comp) {
            comp = null;
            if (being == null || !being.IsActive) { return false; }
            var bd = being.Data;
            if (bd.Inventory == null) { return false; }
            // Own beings: always. Others: only when down (dead / incapacitated),
            // mirrors DisposableComp.CanDisposeNow non-friendly rule.
            if (!being.IsDead) {
                if (bd.IsFriendly) { return false; }
                if (!bd.Body.IsIncapacitated) { return false; }
            }
            if (!being.TryGetComponent(out InventoryComp ic)) { return false; }
            if (ic.HasGoal) { return false; }
            if (ic.Inventory?.HasRemovableEquipment != true) { return false; }
            comp = ic;
            return true;
        }
    }
}
