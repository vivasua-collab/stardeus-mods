using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Trade {
    public sealed class TradeItemView {
        public TradeableItem First;
        public List<TradeableItem> List;
        public bool IsEnemy;
        public bool ForceGroup;
        public ItemOriginBucket Origin;
        // Scratch buffer for the group header's glyph prefix - cleared before and
        // after use so it never holds an ObjData reference between panel builds.
        private static readonly List<ObjData> originScratch = new();
        private Dictionary<TradeableItem, WInputAmtIconText2> itemAmtInputs;
        private Dictionary<TradeableItem, int> target;
        private WInputAmt multiAmtInput;
        private Trader trader;
        private TradeUI trade;
        private bool isBuying;
        private bool isMarketInsightsUnlocked;
        public int TotalCount {
            get {
                if (List == null) return First.Quantity;
                var cnt = 0;
                foreach (var item in List) cnt += item.Quantity;
                return cnt;
            }
        }

        public double TotalValue(bool isBuying) {
            if (List == null) return First.Quantity * First.FinalPriceOfOne(isBuying);
            var val = 0d;
            foreach (var item in List) val += item.Quantity * item.FinalPriceOfOne(isBuying);
            return val;
        }

        public void AddItem(TradeableItem item) {
            if (First == null) {
                First = item;
                return;
            }
            if (List == null) {
                List = new List<TradeableItem>(4);
                List.Add(First);
            }
            List.Add(item);
        }

        public void SpawnUI(Lifetime life, TradeUI trade, WGroup group,
                    Dictionary<TradeableItem, int> target, bool isBuying,
                    bool isMarketInsightsUnlocked, int idx, string nameSuffix = null) {
            this.isMarketInsightsUnlocked = isMarketInsightsUnlocked;
            this.isBuying = isBuying;
            this.trader = trade.Trader;
            this.trade = trade;
            this.target = target;
            if (List == null && !ForceGroup) {
                SpawnItemInput(life, group.Container, First, idx++);
                return;
            }
            List ??= new List<TradeableItem>(1) { First };

            var w = UISpawn.Widget<WContentListButtonWithAmt>(
                life, UIPrefabId.WContent2ListButtonAmt, group.Container, idx++);

            var cnt = 0;
            var selected = 0;
            double minPrice = double.MaxValue;
            double maxPrice = double.MinValue;
            foreach (var item in List) {
                var priceOf1 = item.FinalPriceOfOne(isBuying);
                cnt += item.Quantity;
                selected += item.QuantitySelected;
                if (priceOf1 < minPrice) minPrice = priceOf1;
                if (priceOf1 > maxPrice) maxPrice = priceOf1;
            }
            var minStr = Wallet.ToValueString(minPrice);
            var maxStr = Wallet.ToValueString(maxPrice);
            var priceStr = minStr == maxStr ? minStr : $"{minStr}-{maxStr}";
            // warns as soon as one item of the group is out of reach
            priceStr = TradeUI.PriceText(priceStr, maxPrice, isBuying, A.S, trader);

            var itemName = nameSuffix != null ? $"{First.Def.NameT} {nameSuffix}" : First.Def.NameT;
            if (Origin != ItemOriginBucket.Untracked) {
                // Reuse the equip picker's glyph stacking rather than re-derive it,
                // so a mixed-tattered group can never read differently here.
                originScratch.Clear();
                foreach (var item in List) {
                    if (item.OurObjData != null) { originScratch.Add(item.OurObjData); }
                }
                itemName = ItemOwnership.OriginLabelFor(originScratch, null, itemName);
                originScratch.Clear();
            }
            w.Item.Load(First.Icon, $"{priceStr} {itemName}", Units.Num(cnt));
            w.Item.LoadOnToggle(life, ToggleGroup);
            w.Amt.Load(selected, (a) => OnMultiChange(target, a), (a) => ValidateMultiChange(a));
            w.Amt.Input.OnClick = () => {
                D.Warn("Getting on click on a multi item. Focused: {0}", w.Amt.Input.IsFocused);
                if (!w.Amt.Input.IsFocused) {
                    ShowQuickChoice(w.Amt, selected, cnt);

                }
            };
            w.Amt.Input.ToggleClearButton(false);
            UIUtils.AddTipFunc(life, w, () => {
                var totalCnt = TotalCount;
                var totalVal = TotalValue(isBuying);
                var total = Wallet.ToValueString(totalVal, true);
                var totalText = TradeUI.PriceText(total, totalVal, isBuying, A.S, trader);
                if (isBuying) {
                    return T.CanBuyFor_V2.F(Texts.Accented(totalCnt), totalText);
                } else {
                    return T.CanSellFor_V2.F(Texts.Accented(totalCnt), totalText);
                }
            });
            UIUtils.AddCodexTooltip(life, w.gameObject, w, First.Def);
            multiAmtInput = w.Amt;
        }

        private WInputAmtIconText2 SpawnItemInput(Lifetime life, RectTransform c, TradeableItem item, int idx) {
            var priceOf1 = item.FinalPriceOfOne(isBuying);
            var priceStr = Wallet.ToValueString(priceOf1);
            var sideText = TradeUI.PriceText(priceStr, priceOf1, isBuying, A.S, trader);
            var qText = Units.Num(item.Quantity);

            var w = UISpawn.Widget<WInputAmtIconText2>(
                life, UIPrefabId.WContent2Text2Amt, c, idx);
            w.Item.Load(item.Icon, $"{sideText} {item.DecoratedName}", qText);
            if (item.Being != null) {
                item.Being.Avatar.Load(w.Item.Image);
            } else if (item.Color.a > 0f) {
                w.Item.SetTint(item.Color);
            }
            w.Amt.Load(item.QuantitySelected,
                (a) => OnChangeAmt(target, item, a),
                (a) => ValidateAmtChange(item, a));
            w.Amt.Input.OnClick = () => {
                D.Warn("Getting on click on a single item. Focused: {0}", w.Amt.Input.IsFocused);
                if (!w.Amt.Input.IsFocused) {
                    ShowQuickChoice(w.Amt, item.QuantitySelected, item.Quantity);
                }
            };
            // we have a better way, on click
            w.Amt.Input.ToggleClearButton(false);
            UIUtils.AddTipFunc(life, w, () => TradeUI.ShowSellableDetails(A.S, trader, item, isBuying));
            UIUtils.AddCodexTooltip(life, w.Item.gameObject, w.Item, item.Def);
            return w;
        }

        private void ShowQuickChoice(WInputAmt amt, int a, int qty) {
            var qc = QuickChoice.Create();
            qc.Widget = amt.Input.Rect;
            var input = amt.Input;
            foreach (var num in StorageUtils.QuantityChoiceOptions(qty)) {
                string label = StorageUtils.QuantityChoiceLabel(qty, num);
                qc.AddButton(label, () => {
                    input.SetValue(Units.Num(num));
                    input.Unfocus();
                });
            }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private int ValidateMultiChange(int a) {
            var cnt = 0;
            foreach (var item in List) {
                cnt += item.Quantity;
            }
            if (a < 0) { return 0; }
            if (a > cnt) { return cnt; }
            return a;
        }

        private void OnMultiChange(Dictionary<TradeableItem, int> target, int a) {
            if (isBuying) {
                // select best items first
                for (int i = 0; i < List.Count; i++) {
                    a = MultiChangeItem(target, a, i);
                }
            } else {
                // select worst items first
                for (int i = List.Count - 1; i >= 0; i--) {
                    a = MultiChangeItem(target, a, i);
                }
            }
            trade.UpdateOutcome();
            A.S.UIQuery.CancelQuickChoice.Ask();
        }

        private int MultiChangeItem(Dictionary<TradeableItem, int> target, int a, int i) {
            var item = List[i];
            var q = Mathf.Min(item.Quantity, a);
            target[item] = q;
            if (itemAmtInputs != null && itemAmtInputs.TryGetValue(item, out var input)) {
                input.Amt.SetValue(q);
            }
            item.QuantitySelected = q;
            a -= q;
            return a;
        }

        private bool ToggleGroup(Lifetime life, RectTransform c, bool willShow, bool wasLoaded) {
            if (!willShow) {
                if (itemAmtInputs != null) {
                    foreach (var item in List) {
                        if (itemAmtInputs.TryGetValue(item, out var input)) {
                            input.Dispose();
                        }
                        itemAmtInputs.Remove(item);
                    }
                }
                return wasLoaded;
            }
            itemAmtInputs ??= new Dictionary<TradeableItem, WInputAmtIconText2>();
            int idx = 0;
            foreach (var item in List) {
                itemAmtInputs[item] = SpawnItemInput(life, c, item, idx++);
            }
            return true;
        }

        private void UpdateMultiInput() {
            if (multiAmtInput == null) { return; }
            var cnt = 0;
            foreach (var item in List) {
                cnt += item.QuantitySelected;
            }
            D.Warn("Updating multi input to {0}", cnt);
            multiAmtInput.Input.SetValue(Units.Num(cnt), false);
        }

        private int ValidateAmtChange(TradeableItem val, int a) {
            if (a < 0) { return 0; }
            if (a > val.Quantity) { return val.Quantity; }
            return a;
        }

        private void OnChangeAmt(Dictionary<TradeableItem, int> target,
            TradeableItem val, int a) {
            target[val] = a;
            val.QuantitySelected = a;
            A.S.UIQuery.CancelQuickChoice.Ask();
            UpdateMultiInput();
            trade.UpdateOutcome();
        }
    }
}
