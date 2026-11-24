using System;
using KL.Utils;
using MessagePack;
using Game.Constants;
using UnityEngine;
using KL.Randomness;
using System.Collections.Generic;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using System.Linq;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class MarketItemModifier {
        [Key("Id")] public string Id;
        [IgnoreMember] public int IdH;
        #if DEBUG_PRICE_MUL
        [Key("PriceMul")] public float PriceMul {
            get => priceMul;
            set {
                if (priceMul != 0f) {
                    var ratio = (priceMul - value) / priceMul;
                    if (ratio > 0.10f || ratio < -0.10f) {
                        D.Err("Moving price mul of {0} by {1}!", Id, ratio);
                    }
                }
                priceMul = value;
            }
        }
        private float priceMul;
        #else
        [Key("PriceMul")] public float PriceMul;
        #endif
        [Key("MaxPriceMul")] public float MaxPriceMul;
        [Key("MinPriceMul")] public float MinPriceMul;
        [Key("RootMul")] public float RootMul;
        [Key("RootLockDays")] public int RootLockDays;
        /// <summary>
        /// Whenever the player buys an item, demand goes up. That in turn reflects on global stock market the next day
        /// </summary>
        ///
        [Key("Demand")] public float Demand {
            get => demand;
            set {
                // if (value != 0f) {
                //     D.Warn("Setting market item demand: {0}: {1}=>{2}", Id, demand, value);
                // }
                demand = value;
            }
        }
        private float demand;
        [Key("Trend")] public MarketTrend Trend;
        // Range(-Volatility, +Volatility) * 0.5
        [Key("Luck")] public float Luck;
        // Day => PriceMul
        [Key("History")] public Dictionary<int, float> History;
        [IgnoreMember] public GameState S;

        private float recessionEffect;
        [IgnoreMember] public float RecessionEffect {
            get {
                if (!Maths.Approximately(recessionEffect, 0f)) {
                    return recessionEffect;
                }
                recessionEffect = 1f;
                foreach (var g in Item.GroupsH) {
                    var gr = MarketGroup.Find(g);
                    recessionEffect = Mathf.Min(recessionEffect, gr.RecessionEffect);
                }
                if (Maths.Approximately(recessionEffect, 0f)) {
                    D.Err("Recession Effect is flat 0 for market item: {0}", Id);
                }
                return recessionEffect;
            }
        }
        public static IOrderedEnumerable<KeyValuePair<int, MarketItemModifier>> SortedBy(
                Dictionary<int, MarketItemModifier> input, MarketSortMode mode) {
            switch (mode) {
                case MarketSortMode.Group: {
                    return input.OrderBy(i => i.Value.Item.FirstGroup.NameT)
                        .ThenBy(i => i.Value.Item.Name);
                }
                case MarketSortMode.ValueDesc: {
                    return input.OrderByDescending(i => i.Value.Price);
                }
                case MarketSortMode.ValueAsc: {
                    return input.OrderBy(i => i.Value.Price);
                }
                case MarketSortMode.Alphabetic: {
                    return input.OrderBy(i => i.Value.Item.Name);
                }
            }
            D.Err("Unsupported Sort Mode: {0}", mode);
            return null;
        }

        public void Reset() {
            RootMul = PriceMul = 1f;
            History?.Clear();
        }

        public string PriceChange(int days = 1) {
            if (Arrays.IsEmpty(History)) { return Texts.Grey("-"); }
            if (days == -1) {
                // All time
                var firstPrice = History.First().Value * Item.Value;
                var allTimeChangePct = (Price - firstPrice) / firstPrice;
                return Units.PriceChange(allTimeChangePct);
            }
            var curDay = S.Market.Day;
            var targetDay = curDay - days;
            if (targetDay < 0) {
                return Texts.Grey("-");
            }
            if (History != null && History.TryGetValue(targetDay, out var histMul)) {
                // We have a price change!
                var curPrice = Price;
                var oldPrice = Item.Value * histMul;
                var pct = (curPrice - oldPrice) / oldPrice;
                //D.Log("PriceChange in {0} days: {1} -> {2} -> {3} pct",
                    //days, oldPrice, curPrice, pct);
                return Units.PriceChange(pct);
            }
            return Texts.Grey("-");
        }

        private float priceMulHigh;
        private float priceMulLow;

        [IgnoreMember] public float PriceHigh {
            get {
                if (priceMulHigh < 0.0000001f) {
                    UpdatePriceHighLow();
                }
                return item.Value * priceMulHigh;
            }
        }

        [IgnoreMember] public float PriceLow {
            get {
                if (priceMulLow < 0.0000001f) {
                    UpdatePriceHighLow();
                }
                return item.Value * priceMulLow;
            }
        }

        public void UpdatePriceHighLow() {
            if (History == null) { return; }
            priceMulHigh = priceMulLow = PriceMul;
            foreach (var p in History) {
                if (p.Value > priceMulHigh) {
                    priceMulHigh = p.Value;
                }
                if (p.Value < priceMulLow) {
                    priceMulLow = p.Value;
                }
            }
        }

        private MarketItem item;
        [IgnoreMember] public MarketItem Item => item ??= MarketItem.Find(IdH);
        [IgnoreMember] public float Price => Item.Value * PriceMul;
        public AdjustedMarketItem Adjusted() {
            return Adjusted(Item);
        }

        public AdjustedMarketItem Adjusted(MarketItem mi) {
            var adj = AdjustedMarketItem.Create(mi);
            return Adjusted(adj);
        }

        public AdjustedMarketItem Adjusted(AdjustedMarketItem mi) {
            if (Id != mi.Id) {
                D.Err("Trying to adjust an item with wrong id! {0} != {1}", Id, mi.Id);
                return mi;
            }
            mi.Value *= PriceMul;
            return mi;
        }

        public void WriteHistoryEntry(int day) {
            (History ??= new Dictionary<int, float>())[day] = PriceMul;
            if (priceMulLow < 0.000001f) {
                UpdatePriceHighLow();
            } else {
                priceMulLow = Mathf.Min(priceMulLow, PriceMul);
                priceMulHigh = Mathf.Max(priceMulHigh, PriceMul);
            }
        }

        public void TickLockDays(Rng rng) {
            if (RootLockDays-- < 1) {
                if (PriceMul > RootMul) {
                    RootMul = Mathf.Min(PriceMul, RootMul * 1.05f);
                } else {
                    RootMul = Mathf.Max(PriceMul, RootMul * 0.95f);
                }
                if (Item.AssetType == MarketItemType.CFD) {
                    RootMul = Mathf.Clamp(RootMul, 0.5f, 10f);
                }
                RootLockDays = rng.Range(
                    Tunables.MarketLongTrendMinDays,
                    Tunables.MarketLongTrendMaxDays);
            }
        }

        public override string ToString() {
            return $"MarketItemMod[{Id}:{PriceMul}]";
        }
    }
}
