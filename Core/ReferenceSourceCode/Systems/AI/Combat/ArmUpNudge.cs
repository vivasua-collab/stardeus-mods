using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Policies;
using KL.Utils;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// When a raid threatens (announced in advance, or with the raid alert), beings whose
    /// weapon policy lets them arm themselves go get a weapon right away instead of after their
    /// current task or a day of equipment backoff. A Manual weapon policy is left alone: the
    /// player said hands off.
    /// </summary>
    public static class ArmUpNudge {
        public static void Send(GameState S) {
            var need = Registries.AINeeds.FastGet(NeedIdH.Equipment) as AINeedEquipment;
            if (need == null) { return; }
            var ticks = S.Ticks;
            foreach (var data in S.Query.AllBeingData.Ask().Values) {
                if (WantsToArm(S, data)) {
                    need.ForceArmUp(data.Being.AIAgent, ticks);
                }
            }
        }

        public static bool WantsToArm(GameState S, BeingData data) {
            if (!data.IsPlayer || data.IsGuest) { return false; }
            var being = data.Being;
            if (being == null || !being.IsAlive || data.Body.IsIncapacitated) { return false; }
            if (data.HasCondition(ConditionIdH.Sleeping)) { return false; }
            var agent = being.AIAgent;
            if (agent.DirectControl.IsUnderDirectControl) { return false; }
            // walking where something else sent it (an order, a scripted move)
            if (agent.Motion.IsForcedWalk) { return false; }
            if (agent.Goal?.Type == AIGoalType.Combat) { return false; }
            var inventory = data.Inventory;
            if (inventory == null
                    || !inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var slot)) {
                return false;
            }
            if (!data.Mind.Jobs.CanHaveWeapon()) { return false; }
            var policy = S.Query.EquipmentPolicyFor.Ask(data, slot.Type);
            if (policy == null || policy.Mode == AutoEquipMode.Manual) { return false; }
            var filter = EquipmentPolicyFilter.For(policy, data);
            if (!EquipmentPolicyUtils.SlotWantsItem(slot.ObjData, policy, filter,
                    out var treatAsEmpty) || !treatAsEmpty) {
                // already holding a weapon the policy allows
                return false;
            }
            // nothing to fetch: don't drop the current task for it
            return S.Query.BestAvailableEquipment.Ask(slot, null, null) != null;
        }
    }
}
