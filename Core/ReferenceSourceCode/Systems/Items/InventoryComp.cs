using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Items {

    public sealed class InventoryComp : BaseComponent<InventoryComp>, IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new InventoryComp());
        }

        public BeingInventory Inventory => Being.Data.Inventory;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.FactionChanged && HoldsRestrictedItem()) {
                // A robot that joins the colony falls under the colony's law. Not right
                // now: the lazy EffectiveFaction getter sends this mid-iteration of the
                // objects, and dropping creates one.
                var data = Being.Data;
                var s = S;
                Coroutines.OnNextFrame(() => {
                    if (A.S != s) { return; }
                    RobotWeaponLaw.DropIllegal(data);
                });
            }
        }

        private bool HoldsRestrictedItem() {
            var inventory = Being?.Data?.Inventory;
            if (inventory == null) { return false; }
            foreach (var slot in inventory.Equipment.Values) {
                if (slot.Item != null && slot.Item.RequiredBodyFlags != StatFlags.None) {
                    return true;
                }
            }
            return false;
        }

        public void OrderStripEquipment() {
            if (HasGoal) {
                S.Sig.ShowErrorMessage.Send(
                    T.AnotherTaskInProgress_V1.F(Texts.Accented(entity.Goal.Name)));
                return;
            }
            if (Inventory?.HasRemovableEquipment != true) {
                UISounds.PlayActionDenied();
                return;
            }
            if (HasAnyGoal) {
                AIGoal.Cancel(entity.Goal, G.Ticks, T.StripEquipment);
                if (HasAnyGoal) {
                    S.Sig.ShowErrorMessage.Send(
                        T.AnotherTaskInProgress_V1.F(Texts.Accented(entity.Goal.Name)));
                    return;
                }
            }
            var g = CreateGoal(JobTypeIdH.Task, IconId.CNoClothing, T.StripEquipment, this)
                .WithDesired(AIVarsH.IsEquipmentStripped, true)
                // .WithWorldToDesired(AIVarsH.IsEquipmentStripped, false, true)
                .ByPlayer(true)
                .WithRequiredAbilities(AbilityIdH.UseItem, AbilityIdH.DumbLabor)
                .WriteInitialState();
            if (!entity.TrySetGoal(g)) {
                UISounds.PlayActionDenied();
            }
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }
    }
}
