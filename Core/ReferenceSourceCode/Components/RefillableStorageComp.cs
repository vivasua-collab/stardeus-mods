using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Firex;
using Game.Systems.Inventory;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class RefillableStorageComp : BaseComponent<RefillableStorageComp>,
            IMatRequester, IRelocatable, IAIGoalPriorityListener,
            IAIGoalProvider, ICopyableComp, ICanBeUnconfigured,
            IExplosivenessProvider {
        private const float refillThreshold = 0.5f;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new RefillableStorageComp());
        }
        private MatType targetMaterial;
        public MatType MatType => targetMaterial;
        private MatDeficitEventHolder deficitEv;
        private string iconId;
        private int targetStock;
        private bool isConfigured;
        public bool IsReachableForCommonAction =>
            lockedMaterial == null &&
            Tile.ENode?.IsReachable != false;
        public bool IsConfigured => isConfigured;
        private float currentStock;
        public float CurrentStock => currentStock;
        private int materialCapacity;
        public readonly BFloat BurnRatePerH = new(0f);
        private bool showFuelEfficiency;
        public bool ShowFuelEfficiency => showFuelEfficiency;
        public bool IsEmpty => currentStock < 0.00000000001f;
        public bool IsFull => Maths.Approximately(CurrentStock, materialCapacity);
        // reusable thing
        private MatRequest cachedRefillRequest;
        private MatRequest refillRequest;
        private MatType lockedMaterial;
        public Action<float> OnStockChange;
        private BFloat stockB;
        public BFloat StockB => stockB ??= new BFloat(currentStock);
        private readonly Dictionary<MatType, int> availableMaterials = new();
        private BCallback uiChanged;

        private int SeekedRefillAmount => Mathf.FloorToInt(
            targetStock - currentStock);

        public bool IsCopyable => lockedMaterial == null;
        public bool HasLockedMaterial => lockedMaterial != null;
        public int TargetStock => targetStock;
        public int MaterialCapacity => materialCapacity;
        public int RefillPriority => refillPriority;
        public bool TrackBurnRate => trackBurnRate;
        public string Purpose => purpose;
        // Stopping unconfigures the storage and brings the material picker back, so where a
        // purpose has a "change" label it reads as what the player wants to do: playtesters
        // looking to switch reactor fuel did not see it behind "Eject Fuel"
        public string StopActionText => $"change.{purpose}".TOrNull()
            ?? $"eject.{purpose}".TOrNull() ?? T.Eject;
        public BCallback UIChanged => uiChanged ??= new BCallback();
        private string purpose;
        private string purposeT;
        public string PurposeT => purposeT;
        private string deviceType;
        public string CopyConfigText { get; private set; }
        private int refillPriority;
        private string[] propertyFilter;
        public string[] PropertyFilter => propertyFilter;
        private string[] groupFilter;
        public string[] GroupFilter => groupFilter;
        public int InPort => Tile.Transform.InPort;
        private List<MatRequest> missingMats;
        // Wraps the single refill request so the claim system can see what this device
        // is waiting for (MatClaims.IsValid checks live demand through this list) - a
        // bare null would make a refillable unable to ever hold a claim
        public List<MatRequest> MissingMats {
            get {
                if (refillRequest == null) { return null; }
                if (missingMats == null) {
                    missingMats = new List<MatRequest>(1) { refillRequest };
                } else if (missingMats.Count == 0 || missingMats[0] != refillRequest) {
                    missingMats.Clear();
                    missingMats.Add(refillRequest);
                }
                return missingMats;
            }
        }
        public MatRequest NextMissingMat => refillRequest;
        public string DeviceCategory => purposeT;
        public Tile DeviceTile => Tile;
        public bool IsExplosivenessProviderDisabled { get; private set; }

        // Strength comes from what is loaded, not how much, like the other
        // IExplosivenessProviders: a single highly explosive unit must still blow hard.
        // currentStock is deliberately not divided by capacity - the Lerp only fades the
        // strength out while the last unit burns off
        public int ExplosionLevel {
            get {
                if (targetMaterial == null || IsEmpty) { return 0; }
                return Mathf.RoundToInt(
                    Mathf.Lerp(0f, targetMaterial.ExplosionLevel, currentStock));
            }
        }

        public Curve ExplosivenessCurve {
            get {
                if (targetMaterial == null || IsEmpty) { return null; }
                return targetMaterial.ExplosivenessCurve;
            }
        }

        public override void OnRemove() {
            if (TryGetGoal(out var goal)) {
                AIGoal.Cancel(goal, G.Ticks);
            }
            if (isDeficitNotified) {
                deficitEv?.Clear();
            }
            if (isConfigured) {
                if (Tile.Reinstallable?.IsMidRelocation == false) {
                    DumpContents();
                }
            } else {
                S.Sig.UnconfiguredChanged.Send(this, false);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("TargetMaterial", targetMaterial?.Id);
            data.SetBool("IsConfigured", isConfigured);
            deficitEv.Save(data);
            data.SetInt("TargetStock", targetStock);
            data.SetFloat("CurrentStock", currentStock);
            data.SetInt("RefillPriority", refillPriority);
        }

        private void NotifyUIChanged() {
            uiChanged?.Invoke();
        }

        protected override void OnLoad(ComponentData data) {
            isConfigured = lockedMaterial != null || data.GetBool("IsConfigured", false);
            var targetMat = data.GetString("TargetMaterial", null);
            if (lockedMaterial == null) {
                if (targetMat != null) {
                    targetMaterial = MatType.Get(targetMat);
                }
            } else {
                targetMaterial = lockedMaterial;
            }
            targetStock = data.GetInt("TargetStock", targetStock);
            if (targetStock > materialCapacity) {
                targetStock = materialCapacity;
            }
            currentStock = data.GetFloat("CurrentStock", 0);
            if (currentStock > materialCapacity) {
                currentStock = materialCapacity;
            }
            refillPriority = data.GetInt("RefillPriority", refillPriority);
            if (isConfigured) {
                if (targetMaterial == null) {
                    if (lockedMaterial != null) {
                        targetMaterial = lockedMaterial;
                    } else {
                        D.Err("Refillable storage is configured, but target material is missing. Unconfiguring: {0} -> {1}",
                            targetMat, Entity);
                        isConfigured = false;
                        currentStock = 0;
                    }
                } else {
                    deficitEv.Load(data);
                }
            }
        }

        private bool trackBurnRate;
        private Color barColor;

        protected override void OnConfig() {
            trackBurnRate = Config.GetBool(PropertyIdH.ShowBurnRate, true);
            materialCapacity = Config.GetInt(PropertyIdH.MaterialCapacity, 100);
            deviceType = Config.GetString(PropertyIdH.DeviceType, null);
            purpose = Config.GetString(PropertyIdH.Purpose);
            purposeT = purpose.T();
            var color = Config.GetString(PropertyIdH.BarColor, null);
            if (color == null || !Colors.TryFromHex(color, out barColor)) {
                barColor = Consts.ColorFuelBar;
            }
            deficitEv = new MatDeficitEventHolder(Tile, this, purposeT);
            lastBarTemplate = $"{purposeT}: {{0}}/{{1}}";
            iconId = Config.GetString(PropertyIdH.Icon, IconId.CEnergy);
            var lockMatType = Config.GetString(PropertyIdH.LockedMaterial, null);
            if (lockMatType != null) {
                lockedMaterial = MatType.Get(lockMatType);
            }
            if (lockMatType == null) {
                CopyConfigText = $"copy.config.{deviceType}".T();
            }

            refillPriority = Config.GetInt(PropertyIdH.DefaultPriority, 3);
            targetStock = materialCapacity;
            propertyFilter = Config.GetStringSet(PropertyIdH.PropertyFilter, null);
            groupFilter = Config.GetStringSet(PropertyIdH.GroupFilter, null);
            IsExplosivenessProviderDisabled =
                Config.GetBool(PropertyIdH.DisableExplosivenessProvider, false);
        }

        private ExtraInfoComp extraInfo;
        private bool isShowingUnconfigured;
        public bool IsShowingUnconfigured => isShowingUnconfigured;

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, true, false, this);
            if (!Entity.TryGetComponent(out extraInfo)) {
                D.Err("RefillableStorageComp needs to have a ExtraInfoComp in the same Def! Please add ExtraInfo component to {0}",
                    Entity.DefinitionId);
            }
            // MatType.EnergyOutput only affects matter reactors, other fuel burners ignore it
            showFuelEfficiency = Entity.HasComponent<MatterReactorComp>();
            if (!Tile.Transform.HasInPort) {
                D.Err("InPort is required to be defined in TileTransform when RefillableStorage exists! {0}",
                    Entity.Definition.Id);
            }
            if (IsReachableForCommonAction) {
                UpdateFuelBar();
                CheckIfConfigured();
            }
            if (wasLoaded) {
                deficitEv.OnLateReady();
                UpdateExtraInfo();
            }
        }

        public override string ToString() {
            return " * RefillableStorage";
        }
        public bool CheckIfConfigured() {
            if (!Tile.IsConstructed) { return false; }
            // if (!Ready.Gen) { return false; }
            if (isConfigured) {
                if (isShowingUnconfigured) {
                    isShowingUnconfigured = false;
                    S.Sig.UnconfiguredChanged.Send(this, false);
                }
                return true;
            } else {
                if (lockedMaterial != null) {
                    SetTargetMaterial(lockedMaterial);
                    return true;
                }
            }
            ShowUnconfigured(UnconfiguredWarning.IsApplicable(Tile));
            return false;
        }

        private void ShowUnconfigured(bool show) {
            if (isShowingUnconfigured == show) { return; }
            isShowingUnconfigured = show;
            if (show) {
                ShowInfoIcon(IconId.CNotConfigured, T.InfoDeviceUnconfigured);
            } else {
                HideInfoIcon();
            }
            S.Sig.UnconfiguredChanged.Send(this, show);
        }

        public void ClearDeficitFlag() {
            if (!isDeficitNotified) { return; }
            isDeficitNotified = false;
            HideInfoIcon();
            deficitEv.Clear();
        }
        private bool isDeficitNotified;

        public void NotifyLack() {
            if (isDeficitNotified) { return; }
            isDeficitNotified = true;

            // if (refillRequest == null || S.Sys.Inventory.HasAny(refillRequest.Type)) {
            //     ShowInfoIcon(IconId.CLackingMaterials, T.InfoDeviceLacksMaterials);
            // }
            if (refillRequest == null) {
                // D.Warn("Refill request is null, not notifying production failure");
                return;
            }
            if (!S.Sys.Inventory.HasEnough(refillRequest.Type, refillRequest.Amount)) {
                NotifyMatDeficit(refillRequest.Type);
            }
        }

        public void NotifyMatDeficit(MatType type) {
            deficitEv.Notify(type);
        }

        public void ClearBurnRate() {
            if (trackBurnRate) {
                BurnRatePerH.Set(0f);
            }
        }

        public bool TryConsume(float amt) {
            if (!isConfigured) { return false; }
            if (currentStock <= 0) {
                CreateOrUpdateRefillGoal();
                ClearBurnRate();
                return false;
            }
            if (trackBurnRate) {
                BurnRatePerH.Set(amt * (Consts.TicksPerHour / 10f));
            }
            var res = false;
            if (currentStock >= amt) {
                currentStock -= amt;
                res = true;
            } else {
                currentStock = 0;
            }
            stockB?.Set(currentStock);
            CreateOrUpdateRefillGoal();
            UpdateUIBlock(true);
            return res;
        }


        public void DumpContents() {
            var amt = Mathf.RoundToInt(currentStock);
            if (amt > 0) {
                EntityUtils.SpawnRawMaterial(
                    new Mat() {
                        Type = targetMaterial,
                        StackSize = amt,
                        MaxStackSize = amt,
                    },
                    Tile.Transform.AnyPortOutFirst, 0.1f);
            }
            currentStock = 0;
            stockB?.Set(currentStock);
            ClearBurnRate();
            OnStockChange?.Invoke(currentStock);
        }

        private void UpdateUIBlock(bool wasUpdated) {
            UpdateFuelBar();
        }

        private MatType lastMaterial;
        private string lastBarTemplate;
        private string lastBarText;
        private double lastStock = -1;
        private int lastTargetStock = -1;

        private void UpdateFuelBar() {
            if (isConfigured) {
                if (targetMaterial != lastMaterial) {
                    lastStock = -1;
                    lastTargetStock = -1;
                    lastMaterial = targetMaterial;
                    UpdateExtraInfo();
                }
                var curStock = Math.Round(currentStock, 1);
                if (lastStock != curStock || lastTargetStock != targetStock) {
                    lastStock = curStock;
                    lastTargetStock = targetStock;
                    lastBarText = string.Format(lastBarTemplate, curStock, targetStock);
                }
                ShowInfoBar(barColor, currentStock / targetStock, lastBarText);
            } else {
                HideInfoBar();
            }
        }

        public void CreateOrUpdateRefillGoal() {
            // check if entity has a goal of some other type
            if (!TryGetGoal(out var g)) {
                if (g != null) {
                    // there is some other type of job associated with this device.
                    // It keeps the goal slot, unless it's a starved goal that can wait
                    // while we refill (see IYieldingGoalProvider)
                    if (!TryBorrowGoalSlot(g)) {
                        return;
                    }
                    g = null;
                }
            }
            if (refillPriority == 0) {
                if (g != null) {
                    deficitEv?.Clear();
                    AIGoal.Cancel(g, G.Ticks);
                }
                return;
            }
            // Don't refuel devices that aren't reachable by the ship computer
            // (e.g. derelict ship engines spawned in disconnected corners).
            // Note: can't reuse IsReachableForCommonAction here -- it also requires
            // lockedMaterial == null, which is false for locked engines like FusionDrive.
            if (Tile.ENode?.IsReachable == false) {
                if (g != null) {
                    deficitEv?.Clear();
                    AIGoal.Cancel(g, G.Ticks);
                }
                return;
            }
            if (!isConfigured) {
                deficitEv?.Clear();
                if (g != null) {
                    #if !PRODUCTION_BUILD
                    // expected, if we click stop
                    // D.Err("Not configured but task exists: {0}", g);
                    #endif
                    AIGoal.Cancel(g, G.Ticks);
                }
                if (refillRequest != null) {
                    refillRequest.IsCancelled = true;
                    refillRequest = null;
                }
                return;
            }

            EnsureRefillRequest();
            if (refillRequest.Amount < targetStock * refillThreshold) {
                // We have enough
                HideInfoIcon();
                return;
            }

            // Otherwise it will fail during relocation
            if (EntityId == 0) { return; }
            if (AIGoal.IsFailed(g)) {
                if (AIGoal.TryRetry(g, G.Ticks, false, false)) {
                    return;
                }
            }

            if (g == null) {
                // D.Warn("Creating refill goal");
                var goal = CreateGoal(JobTypeIdH.Hauling, IconId.CHaul, T.Refill, this)
                    .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true)
                    .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                    .NanobotCompatible()
                    .WithPriority(refillPriority);
                goal.CheckDoable = CheckCanRefill;
                entity.TrySetGoal(goal);
            }
        }

        // Refills take part in material claims with the refill priority the player set:
        // a scarce fuel is arbitrated by that priority rather than by whoever grabs each
        // unit first, and raising it is how the player protects a reactor from a
        // blueprint accumulating the same material. See MaterialClaims.md
        private AIGoalCheckResult CheckCanRefill(GameState s, AIGoal goal) {
            if (!isConfigured || targetMaterial == null) {
                // CreateOrUpdateRefillGoal cancels the goal itself in this state
                return AIGoalCheckResult.Doable;
            }
            EnsureRefillRequest();
            if (refillRequest.Amount < 1) {
                // Nothing left to bring - let the action finish the goal
                return AIGoalCheckResult.Doable;
            }
            return MatClaimGate.CheckHaulGoal(s, this, refillRequest, goal.GoalPriority);
        }

        // Runs on every grid tick of a starving device while another goal holds the
        // slot, so the cheap type check goes first
        private bool TryBorrowGoalSlot(AIGoal other) {
            if (other.Target is not IYieldingGoalProvider yielding) { return false; }
            if (!NeedsRefill()) { return false; }
            return yielding.TryYieldGoalSlot();
        }

        // Mirrors the checks CreateOrUpdateRefillGoal makes before creating a goal
        private bool NeedsRefill() {
            if (refillPriority == 0) { return false; }
            if (Tile.ENode?.IsReachable == false) { return false; }
            if (!isConfigured) { return false; }
            if (EntityId == 0) { return false; }
            return SeekedRefillAmount >= targetStock * refillThreshold;
        }

        private Action resumeYieldedGoal;

        private void ResumeYieldedGoal() {
            if (entity == null || !entity.IsActive) { return; }
            if (entity.TryGetComponent<IYieldingGoalProvider>(out var yielding)) {
                yielding.ResumeAfterYield();
            }
        }

        private void EnsureRefillRequest() {
            if (refillRequest == null) {
                if (cachedRefillRequest == null) {
                    cachedRefillRequest = new MatRequest() {
                        IsAmountOptional = true,
                        Requester = this,
                    };
                }
                refillRequest = cachedRefillRequest;
                refillRequest.IsCancelled = false;
                refillRequest.Type = targetMaterial;
            }
            refillRequest.Amount = SeekedRefillAmount;
        }

        public void Stop(bool updateExtraInfo = true) {
            deficitEv?.Clear();
            HideInfoBar();
            ClearDeficitFlag();
            if (lockedMaterial != null) {
                if (TryGetGoal(out var goal)) {
                    AIGoal.Cancel(goal, G.Ticks);
                }
                NotifyUIChanged();
                return;
            } else {
                isConfigured = false;
                DumpContents();
                CreateOrUpdateRefillGoal();
                CheckIfConfigured();
            }
            if (updateExtraInfo) {
                UpdateExtraInfo();
            }
            NotifyUIChanged();
        }

        private void UpdateExtraInfo() {
            if (extraInfo != null) {
                if (lockedMaterial != null) {
                    if (HasGoal) {
                        extraInfo.ShowInfoIcon(lockedMaterial.Def.Preview,
                            lockedMaterial.Def.CodexLink);
                    } else {
                        extraInfo.HideInfoIcon();
                    }
                } else {
                    if (isConfigured) {
                        extraInfo.ShowInfoIcon(targetMaterial.Def.Preview,
                            targetMaterial.Def.CodexLink);
                    } else {
                        extraInfo.HideInfoIcon();
                    }
                }
            }
        }



        public void AdjustRefillPriority(int direction) {
            refillPriority = UIUtils.AdjustPriority0To5(refillPriority, direction);
            if (TryGetGoal(out var g)) {
                // SetPriority, not WithPriority: raising a failed goal's priority must
                // re-run it through the doability gate right away, or a refill the
                // player just prioritised keeps yielding to the old claim holder until
                // its retry timer fires
                g.SetPriority(G.Ticks, refillPriority);
            }
            CreateOrUpdateRefillGoal();
            UpdateUIBlock(true);
            NotifyUIChanged();
        }

        private void UpdateUITargetAmt() {
            if (refillRequest != null) {
                refillRequest.Amount = SeekedRefillAmount;
            }
            ClearDeficitFlag();
            CreateOrUpdateRefillGoal();
            NotifyUIChanged();
        }

        public void SetTargetStockByUI(int target) {
            targetStock = Mathf.Clamp(target, 1, materialCapacity);
            UpdateUITargetAmt();
            UpdateUIBlock(true);
        }

        private string cachedCompatibleItems;

        public string GetCompatibleItemsTip() {
            if (cachedCompatibleItems != null) {
                return cachedCompatibleItems;
            }
            Caches.DefSet.Clear();
            foreach (var mat in MatType.Known.OrderBy(t => t.Value.NameT)) {
                var m = mat.Value;
                if (propertyFilter != null && !Arrays.Contains(propertyFilter, m.Property)) {
                    continue;
                }
                if (groupFilter != null && !Arrays.Contains(groupFilter, m.Group)) {
                    continue;
                }
                Caches.DefSet.Add(m.Def);
            }
            if (Caches.DefSet.Count == 1) {
                // not caching this in cachedCompatibleItems will allow ctrl for expanding tooltip
                var def = Caches.DefSet.First();
                return def.ExtendedTooltip(S, $"{T.CompatibleItem}: {Texts.Red(def.CodexLink)}");
            }

            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(T.CompatibleItems);
            sb.Append(":");
            int count = 0;
            int total = Caches.DefSet.Count;
            foreach (var def in Caches.DefSet) {
                sb.AppendFormat("\n - <b>{0}</b>", Texts.Red(def.CodexLink));
                if (count++ >= 20) {
                    break;
                }
            }
            if (count < total) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"+{total - count}"));
            }
            cachedCompatibleItems = sb.ToString();
            sb.Clear();
            return cachedCompatibleItems;
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                CheckIfConfigured();
            } else if (message == MsgIdH.ElectricNodeAdded
                    || message == MsgIdH.ElectricNodeConnectionChanged
                    || message == MsgIdH.ElectricNodeReachabilityChanged
                    || message == MsgIdH.ElectricNodeSwitchedOn
                    || message == MsgIdH.ElectricNodeSwitchedOff) {
                // The unconfigured warning waits for the device to be on the grid. On load
                // ElectricNodeAdded is the first point where the restored node can be read -
                // OnLateReady runs before ElectricitySys restores it
                if (!isConfigured && lockedMaterial == null) {
                    CheckIfConfigured();
                }
            /* FIXME this will likely have to be reimplemented
            } else if (message == MsgIdH.EnergyNodeChanged) {
                if (refillAd == null && !Tile.ENode.IsPowered) {
                    deficitEv.Clear();
                }
            */
            }
        }

        public bool SetTargetMaterial(MatType material) {
            // check if we had something loaded previously!
            if (isConfigured) {
                if (targetMaterial == material) {
                    return false;
                } else {
                    Stop(false);
                }
            }
            // why was this here?
            //deficitEv.Clear();
            ClearDeficitFlag();
            isConfigured = true;
            isShowingUnconfigured = false;
            // Sent even when no warning was showing: ConfigureMatterReactorQuestStep re-checks on it
            S.Sig.UnconfiguredChanged.Send(this, false);
            refillRequest = null;
            currentStock = 0;
            stockB?.Set(currentStock);
            OnStockChange?.Invoke(currentStock);
            ClearBurnRate();
            targetMaterial = material;
            HideInfoIcon();
            if (Ready.Gen && Tile.ENode?.IsPowerable == true) {
                S.Clock.OnNextTick(CreateOrUpdateRefillGoal);
            }
            UpdateExtraInfo();
            NotifyUIChanged();
            return true;
        }
        public bool CanCopyTo(ICopyableComp target) {
            return target is RefillableStorageComp m && m.deviceType == deviceType
                && m.propertyFilter == propertyFilter;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            bool changed = false;
            if (CanCopyTo(target)) {
                RefillableStorageComp m = target as RefillableStorageComp;
                if (m.targetStock != targetStock) {
                    m.targetStock = Mathf.Clamp(
                        targetStock, 0, m.materialCapacity);
                    changed = true;
                }
                if (m.refillPriority != refillPriority) {
                    m.refillPriority = refillPriority;
                    changed = true;
                }
                if (changed) {
                    m.CreateOrUpdateRefillGoal();
                }
                if (isConfigured) {
                    return m.SetTargetMaterial(targetMaterial) || changed;
                } else if (m.isConfigured) {
                    m.Stop();
                    return true;
                }
            }
            return changed;
        }

        public bool ProvideRequestedMat(UnstoredMatComp unstoredMat, Being worker,
                AIGoal goal = null) {
            int givenAmount = unstoredMat.StackSize;
            if (givenAmount == 0) {
                D.Err("Trying to load 0 material into refillable storage: {0}", unstoredMat);
                return currentStock >= targetStock;
            }
            var canTake = materialCapacity - currentStock;
            if (canTake <= 0) {
                D.ErrSoft("Trying to refill a storage that apparently does not need refilling: {0}. Mat cap: {1}, Cur stock: {2}",
                    unstoredMat, materialCapacity, currentStock);
                return true;
            }
            int takenAmount = Mathf.FloorToInt(Mathf.Min(givenAmount, canTake));
            takenAmount = unstoredMat.Take(targetMaterial, null, takenAmount,
                goal ?? worker?.AIAgent?.Goal ?? entity.Goal);
            currentStock += takenAmount;
            OnStockChange?.Invoke(currentStock);
            stockB?.Set(currentStock);
            refillRequest.Amount -= takenAmount;
            UpdateUIBlock(true);
            ClearLackNotification(unstoredMat.Type);
            if (takenAmount > 0) {
                S.Sig.MatConsumed.Send(unstoredMat.Type, takenAmount);
            }
            var isFulfilled = currentStock >= targetStock;
            return isFulfilled;
        }

        public void Fill(int amt) {
            currentStock = Math.Min(amt, materialCapacity);
            stockB?.Set(currentStock);
            OnStockChange?.Invoke(currentStock);
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
                UpdateUIBlock(true);
            }
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<RefillableStorageComp>(out var rf)) {
                CopyConfigTo(rf);
                rf.currentStock = currentStock;
                rf.stockB?.Set(currentStock);
                CheckIfConfigured();
                if (isConfigured) {
                    UpdateFuelBar();
                }
            }
        }

        public void ClearLackNotification(MatType type) {
            deficitEv.ClearIf(type);
        }

        public void OnAIGoalPriorityChange(AIGoal goal) {
            refillPriority = goal.GoalPriority;
            NotifyUIChanged();
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCancelled(to)) {
                deficitEv.Clear();
                UpdateExtraInfo();
                NotifyUIChanged();
            }
            if (AIGoal.IsCompleted(to) || AIGoal.IsCancelled(to)) {
                // Hand the goal slot back to a component that yielded it to us.
                // Deferred, because whoever cancelled us (e.g. OrderUpgrade) may be
                // about to set its own goal right after
                S.ActQ.Enqueue(resumeYieldedGoal ??= ResumeYieldedGoal);
            }
        }
        public void OnExplode() {
            // destroy the fuel, so it wouldn't get dumped
            currentStock = 0f;
            stockB?.Set(currentStock);
            OnStockChange?.Invoke(0f);
        }

        public void OnGoalLoad(AIGoal goal) {
            if (AIGoal.IsActive(goal)) {
                EnsureRefillRequest();
            }
            if (goal.JobIdH == JobTypeIdH.Hauling) {
                goal.CheckDoable = CheckCanRefill;
            }
        }
    }
}
