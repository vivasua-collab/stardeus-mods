using Game.Data;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    [MessagePackObject]
    public struct CombatUnitData {
        [Key("State")] public CombatUnitState State;
        [Key("Goals")] public AIGoalData[] UnitGoals;
        [Key("LastTick")] public long LastTick;
        [Key("Supp")] public float SuppressionLevel;
        [Key("Forced")] public bool IsAttackTargetForced;
        [Key("RoutRolled")] public bool HasRoutFleeRoll;
        [Key("RoutFlee")] public bool WantsToFleeOnRout;
        [Key("GaveUpFlee")] public bool GaveUpFleeing;
        // BeingData id of a player-ordered target, 0 for none
        [Key("ForcedTarget")] public uint ForcedTargetId;
        // the order on ForcedTarget may walk the unit closer (CombatUnit.OrderMayApproach)
        [Key("Approach")] public bool OrderMayApproach;
        [Key("Leaving")] public bool IsLeaving;

        public static CombatUnitData Serialize(CombatUnit cu) {
            var data = new CombatUnitData {
                LastTick = cu.LastTick,
                State = cu.State,
                SuppressionLevel = cu.SuppressionLevel,
                UnitGoals = AIGoalData.Serialize(cu.Goals),
                IsAttackTargetForced = cu.IsAttackTargetForced,
                HasRoutFleeRoll = cu.HasRoutFleeRoll,
                WantsToFleeOnRout = cu.WantsToFleeOnRout,
                GaveUpFleeing = cu.GaveUpFleeing,
                // a provoked retaliation is not an order: it is meant to lapse
                ForcedTargetId = cu.IsAttackTargetForced && !cu.IsRetaliating
                    && cu.AttackTarget != null ? cu.AttackTarget.Data.Id : 0,
                OrderMayApproach = cu.OrderMayApproach,
                IsLeaving = cu.IsLeaving,
            };
            return data;
        }

        public static CombatUnit Deserialize(GameState S, AIAgentComp agent, CombatUnitData data) {
            var cu = agent.CombatUnit;
            cu.LastTick = data.LastTick;
            cu.SuppressionLevel = data.SuppressionLevel;
            cu.LoadState(data.State);
            cu.IsAttackTargetForced = data.IsAttackTargetForced;
            cu.HasRoutFleeRoll = data.HasRoutFleeRoll;
            cu.WantsToFleeOnRout = data.WantsToFleeOnRout;
            cu.GaveUpFleeing = data.GaveUpFleeing;
            cu.PendingForcedTargetId = data.ForcedTargetId;
            cu.OrderMayApproach = data.OrderMayApproach;
            cu.IsLeaving = data.IsLeaving;
            if (data.ForcedTargetId == 0) {
                // an order without its target (older saves, a provoked retaliation): a
                // forced flag with no target would only keep target selection away
                cu.IsAttackTargetForced = false;
            }
            AIGoalData.Deserialize(S, data.UnitGoals, cu.Goals);
            cu.Comp?.SyncSuppressionThoughtBubble(cu);
            return cu;
        }
    }
}
