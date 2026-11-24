using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.UI;
using Game.Systems.Economy;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using System;
using Game.Systems.BodyParts;
using KL.Randomness;

namespace Game.Systems {
    public sealed class EconomySys : GameSystem, IOverlayProvider {
        public const string SysId = "EconomySys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new EconomySys());
        }

        private Wallet wallet;
        public List<OverlayInfo> Overlays => overlays;

        private readonly List<OverlayInfo> overlays = new();

        private float mineRate;
        private readonly B<float> mineRateB = new B<float>(0f);
        public B<float> MineRateB => mineRateB;
        public Entity Entity => null;

        private void InitializeMarket() {
            bool needsPrewarm = false;
            if (S.Market == null) {
                S.Market = Market.CreateNew(S);
                needsPrewarm = true;
            }
            S.Market.S = S;
            S.Market.Rng = S.Rng.Fork();
            S.Market.InitializeTunables();
            // Order matters!
            if (MarketItem.All.Count == 0) {
                D.Err("Market items not initialized!");
            }
            S.Market.EnsureModifiers();
            S.Market.EnsureGroupTrends();
            S.Portfolio?.AfterLoad();
            // ValidateBodyPartMarketItems();
            ValidateMarketItems(10, false);
            S.Market.RemoveUnknownModifiers();
            ApplyMarketItems();

            if (needsPrewarm) {
                using (D.Timer("Market Prewarm")) {
                    S.Market.Prewarm(180);
                }
            }

            UpdateNetWorth();

            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning && Market.HighSpeedInTests) { return; }
            #endif

