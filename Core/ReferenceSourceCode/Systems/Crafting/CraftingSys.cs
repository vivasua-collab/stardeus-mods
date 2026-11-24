#if UNITY_EDITOR
// #define GENERATE_RANDOM
#define DEBUG_BREAKPOINT
#define DEBUG_QUALITY
#endif
using KL.Utils;
using Game.Constants;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Collections;
using Game.Systems.Hints;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Systems.BodyParts;
using Game.Achievements;
using Game.Commands;
using Game.Systems.UIUX;

namespace Game.Systems.Crafting {
    public sealed class CraftingSys : GameSystem, ISaveableSpecial {

        public const string SysId = "CraftingSys";
        public override string Id => SysId;
        #if DEBUG_BREAKPOINT
        public static bool EnableBreakpoint;
        #endif

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new CraftingSys());
        }
        private readonly List<MultiCrafterComp> Crafters = new();
        private readonly Dictionary<MultiCrafterComp, List<CraftingOrder>> crafterOrders = new();
        private readonly Dictionary<Craftable, List<CraftingOrder>> craftingOrders = new();
        private readonly Dictionary<Craftable, BCallback> craftableChangedCBs = new();
        private readonly Queue<MultiCrafterComp> PendingCrafters = new();
        private CraftingSysData data;

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RegisterCrafter.AddListener(OnRegisterCrafter);
            S.Query.GetAllCraftingOrders.SetResponder(() => craftingOrders);
            S.UIQuery.GetCraftableChangedCallback.SetResponder(GetCraftableChangedCB);
        }

        private BCallback GetCraftableChangedCB(Craftable cr) {
            if (!craftableChangedCBs.TryGetValue(cr, out var cb)) {
                cb = new BCallback();
                craftableChangedCBs.Add(cr, cb);
            }
            return cb;
        }

        private void OnRegisterCrafter(MultiCrafterComp crafter, bool added) {
            if (added) {
                if (!Crafters.AddUniqueInefficient(crafter)) {
                    D.Err("Trying to register crafter more than once! {0}", crafter);
                }
            } else {
                if (Crafters.Remove(crafter)) {
                    // D.Warn("Removing orders for unregistered crafter: {0}", crafter);
                    if (crafterOrders.TryGetValue(crafter, out var orders)) {
                        for (int i = orders.Count - 1; i >= 0; i--) {
                            CraftingOrder order = orders[i];
                            RemoveFromCraftingOrders(order);
                        }
                    }
                    crafterOrders.Remove(crafter);
                } else {
                    D.Err("Trying to unregister a crafter that wasn't registered: {0}", crafter);
                }
            }
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.GetCraftingOrdersFor.SetResponder(OnGetCraftingOrders);
            state.Query.CreateCraftingOrder.SetResponder(OnCreateCraftingOrder);
            state.Query.RemoveCraftingOrder.SetResponder(OnRemoveCraftingOrder);
            state.Query.CopyCrafterOrders.SetResponder(OnCopyCrafterOrders);
            // D.Warn("Will restore data after all systems loaded: {0}", data);
            if (data != null) {
                Ready.When(WhenH.AllSystemsLoaded, FinishRestore);
            }
        }

        private bool OnCopyCrafterOrders(MultiCrafterComp source, MultiCrafterComp target) {
            var sourceOrders = source.Orders;
            var targetOrders = target.Orders;
            bool changed = false;
            // 1. remove orders that do not exist in source
            if (!Arrays.IsEmpty(targetOrders)) {
                for (int i = targetOrders.Count - 1; i >= 0; i--) {
                    var order = targetOrders[i];
                    if (GetCorrespondingOrder(sourceOrders, order) == null) {
                        target.UI.StopProducing(order);
                        targetOrders = target.Orders;
                        changed = true;
                    }
                }
            }

            // 2. add orders that did not exist in source
            if (!Arrays.IsEmpty(sourceOrders)) {
                for (int i = 0; i < sourceOrders.Count; i++) {
                    var o = sourceOrders[i];
                    var corresponding = GetCorrespondingOrder(targetOrders, o);
                    if (corresponding != null) {
                        changed |= o.CopyTo(corresponding);
                    } else {
                        var newO = target.StartProducing(o.Demand.Craftable);
                        o.CopyTo(newO);
                        targetOrders = target.Orders;
                        changed = true;
                    }
                }
            }
            if (Arrays.Count(sourceOrders) != Arrays.Count(targetOrders)) {
                // nice method didn't work, there are duplicates let's hammer this shit
                D.Warn("Duplicate recipes detected in source orders, will stop / restart target orders because of that: {0}",
                    D.DumpCollection(sourceOrders));
                target.UI.StopAll();
                for (int i = 0; i < sourceOrders.Count; i++) {
                    var o = sourceOrders[i];
                    var newO = target.StartProducing(o.Demand.Craftable);
                    o.CopyTo(newO);
                }
                changed = true;
            } else {
                changed |= EnsureSameOrder(sourceOrders, targetOrders);
            }

            if (!Arrays.IsEmpty(targetOrders)) {
                for (int i = 0; i < targetOrders.Count; i++) {
                    var sourceOrder = sourceOrders[i];
                    var targetOrder = targetOrders[i];
                    bool sourcePaused = sourceOrder.State == CraftingOrderState.Paused;
                    bool targetPaused = targetOrder.State == CraftingOrderState.Paused;
                    if (sourcePaused != targetPaused) {
                        targetOrder.State = sourcePaused
                            ? CraftingOrderState.Paused
                            : CraftingOrderState.InQueue;
                        targetOrder.Ping();
                        changed = true;
                    } else if (targetOrder.State == CraftingOrderState.ProducedEnough) {
                        // force refresh
                        targetOrder.State = CraftingOrderState.InQueue;
                    }
                }
            }

            return changed;
        }
        public bool EnsureSameOrder(List<CraftingOrder> source,
                List<CraftingOrder> target) {
            if (Arrays.IsEmpty(source)) { return false; }
            if (source.Count != target.Count) {
                D.Err("Can't ensure same order, count mismatch! {0} and {1}",
                    D.DumpCollection(source), D.DumpCollection(target));
                return false;
            }
            bool changed = false;
            for (int i = 0; i < source.Count; i++) {
                var targetOrder = GetCorrespondingOrder(target, source[i]);
                var targetOrderIdx = target.IndexOf(targetOrder);
                if (targetOrderIdx == -1) {
                    D.Err("Failed to find corresponding order: {0} in {1}",
                        source[i], D.DumpCollection(target));
                    continue;
                }
                if (targetOrderIdx != i) {
                    target.RemoveAt(targetOrderIdx);
                    target.Insert(i, targetOrder);
                    changed = true;
                }
            }
            return changed;
        }

        private CraftingOrder GetCorrespondingOrder(List<CraftingOrder> orders, CraftingOrder order) {
            if (order == null) {
                D.Err("Order is null in GetCorrespondingOrder!");
                return null;
            }
            if (orders == null) { return null; }
            for (int i = orders.Count - 1; i >= 0; i--) {
                CraftingOrder o = orders[i];
                if (CraftingDemand.IsSameCraftable(o.Demand, order.Demand)) {
                    return o;
                }
            }
            return null;
        }

        private bool OnRemoveCraftingOrder(CraftingOrder order) {
            if (crafterOrders.TryGetValue(order.Crafter, out var orders)) {
                order.Deficit?.Clear();
                if (!Arrays.IsEmpty(order.Ingredients)) {
                    for (int i = order.Ingredients.Length - 1; i >= 0; i--) {
                        Mat mat = order.Ingredients[i];
                        if (mat.StackSize < 1) { continue; }
                        EntityUtils.SpawnRawMaterial(mat,
                            order.Crafter.OutPort, 0.5f, true);
                    }
                    order.Ingredients = null;
                }
                var removed = orders.Remove(order);
                if (removed) {
                    RemoveFromCraftingOrders(order);
                }
                return removed;
            }
            return false;
        }

        private void RemoveFromCraftingOrders(CraftingOrder order) {
            var craftable = order.Demand?.Craftable;
            if (craftable == null) { return; }
            if (craftingOrders.TryGetValue(craftable, out var list)) {
                list.Remove(order);
                if (list.Count == 0) {
                    craftingOrders.Remove(craftable);
                }
                NotifyCraftableChanged(craftable);
            }
        }

        private void AddToCraftingOrders(CraftingOrder order) {
            var craftable = order.Demand?.Craftable;
            if (craftable == null) { return; }
            if (!craftingOrders.TryGetValue(craftable, out var list)) {
                list = new();
                craftingOrders.Add(craftable, list);
            }
            list.Add(order);
            NotifyCraftableChanged(craftable);
        }

        private void NotifyCraftableChanged(Craftable craftable) {
            if (craftableChangedCBs.TryGetValue(craftable, out var cb)) {
                cb.Invoke();
            }
        }

        private CraftingOrder OnCreateCraftingOrder(MultiCrafterComp crafter,
                CraftingDemand demand) {
            var order = new CraftingOrder();
            order.Crafter = crafter;
            order.Demand = demand;
            if (!crafterOrders.TryGetValue(crafter, out var orders)) {
                orders = new();
                crafterOrders.Add(crafter, orders);
                order.Crafter.SetOrders(orders);
            }
            order.Ingredients = CraftingOrder.IngredientsFor(
                demand.Craftable, demand.BatchSize);
            order.CheckMissingIngredients(S, true);
            orders.Add(order);
            AddToCraftingOrders(order);
            if (orders.Count == 1) {
                crafter.CurrentOrder = order;
            }
            MaybeHintCraftingTargetInventory(order);
            return order;
        }
        private void MaybeHintCraftingTargetInventory(CraftingOrder order) {
            /*
            Uncomment if this will become non-recurring
            if (!S.Sys.Hint.IsRelevant(HintCrafterTargetInventory.Id)) {
                return;
            }
            */

            int found = 0;
            for (int i = Crafters.Count - 1; i >= 0; i--) {
                MultiCrafterComp cr = Crafters[i];
                var o = cr.CurrentOrder;
                if (o == null) { continue; }
                if (IsSimilarOrderWithDefaults(order, o)) {
                    if (++found > 2) {
                        S.Sys.Hint.Show(HintMultipleCraftersWithDefaultSettings.Id, false);
                        break;
                    }
                }
            }
        }

        private List<CraftingOrder> OnGetCraftingOrders(MultiCrafterComp crafter) {
            if (crafterOrders.TryGetValue(crafter, out var orders)) {
                return orders;
            }
            return null;
        }

        private void FinishRestore() {
            foreach (var od in data.Orders) {
                if (!CraftingOrderData.TryDeserialize(S, od, out var order)) {
                    continue;
                }
                if (!crafterOrders.TryGetValue(order.Crafter, out var orders)) {
                    orders = new();
                    crafterOrders.Add(order.Crafter, orders);
                    order.Crafter.SetOrders(orders);
                }
                orders.Add(order);
                AddToCraftingOrders(order);
                if (order.Deficit?.HasDeficit == true) {
                    order.Deficit.OnLateReady();
                }
                if (order.State == CraftingOrderState.InProgress) {
                    order.Crafter.CurrentOrder = order;
                // restore shit correctly, an extra cycle will help
                } else if (order.State == CraftingOrderState.ProducedEnough) {
                    order.State = CraftingOrderState.InQueue;
                }
                if (!Crafters.Contains(order.Crafter)) {
                    if (order.Crafter.Tile.IsConstructed) {
                        D.Err("Crafter not registered when restoring data! {0}",
                            order.Crafter.Tile);
                    }
                }
            }
            foreach (var crafter in Crafters) {
                crafter.UpdateExtraInfo();
                crafter.UI.UpdateProgressBar();
            }
        }

        private void OnTick(long ticks) {
            // Electricity ticks on 0, so let's give it a bit of time to catch up
            // then process craftables half way towards the next grid tick
            if (ticks % 10 == 5) {
                EnqueuePendingCrafters();
            }
            ProcessPendingCrafters(false);
        }

        private void ProcessPendingCrafters(bool force) {
            int i = 1;
            while (PendingCrafters.TryDequeue(out var crafter)) {
                ProcessCrafter(crafter);
                if (!force && i++ % 5 == 0 && !GameTimer.HasAvailableFrameBudget(verbose: false)) {
                    #if !PRODUCTION_BUILD
                    if (G.ClockSpeed < ClockSpeed.Unlimited) {
                        D.Warn("Out of budget with {0} remaining crafters. Total: {1}",
                            PendingCrafters.Count, Crafters.Count);
                    }
                    #endif
                    break;
                }
            }
            // D.Warn("After process crafters: {0} (this: {1})", PendingCrafters.Count, i);
        }

        private void ProcessCrafter(MultiCrafterComp crafter) {
            #if DEBUG_BREAKPOINT
            if (EnableBreakpoint) {
                D.Warn("Processing crafter with breakpoint");
            }
            #endif
            if (!crafter.Entity.IsActive) { return; }
            if (!crafterOrders.TryGetValue(crafter, out var orders)) {
                orders = null;
            }
            var elNode = crafter.ENode;
            bool hasOrders = !Arrays.IsEmpty(orders);
            var ui = crafter.UI;

            // turned off or disconnected crafter
            if (!elNode.IsPowerable) {
                if (hasOrders) {
                    ui.HideIdleNotification();
                    ui.ClearDeficits();
                }
                ui.MarkConfigured();
                crafter.CheckOperator(false);
                return;
            }

            // no orders
            if (!hasOrders) {
                if (crafter.HasGoal) {
                    ui.CancelHaulingGoal(T.InfoDeviceUnconfigured);
                }
                ui.MarkUnconfigured();
                crafter.CheckOperator(false);
                crafter.CurrentOrder = null;
                elNode.SetConsumerWantedInput(0f);
                return;
            }

            var order = crafter.CurrentOrder;
            if (order == null) {
                order = orders[0];
                crafter.CurrentOrder = order;
            }

            // var order = GetNextWorkableOrder(crafter, orders);
            if (order.ShouldStopProducing(S)) {
                order.Deficit?.Clear();
                crafter.CheckOperator(false);
                elNode.SetConsumerWantedInput(0f);
                if (order.State == CraftingOrderState.Paused) {
                    ui.ShowIdleNotification();
                    if (crafter.TryGetGoal(out var g)) {
                        AIGoal.Cancel(g, G.Ticks);
                    }
                    crafter.HideInfoBar();
                    crafter.CycleNextOrder("Paused order");
                } else if (order.State != CraftingOrderState.ProducedEnough) {
                    order.State = CraftingOrderState.ProducedEnough;
                    ui.ShowIdleNotification();
                    if (crafter.TryGetGoal(out var g)) {
                        AIGoal.Cancel(g, G.Ticks);
                    }
                    crafter.HideInfoBar();
                    ui.ShowProductionGoalReached();
                    order.Ping();
                } else {
                    crafter.CycleNextOrder("Spawned craftable");
                }
                return;
            } else {
                order.State = CraftingOrderState.InProgress;
                ui.HideIdleNotification();
                order.Ping();
            }

            ui.HideIdleNotification();

            if (!order.CheckMissingIngredients(S, false)) {
                // we are not ready to build, waiting for materials
                crafter.CheckOperator(false);
                elNode.SetConsumerWantedInput(0f);
                crafter.HideInfoBar();
                return;
            }

            if (order.Demand.Craftable == null) {
                D.Err("Broken demand in a crafter: {0}", order);
                ui.StopProducing(order);
                crafter.CheckOperator(false);
                elNode.SetConsumerWantedInput(0f);
                return;
            }

            var isOpRequired = crafter.Settings.IsOperatorRequired;

            if (isOpRequired) {
                crafter.CheckOperator(true);
                if (crafter.Operatable.Operator == null) {
                    elNode.SetConsumerWantedInput(0f);
                    return;
                }
            }

            // actual progress
            var demand = order.Demand;
            var curBatchSize = demand.BatchSize;
            var batchCost = Equations.BatchSizeCost(curBatchSize);
            var energyCost = demand.Craftable.EnergyCost * Tunables.CraftingEnergyCost;
            energyCost /= crafter.UpgradeEnergyEfficiency;
            if (curBatchSize > 1) {
                energyCost /= batchCost;
            }
            if (elNode.IsConsuming) {
                order.ElectricityDeficitKW = 0f;
                float operatorSpeed = isOpRequired ? crafter.Operatable.OperatorEfficiency : 1f;
                var addProgress = 10f / Consts.TicksPerHour / demand.Craftable.ProductionTimeHours;
                addProgress *= Tunables.CraftingSpeed * crafter.Settings.SpeedMultiplier * crafter.UpgradeSpeed * operatorSpeed;
                if (curBatchSize > 1) {
                    addProgress /= curBatchSize;
                    addProgress /= batchCost;
                }
                // D.Log("add progress: {0} {1}", addProgress, Tunable.Float(TunableKey.CraftingSpeed));
                var newProg = order.Progress + addProgress;
                if (newProg > 1f) {
                    newProg = 1f;
                }
                order.Progress = newProg;
                if (newProg >= 1f) {
                    SpawnCraftable(order);
                    elNode.SetConsumerWantedInput(0f);
                    crafter.CycleNextOrder("Spawned craftable");
                } else {
                    elNode.SetConsumerWantedInput(energyCost);
                }
                order.Ping();
                ui.UpdateProgressBar();
                return;
            } else {
                if (elNode.HasDeficit) {
                    order.ElectricityDeficitKW = elNode.Node.Deficit;
                    S.Sys.Log.AddLine(crafter.Tile,
                        $"{Texts.Red(T.ElectricityDeficit)}: {order.Demand.CraftableItemName} ({Units.Electricity(order.ElectricityDeficitKW)})");
                    crafter.CycleNextOrder("Electricity deficit");
                }
                order.Ping();
                #if !PRODUCTION_BUILD
                order.Crafter.OnStartCrafting?.Invoke(demand);
                #endif
                elNode.SetConsumerWantedInput(energyCost);
            }
        }

        private void SpawnCraftable(CraftingOrder order) {
            var craftable = order.Demand.Craftable;
            var curBatchSize = order.Demand.BatchSize;
            var quantity = craftable.OutputMultiplier * curBatchSize;
            var outPort = order.Crafter.OutPort;
            if (quantity == 0) {
                D.Err("Craftable quantity 0! Setting to 1 {0}", craftable.Id);
                quantity = 1;
            }
            var productDef = craftable.ProductDef;
            if (productDef.IsBeing) {
                quantity = SpawnCraftable_Being(order, quantity, outPort, productDef);
            } else if (productDef.MatType != null) {
                quantity = SpawnCraftable_Mat(order, quantity, outPort, productDef);
            } else {
                quantity = SpawnCraftable_Obj(order, quantity, outPort, productDef);
            }
            order.Crafter.UpdateExtraInfo();
            order.Progress = 0;
            order.ResetIngredients();
            order.Demand.AmountProduced += quantity;
            order.Demand.AmountProducedThis += quantity;
            order.Crafter.TotalProduced += quantity;
            S.Sig.CrafterProducedItem.Send(order, quantity);
            var productMatType = craftable.ProductDef?.MatType;
            if (productMatType != null) {
                S.Sig.MatProduced.Send(productMatType, quantity);
            } else if (!productDef.IsBeing) {
                S.Sig.ObjProduced.Send(productDef, quantity);
            }
        }

        private int SpawnCraftable_Obj(CraftingOrder order, int quantity, int outPort, Def productDef) {
            // Spawn an item
            for (int i = 0; i < quantity; i++) {
                // var data = S.Query.GenObjFull.Ask(productDef, Rng.Unseeded);
                var color = order.NextProductColor ?? order.ProductColor;
                order.NextProductColor = order.ProductColor;
                var quality = QualityFor(order);
                var obj = EntityUtils.SpawnNonMatObj(
                    EntityUtils.CenterOf(outPort) + Rng.UInsideUnitCircle() * 0.25f,
                    order.Demand.Product, quality, color);
                if (order.Crafter.Settings.IsAutoHaulProduct) {
                    obj?.StorableObj?.TryOrderStoring(false, true, out _);
                }
            }
            return quantity;
        }

        private float QualityFor(CraftingOrder order) {
            float maxQ;
            float minQ;
            var op = order.Crafter.Operatable?.Operator;
            if (op != null) {
                var jobType = order.Crafter.Operatable?.JobType.IdHash
                    ?? JobTypeIdH.Crafting;
                CraftingQualityFor(op, jobType, out minQ, out maxQ);
            } else {
                var qualityCurve = NoOperatorQualityCurveFor(order.Crafter.Settings);
                if (qualityCurve != null) {
                    // the curve maps a uniform roll to quality, so it is the whole distribution
                    return Rng.UCurve(qualityCurve) * order.Crafter.Tile.Health.HealthPercent;
                }
                maxQ = Tunables.CraftingQualityWithoutOperatorMax;
                minQ = Tunables.CraftingQualityWithoutOperatorMin;
            }
            var q = Rng.UChance(0.33f) ?
                Rng.URange(minQ, maxQ) : Rng.URangeSmooth(minQ, maxQ);
            // TODO add operator trait effects?
            var health = order.Crafter.Tile.Health.HealthPercent;
            return q * health;
        }

        private Curve NoOperatorQualityCurveFor(CrafterConfig settings) {
            if (settings.NoOperatorQualityCurveResearched != null
                    && S.Research.IsUnlocked(settings.NoOperatorQualityResearch)) {
                return settings.NoOperatorQualityCurveResearched;
            }
            return settings.NoOperatorQualityCurve;
        }

        private Curve skillQualityMinCurve;
        private Curve skillQualityMaxCurve;
        private Curve sanitySkillCurve;

        private void CraftingQualityFor(Being b, int jobType, out float minQ, out float maxQ) {
            if (sanitySkillCurve == null) {
                skillQualityMinCurve = CurveDef.FastGet(CurveIdH.Crafting_SkillToQualityMin);
                skillQualityMaxCurve = CurveDef.FastGet(CurveIdH.Crafting_SkillToQualityMax);
                sanitySkillCurve = CurveDef.FastGet(CurveIdH.Crafting_SanitySkill);
            }
            float skill = 0f;
            if (b.Data.Mind.Jobs.TryJob(jobType, out var job)) {
                skill = job.Level;
            }
            var mentalHealth = b.Data.Mind.Health;
            // robots will have 1f
            float sanityMul = 1f;
            var sanity = mentalHealth?.Sanity.Value ?? 0f;
            var maxMul = 1f;
            if (b.Data.HasCondition(ConditionIdH.Inspired)) {
                maxMul = Rng.URange(1.15f, 1.5f);
            }
            if (sanity > 0f) {
                sanityMul *= sanitySkillCurve.Evaluate(sanity);
            }
            minQ = skillQualityMinCurve.Evaluate(skill) * sanityMul;
            maxQ = skillQualityMaxCurve.Evaluate(skill) * sanityMul * maxMul;
            #if DEBUG_QUALITY
            D.Warn("Crafting quality for operator level {0}. Sanity: {1}, sanityMul: {2}, QRange: {3}-{4} | {5}",
                skill, sanity, sanityMul, minQ, maxQ, b);
            #endif
        }

        private int SpawnCraftable_Mat(CraftingOrder order, int quantity, int outPort, Def productDef) {
            if (order.Crafter.Tile.MaterialStorage?.IsFull == false) {
                order.Crafter.Tile.MaterialStorage.Store(
                    productDef.MatType, null, quantity, null);
            } else {
                S.CmdQ.Enqueue(new CmdSpawnMatByType(productDef.MatType, quantity,
                    outPort, 0f, true,
                        orderHaul: order.Crafter.Settings.IsAutoHaulProduct,
                        haulPriority: order.Crafter.Settings.TaskPriority));
            }
            return quantity;
        }

        private int SpawnCraftable_Being(CraftingOrder order, int quantity, int outPort, Def productDef) {
            var species = Species.ForDef(productDef.IdH);
            if (species == null) {
                D.Err("Trying to produce unknown species for being def: {0}", productDef);
                quantity = 0;
            }
            for (int i = 0; i < quantity; i++) {
                BeingData data;
                if (species.IsBiological) {
                    var vars = new GenBeingVars(species, BeingType.Clone);
                    vars.IsClone = true;
                    data = S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
                    data.Body.Flags |= BodyFlags.Clone;
                    if (species.IdH == SpeciesIdH.Human) {
                        The.AchievementTracker.Unlock(
                            AchievementCloneHuman.AchievementId);
                    }
                } else {
                    var vars = new GenBeingVars(species, BeingType.Newborn);
                    vars.BodyWithoutParts = true;
                    data = S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
                    data.Body.BodySlot.State.SetQuality(QualityFor(order));
                    data.Body.SetBodyState(BodyState.Incapacitated, true);
                    data.StateFlags = BeingState.Dead;
                }
                data.Body.BirthTick = G.Ticks;
                data.Body.Age = 0;
                if (data.Mind != null) {
                    data.Mind.BirthTick = G.Ticks;
                    data.Mind.Age = 0;
                }
                var cmd = new CmdSpawnBeing(data, outPort);
                S.CmdQ.Enqueue(cmd);
            }

            return quantity;
        }

        private void EnqueuePendingCrafters() {
            if (PendingCrafters.Count > 0) {
                if (G.ClockSpeed < ClockSpeed.Unlimited) {
                    D.Warn("Will force pending crafters: {0}. Total crafters: {1}, Ticks: {2}",
                        PendingCrafters.Count, Crafters.Count, G.Ticks);
                }
                ProcessPendingCrafters(true);
            }
            for (int i = Crafters.Count - 1; i >= 0; i--) {
                PendingCrafters.Enqueue(Crafters[i]);
            }
        }

        private bool IsSimilarOrderWithDefaults(CraftingOrder order, CraftingOrder order2) {
            if (order == order2) { return false; }
            var demand = order2.Demand;
            if (demand.Craftable != order.Demand.Craftable) { return false; }
            return demand.Type == CraftingDemandType.Limit
                    && demand.AmountWanted == CraftingDemand.DefaultAmount;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            var data = new CraftingSysData();
            data.Orders = new List<CraftingOrderData>();
            foreach (var co in crafterOrders) {
                foreach (var o in co.Value) {
                    if (!o.Crafter.Entity.IsActive) { continue; }
                    var od = CraftingOrderData.Serialize(o);
                    if (od != null) {
                        data.Orders.Add(od);
                    }
                }
            }
            sd.Crafting = data;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            // D.Warn("Loading crafting sys data");
            data = sd.Crafting;
        }

        public override void Unload() {
        }
    }
}
