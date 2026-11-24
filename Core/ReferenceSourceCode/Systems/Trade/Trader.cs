using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Input;
using Game.Procgen.Names;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.Ships;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Trade {
    [MessagePackObject]
    public sealed class Trader {
        public static uint LastId;
        [Key("Id")] public uint Id;
        [Key("SOId")] public int SOId;
        [Key("Name")] public string Name;
        [Key("InvRefreshTick")] public long InventoryRefreshTick;
        [Key("Wallet")] public Wallet Wallet;
        [Key("Wealth")] public float Wealth;
        [Key("MinCash")] public float MinCash;
        [Key("MinItems")] public int MinItems;
        [Key("FactionId")] public int FactionId;
        /// <summary>
        /// If you sell a lot of some item, the heat gets reduced, as you
        /// oversaturated the market. You then sell it at lower price,
        /// buy buying price remains unchanged.
        ///
        /// If you buy a lot of same item, heat goes up, you pay more for that item
        /// but if you're selling, the price remains unchanged.
        ///
        /// heat will be lerped towards 1f every day by some amount
        /// </summary>
        [Key("ItemHeat")] public Dictionary<int, float> ItemHeat;
        [Key("Markup")] public float Markup;
        /// <summary>
        /// Markup after the alliance discount. Applied at read time rather than baked
        /// into Markup at creation, so it tracks alliances forming and breaking during
        /// this trader's lifetime. The discount is multiplicative, which is what keeps
        /// an allied buy price above market no matter how Markup rolled.
        /// </summary>
        [IgnoreMember] public float EffectiveMarkup {
            get {
                var dip = A.S?.Sys?.Diplomacy;
                if (dip == null) { return Markup; }
                if (!dip.IsAlliedWithPlayer(Faction)) { return Markup; }
                return Markup * Tunables.AllianceTraderMarkupMul;
            }
        }
        private static readonly Dictionary<int, float> tempItemHeat = new();
        private FactionDef faction;
        private Curve refreshCurve => Tunables.TraderInventoryRefreshCurve;
        [IgnoreMember] public string RefreshLine => T.MerchantInvRefreshTime_V1.F(
            Texts.Accented(Units.TicksToTime(InventoryRefreshTick - G.Ticks)));

        public void UpdateInventoryRefreshTick(long ticks, Rng rng) {
            InventoryRefreshTick =
                ticks + (int) (refreshCurve.Evaluate(rng.Value) * Consts.TicksPerDay);
        }

        // Copy of NPCShip.WillTradeWith
        public bool WillTradeWith(NPCShip ship, out TradeRefuseReason reason) {
            reason = TradeRefuseReason.None;
            var isShipLegal = ship.IsClassValid;
            if (isShipLegal) { return true; }
            var chance = Faction.ChanceTradeWithOutlaw;
            if (chance == 1f) { return true; }
            reason = TradeRefuseReason.Outlaw;
            if (chance == 0f) { return false; }
            return ship.SeededRng(Primes.Prime028).Chance(chance);
        }

        public string FullTooltip(GameState s) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(Name);
            sb.AppendFormat(" ({0})", Faction.NameT);
            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat(" - {0}", Texts.Green(Wallet.ToValueString(Wallet.Amount)));

            var hashes = Caches.IntSet;
            hashes.Clear();
            foreach (var item in Inventory.OrderBy((i) => i.Name)) {
                if (item.Quantity < 1) { continue; }
                if (hashes.Add(item.Def.IdH)) {
                    sb.AppendFormat("\n - {0} {1}", item.Def.CodexLink,
                        Texts.Grey(Units.XNum(item.Quantity)));
                }
            }
            if (s.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketPrediction)) {
                sb.Append(GlyphMap.BRHR);
                sb.Append(RefreshLine);
            }

            return sb.ToString();
        }
        [IgnoreMember] public FactionDef Faction {
            get {
                if (FactionId == 0) {
                    FactionId = FactionIdH.Trader;
                }
                faction ??= FactionDef.FastGet(FactionId);
                return faction;
            }
            set {
                if (value == null) {
                    D.Err("Trying to set null faction on a trader! Fallback to Trader");
                    faction = FactionDef.FastGet(FactionIdH.Trader);
                    FactionId = faction.IdH;
                } else {
                    faction = value;
                    FactionId = value.IdH;
                }
            }
        }

        public double NetWorth(GameState s) {
            var nw = Wallet.Amount;
            foreach (var item in Inventory) {
                nw += s.Market.ValueOf(item.Def) * item.Quantity;
            }
            return nw;
        }

        /// <summary>
        /// Key: market group id hash
        /// Value: market group price adjustment. How much more expensive or cheap this merchant has on the whole group
        /// </summary>
        [Key("TradeableItems")] public Dictionary<int, float> TradeableGroups;
        [Key("AdditionalGroupCostAdjustment")] public Dictionary<int, float> AdditionalGroupCosts;
        /// <summary>
        /// Actual inventory
        /// </summary>
        [Key("Inventory")] public List<TradeableItem> Inventory;
        [Key("IsOnline")] public bool IsOnline;

        public void GenerateName(GameState s, Rng rng) {
            var species = Species.Get(SpeciesIdH.Human);
            var req = NameRequest.ForSpecies(species, BeingType.Trader, rng);
            var nameRes = s.Query.GenerateName.Ask(req);
            var name = nameRes.BeingName;
            name.UpdateName();
            Name = name.FullName;
        }

        public void UpdatePriceFor(GameState s, TradeableItem ti) {
            if (ti.IsBeing) {
                // Priced from its parts, never from the market (see BeingTrade)
                ti.Heat = 1f;
                ti.BeingPrice = BeingTrade.PriceOf(s, this, ti);
                ti.BeingTooltip = null;
                ti.PriceOfOne = ti.BeingPrice.Total;
                return;
            }
            var mi = ti.MarketItem;
            var gp = GroupPriceFor(ti);
            var heat = ti.MarketItem.GetHeat(ItemHeat);
            ti.Heat = heat;
            var marketPrice = s.Market.Adjusted(mi).Value;
            var adjPrice = marketPrice * gp;

            var minPrice = marketPrice * 0.5d;
            if (ti.IsInventoryItem) {
                adjPrice *= ti.Quality;
                minPrice = 1d;
            }
            if (ti.IsBodyPart) {
                adjPrice *= ti.Quality;
                adjPrice *= ti.Condition;
                minPrice = 1d;
            }

            var scarcityAdj = s.Query.GetScarcityAdjustment.Ask(this, ti);
            // if (scarcityAdj != 1f) {
            //     D.Warn("Adjusting price due to scarcity of item: {0} : {1},",
            //         ti.Id, scarcityAdj);
            // }
            adjPrice *= scarcityAdj;

            double limitedPrice;
            if (IsOnline) {
                limitedPrice = adjPrice;
                if (limitedPrice < minPrice) {
                    limitedPrice = minPrice;
                }
            } else {
                limitedPrice = Maths.Clamp(adjPrice, minPrice, marketPrice * 2d);
            }

            // already applied in group price
            // limitedPrice = PriceWithMarkup(ti, limitedPrice);

            // Applied after the market clamp above on purpose - a corpse's suit
            // being cheap is not a market anomaly to be corrected away.
            limitedPrice *= ItemOwnership.PriceMulFor(ti.Origin);

            ti.PriceOfOne = limitedPrice;
        }

        private float PriceWithMarkup(TradeableItem.ItemOwner owner, float priceBeforeMarkup) {
            var markup = EffectiveMarkup;
            if (owner == TradeableItem.ItemOwner.Us) {
                return priceBeforeMarkup * (1f - markup);
            } else {
                return priceBeforeMarkup * (1f + markup);
            }
        }

        public float MarkupFor(TradeableItem.ItemOwner owner) {
            return PriceWithMarkup(owner, 1f);
        }

        public float GroupPriceFor(TradeableItem item) {
            if (TryGroupPriceFor(item.MarketItem, item.Owner, out var price)) {
                return price;
            }
            D.Err("Price adjustment not found for tradable item {0} for {1}", item, this);
            return PriceWithMarkup(item.Owner, 1f);
        }

        // false when this trader deals in none of the item's groups
        public bool TryGroupPriceFor(MarketItem mi, TradeableItem.ItemOwner owner, out float price) {
            if (IsOnline) {
                price = PriceWithMarkup(owner, 1f);
                return true;
            }
            foreach (var g in mi.GroupsH) {
                if (TradeableGroups.TryGetValue(g, out var priceAdj))  {
                    price = PriceWithMarkup(owner, priceAdj);
                    return true;
                } else if (AdditionalGroupCosts != null
                        && AdditionalGroupCosts.TryGetValue(g, out priceAdj)) {
                    price = priceAdj;
                    return true;
                }
            }
            price = 0f;
            return false;
        }

        public void OnMarketDayChanged() {
            if (ItemHeat == null) { return; }
            tempItemHeat.Clear();
            foreach (var h in ItemHeat) {
                if (MarketItem.CFDs.TryGetValue(h.Key, out var mi)) {
                    var reduced = mi.ReduceHeat(h.Value);
                    if (Maths.Approximately(reduced, 1f)) {
                        tempItemHeat[h.Key] = 0f;
                    } else {
                        tempItemHeat[h.Key] = reduced;
                    }
                } else {
                    tempItemHeat[h.Key] = 0f;
                }
            }

            foreach (var k in tempItemHeat) {
                if (k.Value == 0f) {
                    ItemHeat.Remove(k.Key);
                } else {
                    ItemHeat[k.Key] = k.Value;
                }
            }
        }

        public override string ToString() {
            return $"Trader[so:{SOId}|w:{Wealth}|{Name}]";
        }
    }
}
