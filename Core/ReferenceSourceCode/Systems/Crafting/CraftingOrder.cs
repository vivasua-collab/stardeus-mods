#if !PRODUCTION_BUILD
// #define DEBUG_STATE
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Inventory;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Crafting {
    public sealed class CraftingOrder {
        public readonly B<CraftingOrder> Changed;
        public CraftingOrderState State;
        public float Progress;
        // Materials required to print current
        public Mat[] Ingredients;
        public CraftingDemand Demand;
        public MultiCrafterComp Crafter;
        public MatDeficitEventHolder Deficit;
        public float ElectricityDeficitKW;
        public List<MatRequest> MissingMats { get; } = new();
        // public AIGoal HaulingGoal;

        private bool suppressDeficitWarning;
        public bool SuppressDeficitWarning {
            get => suppressDeficitWarning;
            set {
                if (suppressDeficitWarning == value) { return; }
                suppressDeficitWarning = value;
                Deficit?.SetMuted(value);
                if (Crafter?.CurrentOrder == this && Crafter.TryGetGoal(out var g)) {
                    g.RefreshFailIcon();
                }
            }
        }

        public CraftingOrder() {
            Changed = new B<CraftingOrder>(this);
        }

        public void InitDeficit() {
            if (Deficit == null) {
                Deficit = new MatDeficitEventHolder(Crafter.Tile,
                    Crafter, Crafter.DeviceCategory);
                Deficit.SetMuted(suppressDeficitWarning);
            }
        }

        public void NotifyDeficit(MatType type) {
            InitDeficit();
            Deficit.Notify(type, allowNewType: true);
        }

        public MatRequest NextMissingMat {
            get {
                if (Arrays.IsEmpty(MissingMats)) { return null; }
                for (int i = 0; i < MissingMats.Count; i++) {
                    var r = MissingMats[i];
                    if (r.Amount > 0) {
                        return r;
                    }
                }
                return null;
            }
        }
        ///<summary>Returns true if we have everything we need to build</summary>
        public bool CheckMissingIngredients(GameState S, bool checkIfProducedEnough) {
            var entity = Crafter.Entity;
            if (!entity.IsActive) { return false; }
            if (checkIfProducedEnough && !Demand.HasProducedEnough(S)) {
                return false;
            }
            // check if we have a haul goal
            if (Crafter.TryGetGoal(out var g)) {
                if (AIGoal.IsActive(g.State)) {
                    return false;
                }
                if (AIGoal.IsFailed(g.State)) {
                    return false;
                }
            }

            // Just in case
            #if !PRODUCTION_BUILD
            if (Demand.BatchSize < 1) {
                throw new Exception("Demand Batch size zero!!!! FUCK!!!");
                // UpdateBatchSize(1);
            }
            #endif

            bool hasMissing = false;
            foreach (var m in Ingredients) {
                if (m.Diff > 0) {
                    hasMissing = true;
                    break;
                }
            }

            // if (showingGoalReachedIcon) {
            //     HideInfoIcon();
            //     showingGoalReachedIcon = false;
            // }
            if (!hasMissing) {
                return true;
            }

            Crafter.Operatable?.DismissOperator(true);
            RebuildIngredientsReq();

            var nextMissing = NextMissingMat;
            var canHaul = nextMissing != null && MatClaimGate.CanHaul(S, Crafter,
                nextMissing, Crafter.Settings.TaskPriority);

            // we have a failed goal
            if (g != null) {
                if (Crafter.Settings.TaskPriority == 0) {
                    CancelHaulingGoal(T.AdRejNoPriority);
                    return false;
                }
                if (!canHaul) {
                    Crafter.TryClearGoal(T.AdRejLackingMat);
                    return false;
                }
            }

            if (entity.Goal == null) {
                var goal = AIGoal.Create(JobTypeIdH.Hauling, T.HaulIngredients,
                        Crafter, G.Ticks)
                    .WithIcon(IconId.CHaul)
                    .NanobotCompatible()
                    .WithPriority(Crafter.Settings.TaskPriority)
                    .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                    .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true);
                goal.CheckDoable = Crafter.CheckCanHaulIngredients;
                if (!entity.TrySetGoal(goal)) {
                    D.Err("Failed setting haul goal on on crafter! {0}", Crafter.Tile);
                }
            }
            return false;
        }

        /// <summary>
        /// Does not create a hauling goal, this is a quick check for material deficits
        /// </summary>
        /// <param name="s"></param>
        /// <returns>true if there are enough ingredients</returns>
        public bool CheckIngredientAvailability(GameState s) {
            // no need to check
            if (State == CraftingOrderState.ProducedEnough) {
                Deficit?.Clear();
                return true;
            }
            var inv = s.Sys.Inventory;
            var hasDeficit = Deficit?.HasDeficit == true;
            int prio = Crafter.Settings.TaskPriority;
            for (int i = 0; i < Ingredients.Length; i++) {
                Mat mat = Ingredients[i];
                if (mat.Diff < 1) { continue; }
                // Read-only probe used to pick a workable order - it must respect other
                // requesters' claims, but must never stake one of its own
                if (inv.AvailableFor(mat.Type, Crafter, prio) < mat.Diff) {
                    // deficit!
                    if (!hasDeficit || Deficit.DeficitType != mat.Type) {
                        NotifyDeficit(mat.Type);
                    }
                    return false;
                }
            }
            if (hasDeficit) {
                Deficit.Clear();
            }
            return true;
        }

        private void CancelHaulingGoal(string why) {
            // showingGoalReachedIcon = false;
            Deficit?.Clear();
            Crafter.TryClearGoal(why);
        }
        public bool RebuildIngredientsReq() {
            foreach (var m in MissingMats) {
                m.IsCancelled = true;
            }
            MissingMats.Clear();
            Deficit?.Clear();
            foreach (var mat in Ingredients) {
                if (mat.Diff < 1) { continue; }
                MissingMats.Add(new MatRequest() {
                    Type = mat.Type,
                    Amount = mat.Diff,
                    IsAmountOptional = true,
                    Requester = Crafter,
                });
            }
            return MissingMats.Count == 0;
        }

        public void UpdateBatchSize(int newSize) {
            if (Demand == null) { return; }
            if (newSize < 1) {
                D.Err("Trying to set negative batch size!");
                newSize = 1;
            }
            var curBatchSize = Demand.BatchSize;
            if (curBatchSize == newSize) { return; }
            Progress = Maths.Clamp((curBatchSize / (float) newSize)
                * Progress, 0f, 0.999f);
            Demand.SetBatchSize(newSize);
            curBatchSize = Demand.BatchSize;
            var cr = Demand.Craftable;
            if (Ingredients == null) {
                Ingredients = IngredientsFor(cr, curBatchSize);
            }
            for (int i = Ingredients.Length - 1; i >= 0; i--) {
                var curIng = Ingredients[i];
                var crIng = cr.Ingredients[i];
                curIng.MaxStackSize = crIng.MaxStackSize * curBatchSize;
                if (curIng.StackSize > curIng.MaxStackSize) {
                    var diff = curIng.StackSize - curIng.MaxStackSize;
                    Ready.WhenGameLate(this, "Eject excess materials", () => {
                        EntityUtils.SpawnRawMaterial(curIng.Type, diff,
                            Crafter.OutPort, 0.25f);
                    });
                    curIng.StackSize = curIng.MaxStackSize;
                }
                Ingredients[i] = curIng;
            }
            RebuildIngredientsReq();
            var g = Crafter.Entity.Goal;
            if (g != null && AIGoal.IsFailed(g.State)) {
                AIGoal.TryRetry(g, G.Ticks, false, false);
            }
            // UpdateUIBlock(true);
            // UpdateExtraInfo();
        }

        public static Mat[] IngredientsFor(Craftable c, int batchSize) {
            var ings = c.Ingredients;
            var ingredients = new Mat[ings.Length];
            for (int i = 0; i < ings.Length; i++) {
                ingredients[i] = new Mat() {
                    Type = ings[i].Type,
                    MaxStackSize = ings[i].StackSize * batchSize
                };
            }
            return ingredients;
        }

        public Color? NextProductColor;

        public Color ProductColor {
            get {
                var craftable = Demand.Craftable;
                if (craftable.ColorGroupId == null) {
                    return Consts.ColorWhite;
                }
                if (Demand.ColorId == null) {
                    var color = Palette.RandomForGroup(craftable.ColorGroupId, Rng.Unseeded);
                    // var color = Rng.UFrom(palette);
                    // D.Warn("Getting random color: {0}", color.Id);
                    if (color == null) {
                        return Consts.ColorWhite;
                    }
                    return color.Color;
                } else {
                    return Palette.ColorById(Demand.ColorId);
                }
            }
        }
        public string IdleTooltip {
            get {
                return $"{T.InfoProdGoalReached}\n{Demand.TypeName}: {Demand.AmountWanted}\n{T.AvailableCount_V1.F(Crafter.S.Sys.Inventory.CountOfProduct(Demand.Product))}";
            }
        }

        public override string ToString() {
            return $"CraftOrder[{Demand.CraftableItemName}]";
        }

        public void ResetIngredients() {
            for (int i = Ingredients.Length - 1; i >= 0; i--) {
                var mat = Ingredients[i];
                mat.StackSize = 0;
                Ingredients[i] = mat;
            }
        }
        public void VerifyIngredients(bool logErrors) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                logErrors = false;
            }
            #endif
            var ing = Demand.Craftable.Ingredients;
            var needsRebuild = false;
            if (ing.Length != Ingredients.Length) {
                needsRebuild = true;
                if (logErrors) {
                    D.ErrSoft("Incorrect ingredients count for crafter {0} recipe {1}: {2} != {3}",
                        Crafter.Entity.DefinitionId, Demand.Craftable.Id, Ingredients.Length, ing.Length);
                }
            } else {
                for (int i = 0; i < ing.Length; i++) {
                    if (ing[i].Type != Ingredients[i].Type) {
                        if (logErrors) {
                            D.ErrSoft("Incorrect ingredient for crafter {0} recipe {1}: {2} != {3}",
                                Crafter.Entity.DefinitionId, Demand.Craftable.Id,
                                ing[i].Type, Ingredients[i].Type);
                        }
                        needsRebuild = true;
                        break;
                    }
                }
            }
            if (needsRebuild) {
                if (logErrors) {
                    D.ErrSoft("Problematic device: {3}. Ingredients expected for craftable: {0}: {1}. Actual: {2}",
                        Demand.Craftable.Id, D.DumpCollection(ing), D.DumpCollection(Ingredients), Crafter.Tile);
                }
                Ingredients = IngredientsFor(Demand.Craftable, Demand.BatchSize);
            }
        }


        public string Tooltip() {
            if (ElectricityDeficitKW > 0f) {
                return Texts.Red($"{T.ElectricityDeficit}: {Units.Electricity(ElectricityDeficitKW)}");
            }
            if (Deficit?.HasDeficit == true && Deficit.DeficitType?.Def != null) {
                return Texts.Red($"{T.AdRejLackingMat}: {Deficit.DeficitType.Def.CodexLink}");
            }
            return null;
        }

        public bool CopyTo(CraftingOrder other) {
            bool copied = false;
            if (other.Demand.BatchSize != Demand.BatchSize) {
                other.UpdateBatchSize(Demand.BatchSize);
            }
            copied |= Demand.CopyTo(other.Demand);
            other.NextProductColor = NextProductColor;
            other.VerifyIngredients(false);
            if (other.SuppressDeficitWarning != SuppressDeficitWarning) {
                other.SuppressDeficitWarning = SuppressDeficitWarning;
                copied = true;
            }
            return copied;
        }

        public bool ShouldStopProducing(GameState s) {
            if (State == CraftingOrderState.Paused) {
                return true;
            }
            if (Progress > 0f) {
                // don't stop mid production
                return false;
            }
            return Demand.HasProducedEnough(s);
        }
        public void ChangeDemandType(CraftingDemandType dt) {
            var demand = Demand;
            var changed = demand.Type != dt;
            demand.Type = dt;
            if (changed) {
                // A Limit amount is a stock level, a Custom amount a count: carried over, a
                // target of 46 in stock + 10 made 56 more (STA-1762). Each starts from its
                // default. Before the reset below: the stock count writes AmountProduced.
                if (dt == CraftingDemandType.Custom) {
                    demand.AmountWanted = CraftingDemand.DefaultAmount;
                } else if (dt == CraftingDemandType.Limit) {
                    demand.SetDefaultLimitAmount();
                }
            }
            switch (dt) {
                case CraftingDemandType.One: {
                        demand.AmountProduced = 0;
                        break;
                    }
                case CraftingDemandType.Limit:
                case CraftingDemandType.Unlimited:
                case CraftingDemandType.Custom: {
                        demand.AmountProduced = demand.AmountProducedThis = 0;
                        break;
                    }
                default: {
                        D.Err("Unhandled demand type: {0}", demand.Type);
                        break;
                    }
            }
            Ping();
        }
        public string StateText(bool includeBatchSize = false) {
            string text;
            switch (State) {
                case CraftingOrderState.ProducedEnough: text = Texts.Green(T.GoalStateCompleted); break;
                case CraftingOrderState.InQueue: text = T.GoalStatePending; break;
                case CraftingOrderState.Paused: text = Texts.Yellow(T.AdjPaused); break;
                default: text = Units.Percentage(Progress); break;
            }
            if (ElectricityDeficitKW > 0f || Deficit?.HasDeficit == true) {
                text = Texts.Red(text);
            }

            if (includeBatchSize && Demand.BatchSize > 1) {
                text = $"{text} {Texts.Grey(Units.XNum(Demand.OutputMultiplier))}";
            }

            return text;
        }

        public void Ping() {
            Changed.ForceNotify();
        }
    }
}
