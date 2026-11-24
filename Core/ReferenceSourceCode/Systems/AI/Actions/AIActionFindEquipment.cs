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
    public sealed class AIActionFindEquipment : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindEquipment());
        }
        public const string ActId = "FindEquipment";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindEquipment() : base(ActId,
                () => T.VerbSearchingFor_V1.F(T.EquipableItem)) {
            Preconditions = AIState.With(AIVarsH.HasFoundEquipment, false);
            Outcomes = AIState.With(AIVarsH.HasFoundEquipment, true);
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.UseItem);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetObjData = null;
            agent.BB.ClearBlacklist();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            if (AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal)) {
                if (agent.BB.TargetObjData != null) {
                    agent.BB.TargetObjData.TryUnreserve(goal);
                    agent.BB.TargetObjData = null;
                }
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var itemPool = ArrayPool<ObjData>.Shared;
            var itemsArr = itemPool.Rent(512);
            // 50% chance to search for a specific slot that has a missing item
            int missingCnt = 0;
            bool canHaveHelmet = false;
            if (agent.BeingData.Inventory.Equipment.TryGetValue(InventorySlotIdH.Suit, out var suit)) {
                canHaveHelmet = suit.Item != null;
            }
            // Slots that need an item and already came back empty-handed, for the fail reason
            string unfilledSlots = null;
            foreach (var s in agent.BeingData.Inventory.Equipment) {
                if (s.Key == InventorySlotIdH.Helmet && !canHaveHelmet) {
                    // don't try to get a helmet when you have no suit
                    continue;
                }
                var invSlot = s.Value;
                var policy = S.Query.EquipmentPolicyFor.Ask(agent.BeingData, invSlot.Type);
                if (policy == null) { continue; }
                var filter = EquipmentPolicyFilter.For(policy, agent.BeingData);
                // This is the fallback for AIActionFindBestEquipment and only ever
                // fills slots, so it wants the ones holding nothing usable: empty, or
                // wearing something the policy forbids.
                if (!EquipmentPolicyUtils.SlotWantsItem(invSlot.ObjData, policy, filter,
                        out var treatAsEmpty)) {
                    continue;
                }
                if (!treatAsEmpty) { continue; }
                if (agent.BB.IsBlacklisted(s.Key)) {
                    unfilledSlots = unfilledSlots == null
                        ? invSlot.NameT : $"{unfilledSlots}, {invSlot.NameT}";
                    continue;
                }
                Caches.IntArray32[missingCnt++] = s.Key;
            }

            int slotId;
            if (missingCnt > 0) {
                slotId = Caches.IntArray32[Rng.URange(0, missingCnt)];
            } else {
                agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer30Minutes,
                    Consts.TicksPerHour);
                if (unfilledSlots == null) {
                    // Nothing to fill, which is not worth a log line or a thought
                    // bubble, so cancel, not fail
                    AINeedEquipment.BackOffUpgradeSearch(agent, ticks);
                    return Cancelled($"{T.Equipment}: {T.Ok}");
                }
                return Failed($"{T.RequiredObjectIsNotAvailable} ({unfilledSlots})");
            }
            int found = S.Query.FindCompatibleEquipment.Ask(agent, slotId,
                itemsArr, agent.BB.BlacklistUint, agent.BB.Blacklist2);
            if (found == 0) {
                agent.BB.AddToBlacklist(slotId);
                itemPool.Return(itemsArr, true);
                //try one more time with broader search
                return InProgress;
            }
            var item = FindClosestEquipmentItem(S, agent, itemsArr, found);
            itemPool.Return(itemsArr, true);
            if (item != null) {
                int itemPos = item.PosIdx(S, out var storage);
                if (item != null) {
                    D.Warn("Found closest equipment item: {0} | {1} | St: {2}",
                        Pos.String(itemPos), item, storage);
                }
                if (!agent.Motion.EstimatePathTo(itemPos, onDone: null,
                        flags: PathReqFlags.Instant)) {
                    if (agent.TicksInCurrentAction > Consts.TicksPer2Hours) {
                        agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer2Hours,
                            Consts.TicksPer3Hours);
                        return Failed(T.AdRejUnreachableItem);
                    }
                    //can't reach
                    agent.BB.AddToBlacklistUint(item.Id);
                    if (storage != null) {
                        agent.BB.AddToBlacklist2(storage.EntityId);
                    }
                    return InProgress;
                }
                if (agent.BB.SetTargetObj(item, goal)) {
                    goal.WithWorld(AIVarsH.HasFoundEquipment, true);
                    return Success;
                } else {
                    agent.BB.SetCooldown(IdH, ticks, Consts.TicksPerHour,
                        Consts.TicksPer2Hours);
                    // agent.BB.SetCooldown(NeedIdH.Equipment, ticks, Consts.TicksPerHour,
                    //     Consts.TicksPer20Hours);
                    return Failed(T.AdRejCouldNotReserve);
                }
            }

            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer2Hours,
                Consts.TicksPer3Hours);
            // agent.BB.SetCooldown(NeedIdH.Equipment, ticks, Consts.TicksPerHour,
            //     Consts.TicksPer2Hours);
            return Failed(T.AdRejUnreachableItem);
        }

        private ObjData FindClosestEquipmentItem(GameState S, AIAgentComp agent,
                ObjData[] list, int cnt) {
            var wp = agent.PosIdx;
            var isG = agent.Motion.IsGrounded;
            var reqPool = ArrayPool<RegPathReq>.Shared;
            var regPathReqs = reqPool.Rent(cnt);
            var resPool = ArrayPool<int>.Shared;
            var resPathLengths = resPool.Rent(cnt);
            for (int i = 0; i < cnt; i++) {
                // rented arrays are not cleared, a default request resolves to -1
                regPathReqs[i] = default;
                var item = list[i];
                if (!item.IsAvailable) { continue; }
                if (item.Obj?.StorableObj?.IsForbidden == true) { continue; }
                var pos = item.PosIdx(S, out _);
                if (pos == Pos.Invalid || !agent.IsFeelingSafeAt(pos)) { continue; }
                regPathReqs[i] = RegPathReq.To(wp, pos, isG);
                regPathReqs[i].Permissions = agent.Motion.Permissions;
            }

            ObjData closest = null;
            float closestDist = float.MaxValue;

            if (S.Query.RegPathLengths.Ask(regPathReqs, cnt, resPathLengths)) {
                for (int i = 0; i < cnt; i++) {
                    float dist = resPathLengths[i];
                    if (dist < 0) { continue; }
                    // to break the monotony of always going for the same item
                    // in same region
                    dist += Rng.UValue * 0.2f;
                    if (dist < closestDist) {
                        closest = list[i];
                        closestDist = dist;
                        if (dist < 0.1f) {
                            break;
                        }
                    }
                }
            }
            reqPool.Return(regPathReqs);
            resPool.Return(resPathLengths);

            return closest;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return CachedRandomCostWithCoolDown(agent.BB, ticks, Consts.TicksPer30Minutes, 0.9f, 1.1f);
        }
    }
}
