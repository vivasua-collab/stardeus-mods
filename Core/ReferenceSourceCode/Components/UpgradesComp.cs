using System.Collections.Generic;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Systems.Inventory;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using Game.Systems.AI;

namespace Game.Components {
    public sealed class UpgradesComp : BaseComponent<UpgradesComp>,
            IYieldingGoalProvider, IRelocatable, IMatRequester, IWithEntity {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new UpgradesComp());
        }
        private MatDeficitEventHolder deficitEv;
        private string[] upgradesToRestore;

        public string[] Upgrades;
        public int UsedSlots;
        public int NumSlots;
        private bool isChoosingUpgrade;

        public string[] PendingUpgrades;
        private List<MatRequest> missingMatsCache;
        // Set while we cancel our own goal ourselves (to hand the goal slot to another
        // component, or to recreate it), so OnGoalChange keeps the pending upgrades
        private bool isCancellingOwnGoal;

        public int PendingCount {
            get {
                if (PendingUpgrades == null) { return 0; }
                int count = 0;
                for (int i = 0; i < PendingUpgrades.Length; i++) {
                    if (PendingUpgrades[i] != null) { count++; }
                }
                return count;
            }
        }

        public bool CanOrderUpgrade => (UsedSlots + PendingCount) < NumSlots;
        private BCallback upgradesChanged;
        public BCallback UpgradesChanged => upgradesChanged ??= new BCallback();

        public int InPort {
            get {
                if (Tile != null && Tile.Transform.HasInPort) {
                    return Tile.Transform.InPort;
                }
                return 0;
            }
        }

        public List<MatRequest> MissingMats {
            get {
                if (PendingUpgrades == null) { return null; }
                if (missingMatsCache == null) {
                    missingMatsCache = new List<MatRequest>();
                    for (int i = 0; i < PendingUpgrades.Length; i++) {
                        var upId = PendingUpgrades[i];
                        if (upId == null) { continue; }
                        var upgrade = UpgradeRegistry.Get(upId);
                        if (upgrade == null) { continue; }
                        var matType = MatType.Get(upgrade.MatTypeId);
                        if (matType == null) { continue; }
                        missingMatsCache.Add(new MatRequest() {
                            Type = matType,
                            Amount = 1,
                            Requester = this,
                        });
                    }
                }
                return missingMatsCache.Count > 0 ? missingMatsCache : null;
            }
        }

        public MatRequest NextMissingMat {
            get {
                if (PendingUpgrades == null) { return null; }
                // Prefer a pending upgrade whose material is available
                MatRequest firstPending = null;
                for (int i = 0; i < PendingUpgrades.Length; i++) {
                    var upId = PendingUpgrades[i];
                    if (upId == null) { continue; }
                    var upgrade = UpgradeRegistry.Get(upId);
                    if (upgrade == null) { continue; }
                    var matType = MatType.Get(upgrade.MatTypeId);
                    if (matType == null) { continue; }
                    var req = new MatRequest() {
                        Type = matType,
                        Amount = 1,
                        Requester = this,
                    };
                    if (firstPending == null) {
                        firstPending = req;
                    }
                    // Must agree with CanInstallUpgrade about what is reachable, or the
                    // worker gets sent after a material that is claimed by a higher
                    // priority task
                    if (S.Sys.Inventory.AvailableFor(matType, this,
                            entity.Goal?.GoalPriority ?? 0) > 0) {
                        return req;
                    }
                }
                return firstPending;
            }
        }

        public int FirstEmptySlot {
            get {
                for (int i = 0; i < NumSlots; i++) {
                    if (Upgrades.SafeGet(i) != null) { continue; }
                    if (PendingUpgrades.SafeGet(i) != null) { continue; }
                    return i;
                }
                return -1;
            }
        }

        private void InvalidateMissingMatsCache() {
            missingMatsCache = null;
        }

        private void EnsurePendingArray() {
            if (PendingUpgrades == null) {
                PendingUpgrades = new string[NumSlots];
            }
        }

        protected override void OnConfig() {
            NumSlots = Config.GetInt(PropertyIdH.NumSlots, 3);
            deficitEv = new MatDeficitEventHolder(Entity, this, T.Upgrades);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.DeconstructionFinished
                    || message == MsgIdH.HealthWentToZero
                    || message == MsgIdH.ProcessingEnqueued) {
                EjectAll();
            }
        }

        public override void OnRemove() {
            deficitEv.Clear();
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetStringSet("Upgrades", Upgrades);
            data.SetStringSet("PendingUpgrades", PendingUpgrades);
            if (PendingCount > 0) {
                deficitEv.Save(data);
            }
        }

        private bool isAddedToUpgradedList;
        public void EjectAll() {
            ClearAllPending();
            if (Upgrades == null) { return; }
            for (var i = 0; i < Upgrades.Length; i++) {
                var u = Upgrades[i];
                if (u == null) {
                    continue;
                }

                var up = UpgradeRegistry.Get(u);
                if (up != null) {
                    EjectSlot(i, up);
                }
            }
        }

        private void ClearAllPending() {
            if (PendingUpgrades == null) { return; }
            for (int i = 0; i < PendingUpgrades.Length; i++) {
                PendingUpgrades[i] = null;
            }
            InvalidateMissingMatsCache();
            deficitEv.Clear();
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
                HideInfoIcon();
            }
        }

        public void Install(int slot, Upgrade upgrade, bool isLoading = false) {
            if (upgrade == null) {
                #if !PRODUCTION_BUILD
                D.Err("Refusing to install null upgrade");
                #endif
                return;
            }
            if (Upgrades == null) {
                Upgrades = new string[NumSlots];
            }
            if (Upgrades[slot] != null) {
                D.Err("Slot {0} already taken by {1} while trying to install {2}",
                    slot, Upgrades[slot], upgrade);
                if (PendingUpgrades != null) { PendingUpgrades[slot] = null; }
                InvalidateMissingMatsCache();
                return;
            }
            UsedSlots++;
            Upgrades[slot] = upgrade.UpgradeId;
            if (!isAddedToUpgradedList) {
                isAddedToUpgradedList = true;
                S.Sys.Upg.AddToUpgraded(this);
            }
            try {
                upgrade.OnInstall(Entity);
            } catch (System.Exception ex) {
                var upgradeMat = MatType.Get(upgrade.MatTypeId);
                var upgradeDef = upgradeMat?.Def;
                string modInfo = upgradeDef != null
                    ? $"\n\n{The.ModLoader.ModTagFor(upgradeDef)}" : string.Empty;
                D.LogEx(ex, "Failed upgrade.OnInstall for {0}", upgrade);
                WPopup.Create(true, IconId.CWarning, T.OperationFailed,
                $"A system error occurred when installing {upgrade.Title}.{modInfo}", Texts.Red(ex.Message))
                    .Spawn();
            }
            if (PendingUpgrades != null) {
                PendingUpgrades[slot] = null;
            }
            InvalidateMissingMatsCache();
            if (!isLoading) {
                if (PendingCount == 0) {
                    deficitEv.Clear();
                }
                S.Sig.TileUpgradeChanged.Send(upgrade, Tile, true);
            }
            upgradesChanged?.Invoke();
        }
        public void DestroyUpgrade(int slot, string id) {
            var upg = Upgrades[slot];
            if (upg != id) {
                D.Err("Wrong upgrade installed in slot {0}! Expected {1}, was {2}",
                    slot, id, upg);
                return;
            }
            Uninstall(slot, UpgradeRegistry.Get(id));
        }

        private void Uninstall(int slot, Upgrade upgrade) {
            D.Warn("Uninstalling upgrade {0} from slot {1} in {2}", upgrade, slot, Entity);
            if (Upgrades == null) {
                D.Err("Trying to uninstall slot {0} while upgrades are null!", slot);
                return;
            }
            if (Upgrades[slot] == null) {
                D.Err("Trying to uninstall empty slot {0}", slot);
                return;
            }
            UsedSlots--;
            Upgrades[slot] = null;
            if (UsedSlots == 0 && isAddedToUpgradedList) {
                S.Sys.Upg.RemoveFromUpgraded(this);
                isAddedToUpgradedList = false;
            }
            upgrade.OnUninstall(Entity);
            S.Sig.TileUpgradeChanged.Send(upgrade, Tile, false);
            upgradesChanged?.Invoke();
        }

        protected override void OnLoad(ComponentData data) {
            upgradesToRestore = data.GetStringSet("Upgrades", null);
            PendingUpgrades = data.GetStringSet("PendingUpgrades", null);
            // Backward compatibility: migrate old single-pending format
            if (PendingUpgrades == null) {
                var oldPendingId = data.GetString("PendingUpgradeId", null);
                if (oldPendingId != null) {
                    var oldSlot = data.GetInt("PendingUpgradeSlot", 0);
                    PendingUpgrades = new string[NumSlots];
                    if (oldSlot >= 0 && oldSlot < NumSlots) {
                        PendingUpgrades[oldSlot] = oldPendingId;
                    }
                }
            }
            if (PendingCount > 0) {
                deficitEv.Load(data);
            }
        }


        public override string ToString() {
            return $" * Upgrades [{UsedSlots}/{NumSlots}]";
        }

        public override void OnLateReady(bool wasLoaded) {
            // We have to apply all upgrades after load
            if (!wasLoaded) { return; }
            if (PendingCount > 0) {
                deficitEv.OnLateReady();
                // A pending upgrade whose goal yielded the slot must not be stranded
                // if the game was saved before the borrowing goal ended
                S.Sig.AfterLoad.AddListener(ResumeAfterLoad);
            }
            if (upgradesToRestore != null) {
                S.Sig.AfterLoad.AddListener(RestoreUpgrades);
            }
        }

        private void RestoreUpgrades() {
            S.Sig.AfterLoad.RemoveListener(RestoreUpgrades);
            for (int i = 0; i < upgradesToRestore.Length; i++) {
                string u = upgradesToRestore[i];
                if (!string.IsNullOrWhiteSpace(u)) {
                    if (i > NumSlots) {
                        D.Err("Will not restore installed upgrade. Slot {0} > {1}: {2}",
                            i, NumSlots, Tile);
                        continue;
                    }
                    var up = UpgradeRegistry.Get(u);
                    if (up != null) {
                        if (up.IsCompatibleWith(Entity)) {
                            Install(i, up, isLoading: true);
                        } else {
                            D.ErrSoft("Ignoring incompatible upgrade: {0}-> {1}",
                                Entity, up);
                            // We have to eject the upgrade to not destroy it
                            Ready.WhenGameLate(this, "EjectUpgrade", () => {
                                SpawnEjectedUpgrade(up, Pos.Invalid);
                            });
                        }
                    } else {
                        D.Err("Failed restoring upgrade: {0} -> {1}", Entity, u);
                    }
                }
            }
        }

        public bool HasUpgrades() {
            if (!Tile.IsConstructed) { return false; }
            // if (Tile?.ENode != null) {
            //     if (!Tile.IsConstructed || !Tile.ENode.IsReachable) {
            //         return false;
            //     }
            // }
            availableUpgrades = S.Sys.Upg.AvailableFor(Entity);
            if (UsedSlots == 0) {
                if (Arrays.IsEmpty(availableUpgrades)) {
                    return false;
                }
            }
            return true;
        }

        private List<Upgrade> availableUpgrades;

        // Ejects the upgrade as a physical item near the device and returns the spawned
        // (or merged-into) pile, so callers can grab it right away. Null if spawning failed.
        public UnstoredMatComp EjectSlot(int i, Upgrade upgrade) {
            return EjectSlot(i, upgrade, Pos.Invalid);
        }

        // atPos overrides where the module lands. Callers that pick the module up on the
        // spot must pass their own position: a device's port or outline tile sits outside
        // the device footprint, so it can end up two tiles away from someone who is merely
        // adjacent to the device, which is further than a carrier is allowed to reach.
        public UnstoredMatComp EjectSlot(int i, Upgrade upgrade, int atPos) {
            Uninstall(i, upgrade);
            return SpawnEjectedUpgrade(upgrade, atPos);
        }

        private UnstoredMatComp SpawnEjectedUpgrade(Upgrade upgrade, int atPos) {
            if (atPos != Pos.Invalid) {
                // No merging: the caller wants this exact module, and a merge would hand
                // it a stack that other haulers may already have reserved.
                return EntityUtils.SpawnRawMaterial(upgrade.MatTypeId, 1, atPos, 0.3f,
                    true, false);
            }
            var pos = Entity.PosIdx;
            // Spawn the module
            if (Tile != null) {
                if (Tile.Transform.HasAnyPort) {
                    pos = Tile.Transform.AnyPortInFirst;
                } else {
                    foreach (var p in Tile.Transform.Outline) {
                        if (EntityUtils.IsEmptyFloorAt(p)) {
                            pos = p;
                        }
                        break;
                    }
                }
            }
            return EntityUtils.SpawnRawMaterial(upgrade.MatTypeId, 1, pos, 0.3f, true);
        }

        public bool OrderUpgrade(int slot, Upgrade type) {
            if (!entity.IsActive) {
                D.Err("Trying to order an upgrade for an inactive entity! {0}", Entity);
                return false;
            }
            if (Tile?.IsConstructed == false) {
                D.Err("Trying to order an upgrade for an unconstructed tile! {0}",
                    Tile);
                return false;
            }
            if (type == null) {
                D.Err("Trying to order null upgrade!");
                return false;
            }
            if (!type.CanInstallNow(entity)) {
                return false;
            }
            EnsurePendingArray();
            PendingUpgrades[slot] = type.UpgradeId;
            InvalidateMissingMatsCache();
            upgradesChanged?.Invoke();
            if (HasGoal) {
                // Goal already running for this component - it will pick up
                // the new pending via dynamic NextMissingMat.
                // But if the goal is in a failed/cooling state, recreate it
                // so it retries immediately with the new upgrade available.
                if (TryGetGoal(out var existingGoal)
                        && AIGoal.IsFailed(existingGoal)) {
                    // must not count as an external cancel, which clears every pending
                    // upgrade including the one just ordered
                    CancelOwnGoal(existingGoal, T.UpgradeInProgres);
                    return CreateUpgradeGoal();
                }
                return true;
            }
            if (HasAnyGoal) {
                // Another component's goal (e.g. refill) - cancel it
                AIGoal.Cancel(entity.Goal, G.Ticks, T.ActInstallupgrade);
            }
            return CreateUpgradeGoal();
        }

        private bool CreateUpgradeGoal() {
            var g = CreateGoal(JobTypeIdH.Construction, IconId.CUpgrade,
                    T.InstallUpgrade, this)
                .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true)
                .ByPlayer(true)
                .WithPriority(4)
                .WithRequiredAbilities(AbilityIdH.Carry,
                    AbilityIdH.SkilledLabor, AbilityIdH.DumbLabor)
                .WriteInitialState();
            g.CheckDoable = CanInstallUpgrade;
            return entity.TrySetGoal(g);
        }

        private AIGoalCheckResult CanInstallUpgrade(GameState S, AIGoal goal) {
            if (PendingUpgrades == null) {
                return AIGoalCheckResult.Completed;
            }
            // If ANY pending upgrade material is available, the goal is doable
            if (AnyPendingMatAvailable(goal.GoalPriority)) {
                return AIGoalCheckResult.Doable;
            }
            // None available - notify deficit for the first pending
            var next = NextMissingMat;
            if (next != null) {
                if (S.Sys.Inventory.AllowedCount(next.Type) > 0) {
                    // The material exists, it is just spoken for
                    return AIGoalCheckResult.Fail(T.AdRejMatReserved, true,
                        IconId.CLocked, MatClaimGate.RetryTicks);
                }
                NotifyMatDeficit(next.Type);
                return AIGoalCheckResult.Fail(next.Type.LackRejectionT,
                    true, IconId.CLackingMaterials, MatClaimGate.RetryTicks);
            }
            return AIGoalCheckResult.Completed;
        }

        public void CancelGoal() {
            if (PendingUpgrades != null) {
                for (int i = 0; i < PendingUpgrades.Length; i++) {
                    PendingUpgrades[i] = null;
                }
                InvalidateMissingMatsCache();
            }
            deficitEv.Clear();
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
                HideInfoIcon();
            }
            upgradesChanged?.Invoke();
        }

        public void CancelPendingUpgrade(int slot) {
            if (PendingUpgrades == null) { return; }
            if (PendingUpgrades[slot] == null) { return; }
            PendingUpgrades[slot] = null;
            InvalidateMissingMatsCache();
            if (PendingCount == 0) {
                deficitEv.Clear();
                if (TryGetGoal(out var g)) {
                    AIGoal.Cancel(g, G.Ticks);
                    HideInfoIcon();
                }
            }
            upgradesChanged?.Invoke();
        }

        public void RelocateTo(Entity target) {
            var other = target.GetComponent<UpgradesComp>();
            if (Arrays.IsEmpty(Upgrades)) {
                return;
            }
            other.upgradesToRestore = Upgrades;
            other.RestoreUpgrades();
        }

        public bool ProvideRequestedMat(UnstoredMatComp unstoredMat, Being worker,
                AIGoal goal = null) {
            if (Entity.IsImmutable) {
                CancelGoal();
                return true;
            }
            if (PendingUpgrades == null) { return true; }
            // Find first pending upgrade that matches the delivered material
            for (int i = 0; i < PendingUpgrades.Length; i++) {
                var upId = PendingUpgrades[i];
                if (upId == null) { continue; }
                var upgrade = UpgradeRegistry.Get(upId);
                if (upgrade == null) { continue; }
                if (upgrade.MatTypeId != unstoredMat.Type.Id) { continue; }
                var took = unstoredMat.Take(unstoredMat.Type, null, 1,
                    goal ?? worker?.AIAgent?.Goal ?? entity.Goal);
                if (took == 0) { return false; }
                S.Sig.MatConsumed.Send(unstoredMat.Type, took);
                Install(i, upgrade);
                return PendingCount == 0;
            }
            return false;
        }

        public void NotifyMatDeficit(MatType type) {
            deficitEv.Notify(type, allowNewType: true);
        }

        public void ClearLackNotification(MatType type) {
            deficitEv.ClearIf(type);
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCompleted(to)) {
                if (PendingCount > 0) {
                    // Goal completed but upgrades remain - worker may have
                    // ignored the task. Recreate the goal.
                    CreateUpgradeGoal();
                    return;
                }
                Sounds.PlayTileSelect(entity.Position);
                HideInfoIcon();
            } else if (AIGoal.IsFailed(to)) {
                if (PendingCount > 0) {
                    // Check if any pending material is now available,
                    // and if so, immediately recreate the goal instead
                    // of waiting for cooldown.
                    if (AnyPendingMatAvailable(goal.GoalPriority)) {
                        AIGoal.TryRetry(goal, G.Ticks, false, false);
                        return;
                    }
                }
            } else if (AIGoal.IsCancelled(to)) {
                if (isCancellingOwnGoal) {
                    // We cancelled it ourselves: the pending upgrades and their deficit
                    // warning stay (a new goal follows, or ResumeAfterYield brings it back)
                    return;
                }
                // External cancel clears all pending
                if (PendingUpgrades != null) {
                    for (int i = 0; i < PendingUpgrades.Length; i++) {
                        PendingUpgrades[i] = null;
                    }
                    InvalidateMissingMatsCache();
                }
                deficitEv.Clear();
                upgradesChanged?.Invoke();
            }
            if (!AIGoal.IsActive(to) && !AIGoal.IsFailed(to)) {
                HideInfoIcon();
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.CheckDoable = CanInstallUpgrade;
        }

        // Respects, but never stakes, material claims: an upgrade must not drain
        // stock a higher priority task is accumulating (see MatClaims)
        private bool AnyPendingMatAvailable(int priority) {
            if (PendingUpgrades == null) { return false; }
            for (int i = 0; i < PendingUpgrades.Length; i++) {
                var upId = PendingUpgrades[i];
                if (upId == null) { continue; }
                var upgrade = UpgradeRegistry.Get(upId);
                if (upgrade == null) { continue; }
                var matType = MatType.Get(upgrade.MatTypeId);
                if (matType != null && S.Sys.Inventory.AvailableFor(matType, this,
                        priority) > 0) {
                    return true;
                }
            }
            return false;
        }

        // A starved upgrade waits for a module that may never come, so it must not
        // keep a refill (or any other borrower) out of the entity goal slot
        public bool TryYieldGoalSlot() {
            if (!TryGetGoal(out var g)) { return false; }
            // Only a goal that is waiting on a retry. Interrupted can mean a worker
            // already carries the module
            if (g.State != AIGoalState.Failed && g.State != AIGoalState.CoolingDown) {
                return false;
            }
            if (AnyPendingMatAvailable(g.GoalPriority)) { return false; }
            CancelOwnGoal(g, null);
            return !HasAnyGoal;
        }

        private void CancelOwnGoal(AIGoal g, string reason) {
            isCancellingOwnGoal = true;
            try {
                AIGoal.Cancel(g, G.Ticks, reason);
            } finally {
                isCancellingOwnGoal = false;
            }
        }

        public void ResumeAfterYield() {
            if (PendingCount == 0) { return; }
            if (HasAnyGoal) { return; }
            if (!entity.IsActive || Tile?.IsConstructed != true) { return; }
            CreateUpgradeGoal();
        }

        private void ResumeAfterLoad() {
            S.Sig.AfterLoad.RemoveListener(ResumeAfterLoad);
            // AI agents are not ready to take goals during AfterLoad yet
            S.Clock.OnNextTick(ResumeAfterYield);
        }

        public bool OrderUpgrade(Upgrade upgrade) {
            var slot = FirstEmptySlot;
            if (slot < 0) { return false; }
            return OrderUpgrade(slot, upgrade);
        }
    }
}
