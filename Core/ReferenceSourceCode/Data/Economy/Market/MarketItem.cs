#if !PRODUCTION_BUILD
// #define DEBUG_HEAT
#endif
using System;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Diagnostics;
using Game.Mods;
using System.IO;
using Game.Constants;
using KL.Randomness;
using Game.Modifiers;
using Game.Systems.Crafting;
using Game.Data.Space;
using System.Linq;

namespace Game.Data.Economy {
    [Serializable]
    public sealed class MarketItem {
        [SelfInit]
        private static void Register() {
            All.Clear();
            CFDs.Clear();
            Stocks.Clear();
            _traderDifficultyQuantityMulCurve = null;
        }
        public static readonly Dictionary<int, MarketItem> All = new();
        public static readonly Dictionary<int, MarketItem> CFDs = new();
        public static readonly Dictionary<int, MarketItem> Stocks = new();
        private static Curve _traderDifficultyQuantityMulCurve;
        private static Curve traderDifficultyQuantityMulCurve => _traderDifficultyQuantityMulCurve ??= Tunables.TraderDifficultyQuantityMulCurve;
        [JsonEnum(typeof(Game.Data.Economy.MarketItemType))]
        public string Type;
        public MarketItemType AssetType;
        public Def Def;
        public string Id;
        public int IdH;
        public MarketGroup FirstGroup;
        public string[] Groups;
        public int[] GroupsH;
        /// <summary>
        /// Item cost cannot be cheaper than sum of all product costs.
        /// Has only items that are in the market
        /// Key: defIdH, Value: quantity divided by output multiplier
        /// </summary>
        public Dictionary<int, float> ProductOf;
        // Base value, not adjusted
        public float Value;
        public float PlayerBuyPriceMultiplier = 1f;
        public float PlayerSellPriceMultiplier = 1f;
        /// <summary>
        /// This will be multiplied by TraderQuantityCurve. Overrides Group curve.
        /// </summary>
        public float TraderQuantity;
        /// <summary>
        /// A ceiling for trader quantity, if > 0, we will never see a trader offer
        /// more than this amount of an item
        /// </summary>
        public int MaxTraderQuantity;
        public bool IsBlackMarket;
        /// <summary>
        /// If set to true, this can be bought in the online shop
        /// </summary>
        public int OnlineShopQuantity;
        public float CustomChanceInStock;
        // How likely a merchant will have this item (1 = high chance, 0 = no chance)
        public float Rarity;
        /// <summary>
        /// What is the minimum difficulty of a sector in order to find this item
        /// in trader's inventory
        /// </summary>
        public float MinSectorDifficulty;
        // Story items with their own authored routes (quests, events, special traders)
        // never come out of random loot or signal offers (MarketLootPool)
        public bool NoRandomLoot;
        public string Icon;
        public string Name;
        public string SearchName;
        public float Volatility;
        private string assetClassT;
        private string assetClassDescT;
        public string AssetClassT => assetClassT ??= $"market.asset.class.{Type}".T();
        public string AssetClassDescT => assetClassDescT ??= $"market.asset.class.{Type}.info".T();

        public static bool Exists(int id) {
            return All.ContainsKey(id);
        }

        public int OwnedPositions(MarketPortfolio p) {
            if (p == null) { return 0; }
            return p.OwnedPositions(IdH);
        }
        public override string ToString() {
            return $"MarketItem[{Id}->{Value}]";
        }

        public static void LoadKnown() {
            if (All.Count > 0) {
                D.Err("MarketItems already loaded!");
                return;
            }
            MarketGroup.LoadMarketGroups();
            LoadMarketItems();
        }
        private static Curve quantityCurve => Tunables.TraderQuantityCurve;
        private static float heatRecoveryPerDay => Tunables.MarketItemHeatRecoveryPerDay;
        private static float heatChangeRate => Tunables.MarketItemHeatChangeRate;

