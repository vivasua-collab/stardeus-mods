using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using System.Buffers;
using Game.Systems.Policies;
using Game.Systems.Reg;
using KL.Grid;
using Game.Systems.Path;

namespace Game.Systems.AI {
    public sealed class AIActionFindBestEquipment : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindBestEquipment());
        }
        public const string ActId = "FindBestEquipment";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindBestEquipment() : base(ActId,
                () => T.VerbSearchingFor_V1.F(T.EquipableItem)) {
            Preconditions = AIState.With(AIVarsH.HasFoundEquipment, false);
            Outcomes = AIState.With(AIVarsH.HasFoundEquipment, true);
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.UseItem);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetObjData = null;
            agent.BB.ClearBlacklist();
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            agent.BB.ClearMoveTarget();
            if (AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal)) {
                if (agent.BB.TargetObjData != null) {
                    agent.BB.TargetObjData.TryUnreserve(goal);
                    agent.BB.TargetObjData = null;
                }
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // 50% chance to search for a specific slot that has a missing item
            int missingCnt = 0;
            int blacklistedCnt = 0;
            int equippedCount = 0;
            int slotCount = 0;
            // Slots that need an item and already came back empty-handed, for the fail reason
            string unfilledSlots = null;
            var canHaveWeapon = agent.BeingData.Mind.Jobs.CanHaveWeapon();
            bool canHaveHelmet = false;
            if (agent.BeingData.Inventory.Equipment.TryGetValue(InventorySlotIdH.Suit, out var suit)) {
                canHaveHelmet = suit.Item != null;
            }
            foreach (var s in agent.BeingData.Inventory.Equipment) {
                if (s.Key == InventorySlotIdH.Weapon) {
                    if (!canHaveWeapon) {
                        continue;
                    }
                } else if (s.Key == InventorySlotIdH.Helmet) {
                    if (!canHaveHelmet) {
                        continue;
                    }
                }
                var invSlot = s.Value;
                var policy = S.Query.EquipmentPolicyFor.Ask(agent.BeingData, invSlot.Type);
                if (policy == null) { continue; }
                var filter = EquipmentPolicyFilter.For(policy, agent.BeingData);
                if (!EquipmentPolicyUtils.SlotWantsItem(invSlot.ObjData, policy, filter,
                        out var treatAsEmpty)) {
                    // Manual, or already wearing something allowed under IfEmpty.
                    continue;
                }
                slotCount++;
                if (agent.BB.IsBlacklisted(s.Key)) {
                    blacklistedCnt++;
                    if (treatAsEmpty) {
                        unfilledSlots = unfilledSlots == null
                            ? invSlot.NameT : $"{unfilledSlots}, {invSlot.NameT}";
                    }
                    continue;
                }
                // A slot whose item violates the policy counts as missing, so it is
                // filled before slots that only want an upgrade.
                if (treatAsEmpty) {
                    Caches.IntArray32[missingCnt++] = s.Key;
                } else {
                    Caches.IntArray128[equippedCount++] = s.Key;
                }
            }

            int slotId;
            if (missingCnt > 0) {
                // get the random missing slot
                slotId = Caches.IntArray32[Rng.URange(0, missingCnt)];
                // told to arm up for a raid (AINeedEquipment.ForceArmUp): the gun comes first
                if (agent.BB.IsCoolingDown(AINeedEquipment.ArmUpIdH, ticks)) {
                    for (int i = 0; i < missingCnt; i++) {
                        if (Caches.IntArray32[i] == InventorySlotIdH.Weapon) {
                            slotId = InventorySlotIdH.Weapon;
                            break;
                        }
                    }
                }
            } else {
                if (blacklistedCnt == slotCount) {
                    if (unfilledSlots == null) {
                        // Nothing is missing, there just is nothing better around. That is
                        // not worth a log line or a thought bubble, so cancel, not fail.
                        AINeedEquipment.BackOffUpgradeSearch(agent, ticks);
                        return Cancelled($"{T.Equipment}: {T.Ok}");
                    }
                    // we had something missing but failed to get it
                    return Failed($"{T.RequiredObjectIsNotAvailable} ({unfilledSlots})");
                } else {
                    // let's try to upgrade something
                    // that we already have equipped
                    slotId = Caches.IntArray128[Rng.URange(0, equippedCount)];
                }
            }
            var slot = agent.BeingData.Inventory.Equipment[slotId];
            var bestItem = S.Query.BestAvailableEquipment.Ask(slot,
                    agent.BB.BlacklistUint, agent.BB.Blacklist2);
            if (bestItem == null) {
                agent.BB.AddToBlacklist(slotId);
                //try one more time with broader search
                return InProgress;
            }
            if (bestItem.Obj?.StorableObj?.IsForbidden == true) {
                agent.BB.AddToBlacklistUint(bestItem.Id);
                return InProgress;
            }

            var itemPos = bestItem.PosIdx(S, out var storage);


            var canReach = agent.Motion.EstimatePathTo(itemPos, onDone: null,
                    flags: PathReqFlags.Instant);
            if (!canReach) {
                if (agent.TicksInCurrentAction > Consts.TicksPer2Hours) {
                    agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer2Hours,
                        Consts.TicksPer3Hours);
                    return Failed(T.AdRejUnreachableItem);
                }
                agent.BB.AddToBlacklistUint(bestItem.Id);
                if (storage != null) {
                    agent.BB.AddToBlacklist2(storage.EntityId);
                }
                return InProgress;
            }

            // We do full PF instead
            // var dist = S.Query.RegionDist.Ask(
            //     RegPathReq.Create(agent.PosIdx, itemPos, agent.Motion.IsGrounded, true));

            // if (dist == -1) {
            //     agent.BB.AddToBlacklistUint(bestItem.Id);
            //     return InProgress;
            // }

            if (agent.BB.SetTargetObj(bestItem, goal)) {
                goal.WithWorld(AIVarsH.HasFoundEquipment, true);
                return Success;
            } else {
                agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer6Hours,
                    Consts.TicksPer8Hours);
                // agent.BB.SetCooldown(NeedIdH.Equipment, ticks, Consts.TicksPerHour,
                //     Consts.TicksPer2Hours);
                agent.BB.AddToBlacklistUint(bestItem.Id);
                return InProgress;
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return CachedRandomCostWithCoolDown(agent.BB, ticks, Consts.TicksPer30Minutes, 0.7f, 0.9f);
        }
    }
}
