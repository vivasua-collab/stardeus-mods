#if !PRODUCTION_BUILD
// #define DEBUG_CRAFTING
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using Game.Components;
using KL.Utils;
using System;
using Game.Components.Upgrades;
using Game.Systems.Firex;
using Game.Systems.Inventory;

namespace Game.Systems.Crafting {
    public sealed class MultiCrafterComp : BaseComponent<MultiCrafterComp>,
            IAIGoalProvider, IAISuppressableGoalProvider, IMatRequester,
            ICanBeIdle, ICanBeUnconfigured, IEnergyEfficiencyUpgradeable, ISpeedUpgradeable,
            IAfterInitDef, IAIGoalPriorityListener,
            ICopyableComp, IRelocatable, IExplosivenessProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MultiCrafterComp());
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            CrafterDefInfo.AddPower(def, ref power);
        }

        public int InPort => Tile.Transform.InPort;
        public int OutPort => Tile.Transform.OutPort;
        public string CopyConfigText => Tile.Definition.NameT;
        public bool IsCopyable => Tile.ENode?.IsReachable != false;

        public List<MatRequest> MissingMats => CurrentOrder?.MissingMats;

        public MatRequest NextMissingMat => CurrentOrder?.NextMissingMat;
        private List<CraftingOrder> orders;
        public List<CraftingOrder> Orders => orders ??= S.Query.GetCraftingOrdersFor.Ask(this);
        public void SetOrders(List<CraftingOrder> orders) {
            if (this.orders != null) {
                D.Err("Trying to set orders more than once! Only CraftingSys should ever call this method! {0}. same? {1}",
                    D.Dump(orders), this.orders == orders);
            }
            this.orders = orders;
        }
        public string IdleCategory => Tile.Definition.NameT;
        public Color ProductColor => CurrentOrder.ProductColor;
        public Def ProductDef => CurrentOrder?.Demand.Product;

        public Tile DeviceTile => Tile;
        public ElectricNodeComp ENode { get; private set; }

        public string IdleTooltip => CurrentOrder?.IdleTooltip;
        public string IdleProducts { get; set; }

        public string DeviceCategory => T.MenuProduction;

        public bool IsEnergyEfficiencyUpgradeable => true;

        public bool IsSpeedUpgradeable => true;
        #if !PRODUCTION_BUILD
        /// <summary>
        /// bool: isFulfilled
        /// </summary>
        public Action<UnstoredMatComp, bool> OnProvideMat;
        public Action<CraftingDemand> OnStartCrafting;

        #endif
        public MultiCrafterUI UI;
        private ExtraInfoComp extraInfo;
        public CrafterConfig Settings;
        public OperatableDeviceComp Operatable;
        public CraftingOrder CurrentOrder;

        public int ExplosionLevel {
            get {
                var co = CurrentOrder;
                if (co == null) { return 0; }
                if (Arrays.IsEmpty(co.Ingredients)) { return 0; }
                int max = 0;
                foreach (var ing in co.Ingredients) {
                    if (ing.StackSize > 0 && ing.Type.ExplosionLevel > max) {
                        max = ing.Type.ExplosionLevel;
                    }
                }
                return max;
            }
        }

        public Curve ExplosivenessCurve {
            get {
                var co = CurrentOrder;
                if (co == null) { return null; }
                if (Arrays.IsEmpty(co.Ingredients)) { return null; }
                int max = 0;
                MatType mostExplosive = null;
                foreach (var ing in co.Ingredients) {
                    if (ing.StackSize > 0 && ing.Type.ExplosionLevel > max) {
                        max = ing.Type.ExplosionLevel;
                        mostExplosive = ing.Type;
                    }
                }
                return mostExplosive?.ExplosivenessCurve;
            }
        }
        public bool IsExplosivenessProviderDisabled { get; private set; }
        protected override void OnConfig() {
            Settings = new();
            Settings.IsOperatorRequired = Config.GetBool(PropertyIdH.IsOperatorRequired, false);
            Settings.TaskPriority.SetSilent(Config.GetInt(PropertyIdH.DefaultPriority, 3));
            Settings.IsIdleWarnOn = Config.GetBool(PropertyIdH.IdleWarnDefault, false);
            Settings.IsAutoHaulProduct = Config.GetBool(PropertyIdH.AutoHaulProduct, false);
            Settings.MaxBatchSize = Config.GetInt(PropertyIdH.MaxBatchSize,
                Tunables.DefaultCraftingMaxBatchSize);
            if (Settings.MaxBatchSize < 1) {
                Settings.MaxBatchSize = 1;
            }
            Settings.CompatibleTypes = new HashSet<string>(
                Config.GetStringSet(PropertyIdH.CompatibleTypes));
            Settings.SpeedMultiplier = Config.GetFloat(PropertyIdH.SpeedMultiplier, 1f);

            Settings.UIItemIcon = Config.GetString(PropertyIdH.UIItemIcon, null);
            if (string.IsNullOrWhiteSpace(Settings.UIItemIcon)) {
                Settings.UIItemIcon = Tile.Definition.Preview;
            }
            Settings.ProgressBarKey = Config.GetString(PropertyIdH.ProgressBarKey,
                "printing.item");
            Settings.ProgressBarKeyHash = Hashes.S(Settings.ProgressBarKey);
            var craftKey = Config.GetString(PropertyIdH.CraftKey, null);
            if (craftKey == null) {
                Settings.CraftActionNameT = T.VerbProduce;
            } else {
                Settings.CraftActionNameT = craftKey.T();
            }
            IsExplosivenessProviderDisabled =
                Config.GetBool(PropertyIdH.DisableExplosivenessProvider, false);
            var qualityCurveId = Config.GetString(PropertyIdH.NoOperatorQualityCurve, null);
            if (qualityCurveId != null) {
                Settings.NoOperatorQualityCurve = CurveDef.Get(qualityCurveId, false);
            }
            var researchedCurveId = Config.GetString(
                PropertyIdH.NoOperatorQualityCurveResearched, null);
            var qualityResearchId = Config.GetString(PropertyIdH.NoOperatorQualityResearch, null);
            if (researchedCurveId != null && qualityResearchId != null) {
                Settings.NoOperatorQualityCurveResearched = CurveDef.Get(researchedCurveId, false);
                Settings.NoOperatorQualityResearch = Hashes.S(qualityResearchId);
            }
        }

        public override string ToString() {
            return " * MultiCrafterComp";
        }

        private bool isRegistered;
        public float UpgradeSpeed = 1f;
        public float UpgradeEnergyEfficiency = 1f;
        public int TotalProduced;

        private void RegisterCrafter(bool yes) {
            if (yes != isRegistered) {
                isRegistered = yes;
                S.Sig.RegisterCrafter.Send(this, yes);
            }
        }
        public void CheckOperator(bool isWanted) {
            if (!Settings.IsOperatorRequired) { return; }
            if (Operatable == null) {
                D.Err("Crafter with IsOperatorRequired setting must have an OperatableDevice component in the def. Such component not found for {0}",
                    Entity.DefinitionId);
                return;
            }
            //D.Log("Want operator: {0}", isWanted);
            if (isWanted) {
                Operatable.SetPriority(Settings.TaskPriority);
                Operatable.HailOperator(false);
            } else {
                Operatable.DismissOperator(true);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            extraInfo = Entity.GetRequiredComponent<ExtraInfoComp>(this);
            Entity.TryGetComponent(out Operatable);
            ENode = Tile.ENode;
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            UI = new MultiCrafterUI(this);
            if (Tile.IsConstructed) {
                RegisterCrafter(true);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterCrafter(true);
            } else if (message == MsgIdH.HealthWentToZero) {
                RegisterCrafter(false);
            }
        }

        public override void OnRemove() {
            RegisterCrafter(false);
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (goal.JobIdH == JobTypeIdH.Hauling && (AIGoal.IsFailed(to) || AIGoal.IsCancelled(to))) {
                // cycle next order
                CycleNextOrder("Goal failed");
            }
        }

        public bool ShouldSuppressGoalFailure(AIGoal goal) {
            return goal.JobIdH == JobTypeIdH.Hauling
                && CurrentOrder?.SuppressDeficitWarning == true;
        }

        public void CycleNextOrder(string why) {
            var orders = Orders;
            if (Arrays.Count(orders) < 2) {
                return;
            }
            // try to get next workable order
            var curOrder = CurrentOrder;
            bool hadProducedEnough = curOrder?.ShouldStopProducing(S) == true; //curOrder?.State == CraftingOrderState.ProducedEnough;
            var orderIdx = curOrder == null ? -1 : orders.IndexOf(curOrder);
            if (orderIdx == -1) {
                CurrentOrder = null;
                curOrder?.Ping();
                curOrder = null;
                orderIdx = 0;
            }

            CraftingOrder nextOrder = null;
            CraftingOrder nextBackup = null;

            var oc = orders.Count;

            // <= so we can loop back to the first order
            for (int i = orderIdx; i < oc + orderIdx; i++) {
                var order = orders[i % oc];
                var progress = order.Progress;
                var demand = order.Demand;
                var shouldStop = order.ShouldStopProducing(S);
                // TODO when amount produced increases, update amount block!
                if (shouldStop) {
                    order.Deficit?.Clear();
                    if (order.State == CraftingOrderState.Paused) {
                        // nothing to do
                    } else if (order.State != CraftingOrderState.ProducedEnough) {
                        order.State = CraftingOrderState.ProducedEnough;
                        order.Ping();
                    }
                } else if (order != curOrder && nextOrder == null) {
                    if (order.CheckIngredientAvailability(S)) {
                        nextOrder = order;
                        break;
                    } else {
                        nextBackup ??= order;
                    }
                }
            }

            // in case none of the orders are doable, cycle forward
            if (nextOrder == null && nextBackup != null
                    && curOrder?.CheckIngredientAvailability(S) != true) {
                nextOrder = nextBackup;
            }

            if (nextOrder != null) {
                var co = CurrentOrder;
                if (co != null) {
                    if (co.State == CraftingOrderState.InProgress) {
                        co.State = CraftingOrderState.InQueue;
                    }
                }
                #if DEBUG_CRAFTING
                D.Warn("Switching current order: [{2}] {0} => {1}",
                    CurrentOrder, nextOrder, why);
                #endif
                nextOrder.State = CraftingOrderState.InProgress;
                CurrentOrder = nextOrder;
                nextOrder.Ping();
                co?.Ping();
                UpdateExtraInfo();
            }

            if (!hadProducedEnough && CurrentOrder?.State == CraftingOrderState.ProducedEnough) {
                CurrentOrder.Ping();
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            if (goal.JobIdH == JobTypeIdH.Hauling) {
                goal.CheckDoable = CheckCanHaulIngredients;
            }
        }

        // Re-runs on every pass the goal spends waiting for a worker, which is what stops
        // a standing ingredient haul from draining material a higher priority task is
        // accumulating (see MatClaims). CraftingOrder.CheckMissingIngredients cannot do
        // this - it bails out early while the goal is active.
        public AIGoalCheckResult CheckCanHaulIngredients(GameState s, AIGoal goal) {
            var next = NextMissingMat;
            if (next == null) {
                // Order fulfilled or swapped out under the goal - let the crafter's own
                // pass clean it up instead of failing the task here
                return AIGoalCheckResult.Doable;
            }
            return MatClaimGate.CheckHaulGoal(s, this, next, goal.GoalPriority);
        }

        public bool ProvideRequestedMat(UnstoredMatComp unstoredMat, Being worker,
                AIGoal goal = null) {
            var co = CurrentOrder;
            if (co == null) {
                D.Err("Current order is null when providing requested mat to crafter! {0} | {1}",
                    Entity.DefinitionId, worker);
                return false;
            }
            var ingredients = co.Ingredients;
            D.Ass(ingredients != null,
                "Crafter taking ingredients from being while having null ingredients");
            for (int i = ingredients.Length - 1; i >= 0; i--) {
                var mat = ingredients[i];
                if (mat.Type != unstoredMat.Type) { continue; }
                var d = mat.Diff;
                if (d <= 0) { continue; }
                var taken = Mathf.Min(d, unstoredMat.StackSize);
                taken = unstoredMat.Take(mat.Type, null, taken,
                    goal ?? worker?.AIAgent?.Goal);
                mat.StackSize += taken;
                ingredients[i] = mat;
                if (mat.StackSize == mat.MaxStackSize) {
                    co.Deficit?.ClearIf(mat.Type);
                }
                if (taken > 0) {
                    S.Sig.MatConsumed.Send(mat.Type, taken);
                }
            }
            //UpdateUIBlock(true);

            var isFulfilled = co.RebuildIngredientsReq();
            if (isFulfilled) {
                // CraftingSys.EnableBreakpoint = true;
                // was MatDeficit
                co.State = CraftingOrderState.InProgress;
                co.Ping();
                // CurrentOrder.HaulingGoal = null;
                UI.HideIcon();
            }

            #if !PRODUCTION_BUILD
            OnProvideMat?.Invoke(unstoredMat, isFulfilled);
            #endif

            return isFulfilled;
        }

        public CraftingOrder StartProducing(Craftable craftable) {
            return UI.StartProducing(craftable);
        }

        public void NotifyMatDeficit(MatType type) {
            var co = CurrentOrder;
            if (co == null) {
                D.Err("Trying to notify deficit to multi crafter without current order!");
                return;
            }
            co.NotifyDeficit(type);
            // CycleNextOrder();
        }

        public void ClearLackNotification(MatType type) {
            foreach (var order in Orders) {
                order.Deficit?.ClearIf(type);
            }
        }
        public void UpdateExtraInfo() {
            if (extraInfo == null) { return; }
            var curOrd = CurrentOrder;
            if (curOrd == null && !Arrays.IsEmpty(Orders)) {
                curOrd = Orders[0];
            }
            if (curOrd != null) {
                extraInfo.ShowInfoIconWithTint(curOrd.Demand.Product.Preview,
                    UI.MainTooltip(), curOrd.ProductColor);
            } else {
                extraInfo.HideInfoIcon();
            }
        }

        public void ApplyEfficiencyUpgrade(float amt) {
            UpgradeEnergyEfficiency += amt;
            if (UpgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} ->{1}",
                    amt, UpgradeEnergyEfficiency);
                UpgradeEnergyEfficiency = 1f;
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            UpgradeSpeed += amt;
            if (UpgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} ->{1}",
                    amt, UpgradeSpeed);
                UpgradeSpeed = 1f;
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Priority", Settings.TaskPriority);
            data.SetInt("TotalProd", TotalProduced);
            data.SetBool("IdleWarn", Settings.IsIdleWarnOn);
            data.SetBool("IgnoreUnconfigured", Settings.IsIgnoreUnconfiguredOn);
        }

        protected override void OnLoad(ComponentData data) {
            Settings.TaskPriority.SetSilent(data.GetInt("Priority", Settings.TaskPriority));
            TotalProduced = data.GetInt("TotalProd", TotalProduced);
            Settings.IsIdleWarnOn = data.GetBool("IdleWarn", Settings.IsIdleWarnOn);
            Settings.IsIgnoreUnconfiguredOn = data.GetBool("IgnoreUnconfigured", Settings.IsIgnoreUnconfiguredOn);
        }

        public void AfterInitDef(Def def, ComponentConfig config) {
            var types = config.GetStringSet(PropertyIdH.CompatibleTypes, null);
            if (types == null) {
                D.Err("CrafterComp does not have compatible types! {0}", def.Id);
                return;
            }
            foreach (var type in types) {
                if (!Craftable.CraftersByType.TryGetValue(type, out var crafters)) {
                    crafters = new();
                    Craftable.CraftersByType.Add(type, crafters);
                }
                crafters.Add(def);
            }
        }
        public void OnAIGoalPriorityChange(AIGoal goal) {
            var priority = goal.GoalPriority;
            OnPriorityChange(priority);
        }
        private void OnPriorityChange(int priority) {
            Settings.TaskPriority.Set(priority);
            Operatable?.SetPriority(priority);
            if (priority == 0 && HasGoal) {
                UI.CancelHaulingGoal(T.TaskPriority);
            }
        }

        public bool CanCopyTo(ICopyableComp target) {
            return target is MultiCrafterComp cr && cr.Entity.Definition == Entity.Definition;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is not MultiCrafterComp cr
                    || cr.Entity.Definition != Entity.Definition) {
                return false;
            }
            bool copied = false;
            if (cr.Settings.TaskPriority != Settings.TaskPriority) {
                cr.OnPriorityChange(Settings.TaskPriority);
                copied = true;
            }
            if (cr.Settings.IsIdleWarnOn != Settings.IsIdleWarnOn) {
                cr.Settings.IsIdleWarnOn = Settings.IsIdleWarnOn;
                if (!Settings.IsIdleWarnOn) {
                    cr.UI.HideIdleNotification();
                } else if (cr.CurrentOrder?.State == CraftingOrderState.ProducedEnough) {
                    cr.UI.ShowIdleNotification();
                }
                copied = true;
            }
            if (cr.Settings.IsIgnoreUnconfiguredOn != Settings.IsIgnoreUnconfiguredOn) {
                cr.Settings.IsIgnoreUnconfiguredOn = Settings.IsIgnoreUnconfiguredOn;
                if (Settings.IsIgnoreUnconfiguredOn) {
                    cr.UI.MarkConfigured();
                }
                copied = true;
            }
            copied |= S.Query.CopyCrafterOrders.Ask(this, cr);
            cr.UI.UpdateProgressBar();
            cr.UpdateExtraInfo();
            return copied;
        }
        public void RelocateTo(Entity target) {
            if (!target.TryGetComponent<MultiCrafterComp>(out var crafter)) {
                return;
            }
            CopyConfigTo(crafter);
            if (Arrays.Count(Orders) == 0) { return; }
            var co = CurrentOrder;
            for (int i = Orders.Count - 1; i >= 0; i--) {
                if (co == Orders[i]) {
                    crafter.CurrentOrder = crafter.Orders[i];
                }
                crafter.Orders[i].Progress = Orders[i].Progress;
                crafter.Orders[i].Ingredients = Orders[i].Ingredients;
                // Copy Paused state, but reset InProgress to InQueue
                var sourceState = Orders[i].State;
                if (sourceState == CraftingOrderState.Paused) {
                    crafter.Orders[i].State = CraftingOrderState.Paused;
                } else if (sourceState == CraftingOrderState.InProgress) {
                    crafter.Orders[i].State = CraftingOrderState.InQueue;
                }
                // Transfer demand counters (AmountProduced, AmountProducedThis)
                // These should transfer on relocate but not on copy config
                crafter.Orders[i].Demand.AmountProduced = Orders[i].Demand.AmountProduced;
                crafter.Orders[i].Demand.AmountProducedThis = Orders[i].Demand.AmountProducedThis;
            }
            crafter.CurrentOrder?.RebuildIngredientsReq();
            crafter.UI.UpdateProgressBar();
        }

        public void OnExplode() {
            // nothing to do
        }
    }
}
