using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Crafting {
    public sealed class MultiCrafterUI : IWithEntity {

        [ExecuteOnReload]
        private static void Cleanup() {
            suggestedCrawlerAlerts = false;
        }

        private static readonly Dictionary<int, string> barCache = new();
        private static readonly List<string> demandTypeOpts = new();
        private static string demandTip;
        private readonly MultiCrafterComp crafter;
        private readonly Tile tile;
        private readonly ElectricNodeComp elNode;
        private readonly GameState S;
        private readonly CrafterConfig Settings;
        private CraftingOrder selectedOrder;
        private List<CraftingOrder> orders => crafter.Orders;
        private List<Craftable> allCraftables;
        private Def lastProcDef;
        private int lastPCT;
        private ColorChoice colChoice;
        private bool showingUnconfiguredIcon;
        public bool IsShowingUnconfigured => showingUnconfiguredIcon;
        private bool showingGoalReachedIcon;
        private Def showingGoalReachedProduct;
        private int showingGoalReachedProductAmt;
        private bool isShowingIdle;
        public bool IsShowingIdle => isShowingIdle;
        private static bool suggestedCrawlerAlerts;

        public MultiCrafterUI(MultiCrafterComp crafter) {
            this.crafter = crafter;
            tile = crafter.Tile;
            elNode = crafter.Tile.ENode;
            Settings = crafter.Settings;
            S = crafter.S;
        }

        public Entity Entity => crafter.Entity;
        public string GetName() {
            return "MultiCrafterUI";
        }


        public string MainTooltip() {
            if (Arrays.IsEmpty(orders)) {
                return null;
            }
            if (orders?.Count > 1)  {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                var oc = orders.Count;
                for (int i = 0; i < oc; i++) {
                    CraftingOrder order = orders[i];
                    if (i > 0) {
                        sb.Append(", ");
                    }
                    sb.Append(order.Demand.CraftableItemNameTip);
                }
                return sb.ToString();
            }
            return null;
        }
        public void ShowProductionGoalReached() {
            if (orders.Count > 1) {
                if (!showingGoalReachedIcon || showingGoalReachedProduct != null || showingGoalReachedProductAmt != 0) {
                    HideIcon();
                    showingGoalReachedIcon = true;
                    showingGoalReachedProductAmt = 0;
                    showingGoalReachedProduct = null;
                    string prodName = T.NounMultipleItems_V1.F(orders.Count);
                    crafter.ShowInfoIcon(IconId.CCheck, $"{T.InfoProdGoalReached}: {Texts.Accented(prodName)}");
                }
                return;
            }
            var demand = orders[0].Demand;
            if (!showingGoalReachedIcon || showingGoalReachedProductAmt != demand.AmountWanted
                    || showingGoalReachedProduct != demand.Product) {
                HideIcon();
                showingGoalReachedIcon = true;
                showingGoalReachedProductAmt = demand.AmountWanted;
                showingGoalReachedProduct = demand.Product;
                var prodName = demand.Product.CodexLink;
                if (showingGoalReachedProductAmt > 1 && demand.Type != CraftingDemandType.One) {
                    prodName = $"{prodName} {Units.XNum(showingGoalReachedProductAmt)}";
                }
                crafter.ShowInfoIcon(IconId.CCheck, $"{T.InfoProdGoalReached}: {prodName}");
            }
        }

        public void StopAll() {
            for (int i = orders.Count - 1; i >= 0; i--) {
                CraftingOrder order = orders[i];
                S.Query.RemoveCraftingOrder.Ask(order);
            }
            crafter.CurrentOrder = null;
            CancelHaulingGoal(T.Stop);
            HideIcon();
            crafter.UpdateExtraInfo();
            UpdateProgressBar();
            MarkUnconfigured();
        }


        public void CheckCrawlerAlerts(Craftable c) {
            if (suggestedCrawlerAlerts || c == null) { return; }
            if (c.ProductDef.IdH != DefIdsH.ObjEggsEgg01
                    && c.ProductDef.IdH != DefIdsH.BeingsCrawler01) {
                return;
            }
            suggestedCrawlerAlerts = true;
            var on = S.Prefs.GetBool(Pref.CrawlerCombatAlert, PrefH.CrawlerCombatAlert, true);
            if (!on) { return; }
            WPopup.Create(false, IconId.CPreviewCrawler01, T.CrawlerCombatAlert,
                $"{T.CrawlerCombatAlertDesc}{GlyphMap.BRHR}{Texts.Accented(T.MenuSettings)} {GlyphMap.ArrowRight} {Texts.Accented(T.MenuSettingsCurrent_Game)} {GlyphMap.ArrowRight} {Texts.Accented(T.CrawlerCombatAlert)}: {Texts.Green(T.On)}")
                .WithAction($"{T.Change} ({Texts.Green(T.On)} {GlyphMap.ArrowRight} {Texts.Red(T.Off)})", () => {
                    S.Prefs.SetBool(Pref.CrawlerCombatAlert, false);
                })
                .WithAction2($"{T.VerbKeep} ({Texts.Green(T.On)})", () => {
                    S.Prefs.UnsetBool(Pref.CrawlerCombatAlert);
                })
                .Spawn(center: true);
        }

        public CraftingOrder StartProducing(Craftable craftable) {
            if (craftable == null) { return null; }
            if (craftable.ProductDef == null) {
                D.Err("Cannot switch to crafting {0}. Product def is not available!",
                    craftable.ProductDefId);
                WPopup.Create(true, IconId.CWarning, "MISCONFIGURED CRAFTABLE", $"Cannot craft <b>{craftable.ProductDefId}</b> - Definition does not exist. It could be a problem with a mod.").Spawn();
                return null;
            }
            CheckCrawlerAlerts(craftable);
            MarkConfigured();
            var demand = CraftingDemand.CreateFor(craftable);
            var order = S.Query.CreateCraftingOrder.Ask(crafter, demand);
            if (S.Query.IsExclusivelySelected.Ask(crafter.Tile)) {
                selectedOrder = order;
            }
            order.NextProductColor = order.ProductColor;
            crafter.UpdateExtraInfo();
            return order;
        }

        public void MarkConfigured() {
            if (showingUnconfiguredIcon) {
                showingUnconfiguredIcon = false;
                S.Sig.UnconfiguredChanged.Send(crafter, false);
                crafter.HideInfoIcon();
            }
        }

        public List<Craftable> GetCraftableItems() {
            if (allCraftables == null) {
                allCraftables = new();
                Craftable.ForCrafter(Settings.CompatibleTypes, allCraftables);
            }
            return allCraftables;
        }

        public bool HasUnlockedCraftable() {
            var craftables = GetCraftableItems();
            for (int i = 0; i < craftables.Count; i++) {
                // A mod can leave a craftable without its product def (see StartProducing)
                var product = craftables[i].ProductDef;
                if (product != null && S.Research.IsDefUnlocked(product)) {
                    return true;
                }
            }
            return false;
        }

        public static string CraftableName(GameState s, Craftable c, Def def,
                out string src, out MatType mat) {
            mat = def.MatType;
            string n = def.NameT;
            var cnt = c.Ingredients.Length;
            var remainingMats = Caches.MatCounts;
            s.Sys.Inventory.LoadRemainingMaterials(remainingMats);
            var list = Caches.StringList;
            list.Clear();
            bool canProduce = true;
            for (int i = 0; i < cnt; i++) {
                var ing = c.Ingredients[i];
                if (!remainingMats.TryGetValue(ing.Type, out var amt)) {
                    amt = 0;
                }
                var wantedCnt = ing.MaxStackSize;
                var ingName = ing.Type.NameT;
                if (wantedCnt > 1) {
                    ingName = $"{ingName} x{wantedCnt}";
                }
                if (amt < wantedCnt) {
                    ingName = Texts.Yellow(Texts.Strike(ingName));
                    canProduce = false;
                }
                list.Add(ingName);
            }
            if (canProduce) {
                // n = Texts.Green(n);
            } else {
                n = Texts.Red(n);
            }
            list.Sort();
            src = string.Join(", ", list);
            src = Texts.Grey(src);
            if (c.OutputMultiplier > 1) {
                // version with energy cost in brackets:
                return $"{n} {Texts.Accented(Units.XNum(c.OutputMultiplier))} {EnergyCostText(s, c)}";
                // return $"{n} {Texts.Accented(Units.XNum(c.OutputMultiplier))}";
            }
            // version with energy cost in brackets:
            return $"{n} {EnergyCostText(s, c)}";
            // return n;
        }
        private static string EnergyCostText(GameState s, Craftable c) {
            var gridOut = s.Situation.GridMaxOutput;
            var gridUse = s.Situation.GridConsumption;
            var gridSpare = Mathf.Clamp(gridOut - gridUse, 0, gridOut);
            var cost = c.EnergyCost;
            string color;
            if (cost > gridOut) {
                color = Texts.TMPColorRed;
            } else if (cost > gridSpare) {
                color = Texts.TMPColorYellow;
            } else {
                color = Texts.TMPColorGreen;
            }
            var txt = Texts.WithColor(Units.Electricity(cost), color);
            return Texts.GreyParentheses(txt);
        }
        public string ShowProductionChoiceTooltipFor(Craftable c, Def item) {
            if (c == null || item == null) {
                D.Err("Craftable or item is null! {0} | {1}", c, item);
                return null;
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendFormat("{0}", T.Ingredients);
            var ingrs = CraftingOrder.IngredientsFor(c, 1);
            foreach (var ingr in ingrs) {
                var def = ingr.Type?.Def;
                if (def == null) {
                    D.Err("Ingredient type or type.def is null! {0} | {1}", ingr,
                        ingr.Type);
                    continue;
                }
                var owned = T.AvailableCount_V1.F(S.Sys.Inventory.CountColored(def, 10 * ingr.MaxStackSize));
                sb.AppendFormat("\n - <b>{0}</b> [{1}] {2}",
                    def.CodexLink, Units.XNum(ingr.MaxStackSize), Texts.Grey(owned));
            }

            sb.AppendFormat(GlyphMap.BRHR);
            sb.AppendFormat("{0}: <b>{1}@{2}</b>",
                T.ProductionCost,
                Units.SecondsToShortTime(c.ProductionTimeHours * (60 * 60)),
                Units.Electricity(c.EnergyCost));

            sb.AppendFormat("{0}{1}:", GlyphMap.BRHR, T.ProducedItem);
            var amt = c.OutputMultiplier;
            var amtTxt = Units.XNum(amt);
            amtTxt = Texts.Accented(amtTxt);
            var owned2 = T.AvailableCount_V1.F(S.Sys.Inventory.CountColored(item, 10));
            sb.AppendFormat("\n - <b>{0}</b> {1} {2}",
                item.CodexLink, amtTxt, Texts.Grey(owned2));
            return sb.ToString();
        }

        public void SetAutoCraftingAmountFor(CraftingDemand item) {
            item.SetDefaultLimitAmount();
        }

        public void StopProducing(CraftingOrder order) {
            if (!S.Query.RemoveCraftingOrder.Ask(order)) {
                UISounds.PlayActionDenied();
            }
            colChoice = null;
            order.Deficit?.Clear();
            if (order == crafter.CurrentOrder) {
                if (crafter.Orders.Count > 1) {
                    crafter.CycleNextOrder("Stopping production");
                    if (order == crafter.CurrentOrder) {
                        crafter.CurrentOrder = null;
                    }
                } else {
                    crafter.CurrentOrder = null;
                }
                CancelHaulingGoal("Stopped production");
                crafter.CheckOperator(false);
            }
            HideIcon();
            HideIdleNotification();
            UpdateProgressBar();
            crafter.UpdateExtraInfo();
            if (orders.Count == 0) {
                MarkUnconfigured();
            }
        }
        public void CancelHaulingGoal(string why) {
            showingGoalReachedIcon = false;
            crafter.TryClearGoal(why);
        }

        public void MarkUnconfigured() {
            if (Settings.IsIgnoreUnconfiguredOn) { return; }
            if (!UnconfiguredWarning.IsApplicable(tile)) {
                MarkConfigured();
                return;
            }
            if (!showingUnconfiguredIcon) {
                // A crafter salvaged before any of its recipes is researched has nothing to order yet
                if (!HasUnlockedCraftable()) { return; }
                HideIcon();
                showingUnconfiguredIcon = true;
                crafter.ShowInfoIcon(IconId.CNotConfigured, T.InfoDeviceUnconfigured);
                S.Sig.UnconfiguredChanged.Send(crafter, true);
            }
        }

        public void HideIcon() {
            crafter.HideInfoIcon();
            showingGoalReachedIcon = false;
            // Can't do this here, it messes things up
            //HideIdleNotification();
            //MarkConfigured();
        }

        private static string DemandTipFor(CraftingDemandType t) {
            switch (t) {
                case CraftingDemandType.One: return T.DemandTypeOneTip;
                case CraftingDemandType.Custom: return T.DemandTypeCustomTip;
                case CraftingDemandType.Limit: return T.DemandTypeLimitTip;
                case CraftingDemandType.Unlimited: return T.DemandTypeUnlimitedTip;
            }
            D.Err("No cache for demand type tip: {0}", t);
            return $"demand.type.{t}.tip".T();
        }


        public static List<string> DemandTypeOpts {
            get {
                if (demandTypeOpts.Count == 0) {
                    var sb = Caches.StringBuilder2;
                    sb.Clear();
                    foreach (CraftingDemandType t in Enum.GetValues(typeof(CraftingDemandType))) {
                        var name = CraftingDemand.NameFor(t);
                        demandTypeOpts.Add(name);
                        sb.AppendFormat("<b>{0}</b>: {1}\n", name, DemandTipFor(t));
                    }
                    demandTip = sb.ToString();
                }
                return demandTypeOpts;
            }
        }


        public void UpdateProgressBar() {
            var co = crafter.CurrentOrder;
            if (co == null) {
                crafter.HideInfoBar();
                return;
            }
            var demand = co.Demand;
            var progress = co.Progress;
            if (progress > 0 || !demand.HasProducedEnough(S)) {
                var def = demand.Product;
                var pct = Mathf.FloorToInt(progress * 100);
                if (def == lastProcDef && lastPCT == pct) {
                    return;
                } else {
                    if (lastProcDef != def) {
                        lastProcDef = def;
                    }
                    lastPCT = pct;
                    var keyHash = Hashes.Quick(Settings.ProgressBarKeyHash, pct);
                    // keyHash = Hashes.Quick(keyHash, demand.BatchSize);
                    keyHash = Hashes.Quick(keyHash, demand.OutputMultiplier);
                    keyHash = Hashes.Quick(keyHash, def.IdH);

                    if (!barCache.TryGetValue(keyHash, out var barText)) {
                        barText = Settings.ProgressBarKey.T(
                            $"{demand.CraftableItemNameTip}: {Texts.Green(Units.Percentage(progress))}");
                        barCache.Add(keyHash, barText);
                    }
                    crafter.ShowInfoBar(Consts.ColorFuelBar, progress, barText);
                }
            } else {
                crafter.HideInfoBar();
            }
        }


        public void OnChangeColor(CraftingOrder order, ColorDef def) {
            order.Demand.ColorId = def?.Id;
            order.NextProductColor = order.ProductColor;
            order.Ping();
            if (order == selectedOrder) {
                crafter.UpdateExtraInfo();
            }
        }
        public void ShowIdleNotification() {
            if (isShowingIdle) { return; }
            if (!Settings.IsIdleWarnOn) { return; }
            var list = Caches.StringList2;
            list.Clear();
            for (int i = 0; i < crafter.Orders.Count; i++) {
                list.Add(crafter.Orders[i].Demand.CraftableItemName);
            }
            crafter.IdleProducts = string.Join(", ", list);
            isShowingIdle = true;
            ClearDeficits();
            S.Sig.IdleChanged.Send(crafter, true);
        }

        public void ClearDeficits() {
            var orders = crafter.Orders;
            if (!Arrays.IsEmpty(orders)) {
                foreach (var order in orders) {
                    order.Deficit?.Clear();
                }
            }
        }

        public void HideIdleNotification() {
            if (!isShowingIdle) { return; }
            crafter.IdleProducts = null;
            isShowingIdle = false;
            S.Sig.IdleChanged.Send(crafter, false);
        }
        public void MovePriority(int direction) {
            Settings.TaskPriority.Set(
                UIUtils.AdjustPriority0To5(Settings.TaskPriority, direction));
            crafter.Operatable?.SetPriority(Settings.TaskPriority);
            if (crafter.TryGetGoal(out var g)) {
                g.WithPriority(Settings.TaskPriority);
            }
        }

        public override string ToString() {
            return $"MultiCrafterUI[{crafter}|{crafter?.Tile}]";
        }
    }
}
