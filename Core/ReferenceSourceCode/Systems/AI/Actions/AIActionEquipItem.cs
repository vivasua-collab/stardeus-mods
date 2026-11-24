using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;

namespace Game.Systems.AI {
    public sealed class AIActionEquipItem : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionEquipItem());
        }
        public const string ActId = "EquipItem";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionEquipItem() : base(ActId, () => T.AbilityEquipitem) {
            Preconditions = AIState.With(AIVarsH.IsItemEquipped, false)
                .And(AIVarsH.HasFoundEquipment, true);
            Outcomes = AIState.With(AIVarsH.IsItemEquipped, true);
            WithRequiredAbilities(AbilityIdH.UseItem);
            MarkAsRest();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var objData = agent.BB.TargetObjData;
            if (objData == null) {
                D.Err("Equip Item has null TargetObjData in Agent.BB: {0} -> {1}",
                    agent, goal);
                return Error(T.RequiredObjectIsNotAvailable);
            }

            var item = objData.Item;
            if (item == null) {
                D.Err("Equip Item has non InventoryItem TargetObjData in Agent.BB: {0} -> {1} -> {2}",
                    objData, agent, goal);
                return Error(T.RequiredObjectIsNotAvailable);
            }

            if (!objData.IsAvailableIgnoreReservation) {
                return Failed(T.RequiredObjectIsNotAvailable);
            }

            var pos = objData.PosIdx(S, out var storage);
            if (pos == Pos.Invalid) {
                return Failed(T.RequiredObjectIsNotAvailable);
            }

            var wp = agent.PosIdx;
            if (pos == wp) {
                if (storage != null) {
                    if (!storage.TryTake(agent, objData, goal)) {
                        D.Err("Failed taking item from storage! {0}/{1}/{2}", objData, storage, goal);
                        return Failed(T.RequiredObjectIsNotAvailable);
                    }
                }
                if (agent.BeingData.Inventory.TryEquip(S, objData)) {
                    goal.WorldState.Set(AIVarsH.IsItemEquipped, true);
                    return Success;
                } else {
                    // agent.BB.SetCooldown(NeedIdH.Equipment, G.Ticks,
                    //     Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
                    return Failed("Could not equip item!");
                }
            } else {
                // the object is spawned in the map and not inside a storage
                if (!TryGoToOnce(agent, pos, false)) {
                    return Failed(T.AdRejUnreachableItem);
                } else {
                    agent.ShowThoughtBubble(objData.Def.Preview,
                        $"{T.AbilityEquipitem}: {objData.Def.CodexLink}", objData.Color);
                }
            }

            return InProgress;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // if (AIGoal.IsFailed(goal)) {
            //     agent.BB.SetCooldown(NeedIdH.Equipment, G.Ticks,
            //         Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
            // }
            agent.BB.SetTargetObj(null, goal);
            agent.BB.ClearMoveTarget();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
