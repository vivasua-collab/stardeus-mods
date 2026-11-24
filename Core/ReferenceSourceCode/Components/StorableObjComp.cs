using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using KL.Utils;

namespace Game.Components {
    public sealed class StorableObjComp : BaseComponent<StorableObjComp>,
            IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StorableObjComp());
        }
        public string Group;
        public int GroupH;
        private bool isForbidden;
        public bool IsForbidden {
            get => isForbidden;
            set {
                if (isForbidden != value) {
                    isForbidden = value;
                    UpdateForbiddenIcon();
                }
            }
        }
        // Kept so mods built before the rename still load
        [System.Obsolete("Use IsForbidden")]
        public bool IsHaulingForbidden {
            get => IsForbidden;
            set => IsForbidden = value;
        }
        public long SpawnedAt;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsNoHaul", IsForbidden);
            data.SetLong("SpawnedAt", G.Ticks);
        }

        protected override void OnLoad(ComponentData data) {
            IsForbidden = data.GetBool("IsNoHaul", false);
            SpawnedAt = data.GetLong("SpawnedAt", 0);
        }

        protected override void OnConfig() {
            Group = Config.GetString(PropertyIdH.Group);
            GroupH = Hashes.S(Group);
        }
        public override string ToString() {
            return $" * StorableObj [{Group}|{Obj}]";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!wasLoaded || SpawnedAt == 0) {
                SpawnedAt = G.Ticks;
            }
        }


        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.CheckDoable = CanStoreObj;
        }

        public bool HasCompatibleStorage() {
            if (Obj == null) {
                return false;
            }
            var storages = S.Query.ObjStoragesForGroup.Ask(GroupH);
            if (storages == null) { return false; }
            foreach (var s in storages) {
                if (s.IsFull) { continue; }
                if (s.HaulTarget == null || s.HaulTarget.Accepts(Obj.Data)) {
                    return true;
                }
            }
            return false;
        }

        public bool IsItemUsable {
            get {
                if (Obj.Data.Quality == 0f) {
                    return false;
                }
                if (Obj.Data.BodyPartState?.IsDestroyed == true) {
                    return false;
                }
                #if !PRODUCTION_BUILD
                if (Obj.Data.BodyPartState?.Quality <= 0.01f) {
                    D.Err("Body part not destroyed but quality 0f! {0}", Obj);
                }
                #endif
                return true;
            }
        }

        public bool TryOrderStoring(bool byPlayer, bool checkAvailableStorage, out string failReason) {
            failReason = null;
            if (HasGoal) {
                // D.Warn("Already has a storing goal! {0}", Obj);
                if (AIGoal.IsFailed(entity.Goal)) {
                    if (AIGoal.TryRetry(entity.Goal, G.Ticks, true, true)) {
                        return true;
                    }
                }
                if (byPlayer) {
                    failReason = T.AnotherTaskInProgress_V1.F(entity.Goal.TaskLine);
                }
                return false;
            }
            if (IsForbidden) {
                if (byPlayer) {
                    failReason = T.AdjForbidden;
                }
                return false;
            }
            if (!Obj.Data.IsAvailable) {
                if (byPlayer) {
                    failReason = T.NotAvailable;
                }
                return false;
            }
            if (!IsItemUsable) {
                // try order disposal instead!
                if (entity.TryGetComponent<DisposableComp>(out var disposable)) {
                    if (disposable.TryOrderDispose(byPlayer, out _)) {
                        return true;
                    }
                }
                if (!byPlayer) {
                    failReason = $"{T.Condition}: {T.OpinionBad}";
                }
                return false;
            }
            if (checkAvailableStorage) {
                if (!HasCompatibleStorage()) {
                    // D.Warn("No compatible storage for {0}", Obj);
                    if (byPlayer) {
                        failReason = T.StorageUnavailable;
                    }
                    return false;
                }
            }
            var g = CreateGoal(JobTypeIdH.Hauling, IconId.CStore, T.ActionStoreItem, this)
                .WithWorldToDesired(AIVarsH.IsObjStored, false, true)
                // no boost on hauling
                .ByPlayer(false)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WriteInitialState();
            g.CheckDoable = CanStoreObj;
            if (entity.TrySetGoal(g)) {
                return true;
            }
            return false;
        }

        private AIGoalCheckResult CanStoreObj(GameState S, AIGoal goal) {
            if (HasCompatibleStorage()) {
                return AIGoalCheckResult.Doable;
            } else {
                S.Sig.FailedFindingStorageFor.Send(Entity);
                return AIGoalCheckResult.Fail(T.StorageUnavailable, true);
            }
        }

        public void UpdateForbiddenIcon() {
            if (IsForbidden) {
                ShowInfoIcon(IconId.CForbidSmall, T.ItemForbiddenTip);
            } else {
                HideInfoIcon();
            }
        }
    }
}
