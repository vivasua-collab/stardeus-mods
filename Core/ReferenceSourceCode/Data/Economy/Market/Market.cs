#if UNITY_EDITOR
// #define SKIP_GROUPS
// #define DEBUG_MARKET
// #define DEBUG_PRODUCT_OF
// #define DEBUG_STONKS
// #define DEBUG_LIMITS
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Game.Constants;
using Game.Modifiers;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;
using Game.Systems;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class Market {
        #if !PRODUCTION_BUILD
        [SelfInit]
        private static void ResetStatic() {
            HighSpeedInTests = false;
        }
        public static bool HighSpeedInTests;
        #endif
        public const int CurrentVersion = 2;
        [Key("Version")] public int Version;
        [Key("LastTick")] public long LastTick;
        [Key("Mods")] public Dictionary<int, MarketItemModifier> Modifiers;
        [Key("TrendLong")] public MarketTrend TrendLong;
        [Key("TrendShort")] public MarketTrend TrendShort;
        [Key("GTrends")] public Dictionary<int, MarketTrend> GroupTrends;
        [Key("History")] public Dictionary<int, float> History;
        [Key("Day")] public int Day;
        // TODO: make it deterministic with seed
        [IgnoreMember] public Rng Rng;
        [IgnoreMember] public GameState S;
        [IgnoreMember] public bool IsValid => Version == CurrentVersion;
        private int marketDayLengthTicks;
        private float marketMoveSpeed;

        public static Market CreateNew(GameState s) {
            return new Market {
                Version = CurrentVersion,
            };
        }

        public void Prewarm(int days) {
            try {
                for (int i = 0; i < days; i++) {
                    Day = i - days;
                    TickGlobalDay();
                    var priceMulSum = 0f;
                    foreach (var item in Modifiers) {
                        TickDay(item.Value);
                        priceMulSum += item.Value.PriceMul;
                    }
                    WriteHistory(priceMulSum / Modifiers.Count);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed prewarming market at day: {0}", Day);
            }
            Day = 0;
        }

        public void Tick(long ticks) {
            if (LastTick == 0) {
                LastTick = ticks;
            }
            var ticksPassed = ticks - LastTick;
            var daysPassed = (int)(ticksPassed / marketDayLengthTicks);
            if (daysPassed > 1) {
                daysPassed = 1;
            }
            for (int i = 0; i < daysPassed; i++) {
                #if !PRODUCTION_BUILD
                if (HighSpeedInTests && The.TestsAreRunning) {
                    Day++;
                } else {
                    Day = (int)(ticks / marketDayLengthTicks);
                }
                #else
                Day = (int)(ticks / marketDayLengthTicks);
                #endif
                TickGlobalDay();
                var priceMulSum = 0f;
                foreach (var item in Modifiers) {
                    TickDay(item.Value);
                    priceMulSum += item.Value.PriceMul;
                }
                LastTick += marketDayLengthTicks;
                float globIdx = priceMulSum / Modifiers.Count;
                // D.Warn("Global idx: {0}->{1}", Day, globIdx);
                WriteHistory(globIdx);
                #if DEBUG_STONKS
                LogMarketState();
                #endif
                S.Sig.MarketDayChanged.Send();
            }
        }

        private float marketGroupVolatility;
        private float marketShortVolatility;
        private float marketLongVolatility;
        private int marketShortTrendMinDays;
        private int marketShortTrendMaxDays;
        private int marketLongTrendMinDays;
        private int marketLongTrendMaxDays;
        private int marketGroupTrendMinDays;
        private int marketGroupTrendMaxDays;

        public void InitializeTunables() {
            marketLongVolatility = Tunables.MarketLongVolatility;
            marketLongTrendMinDays = Tunables.MarketLongTrendMinDays;
            marketLongTrendMaxDays = Tunables.MarketLongTrendMaxDays;
            marketShortVolatility = Tunables.MarketShortVolatility;
            marketShortTrendMinDays = Tunables.MarketShortTrendMinDays;
            marketShortTrendMaxDays = Tunables.MarketShortTrendMaxDays;
            marketGroupTrendMinDays = Tunables.MarketShortTrendMinDays;
            marketGroupTrendMaxDays = Tunables.MarketShortTrendMaxDays;
            marketGroupVolatility = Tunables.MarketGroupVolatility;
            marketDayLengthTicks = Consts.TicksPerHour * Tunables.MarketDayLengthHours;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning && HighSpeedInTests) {
                marketDayLengthTicks = Consts.TicksPerMinute;
            }
            #endif
            #if DEBUG_MARKET
            marketDayLengthTicks = Consts.TicksPerMinute;
            #endif
            if (marketDayLengthTicks <= 0) {
                D.Err("MarketDayLengthHours tunable must be > 0!");
                marketDayLengthTicks = Consts.TicksPerDay;
            }
            marketMoveSpeed = Tunables.MarketMoveSpeed;
            if (marketMoveSpeed <= 0) {
                D.Err("MarketMoveSpeed tunable must be > 0!");
                marketMoveSpeed = 1f;
            }
        }

        public void EnsureProductMulWithinRange(MarketItemModifier mod) {
            if (Arrays.IsEmpty(mod.Item.ProductOf)) { return; }
            var productCost = 0f;
            foreach (var p in mod.Item.ProductOf) {
                var mim = Find(p.Key);
                if (mim == null) {
                    D.Err("MarketItem ProductOf not found for: {0}", mod.Id);
                    continue;
                }
                productCost += mim.Price * p.Value;
            }
            if (productCost == 0f) {
                D.Err("Product cost 0 for {0}", mod.Id);
                return;
            }
            var priceBefore = mod.Price;
            var ratio = priceBefore / productCost;
            if (ratio < 0.99f) {
                // we have to set the mod.PriceMul to a value that will produce Price that is no less than 0.9f * productCost
                mod.PriceMul /= ratio;
                if (mod.Trend == null) {
                    D.Err("Mod Trend is null for: {0}", mod.Id);
                    return;
                }
                mod.Trend.Behavior = MarketBehaviors.Get(MarketBehaviorSurge.Id);
                mod.Trend.CurrentDays = 0;
                mod.Trend.TotalDays = Rng.Range(3, 6);
                foreach (var p in mod.Item.ProductOf) {
                    var mim = Find(p.Key);
                    if (mim == null) { continue; }
                    if (mim.Trend == null) {
                        D.ErrSoft("MarketItemModifier trend is null for subproduct: {0}->{1}", mod.Id, mim.Id);
                        continue;
                    }
                    if (mim.Trend.Behavior.IsPositive) {
                        // D.ErrSoft("Staling behavior that drove ingr price for {0}: {1}", mod.Id, mim.Item.Id);
                        mim.Trend.Behavior = MarketBehaviors.Get(MarketBehaviorStill.Id);
                        mim.Trend.CurrentDays = 0;
                        mim.Trend.TotalDays = Rng.Range(3, 6);
                    }
                }
                // D.ErrSoft("[Day {4}] Market item got cheaper than ingredient costs. Fixing: {0} => {1} -> {2}. Prod cost: {3}",
                //     mod.Id, priceBefore, mod.Price, productCost, Day);
            }
        }

        private void WriteHistory(float idx) {
            (History ??= new())[Day] = idx;
        }

        #if DEBUG_STONKS
        private void LogMarketState() {
            var file = Application.persistentDataPath + "/Stonks.csv";
            var sb = new StringBuilder();
            if (!File.Exists(file)) {
                sb.Append("Long,Short,");
                foreach (var item in Modifiers) {
                    sb.Append(item.Value.Item.Def.NameT);
                    sb.Append(",");
                }
                sb.Append('\n');
                File.WriteAllText(file, sb.ToString());
                sb.Clear();
            }
            sb.Append(TrendLong.BehaviorId);
            sb.Append(",");
            sb.Append(TrendShort.BehaviorId);
            sb.Append(",");
            foreach (var item in Modifiers) {
                sb.AppendFormat("{0:0.00}", item.Value.Item.Value * item.Value.PriceMul);
                sb.Append(",");
            }
            sb.Append('\n');
            File.AppendAllText(file, sb.ToString());
        }
        #endif

        private void TickGlobalDay() {
            if (MarketTrend.AdvanceDay(TrendLong)) {
                TrendLong ??= new MarketTrend();
                SetNewGlobalTrend(TrendLong, true);
            }
            if (MarketTrend.AdvanceDay(TrendShort)) {
                TrendShort ??= new MarketTrend();
                SetNewGlobalTrend(TrendShort, false);
            }
            #if !SKIP_GROUPS
            foreach (var g in GroupTrends) {
                TickGroupTrend(g.Key);
            }
            #endif
        }

        private void TickDay(MarketItemModifier mod) {
            if (MarketTrend.AdvanceDay(mod.Trend)) {
                SetNewShortTrend(mod);
            }
            var vol = mod.Item.Volatility * Tunables.MarketItemVolatilityMul;
            float priceMulBefore = mod.PriceMul;

            if (mod.PriceMul < 0.0000001f) {
                mod.PriceMul = 1f;
            }
            var lastPriceMul = mod.PriceMul;

            float minRootMul = mod.Item.AssetType == MarketItemType.CFD ? 0.5f : 0.05f;
            var rootMulLow = mod.RootMul * 0.5f;
            if (rootMulLow < minRootMul) {
                // try to force the stonk back up
                mod.RootMul = Rng.Range(0.6f, 0.9f);
                rootMulLow = minRootMul;
            }

            if (mod.PriceMul > rootMulLow) {
                if (mod.Demand != 0f) {
                    AdjustDemand(mod);
                    mod.Demand = 0f;
                }

                // Global market effect
                mod.PriceMul = TrendLong.Adjust(Rng, mod.PriceMul, vol,
                    mod.RecessionEffect, mod.Luck);
                mod.PriceMul = TrendShort.Adjust(Rng, mod.PriceMul, vol,
                    mod.RecessionEffect, mod.Luck);

                // Groups
                #if !SKIP_GROUPS
                if (!Arrays.IsEmpty(mod.Item.GroupsH)) {
                    // D.Warn("Running correlation groups for: {0}: {1}",
                    //     mod.Item, D.Dump(mod.Item.CorrelationGroups));
                    for (int i = mod.Item.GroupsH.Length - 1; i >= 0; i--) {
                        int g = mod.Item.GroupsH[i];
                        // Some groups are tag only, those will be skipped when simulating group trends
                        if (GroupTrends.TryGetValue(g, out var gt)) {
                            // D.Warn("Correlation group for {0}: {1}. Vol: {2}", mod.Item, gt, gt.VolatilityMultiplier);
                            float pm = gt.Adjust(Rng, mod.PriceMul, gt.VolatilityMultiplier,
                                    mod.RecessionEffect, mod.Luck);
                            if (pm != mod.PriceMul) {
                                // D.Warn("Adjusting priceMul through corelation group for {0}: {1}->{2}",
                                //     mod.Id, mod.PriceMul, pm);
                                mod.PriceMul = pm;
                            }
                        }
                    }
                }
                #endif
            } else {
                // price mul was driven to the ground already. We only adjust the demand if
                // it's positive
                if (mod.Demand >= 0f) {
                    AdjustDemand(mod);
                }
                mod.Demand = 0f;
            }

            // Short trend of item
            mod.PriceMul = mod.Trend.Adjust(Rng, mod.PriceMul, vol,
                mod.RecessionEffect, mod.Luck);

            // Only one trend will have this power
            if (!mod.Trend.Behavior.IgnoreRoot || mod.PriceMul <= rootMulLow) {
                var mulDiff = Mathf.Abs(lastPriceMul - mod.PriceMul);

                if (mod.PriceMul < rootMulLow) {
                    var c = (mod.RootMul - mod.PriceMul) * 0.5f;
                    // 100% to invert if we bottomed out completely
                    if (mod.PriceMul < minRootMul) {
                        // time to kill the trend
                        mod.Trend.TotalDays = mod.Trend.CurrentDays;
                        c = 1f;
                    }
                    if (Rng.Chance(c)) {
                        #if DEBUG_LIMITS
                        D.Warn("FLOOR ({3}): Inverting last move with chance: {0}: {1} -> {2}",
                            c, mod.PriceMul, lastPriceMul + mulDiff, mod.Item.Id);
                        #endif
                        // Invert the last move
                        mod.PriceMul = lastPriceMul + mulDiff;
                    #if DEBUG_LIMITS
                    } else {
                        D.Warn("FLOOR ({3}): NOT Inverting last move with chance: {0}: {1} -> {2}",
                            c, mod.PriceMul, lastPriceMul + mulDiff, mod.Item.Id);
                    #endif
                    }
                } else if (mod.PriceMul > mod.RootMul * 2f) {
                    // ceiling
                    var c = (mod.PriceMul - (mod.RootMul * 1.5f)) * 0.5f;
                    if (Rng.Chance(c)) {
                        #if DEBUG_LIMITS
                        D.Warn("CEIL ({3}): Inverting last move with chance: {0}: {1} -> {2}",
                            c, mod.PriceMul, lastPriceMul - mulDiff, mod.Item.Id);
                        #endif
                        // Invert the last move
                        mod.PriceMul = lastPriceMul - mulDiff;
                    #if DEBUG_LIMITS
                    } else {
                        D.Warn("CEIL ({3}): NOT Inverting last move with chance: {0}: {1} -> {2}",
                            c, mod.PriceMul, lastPriceMul - mulDiff, mod.Item.Id);
                    #endif
                    }
                }
            // Prevent absolute crash
            } else if (mod.PriceMul < 0.8f) {
                mod.PriceMul = 0.8f + Rng.Range(-0.01f, 0.01f);
            }

            // Check when to freeze the stonk price range
            mod.TickLockDays(Rng);
            if (Rng.Chance(0.01f)) {
                RandomizeLimitsFor(mod);
            }

            var priceMulDiff = mod.PriceMul - priceMulBefore;
            var priceMovePct = priceMulDiff / priceMulBefore;
            if (priceMovePct > 0.15f || priceMovePct < -0.15f) {
                if (mod.Item.AssetType == MarketItemType.CFD) {
                    D.ErrSoft("Price for {0} moved by {1} at day {2}", mod.Id,
                        Units.PercentageWithFraction3(priceMovePct, false), Day);
                }
            }

            float maxPriceMul = mod.MaxPriceMul;
            float minPriceMul = mod.MinPriceMul;
            if (mod.Item.AssetType == MarketItemType.Equity) {
                // Equity has different rules than CFDs, it will have ceiling and floor increased by a lot
                maxPriceMul += Day * 0.03f;
                // allow having penny stocks over time
                minPriceMul *= 1f - Day * 0.01f;
            } else {
                // commodities will still have a slight chance to become more expensive over long time
                maxPriceMul += Day * 0.001f;
            }

            bool forcePriceDown = priceMulBefore > maxPriceMul;
            bool forcePriceUp = priceMulBefore < minPriceMul;

            var priceMullMod = priceMulDiff * marketMoveSpeed;

            if ((forcePriceDown && priceMullMod > 0f) || (forcePriceUp && priceMullMod < 0f)) {
                if (Rng.Chance(0.51f)) {
                    priceMullMod *= -1f;
                }
            }

            // Add the tunable
            mod.PriceMul = priceMulBefore + priceMullMod;

            // Prevent price going below $1
            if (mod.Price < 1f) {
                mod.PriceMul = (1f / mod.Item.Value) * Rng.Range(1f, 1.05f);
            }

            // Final adjustment to stop copper costing more than copper plate, etc
            EnsureProductMulWithinRange(mod);

            mod.WriteHistoryEntry(Day);
        }

        private static void AdjustDemand(MarketItemModifier mod) {
            if (mod.Demand != 0f) {
                var adj = 1f + Maths.Clamp(mod.Demand, -0.03f, 0.03f);
                // D.Warn("Will adjust price of {0} according to demand: {1} ({2}->{3})",
                //     mod.Id, mod.Demand, mod.PriceMul, mod.PriceMul * adj);
                mod.PriceMul *= adj;
            }
        }

        public void EnsureModifiers() {
            if (Modifiers == null) {
                Modifiers = new();
            } else {
                foreach (var mod in Modifiers) {
                    mod.Value.IdH = Hashes.S(mod.Value.Id);
                    mod.Value.S = S;
                }
            }
        }
        public void EnsureGroupTrends() {
            if (GroupTrends == null) {
                GroupTrends = new();
            }
            foreach (var g in MarketGroup.All.Values) {
                if (!GroupTrends.TryGetValue(g.IdH, out var gt)) {
                    gt = new MarketTrend();
                    GroupTrends.Add(g.IdH, gt);
                }
                gt.GroupNameT = g.NameT;
            }
        }

        /// <summary>
        /// We may have saved some old item modifiers and we want to get rid of them
        /// </summary>
        public void RemoveUnknownModifiers() {
            List<int> modsToRemove = null;
            foreach (var mod in Modifiers) {
                if (!MarketItem.Exists(mod.Value.IdH)) {
                    (modsToRemove ??= new()).Add(mod.Key);
                }
            }
            if (modsToRemove != null) {
                foreach (var mod in modsToRemove) {
                    Modifiers.Remove(mod);
                }
            }
        }

        [Obsolete("Use Find(idH)")]
        public MarketItemModifier Find(string id) {
            return Find(Hashes.S(id));
        }
        public MarketItemModifier Find(int idH) {
            if (Modifiers.TryGetValue(idH, out var mim)) {
                return mim;
            }
            return null;
        }

        public double ValueOf(Mat mat) {
            if (mat.StackSize == 0) { return 0d; }
            var item = Find(mat.Type.Def.IdH);
            if (item == null) { return 0d; }
            return item.Adjusted().Value * mat.StackSize;
        }

        public double ValueOf(Def def) {
            if (def == null) { return 0d; }
            var item = Find(def.IdH);
            if (item == null) { return 0d; }
            return item.Adjusted().Value;
        }

        public void LoadItem(MarketItem item) {
            if (Modifiers.TryGetValue(item.IdH, out var _)) {
                // we had a modifier saved, let's not re-create it
                return;
            }
            var vol = item.Volatility;
            var rng = new Rng(Hashes.Quick(S.Seed, item.IdH));
            var mod = new MarketItemModifier {
                Id = item.Id,
                IdH = item.IdH,
                RootMul = 1f,
                PriceMul = 1f + rng.Range(-vol, vol),
                MinPriceMul = rng.Range(
                    Tunables.MarketItemMinPriceMulFrom,
                    Tunables.MarketItemMinPriceMulTo),
                MaxPriceMul = rng.Range(
                    Tunables.MarketItemMaxPriceMulFrom,
                    Tunables.MarketItemMaxPriceMulTo),
                Luck = rng.Range(-vol, vol) * 0.5f,
            };
            mod.S = S;
            Modifiers.Add(item.IdH, mod);
        }

        private void TickGroupTrend(int id) {
            if (!GroupTrends.TryGetValue(id, out var t)) {
                D.Err("No group trend created for id: {0}", id);
                return;
            }
            if (!MarketTrend.AdvanceDay(t)) { return; }
            var group = MarketGroup.Find(id);
            if (group == null) {
                D.Err("Cannot tick a trend without marketgroup config: {0}", id);
                return;
            }
            var prevBehavior = t.Behavior;
            t.Behavior = MarketBehaviors.GetLongTerm(Rng);
            var dur = Rng.Range(marketGroupTrendMinDays, marketGroupTrendMaxDays);
            UpdateTrendDuration(t, prevBehavior, dur);
            t.VolatilityMultiplier = group.Volatility * marketGroupVolatility * Rng.Range(0.9f, 1.1f);
        }

        private void SetNewShortTrend(MarketItemModifier mod) {
            if (mod.Trend == null) {
                mod.Trend = new MarketTrend();
            }
            var t = mod.Trend;
            var prevBehavior = t.Behavior;

            if (mod.PriceMul < mod.MinPriceMul) {
                // RandomizeLimitsFor(mod);
                #if DEBUG_LIMITS
                D.Warn("Forcing surge for {0}", mod.Item.Id);
                #endif
                t.Behavior = MarketBehaviors.Get(MarketBehaviorSurge.Id);
            } else if (mod.PriceMul > mod.MaxPriceMul) {
                // RandomizeLimitsFor(mod);
                #if DEBUG_LIMITS
                D.Warn("Forcing crash for {0}", mod.Item.Id);
                #endif
                t.Behavior = MarketBehaviors.Get(MarketBehaviorCrash.Id);
            } else {
                t.Behavior = MarketBehaviors.GetShortTerm(Rng);
            }

            var dur = t.Behavior.AdjustDuration(
                    Rng.Range(marketShortTrendMinDays, marketShortTrendMaxDays), 2);
            UpdateTrendDuration(t, prevBehavior, dur);
            t.VolatilityMultiplier = marketShortVolatility * Rng.Range(0.9f, 1.1f);
        }

        private void RandomizeLimitsFor(MarketItemModifier mod) {
            // D.Warn("randomizing limits for: {0}: {1}", Day, mod.Id);
            mod.MinPriceMul = Rng.Range(
                Tunables.MarketItemMinPriceMulFrom,
                Tunables.MarketItemMinPriceMulTo);
            mod.MaxPriceMul = Rng.Range(
                Tunables.MarketItemMaxPriceMulFrom,
                Tunables.MarketItemMaxPriceMulTo);
        }

        private void UpdateTrendDuration(MarketTrend t, MarketBehavior prev, int dur) {
            if (t.Behavior == prev) {
                t.TotalDays += dur;
            } else {
                t.CurrentDays = 0;
                t.TotalDays = dur;
            }
        }

        private void SetNewGlobalTrend(MarketTrend trend, bool isLong) {
            var prevBehavior = trend.Behavior;
            int days;
            int dur;
            if (isLong) {
                trend.Behavior = MarketBehaviors.GetLongTerm(Rng);
                days = Rng.Range(marketLongTrendMinDays, marketLongTrendMaxDays);
                dur = trend.Behavior.AdjustDuration(days, 6);
                trend.VolatilityMultiplier = marketLongVolatility * Rng.Range(0.9f, 1.1f);
            } else {
                trend.Behavior = MarketBehaviors.GetShortTerm(Rng);
                days = Rng.Range(marketShortTrendMinDays, marketShortTrendMaxDays);
                dur = trend.Behavior.AdjustDuration(days, 3);
                trend.VolatilityMultiplier = marketShortVolatility * Rng.Range(0.9f, 1.1f);
            }
            UpdateTrendDuration(trend, prevBehavior, dur);
        }

        public AdjustedMarketItem Adjusted(AdjustedMarketItem mi) {
            var mod = Find(mi.IdH);
            if (mod != null) {
                return mod.Adjusted(mi);
            }
            return mi;
        }
        public AdjustedMarketItem Adjusted(MarketItem mi) {
            var mod = Find(mi.IdH);
            if (mod == null) { return null; }
            return mod.Adjusted(mi);
        }

        public AdjustedMarketItem Adjusted(int idH) {
            var mi = MarketItem.Find(idH);
            if (mi == null) { return null; }
            var mod = Find(idH);
            if (mod == null) { return null; }
            return mod.Adjusted(mi);
        }

        public void Reset() {
            LastTick = 0;
            if (Modifiers != null) {
                foreach (var mod in Modifiers) {
                    mod.Value.Reset();
                }
            }
            History?.Clear();
            Version = CurrentVersion;
        }
    }
}