        /// <param name="rng">If this is null, the stock will not be randomized</param>
        /// <returns></returns>
        public int TraderStock(GameState s, Rng rng, float sectorDiff, float traderWealth) {
            // how much of this item do we want?
            var traderQ = TraderQuantity;
            if (traderQ != 1f) {
                if (traderQ == 0f) {
                    traderQ = FirstGroup.TraderQuantity;
                }
                if (traderQ == 0f) {
                    traderQ = 1f;
                } else {
                    if (rng != null) {
                        traderQ *= rng.Curve(quantityCurve) * traderWealth;
                        traderQ *= Mathf.Max(s.Difficulty.PlanetResources, 1f)
                            * ModifierResourceMul.Current;
                    }
                }
            }
            int q = Mathf.RoundToInt(traderQ);
            if (Def.MatType != null) {
                var isUpgrade = Arrays.ContainsSafe(GroupsH, MarketGroupIdH.Upgrade)
                    || Arrays.ContainsSafe(GroupsH, MarketGroupIdH.RareUpgrade);
                // special exception for fuse
                if (isUpgrade && IdH == DefIdsH.ObjUpgradesFuseUpgrade) {
                    isUpgrade = false;
                }
                if (!isUpgrade) {
                    var qMul = GetQuantityMultiplierFor(sectorDiff);
                    q = Mathf.FloorToInt(q * qMul);
                }
            }
            var maxQ = 0;
            if (MaxTraderQuantity > 0) {
                maxQ = MaxTraderQuantity;
            }
            if (maxQ == 0 && FirstGroup.MaxTraderQuantity > 0) {
                maxQ = FirstGroup.MaxTraderQuantity;
            }
            if (maxQ > 0 && q > maxQ) {
                q = maxQ;
            }
            if (q < 1) {
                q = 1;
            }
            return q;
        }
        private float GetQuantityMultiplierFor(float sectorDiff) {
            var mul = traderDifficultyQuantityMulCurve.Evaluate(sectorDiff);
            if (mul < 1f) {
                mul = 1f;
            }
            return mul;
        }
        /// <summary>
        /// When trader buys an item, the price should go lower, so positive
        /// trader inv change will produce a negative impact and vice versa
        /// </summary>
        /// <param name="heat"></param>
        /// <param name="traderInvChange"></param>
        public void AdjustHeat(GameState s, float sectorDiff, Dictionary<int, float> heat, int traderInvChange) {
            float pureStock = TraderStock(s, null, sectorDiff, 1f);
            float impact = (-traderInvChange / pureStock) * heatChangeRate;
            if (!heat.TryGetValue(IdH, out var h)) {
                h = 1f;
            }
            var globImp = impact * Tunables.MarketItemHeatGlobalImpact;
            #if DEBUG_HEAT
            D.Warn("Trade impact for item: {0}: {1}->{2}.[G:Imp: {3}] [Q: {4}]",
                Id, h, h + impact, globImp, traderInvChange);
            #endif
            h += impact;
            h = Maths.Clamp(h, 0.5f, 2f);
            heat[IdH] = h;

            s.Market.Find(IdH).Demand += globImp;
        }
        public float ReduceHeat(float heat) {
            // TODO: Here we can add extra overrides and adjustments per item
            var newHeat = Mathf.MoveTowards(heat, 1f, heatRecoveryPerDay);
            #if DEBUG_HEAT
            D.Warn("Reducing market heat for item: {0}: {1}->{2}", Id, heat, newHeat);
            #endif
            return newHeat;
        }

        public float GetHeat(Dictionary<int, float> heat) {
            if (heat == null) {
                return 1f;
            }
            if (heat.TryGetValue(IdH, out var h)) {
                return h;
            }
            return 1f;
        }

