using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Trade {
    public static class TradeItemLister {
        public static void BuildList(Lifetime life, TradeUI trade, WGroup container,
                Dictionary<TradeableItem, int> target,
                List<TradeableItem> items, bool isBuying, bool addGroupHeaders,
                TradeSortMode sortMode) {
            int idx = 0;
            var S = A.S;
            string lastGroupName = null;
            var trader = trade.Trader;
            Dictionary<int, TradeItemView> groupedItems = new();
            List<TradeItemView> orderedViews = new();
            foreach (var val in items) {
                var id = val.Def.IdH;
                var isEnemy = val.OurObjData?.Mind?.Faction?.IsPlayer == false;
                if (isEnemy) {
                    id = Hashes.Quick(id, Primes.Prime001);
                }
                // Split a def into its own list per provenance bucket, so a shelf of
                // 30 space suits doesn't hide that 10 of them came off corpses.
                // Untracked (weapons, body parts, trader stock) never splits.
                var origin = val.Origin;
                if (origin != ItemOriginBucket.Untracked) {
                    id = Hashes.Quick(id, (int) origin);
                }
                // Every being is an individual with its own price, never a stack
                if (val.Being != null) {
                    id = Hashes.Quick(id, val.BeingSeed != 0 ? val.BeingSeed : (int) val.Being.Id);
                }
                if (groupedItems.TryGetValue(id, out var view)) {
                    view.AddItem(val);
                } else {
                    view = new TradeItemView {
                        IsEnemy = isEnemy,
                        Origin = origin,
                        ForceGroup = !isBuying && IsMindHolder(val),
                    };
                    view.AddItem(val);
                    groupedItems[id] = view;
                    orderedViews.Add(view);
                }
            }

            switch (sortMode) {
                case TradeSortMode.CountsDesc:
                    orderedViews.Sort((a, b) => b.TotalCount.CompareTo(a.TotalCount));
                    break;
                case TradeSortMode.CountsAsc:
                    orderedViews.Sort((a, b) => a.TotalCount.CompareTo(b.TotalCount));
                    break;
                case TradeSortMode.ValueDesc:
                    orderedViews.Sort((a, b) => b.TotalValue(isBuying).CompareTo(a.TotalValue(isBuying)));
                    break;
                case TradeSortMode.ValueAsc:
                    orderedViews.Sort((a, b) => a.TotalValue(isBuying).CompareTo(b.TotalValue(isBuying)));
                    break;
            }

            var isMarketInsightsUnlocked = S.Research.IsUnlocked(
                DefIdsH.ResearchEconomyMarketInsights);
            foreach (var gItem in orderedViews) {
                if (addGroupHeaders) {
                    var gName = gItem.First.GroupName;
                    if (lastGroupName != gName) {
                        lastGroupName = gName;
                        string label;
                        if (isMarketInsightsUnlocked) {
                            var gp = trader.GroupPriceFor(gItem.First) - 1f;
                            string gPrice;
                            var isGoodPrice = isBuying ? gp < 0f : gp >= 0f;
                            if (isGoodPrice) {
                                gPrice = Texts.Green(Units.PercentageSigned(gp));
                            } else {
                                gPrice = Texts.Red(Units.PercentageSigned(gp));
                            }
                            label = $"{gName} ({gPrice})";
                        } else {
                            label = gName;
                        }
                        var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header,
                            container.Container, idx++);
                        header.Load(label);
                    }
                }
                var nameSuffix = gItem.IsEnemy ? Texts.GreyParenthesesSmaller(Texts.Red(T.NounEnemy)) : null;
                gItem.SpawnUI(life, trade, container, target, isBuying, isMarketInsightsUnlocked, idx++, nameSuffix);
            }
            var w = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, container.Container, idx++);
            w.Load(Texts.Italic(T.NounMultipleItems_V1.F(items.Count)), () => {
                var qc = QuickChoice.Create();
                var deselectName = trade.HasSearchStr ? T.DeselectFiltered : T.DeselectAll;
                var selectName = trade.HasSearchStr ? T.SelectFiltered : T.SelectAll;
                qc.AddButton(selectName, () => {
                    foreach (var item in items) {
                        item.QuantitySelected = item.Quantity;
                        target[item] = item.Quantity;
                    }
                    trade.ReloadPanel();
                });
                qc.AddButton(deselectName, () => {
                    foreach (var item in items) {
                        item.QuantitySelected = 0;
                        if (target.TryGetValue(item, out var qty)) {
                            target[item] = 0;
                        }
                    }
                    trade.ReloadPanel();
                });
                A.S.UIQuery.ShowQuickChoice.Ask(qc);
            });
        }

        private static bool IsMindHolder(TradeableItem val) {
            return val.IsBodyPart
                && BodyPartDef.TryForDef(val.Def.IdH, out var bp)
                && bp.HoldsMind;
        }

        private static string GetPriceDiffStr(bool isBuying, Data.GameState S, TradeableItem val) {
            string sideText = string.Empty;
            var priceOf1 = val.FinalPriceOfOne(isBuying);
            var priceStr = Wallet.ToValueString(priceOf1);
            var isMarketInsightsUnlocked = S.Research.IsUnlocked(
                DefIdsH.ResearchEconomyMarketInsights);
            if (isMarketInsightsUnlocked) {
                // we will compare the price of the item to the market price and add a diff
                var marketPrice = S.Market.Adjusted(val.MarketItem).Value;
                // > 0 = more expensive than market price
                float diff = (float)(priceOf1 / marketPrice) - 1f;
                if (!Maths.Approximately(diff, 0f)) {
                    var isGoodDiff = isBuying ? diff <= 0f : diff >= 0f;
                    if (isGoodDiff) {
                        sideText = Texts.Green(Units.PercentageSigned(diff));
                    } else {
                        sideText = Texts.Red(Units.PercentageSigned(diff));
                    }
                }
                if (isBuying && val.Heat > 1f) {
                    sideText = $"{Texts.Red(GlyphMap.ArrowUp)}{sideText}";
                } else if (!isBuying && val.Heat < 1f) {
                    sideText = $"{Texts.Red(GlyphMap.ArrowDown)}{sideText}";
                }
            }

            return sideText;
        }

    }
}
