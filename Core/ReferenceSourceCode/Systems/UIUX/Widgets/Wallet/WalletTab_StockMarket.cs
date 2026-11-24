using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WalletTab_StockMarket {
        public enum FilterMode {
            All,
            Owned,
            CFD,
            Equity,
        }
        private readonly WalletWidget parent;
        private readonly GameState S;
        private Lifetime lifetime;

        private FilterMode filterMode = FilterMode.All;
        private MarketSortMode sortMode = MarketSortMode.Group;
        private string searchStr;
        private MarketItemModifier hoveredItem;
        private WBase currentDetailWidget;
        private MarketItemModifier currentDetailMod;


        public WalletTab_StockMarket(WalletWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public bool Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("Wallet.StockMarket");

            if (!S.Research.IsUnlocked(DefIdsH.ResearchEconomyStockMarket)) {
                var block = UISpawn.Block(lifetime, parent.WItems, 0);
                var lockedW = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContentTextBlock, block.Container, 0);
                lockedW.Load(T.StockMarketResearchRequired);
                return false;
            }


            hoveredItem = null;
            UpdateChartTitle(T.GlobalMarketIndex);
            parent.RenderStockChart(null);
            var market = S.Market;
            int blockIdx = 0;

            // Global index block
            var headerBlock = UISpawn.Block(lifetime, parent.WItems, blockIdx++);
            int idx = 0;

            // Global market index
            var indexW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, headerBlock.Container, idx++);
            indexW.Load(IconId.CStockMarket, T.GlobalMarketIndex, GetMovePercentage());
            UIUtils.AddHover(lifetime, indexW, on => {
                if (on) {
                    hoveredItem = null;
                    UpdateChartTitle(T.GlobalMarketIndex);
                    parent.RenderStockChart(null);
                }
            });

            // Trends
            if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketPrediction)) {
                AddTrendWidget(lifetime, headerBlock.Container, ref idx, market.TrendLong, T.MarketTrendLongTermGlobal);
                AddTrendWidget(lifetime, headerBlock.Container, ref idx, market.TrendShort, T.MarketTrendShortTermGlobal);
            }


            // Items block
            var itemsBlock = UISpawn.Block(lifetime, parent.WItems, blockIdx++);
            int itemIdx = 0;

            var sortedItems = MarketItemModifier.SortedBy(market.Modifiers, sortMode);
            bool addedAny = false;

            string searchQuery = null;
            if (searchStr != null) {
                searchQuery = searchStr.Trim().ToLowerInvariant().Replace(" ", string.Empty);
                if (searchQuery == string.Empty) {
                    searchQuery = null;
                }
            }

            var isBlackMarketUnlocked = S.Research.IsUnlocked(DefIdsH.ResearchEconomyBlackMarket);
            bool addGroupLabels = sortMode == MarketSortMode.Group;
            string lastGroup = null;

            foreach (var item in sortedItems) {
                if (!isBlackMarketUnlocked && item.Value.Item.IsBlackMarket) {
                    continue;
                }
                if (searchQuery != null && !item.Value.Item.SearchName.Contains(searchQuery)) {
                    continue;
                }

                float cb = 0f;
                float avgP = 0f;
                var owned = S.Portfolio?.OwnedPositions(item.Key, out cb, out avgP) ?? 0;

                if (filterMode == FilterMode.Owned) {
                    if (owned == 0) { continue; }
                } else if (filterMode == FilterMode.CFD) {
                    if (item.Value.Item.AssetType != MarketItemType.CFD) { continue; }
                } else if (filterMode == FilterMode.Equity) {
                    if (item.Value.Item.AssetType != MarketItemType.Equity) { continue; }
                }

                if (addGroupLabels) {
                    var gname = item.Value.Item.FirstGroup.NameT;
                    if (lastGroup != gname) {
                        var labelW = UISpawn.Widget<WText>(lifetime,
                            UIPrefabId.WContent2Header, itemsBlock.Container, itemIdx++);
                        labelW.Load(gname);
                        lastGroup = gname;
                    }
                }

                var itemName = item.Value.Item.Name;
                if (owned != 0) {
                    itemName = Texts.WithColor(itemName, Texts.TMPColorYellow);
                }

                WBase w;
                string priceText = Wallet.ToValueString(item.Value.Price);
                var capturedItem = item.Value;
                if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketInsights)) {
                    var itemW = UISpawn.Widget<WButtonIconText2WithLabel>(lifetime,
                        UIPrefabId.WContent2ButtonText3, itemsBlock.Container, itemIdx++);
                    w = itemW;
                    itemW.Button.Load(item.Value.Item.Icon, itemName, priceText, () => {
                        ShowItemDetail(capturedItem, itemW);
                    });
                    itemW.Label.Load(item.Value.PriceChange(1));
                } else {
                    var itemW = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WContent2Button, itemsBlock.Container, itemIdx++);
                    w = itemW;
                    itemW.Load(item.Value.Item.Icon, itemName, priceText, () => {
                        ShowItemDetail(capturedItem, itemW);
                    });
                }

                Action<bool> hoverFun = on => {
                    if (on) {
                        hoveredItem = capturedItem;
                        UpdateChartTitle(
                            $"{Texts.Green(Wallet.ToValueString(hoveredItem.Price, false))} {Texts.Grey("|")} {hoveredItem.Item.Name}");
                        parent.RenderStockChart(capturedItem);
                    }
                };

                if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyStockTrading)) {
                    var tip = UIUtils.AddTipFunc(lifetime, w, () => {
                        int canBuyCount = Mathf.FloorToInt((float)(S.Wallet.Amount / item.Value.Price));
                        string tip = CanBuyTip(canBuyCount);
                        if (owned > 0) {
                            tip = $"{tip} / {T.AvailableCount_V1.F(owned)}";
                        }
                        return tip;
                    });
                    tip.HoverFun = hoverFun;
                } else {
                    UIUtils.AddHover(lifetime, w, hoverFun);
                }

                addedAny = true;
            }

            if (!addedAny) {
                var emptyW = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, itemsBlock.Container, itemIdx++);
                emptyW.Load(IconId.CInfo, T.Empty);
            }

            // Chart (rendered to WRawImage in the widget prefab)
            bool wantsChart = false;
            if (market.History?.Count > 1) {
                wantsChart = true;
                RenderChart();
            }

            S.Sig.MarketDayChanged.AddListener(OnMarketDayChanged);
            lifetime.OnDispose(() => {
                S.Sig.MarketDayChanged.RemoveListener(OnMarketDayChanged);
            });
            return wantsChart;
        }

        private void OnMarketDayChanged() {
            if (UIShowing.Wallet) {
                S.UIQuery.RebuildWallet.Ask();
            }
        }

        private void ShowItemDetail(MarketItemModifier mod, WBase widget, bool forceShow = false) {
            currentDetailMod = mod;
            currentDetailWidget = widget;

            var subPanel = parent.SubPanel;
            subPanel.DisposeContents();
            if (!forceShow && subPanel.IsShowingFor(widget)) {
                subPanel.Close();
                currentDetailMod = null;
                currentDetailWidget = null;
                return;
            }

            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle($"{mod.Item.Name}  {Wallet.ToValueString(mod.Price)}");
            subPanel.ShowFooter(false);

            int blockIdx = 0;

            // --- Block: Trend (if research unlocked) ---
            if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketPrediction)) {
                {
                    var block = UISpawn.Block(l, subPanel.Container, blockIdx++);
                    int idx = 0;
                    var headerW = UISpawn.Widget<WText>(l,
                        UIPrefabId.WContent2Header, block.Container, idx++);
                    headerW.Load(T.MarketTrend);
                    AddTrendWidget(l, block.Container, ref idx, mod.Trend, T.MarketTrendShortTerm);
                }

                // --- Block: Correlated Group Trends ---
                {
                    var block = UISpawn.Block(l, subPanel.Container, blockIdx++);
                    int idx = 0;
                    var headerW = UISpawn.Widget<WText>(l,
                        UIPrefabId.WContent2Header, block.Container, idx++);
                    headerW.Load(T.MarketGroupTrends);
                    AddTrendWidget(l, block.Container, ref idx, S.Market.TrendLong, T.MarketTrendLongTermGlobal);
                    AddTrendWidget(l, block.Container, ref idx, S.Market.TrendShort, T.MarketTrendShortTermGlobal);
                    for (int i = mod.Item.GroupsH.Length - 1; i >= 0; i--) {
                        int g = mod.Item.GroupsH[i];
                        if (S.Market.GroupTrends.TryGetValue(g, out var gt)) {
                            if (gt.GroupNameT == null) {
                                gt.GroupNameT = $"market.group.name.{mod.Item.GroupsH[i]}".T();
                            }
                            AddTrendWidget(l, block.Container, ref idx, gt, gt.GroupNameT);
                        }
                    }
                }
            }

            // --- Block: Details ---
            {
                var block = UISpawn.Block(l, subPanel.Container, blockIdx++);
                int idx = 0;
                var headerW = UISpawn.Widget<WText>(l,
                    UIPrefabId.WContent2Header, block.Container, idx++);
                headerW.Load(T.Details);

                if (mod.Item.IsBlackMarket) {
                    var blackMktW = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    blackMktW.Load(IconId.CSkull, T.ResearchEconomyBlackmarket);
                }

                var assetW = UISpawn.Widget<WIconText2>(l,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                assetW.Load(IconId.CInfo, T.MarketAssetClass, mod.Item.AssetClassT);
                UIUtils.AddTip(l, assetW, mod.Item.AssetClassDescT);

                if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketInsights)) {
                    var volW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    volW.Load(IconId.CExplosion, T.MarketVolatility,
                        mod.Item.Volatility.ToString("0.0000"));

                    UIUtils.AddTip(l, volW, T.MarketVolatilityInfo);
                    var highW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    highW.Load(IconId.CSell, T.MarketHigh, Wallet.ToValueString(mod.PriceHigh));
                    UIUtils.AddTip(l, highW, T.MarketHighInfo);

                    var lowW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    lowW.Load(IconId.CBuy, T.MarketLow, Wallet.ToValueString(mod.PriceLow));
                    UIUtils.AddTip(l, lowW, T.MarketLowInfo);

                    var dayW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    dayW.Load(IconId.CTime, T.Day, mod.PriceChange(1));

                    var weekW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    weekW.Load(IconId.CTime, T.Week, mod.PriceChange(7));

                    var monthW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    monthW.Load(IconId.CTime, T.Month, mod.PriceChange(30));

                    var allTimeW = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    allTimeW.Load(IconId.CTime, T.AllTime, mod.PriceChange(-1));

                }
            }

            // --- Block: Portfolio (Owned) ---
            AddPortfolioDetail(l, subPanel.Container, ref blockIdx, mod);

            // --- Block: Trade + Footer Buy/Sell ---
            if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyStockTrading)) {
                subPanel.ShowFooter(true);
                int footerIdx = 0;
                var buyBtn = UISpawn.Widget<WButtonIconText>(l,
                    UIPrefabId.WPanelButtonIcon, subPanel.Footer.Container, footerIdx++);
                buyBtn.Load(IconId.CBuy, T.Buy, () => ShowBuyQuickChoice(mod, buyBtn));

                var ownedCount = S.Portfolio?.OwnedPositions(mod.IdH) ?? 0;
                if (ownedCount > 0) {
                    var sellBtn = UISpawn.Widget<WButtonIconText>(l,
                        UIPrefabId.WPanelButtonIcon, subPanel.Footer.Container, footerIdx++);
                    sellBtn.Load(IconId.CSell, T.Sell, () => ShowSellQuickChoice(mod, sellBtn));
                }
            }

            subPanel.Show(widget, !forceShow);
        }

        private void AddPortfolioDetail(Lifetime l, WGroup containerGroup, ref int blockIdx,
                MarketItemModifier mod) {
            if (S.Portfolio == null) { return; }
            var owned = S.Portfolio.OwnedPositions(mod.IdH, out var cb, out var avgP);
            if (owned == 0) { return; }

            var block = UISpawn.Block(l, containerGroup, blockIdx++);
            int idx = 0;

            var headerW = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            headerW.Load(T.OwnedCount);

            var sharesW = UISpawn.Widget<WIconText2>(l,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            sharesW.Load(IconId.CBriefcase, T.MarketYourShares, Units.XNum(owned));

            var costBasisW = UISpawn.Widget<WIconText2>(l,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            costBasisW.Load(IconId.CBriefcase, T.MarketCostBasis, Wallet.ToValueString(cb));
            UIUtils.AddTip(l, costBasisW, T.MarketCostBasisInfo);

            var avgPriceW = UISpawn.Widget<WIconText2>(l,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            avgPriceW.Load(IconId.CBriefcase, T.MarketAvgPrice, Wallet.ToValueString(avgP));
            UIUtils.AddTip(l, avgPriceW, T.MarketAvgPriceInfo);
        }

        private void ShowBuyQuickChoice(MarketItemModifier mod, WBase widget) {
            const int maxTradeAmt = 1_000_000;
            var totalCash = S.Wallet.Amount;
            var maxAmtDbl = totalCash / mod.Price;
            if (maxAmtDbl > maxTradeAmt) { maxAmtDbl = maxTradeAmt; }
            int maxAmt = (int) maxAmtDbl;

            if (maxAmt == 0) {
                A.S.Sig.ShowErrorMessage.Send(T.CannotAfford);
                return;
            }

            string inputStr = "";
            var qc = QuickChoice.Create($"{T.Buy}: {mod.Item.Name}");
            qc.Widget = widget.Rect;

            qc.AddButton($"1 ({Wallet.ToValueString(mod.Price)})", () => ExecuteBuy(mod, 1));
            if (maxAmt >= 10) {
                qc.AddButton($"10 ({Wallet.ToValueString(mod.Price * 10)})",
                    () => ExecuteBuy(mod, 10));
            }
            int half = maxAmt / 2;
            if (half > 1) {
                qc.AddButton($"{T.TexHalf} - {half}x ({Wallet.ToValueString(mod.Price * half)})",
                    () => ExecuteBuy(mod, half));
            }
            if (maxAmt > 1) {
                qc.AddButton($"{T.All} - {maxAmt}x ({Wallet.ToValueString(mod.Price * maxAmt)})",
                    () => ExecuteBuy(mod, maxAmt));
            }

            qc.AddInput(T.EnterAmount, new QuickChoice.Input {
                AutoFocus = true,
                Value = "",
                OnChange = v => inputStr = v,
                OnSubmit = v => {
                    if (int.TryParse(v, out int n) && n > 0) {
                        if (ExecuteBuy(mod, n)) {
                            S.UIQuery.CancelQuickChoice.Ask();
                        }
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
            });
            qc.AddButton(T.Confirm, new QuickChoice.Button {
                OnClickValidate = () => {
                    if (int.TryParse(inputStr, out int n) && n > 0) {
                        return ExecuteBuy(mod, n);
                    }
                    UISounds.PlayActionDenied();
                    return false;
                },
                AutoClose = true,
            });

            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void ShowSellQuickChoice(MarketItemModifier mod, WBase widget) {
            var owned = S.Portfolio?.OwnedPositions(mod.IdH) ?? 0;
            if (owned <= 0) { return; }

            string inputStr = "";
            var qc = QuickChoice.Create($"{T.Sell}: {mod.Item.Name}");
            qc.Widget = widget.Rect;

            qc.AddButton($"1 ({Wallet.ToValueString(mod.Price)})", () => ExecuteSell(mod, 1));
            if (owned >= 10) {
                qc.AddButton($"10 ({Wallet.ToValueString(mod.Price * 10)})", () => ExecuteSell(mod, 10));
            }
            int half = owned / 2;
            if (half > 1) {
                qc.AddButton($"50% - {half}x ({Wallet.ToValueString(mod.Price * half)})", () => ExecuteSell(mod, half));
            }
            qc.AddButton($"{T.All} - {owned}x ({Wallet.ToValueString(mod.Price * owned)})", () => ExecuteSell(mod, owned));

            qc.AddInput(T.EnterAmount, new QuickChoice.Input {
                AutoFocus = true,
                Value = "",
                OnChange = v => inputStr = v,
                OnSubmit = v => {
                    if (int.TryParse(v, out int n) && n > 0) {
                        if (ExecuteSell(mod, n)) {
                            S.UIQuery.CancelQuickChoice.Ask();
                        }
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
            });
            qc.AddButton(T.Confirm, new QuickChoice.Button {
                OnClickValidate = () => {
                    if (int.TryParse(inputStr, out int n) && n > 0) {
                        return ExecuteSell(mod, n);
                    }
                    UISounds.PlayActionDenied();
                    return false;
                },
                AutoClose = true,
            });

            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private bool ExecuteBuy(MarketItemModifier mod, int amount) {
            if (amount <= 0) { UISounds.PlayActionDenied(); return false; }
            amount = Maths.Min(amount, 1_000_000);
            var price = amount * mod.Price;
            if (!S.Wallet.CanAfford(price)) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.TradeNotEnoughCash}");
                return false;
            }
            var pf = S.Portfolio;
            if (pf == null) {
                pf = new MarketPortfolio();
                S.Portfolio = pf;
            }
            pf.Buy(mod, amount);
            UISounds.PlayDigitalConfirm();
            ReshowDetail();
            return true;
        }

        private bool ExecuteSell(MarketItemModifier mod, int amount) {
            if (amount <= 0) { UISounds.PlayActionDenied(); return false; }
            var owned = S.Portfolio?.OwnedPositions(mod.IdH) ?? 0;
            if (owned <= 0) { UISounds.PlayActionDenied(); return false; }
            amount = Maths.Min(amount, owned);
            S.Portfolio.Sell(mod, amount);
            UISounds.PlayDigitalConfirm();
            ReshowDetail();
            return true;
        }

        private void ReshowDetail() {
            if (currentDetailMod != null && currentDetailWidget != null) {
                ShowItemDetail(currentDetailMod, currentDetailWidget, forceShow: true);
            }
        }

        private void AddTrendWidget(Lifetime life, RectTransform container, ref int idx,
                MarketTrend tr, string name) {
            if (tr == null) { return; }
            var trendW = UISpawn.Widget<WIconText2Progress>(life,
                UIPrefabId.WContent2Progress, container, idx++);
            trendW.Load(IconId.CInfo, name, (float)tr.CurrentDays / tr.TotalDays);
            trendW.Item.SetText2(tr.NameT);
            UIUtils.AddTipFunc(life, trendW, () => $"{T.Day}: {tr.CurrentDays}");
        }

        private string GetMovePercentage() {
            var market = S.Market;
            if (market.History == null) { return null; }
            if (market.History.TryGetValue(market.Day, out var idxToday)) {
                if (market.History.TryGetValue(market.Day - 1, out var idxYesterday)) {
                    var pct = (idxToday - idxYesterday) / idxYesterday;
                    return Units.PriceChange(pct);
                }
            }
            return null;
        }

        private void UpdateChartTitle(string title) {
            parent.UpdateChartTitle(title);
        }

        private void RenderChart() {
            parent.RenderStockChart(hoveredItem);
        }

        private static string CanBuyTip(int canBuyCount) {
            if (canBuyCount > 0) {
                return Texts.WithColor(T.CanBuyCount_V1.F(canBuyCount), Texts.TMPColorGreen);
            } else {
                return Texts.WithColor(T.CannotAfford, Texts.TMPColorRed);
            }
        }

        public bool LoadFooter(WGroup footer) {
            int idx = 0;
            var sortW = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, footer.Container, idx++);
            sortW.Load(SortModeOptions, (int)sortMode, v => {
                sortMode = (MarketSortMode)v;
                S.UIQuery.RebuildWallet.Ask();
            });
            UIUtils.AddTip(lifetime, sortW, T.Sort);

            var filterW = UISpawn.Widget<WDropdownWithIcon>(lifetime,
                UIPrefabId.WContent2Dropdown, footer.Container, idx++);
            filterW.Icon.Load(IconId.WFunnel);
            filterW.Dropdown.Load(FilterModeOptions, (int)filterMode, v => {
                filterMode = (FilterMode)v;
                S.UIQuery.RebuildWallet.Ask();
            });
            UIUtils.AddTip(lifetime, filterW, T.FilterMode);
            return true;
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }

        private static readonly List<string> ownershipOptions = new();
        private static readonly List<string> sortModeOptions = new();
        public static List<string> SortModeOptions {
            get {
                if (sortModeOptions.Count == 0) {
                    foreach (MarketSortMode mode in Enum.GetValues(typeof(MarketSortMode))) {
                        switch (mode) {
                            case MarketSortMode.Group: {
                                sortModeOptions.Add(T.SortGroup); break;
                            }
                            case MarketSortMode.Alphabetic: {
                                sortModeOptions.Add(T.SortName); break;
                            }
                            case MarketSortMode.ValueAsc: {
                                sortModeOptions.Add($"{T.SortValue} {GlyphMap.ArrowUp}");
                                break;
                            }
                            case MarketSortMode.ValueDesc: {
                                sortModeOptions.Add($"{T.SortValue} {GlyphMap.ArrowDown}");
                                break;
                            }
                        }
                    }
                }
                return sortModeOptions;
            }
        }
        public static List<string> FilterModeOptions {
            get {
                if (ownershipOptions.Count == 0) {
                    foreach (FilterMode mode in Enum.GetValues(typeof(FilterMode))) {
                        switch (mode) {
                            case FilterMode.All: {
                                ownershipOptions.Add(T.StockOwnershipAll); break;
                            }
                            case FilterMode.Owned: {
                                ownershipOptions.Add(T.StockOwnershipOwned); break;
                            }
                            case FilterMode.CFD: {
                                ownershipOptions.Add(T.MarketAssetClassCFD); break;
                            }
                            case FilterMode.Equity: {
                                ownershipOptions.Add(T.MarketAssetClassEquity); break;
                            }
                        }
                    }
                }
                return ownershipOptions;
            }
        }

    }
}