        private static void LoadMarketItems() {
            var marketItems = The.ModLoader.LoadObjects<MarketItem>(
                "Config/Economy/MarketItems");
            var groupsHTemp = new HashSet<int>();
            foreach (var mi in marketItems) {
                try {
                    if (Arrays.IsEmpty(mi.Groups)) {
                        D.Err("Skipping invalid market item, Groups are not defined! {0}", mi.Id);
                        continue;
                    }
                    groupsHTemp.Clear();
                    mi.IdH = Hashes.S(mi.Id);
                    mi.AssetType = Enums.FromString<MarketItemType>(mi.Type);
                    for (int i = mi.Groups.Length - 1; i >= 0; i--) {
                        string g = mi.Groups[i];
                        var group = MarketGroup.Find(g);
                        if (group == null) {
                            D.Err("Missing market group: {0} in item {1}. Will fallback to Material", g, (object)mi.Id);
                            group = MarketGroup.Find(MarketGroupIdH.Material);
                        }
                        if (groupsHTemp.Add(group.IdH)) {
                            if (group.IdH == MarketGroupIdH.BlackMarket) {
                                mi.IsBlackMarket = true;
                            }
                        }
                    }
                    if (groupsHTemp.Count > 0) {
                        mi.GroupsH = groupsHTemp.ToArray();
                        mi.FirstGroup = MarketGroup.Find(mi.GroupsH[0]);
                    } else {
                        D.Err("Skipping invalid market item, All Groups missing! {0}. {1}", mi.Id, string.Join(", ", mi.Groups));
                        continue;
                    }
                    if (mi.PlayerBuyPriceMultiplier <= 0.001f) {
                        mi.PlayerBuyPriceMultiplier = 1f;
                    }
                    if (mi.PlayerSellPriceMultiplier <= 0.001f) {
                        mi.PlayerSellPriceMultiplier = 1f;
                    }
                    if (mi.AssetType == MarketItemType.CFD) {
                        mi.Def = The.Defs.FastGet(mi.IdH);
                        if (mi.Def == null) {
                            D.Err("Trying to load market item without corresponding def: {0}", (object)mi.Id);
                            continue;
                        }
                        mi.Name = mi.Def.NameT;
                        mi.SearchName = mi.Def.SearchNameT;
                        mi.Icon = mi.Def.Preview;
                        if (mi.Def.IgnoreMarket || mi.Def.MatType?.IgnoreMarket == true) {
                            D.Err("Trying to load market item for def that has IgnoreMarket = true: {0}", (object)mi.Id);
                            continue;
                        }
                        LoadIngredientsFor(mi);
                        if (mi.Volatility > 0.0099f) {
                            D.Err("Volatility too high for CFD: {0}: {1}", mi.Id, mi.Volatility);
                            mi.Volatility = 0.0099f;
                        }
                        if (mi.Rarity < 0.00001f) {
                            D.Err("No Rarity on market item CFD: {0}", mi.Id);
                            mi.Rarity = 0.5f;
                        }
                        if (!CFDs.TryAdd(mi.IdH, mi)) {
                            D.Err("Failed adding market item (CFD) {0} with id {1}. Such id already exists!", mi, mi.Id);
                        }
                    } else {
                        mi.Name = mi.Id;
                        mi.SearchName = Strings.ToSearchString(mi.Name);
                        mi.Icon = IconId.CBriefcase;
                        // stock
                        if (mi.Volatility > 0.099f) {
                            D.Err("Volatility too high for stock: {0}: {1}", mi.Id, mi.Volatility);
                            mi.Volatility = 0.099f;
                        }
                        if (!Stocks.TryAdd(mi.IdH, mi)) {
                            D.Err("Failed adding market item (stock) {0} with id {1}. Such id already exists!", mi, mi.Id);
                        }
                    }

                    if (!All.TryAdd(mi.IdH, mi)) {
                        D.Err("Failed adding market item {0} with id {1} to All list. Such id already exists!", mi, mi.Id);
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed loading market item: {0}", mi.Id);
                }
            }
            // D.Warn("Loaded {0} market items", All.Count);
        }

        public static MarketItem Find(int idH) {
            if (All.TryGetValue(idH, out var mi)) {
                return mi;
            }
            D.Err("Market item not found by idH: {0}", idH);
            return null;
        }

        public static MarketItem FindFor(Def def) {
            if (def == null) { return null; }
            if (def.IgnoreMarket) { return null; }
            if (All.TryGetValue(def.IdH, out var mi)) {
                return mi;
            }
            if (def.MatType?.IgnoreMarket == true) {
                D.Err("Trying to create market item for something where IgnoreMarket is true! {0} | {1}", def, def.MatType);
            } else {
                // CreateMarketItemTemplate(def.Id);
            }

            return mi;
        }

        public static void LoadIngredientsFor(MarketItem item) {
            if (item.ProductOf != null) { return; }
            item.ProductOf = new Dictionary<int, float>();
            var craftables = Craftable.FindForDef(item.Def);
            if (craftables == null) { return; }
            Craftable cheapest = null;
            float cheapestCost = float.MaxValue;
            foreach (var cr in craftables) {
                if (cr.IgnoreMarket || cr.Ingredients == null) { continue; }
                float outmul = 1f;
                if (cr.OutputMultiplier > 1) {
                    outmul = cr.OutputMultiplier;
                }
                var cost = 0f;
                foreach (var ing in cr.Ingredients) {
                    if (ing.Type.IgnoreMarket) { continue; }
                    if (ing.Type.Def.IgnoreMarket) { continue; }
                    if (ing.Type.Def == item.Def) { continue; }
                    cost += ing.StackSize / outmul;
                }
                if (cost == 0f) {
                    D.Err("Craftable with completely 0 cost for {0}", cr.Id);
                    continue;
                }
                if (cost < cheapestCost) {
                    cheapestCost = cost;
                    cheapest = cr;
                }
            }
            if (cheapest != null) {
                foreach (var ing in cheapest.Ingredients) {
                    float outmul = 1f;
                    if (cheapest.OutputMultiplier > 1) {
                        outmul = cheapest.OutputMultiplier;
                    }
                    if (ing.Type.IgnoreMarket) { continue; }
                    if (ing.Type.Def.IgnoreMarket) { continue; }
                    if (ing.Type.Def == item.Def) { continue; }
                    item.ProductOf[ing.Type.Def.IdH] = ing.StackSize / outmul;
                }
            }
            // D.Warn("Loaded ingredients for {0}: {1}",
            //     item.Def.NameT, D.Dump(item.ProductOf));
        }

        [Conditional("UNITY_EDITOR")]
        private static void CreateMarketItemTemplate(string id) {
            var f = id.Split(new char[] {'/'});
            var file = $"{f[f.Length - 1]}.json";
            var dir = $"Config/Economy/MarketItems/{f[0]}";
            dir = ModLoader.CoreModFilePath(dir);
            var filePath = $"{dir}/{file}";
            if (File.Exists(filePath)) { return; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendLine(" FIX THIS! ");
            sb.AppendLine("{");
            sb.AppendFormat("    \"Id\" : \"{0}\",\n", id);
            sb.AppendLine(  "    \"Type\" : \"CFD\",");
            sb.AppendLine(  "    \"Groups\" : [],");
            sb.AppendLine(  "    \"Value\" : 10.0,");
            sb.AppendLine(  "    \"Rarity\" : 0.5,");
            sb.AppendLine(  "    \"Volatility\" : 0.002");
            sb.AppendLine("}");
            Files.EnsureDir(dir);
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