            if (!S.Market.IsValid) {
                D.Warn("Migrating stock market");
                var start = Time.realtimeSinceStartupAsDouble;
                S.Market.Reset();
                for (long tick = 0; tick < G.Ticks; tick += Consts.TicksPerDay) {
                    S.Market.Tick(tick);
                }
                var took = Time.realtimeSinceStartupAsDouble - start;
                D.Warn("Migrated stock market in {0:0.00000}s", took);
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) { return; }
                #endif
                WPopup.Create(false, IconId.CUpgrade, T.SaveUpgraded,
                    T.SaveUpgradedInfo_V1.F(" - Resimulated Stock Market with new algorithm")).Spawn();
            }
        }

        /// <summary>
        /// Some body parts don't have corresponding market items, those are commodities
        /// This method shouldn't be used unless for debugging.
        /// </summary>
        private void ValidateBodyPartMarketItems() {
            foreach (var part in BodyPartDef.All) {
                if (part.Value.Def == null) {
                    D.Err("Part has no def! {0}", part.Value);
                    continue;
                }
                var mi = MarketItem.FindFor(part.Value.Def);
                if (mi == null) {
                    D.Err("Market item not available for body part! {0}", part.Value);
                    continue;
                }
                if (part.Value.IsBiological && !mi.IsBlackMarket) {
                    D.Err("Biological part is not BlackMarket item! {0}", part.Value);
                }
            }
        }

        private void ValidateMarketItems(int iterations, bool lastIteration) {
            //D.Log("Validating market items, iterations left: {0}", iterations);
            bool wasAdjusted = false;
            foreach (var it in MarketItem.CFDs) {
                var item = it.Value;

                // if (item.Def.MatType == null) {
                //     continue;
                // }

                if (Arrays.IsEmpty(item.ProductOf)) {
                    continue;
                }
                var ingredientCost = 0f;
                var itemDef = item.Def;
                foreach (var p in item.ProductOf) {
                    if (MarketItem.CFDs.TryGetValue(p.Key, out var prod)) {
                        ingredientCost += prod.Value * p.Value;
                    } else {
                        D.Err("MarketItem {0} has ProductOf {1}, but it does not exist in MarketItems!",
                            item.Id, p);
                    }
                }
                var isBlackMarket = Arrays.Contains(item.GroupsH, MarketGroupIdH.BlackMarket);
                var minProfitMargin = 500;
                var maxCostMul = 2f;
                if (isBlackMarket) {
                    minProfitMargin = 7000;
                    maxCostMul = 4f;
                }
                // Cost
                var basePrice = item.Value;
                // too cheap
                if (basePrice < ingredientCost * 1.1f) {
                    item.Value = ingredientCost * 1.5f;
                    D.ErrSoft("MarketItem {0} has value of {1}, ingredients cost {2}. Fixing up to {3}",
                        item.Id, basePrice, ingredientCost, item.Value);
                    DumpIngredientsFor(item);
                    wasAdjusted = true;
                // too expensive
                } else if (basePrice > ingredientCost * maxCostMul && (basePrice - ingredientCost) > minProfitMargin) {
                    item.Value = ingredientCost * 1.7f;
                    D.ErrSoft("MarketItem [Black: {4}] {0} has value of {1}, ingredients cost {2}. Fixing down to {3}",
                        item.Id, basePrice, ingredientCost, item.Value, isBlackMarket);
                    DumpIngredientsFor(item);
                    wasAdjusted = true;
                }
            }

            if (wasAdjusted) {
                ValidateMarketItems(iterations - 1, false);
            } else if (!lastIteration) {
                ValidateMarketItems(iterations - 1, true);
            }
        }

        private void DumpIngredientsFor(MarketItem item) {
            Caches.StringBuilder3.Clear();
            var sb = Caches.StringBuilder3;
            foreach (var id in item.ProductOf) {
                var pDef = The.Defs.FastGet(id.Key);
                if (pDef.IgnoreMarket || pDef.MatType?.IgnoreMarket == true) { continue; }
                if (MarketItem.CFDs.TryGetValue(id.Key, out var ingr)) {
                    sb.AppendFormat("{0} => {1}: {2}x{3}={4}\n",
                        item.Name, pDef.NameT, ingr.Value, id.Value, id.Value * ingr.Value);
                }
            }
            D.ErrSoft(sb.ToString());
        }


        private void ApplyMarketItems() {
            foreach (var mi in MarketItem.All.Values) {
                S.Market.LoadItem(mi);
            }
        }

        protected override void OnInitialize() {
            overlays.Clear();
            overlays.Add(new OverlayInfo(OverlayType.Information,
                OverlayId.Wallet, IconId.WWallet));
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (info.Id != OverlayId.Wallet) { return; }
            S.UIQuery.ToggleWallet.Ask(!UIShowing.Wallet);
        }

        private void OnLoadSave(GameState state) {
            wallet = state.Wallet;
            wallet.IsPlayer = true;
            InitializeMarket();
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.RandomMatWithValue.SetResponder(RandomMatWithValue);
        }

        private Mat RandomMatWithValue(int minMatRarity, float minItemPrice, double value, Rng rng) {
            var w = Caches.WeightedInts;
            w.Clear();
            foreach (var miMod in S.Market.Modifiers.Values) {
                var item = miMod.Item;
                if (item.AssetType != MarketItemType.CFD) {
                    continue;
                }
                var matType = item.Def?.MatType;
                if (matType == null || matType.MatRarity < minMatRarity) {
                    continue;
                }
                if (minItemPrice > 0f && minItemPrice > miMod.Price) {
                    continue;
                }
                w.Add(item.IdH, matType.Rarity);
            }
            if (!rng.TryWeightedFrom(w, out var itemIdH)) {
                D.Err("Couldn't pick a random mat! Min price: {0}. MinRarity: {1}",
                    minItemPrice, minMatRarity);
                return RandomMatWithValue(minMatRarity - 1, 0, value, rng);
            }
            var selectedItem = S.Market.Find(itemIdH);
            var amt = Mathf.RoundToInt((float) (value / selectedItem.Price));
            if (amt < 1) {
                amt = 1;
            }
            var mat = new Mat() {
                Type = selectedItem.Item.Def.MatType,
                StackSize = amt,
                MaxStackSize = amt,
            };
            return mat;
        }

        private void OnTick(long ticks) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning && Market.HighSpeedInTests) {
                if (ticks % Consts.TicksPerHour != 17) {
                    S.Market.Tick(ticks);
                }
            }
            #endif
            // Mine currencies
            if (ticks % Consts.TicksPerHour == 42) {
                float ratePerH = MineCurrencyOncePerHour();
                if (!Maths.Approximately(mineRate, ratePerH)) {
                    mineRate = ratePerH;
                    mineRateB.Set(ratePerH);
                }

            }
            // Simulate market once per hour
            if (ticks % Consts.TicksPerHour == 0) {
                S.Market.Tick(ticks);
            }
            if (ticks % Consts.TicksPerHour == 1) {
                UpdateNetWorth();
            }
        }

        private void GetGPUStats(out int gpuActive, out int mem, out int memPerGPU) {
            gpuActive = A.Grid.TotalGPU;
            memPerGPU = Tunables.CurrencyMiningMemRequiredPerGPU;
            mem = A.Grid.TotalMem;
            if (memPerGPU > 0) {
                var maxGPUs = mem / memPerGPU;
                if (gpuActive > maxGPUs) {
                    gpuActive = maxGPUs;
                }
            }
        }

        private float MineCurrencyOncePerHour() {
            var ratePerHour = ComputeMiningRate();
            if (ratePerHour > 0f) {
                S.Wallet.Add(ratePerHour);
            }
            return ratePerHour;
        }

        private float ComputeMiningRate() {
            if (availableMiners.Count == 0) {
                return 0f;
            }
            GetGPUStats(out var gpus, out _, out _);
            var amtPerGPUPerDay = Tunables.CurrencyMiningAmtPerDayPerGPU;
            var ratePerTick = (amtPerGPUPerDay * gpus) / Consts.TicksPerDay;
            return ratePerTick > 0f ? ratePerTick * Consts.TicksPerHour : 0f;
        }

        public float RefreshMiningRate() {
            var ratePerHour = ComputeMiningRate();
            if (!Maths.Approximately(mineRate, ratePerHour)) {
                mineRate = ratePerHour;
                mineRateB.Set(ratePerHour);
            }
            return ratePerHour;
        }

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        private readonly List<CurrencyMinerComp> availableMiners = new();

        public void AddMiner(CurrencyMinerComp miner) {
            if (!availableMiners.AddUniqueInefficient(miner)) {
                D.Err("Miner already added: {0}", miner);
            }
            UpdateMinedCurrencies();
        }

        private void UpdateMinedCurrencies() {
            if (availableMiners.Count > 0) {
                mineRate = 0f;
                mineRateB.Set(0f);
            } else {
                mineRate = 0f;
                mineRateB.Set(0f);
            }
        }

        public void RemoveMiner(CurrencyMinerComp miner) {
            availableMiners.Remove(miner);
            UpdateMinedCurrencies();
        }
        public void UpdateMiner(CurrencyMinerComp miner) {
            if (!availableMiners.Contains(miner)) {
                D.Err("Trying to update a miner when it should have been added instead: {0}",
                    miner.Tile);
                availableMiners.Add(miner);
            }
            UpdateMinedCurrencies();
        }

        // net worth after market adjustments (Situation.NetWorth)
        private double currentNetWorth;
        private readonly NetWorthCalculator netWorthCalc = new();
        private NetWorthCalculator.Breakdown wealthBreakdown;
        /// <summary>
        /// Every wealth bucket from the last UpdateNetWorth pass, for `economy networth` and
        /// `story raid_report` to display without recomputing.
        /// </summary>
        public NetWorthCalculator.Breakdown WealthBreakdown => wealthBreakdown;

        /// <summary>
        /// BaseNetWorth restricted to money + materials + loose/stored sellable objects, i.e.
        /// the pre-widening definition. For calibrating downstream curves (story points, quest
        /// rewards, gifts) that were tuned against the old, narrower number.
        /// </summary>
        public double BaseNetWorthLiquid => wealthBreakdown.Liquid;

        #if !PRODUCTION_BUILD
        /// <summary>Wall-clock cost of the last NetWorthCalculator.Compute() call, in ms.</summary>
        public double LastWealthSweepMs { get; private set; }
        #endif

        public double NetWorthTotal => currentNetWorth;

        public string MiningInfoText {
            get {
                if (availableMiners.Count == 0) { return null; }
                return T.CurrencyMineRateH_V1.F(Wallet.ToValueString(mineRate));
            }
        }

        public Func<string> MiningTooltip => () => {
            GetGPUStats(out var gpuActive, out var mem, out var memPerGPU);
            return $"{T.CurrencyMineGpuTip_V1.F(A.Grid.TotalGPU)}\n{T.CurrencyMineMemTip_V3.F(mem, memPerGPU, gpuActive)}";
        };

        public void UpdateNetWorth() {
            #if !PRODUCTION_BUILD
            var sweepStart = Time.realtimeSinceStartupAsDouble;
            #endif
            wealthBreakdown = netWorthCalc.Compute(S, this);
            #if !PRODUCTION_BUILD
            LastWealthSweepMs = (Time.realtimeSinceStartupAsDouble - sweepStart) * 1000d;
            #endif
            var b = wealthBreakdown;
            currentNetWorth = b.LiquidAdjusted + b.Structures + b.EquippedGear + b.BodyParts;
            S.Situation.Money = (float) (b.Money / 1_000_000d);
            S.Situation.NetWorth = (float) (currentNetWorth / 1_000_000d);
            S.Situation.BaseNetWorth = (float) (b.Total / 1_000_000d);
            S.Situation.Mats = b.MatCount;
        }
        public double WorthOf(Mat mat, bool adjusted) {
            if (mat.StackSize == 0) { return 0d; }
            var mi = MarketItem.FindFor(mat.Type.Def);
            if (mi != null) {
                if (adjusted) {
                    return S.Market.Adjusted(mi).Value * mat.StackSize;
                } else {
                    return mi.Value * mat.StackSize;
                }
            }
            return 0d;
        }
        public double WorthOf(MatType mt, int amt, bool adjusted) {
            var mi = MarketItem.FindFor(mt.Def);
            if (mi != null) {
                if (adjusted) {
                    return S.Market.Adjusted(mi).Value * amt;
                } else {
                    return mi.Value * amt;
                }
            }
            return 0d;
        }
        public double WorthOf(Def def, out MarketItem mi, bool adjusted) {
            mi = MarketItem.FindFor(def);
            if (mi != null) {
                if (adjusted) {
                    return S.Market.Adjusted(mi).Value;
                } else {
                    return mi.Value;
                }
            }
            return 0d;
        }
    }
}
