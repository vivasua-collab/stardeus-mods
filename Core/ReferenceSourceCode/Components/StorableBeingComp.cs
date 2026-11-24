using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using UnityEngine.Pool;
using System;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class StorableBeingComp : BaseComponent<StorableBeingComp>,
            IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StorableBeingComp());
        }
        public bool IsReachableForCommonAction {
            get {
                if (!Being.Data.IsPlayer) {
                    if (Being.IsAlive && !Being.Data.Body.IsIncapacitated) {
                        return false;
                    }
                }
                return true;
            }
        }
        public bool CanBeStoredNow {
            get {
                if (Being.Data.HasStateFlag(BeingState.InDevice)) {
                    return false;
                }
                if (Being.IsDead) {
                    // allow storing anyone dead
                    return true;
                }
                // no storing for non-dead biologicals
                if (Being.Data.Species.IsBiological) {
                    return false;
                }
                // allow storing any incapacitated non-biologicals
                if (Being.Data.Body.IsIncapacitated) {
                    return true;
                }
                // allow storing player bots only
                return Being.Data.IsPlayer;
            }
        }

        protected override void OnConfig() {
        }
        public override string ToString() {
            return " * StorableBeing";
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.CheckDoable = CanStoreBeing;
        }
        public bool CheckCompatibleStorages(HashSet<BeingStorageComp> storages,
                Action<BeingStorageComp, StorableBeingComp> onCompatible = null) {
            bool hadCompatible = false;
            foreach (var s in storages) {
                if (s.IsFull) { continue; }
                if (s.HaulTarget == null || s.HaulTarget.Accepts(Being.Data)) {
                    hadCompatible = true;
                    onCompatible?.Invoke(s, this);
                }
            }
            return hadCompatible;
        }

        public bool HasCompatibleStorage() {
            var result = ListPool<BeingStorageComp>.Get();
            var found = S.Query.BeingStoragesFor.Ask(Being.Data, result);
            if (found == 0) { return false; }
            bool hasStorage = false;
            foreach (var s in result) {
                if (s.IsFull) { continue; }
                if (s.HaulTarget == null || s.HaulTarget.Accepts(Being.Data)) {
                    hasStorage = true;
                    break;
                }
            }
            result.Clear();
            ListPool<BeingStorageComp>.Release(result);
            return hasStorage;
        }

        public bool TryOrderStoring(bool byPlayer, bool checkAvailableStorage) {
            if (HasGoal) {
                // D.Warn("Already has a storing goal! {0}", Obj);
                return false;
            }
            if (Being.Data.HasStateFlag(BeingState.InDevice)) {
                return false;
            }
            if (!IsReachableForCommonAction) {
                return false;
            }
            if (checkAvailableStorage) {
                if (!HasCompatibleStorage()) {
                    // D.Warn("No compatible storage for {0}", Obj);
                    return false;
                }
            }
            if (HasAnyGoal) {
                AIGoal.Cancel(entity.Goal, G.Ticks, T.VerbStoreBeing);
            }
            var g = CreateGoal(JobTypeIdH.Hauling, IconId.CStore, T.VerbStoreBeing, this)
                .WithWorldToDesired(AIVarsH.IsBeingStored, false, true)
                // no boost on hauling
                .ByPlayer(false)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WriteInitialState();
            g.CheckDoable = CanStoreBeing;

            AIAgentComp forcedAgent = null;
            if (!Being.Motion.IsImmobilized) {
                forcedAgent = Being.AIAgent;
                if (forcedAgent.Goal != null) {
                    AIGoal.Interrupt(forcedAgent.Goal, G.Ticks, T.VerbStoreBeing);
                }
            }
            return entity.TrySetGoal(g, forcedAgent);
        }

        private AIGoalCheckResult CanStoreBeing(GameState S, AIGoal goal) {
            if (HasCompatibleStorage()) {
                return AIGoalCheckResult.Doable;
            } else {
                S.Sig.FailedFindingStorageFor.Send(Entity);
                return AIGoalCheckResult.Fail(T.StorageUnavailable, true);
            }
        }
    }
}
