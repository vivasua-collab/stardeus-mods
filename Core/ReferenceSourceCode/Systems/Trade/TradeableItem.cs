using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Trade {
    [MessagePackObject]
    public sealed class TradeableItem {
        public enum ItemOwner {
            Us,
            Them,
        }
        /// <summary>
        /// Unique tradeable item id. Items with obj + color will use a hash of MarketItemId + color
        /// </summary>
        [Key("Id")] public int Id;
        [Key("MIId")] public int MarketItemId;
        [Key("Quantity")] public int Quantity;
        [Key("Owner")] public ItemOwner Owner;
        [Key("Color")] public Color Color;
        [Key("Quality")] public float Quality;
        [Key("BPCondition")] public float Condition;
        /// <summary>
        /// Trader's being offers only: the individual is regenerated from this seed
        /// (see BeingTrade.EnsureBeing)
        /// </summary>
        [Key("BeingSeed")] public int BeingSeed;
        /// <summary>
        /// Name of the being behind BeingSeed (BeingName.Serialize). The legacy name generator
        /// draws from a shared shrinking pool, so the seed alone does not bring the same name back.
        /// </summary>
        [Key("BeingName")] public string BeingName;
        /// <summary>
        /// Our being for sale, or the generated individual behind a trader's being offer
        /// </summary>
        [IgnoreMember] public BeingData Being;
        /// <summary>
        /// Set by Trader.UpdatePriceFor, so the tooltip (rebuilt every frame while hovered)
        /// never has to price every body part again
        /// </summary>
        [IgnoreMember] public BeingTrade.Price BeingPrice;
        /// <summary>
        /// Finished tooltip text, cleared whenever the price is updated
        /// </summary>
        [IgnoreMember] public string BeingTooltip;
        private string beingSearchName;
        [IgnoreMember] public string BeingSearchName => beingSearchName ??=
            Strings.ToSearchString(Being?.Mind?.Name?.FullNameOrNameless ?? string.Empty);
        private MarketItem marketItem;
        [IgnoreMember] public MarketItem MarketItem {
            get {
                if (marketItem == null) {
                    if (!MarketItem.CFDs.TryGetValue(MarketItemId, out marketItem)
                            && !BeingTrade.TryGetMarketItem(MarketItemId, out marketItem)) {
                        D.Err("Failed to get market item by id: {0}", MarketItemId);
                    }
                }
                return marketItem;
            }
        }
        [IgnoreMember] public bool IsBeing => Def.IsBeing;
        [IgnoreMember] public string Icon => MarketItem.Icon;
        [IgnoreMember] public MatType MatType => MarketItem.Def.MatType;
        [IgnoreMember] public Def Def => MarketItem.Def;
        [IgnoreMember] public bool IsInventoryItem => InventoryItemDef.HasForDef(Def.IdH);
        [IgnoreMember] public bool IsBodyPart => BodyPartDef.HasForDef(Def.IdH);
        [IgnoreMember] public string Name => Def.NameT;
        /// <summary>
        /// Which provenance pile OurObjData falls into. Untracked (no grouping, no
        /// discount) for trader stock, weapons, body parts, and anything that
        /// doesn't track ownership - see ItemOwnership.TracksOwnership.
        /// </summary>
        [IgnoreMember] public ItemOriginBucket Origin => ItemOwnership.BucketFor(OurObjData);

        private string decoratedName;
        [IgnoreMember] public string DecoratedName {
            get {
                if (decoratedName == null) {
                    decoratedName = Def.NameT;
                    if (MatType != null) {
                        return decoratedName;
                    }
                    if (Being != null) {
                        // The species is already in the group header and the tooltip bio
                        decoratedName = Being.Mind?.Name?.ShortName ?? Def.NameT;
                        if (BeingTrade.IsWreck(Being)) {
                            decoratedName = $"{decoratedName} {Texts.GreyParentheses(T.TradeBeingWreck)}";
                        }
                        return decoratedName;
                    }
                    var q = Units.Percentage(Quality, false);
                    q = Texts.Grey(q);
                    decoratedName = $"{decoratedName} {q}";

                    if (IsBodyPart) {
                        if (OurObjData != null) {
                            var mind = OurObjData.Mind;
                            if (mind != null) {
                                decoratedName = q;
                                var fac = mind.Faction ?? FactionDef.Player;
                                decoratedName = $"{decoratedName} {mind.Faction.Glyph}";
                                var name = mind.Name.ShortName;
                                if (fac.IsPlayer) {
                                    name = Texts.Green(name);
                                } else {
                                    name = Texts.Red(name);
                                }
                                decoratedName = $"{decoratedName} {name}";
                            }
                        }
                    }
                    if (OurObjData != null) {
                        // Only worn equipment picks up a marker - TracksOwnership
                        // gates it out for body parts and anything else here.
                        decoratedName = ItemOwnership.DecoratedItemName(
                            OurObjData, null, decoratedName, markNew: true);
                    }
                    return decoratedName;
                }
                return decoratedName;


            }
        }
        [IgnoreMember] public string GroupName => MarketItem.FirstGroup.NameT;
        /// <summary>
        /// Only used when we are selling OUR items
        /// </summary>
        [IgnoreMember] public ObjData OurObjData;
        [IgnoreMember] public double PriceOfOne { set; private get; }
        [IgnoreMember] public float Heat;
        /// <summary>
        /// Don't trust this value! Always set / get the actual quantity from Selling / Buying dictionary!
        /// This instance can be used as a dictionary key, and multiple instances can
        /// get created! That's by (admittedly shitty) design.
        /// </summary>
        [IgnoreMember] public int QuantitySelected;

        public double FinalPriceOfOne(bool isBuying) {
            // D.Warn("Item heat: {0} -> {1}", Id, Heat);
            var price = PriceOfOne;
            if (isBuying) {
                if (Heat > 1f) {
                    price *= Heat;
                }
            } else {
                if (Heat < 1f) {
                    price *= Heat;
                }
            }
            var multiplier = isBuying
                ? MarketItem.PlayerBuyPriceMultiplier
                : MarketItem.PlayerSellPriceMultiplier;
            return price * multiplier;
        }

        public static TradeableItem Create(MarketItem mi, int quantity, ItemOwner owner) {
            var ti = new TradeableItem();
            ti.marketItem = mi;
            ti.Id = mi.IdH;
            ti.MarketItemId = mi.IdH;
            ti.Quantity = quantity;
            ti.Owner = owner;
            return ti;
        }

        public static string SortText(TradeSortMode sortMode) {
            switch (sortMode) {
                case TradeSortMode.Group: return T.SortGroup;
                case TradeSortMode.Alphabetic: return T.SortName;
                case TradeSortMode.CountsAsc: return $"{T.SortCount} {GlyphMap.ArrowUp}";
                case TradeSortMode.CountsDesc: return $"{T.SortCount} {GlyphMap.ArrowDown}";
                case TradeSortMode.ValueAsc: return $"{T.SortValue} {GlyphMap.ArrowUp}";
                case TradeSortMode.ValueDesc: return $"{T.SortValue} {GlyphMap.ArrowDown}";
                default: {
                    D.Err("Untranslated sort mode: {0}", sortMode);
                    return sortMode.ToString();
                }
            }
        }
        public static string FilterText(TradeFilterMode filterMode) {
            switch (filterMode) {
                case TradeFilterMode.All: return T.All;
                case TradeFilterMode.Food: return T.Food;
                case TradeFilterMode.Upgrades: return T.Upgrades;
                case TradeFilterMode.Equipment: return T.Equipment;
                case TradeFilterMode.BodyParts: return T.NounBodyParts;
                case TradeFilterMode.Materials: return T.MenuMaterials;
                case TradeFilterMode.Beings: return T.Beings;
                case TradeFilterMode.Selected: return T.Selected;
                case TradeFilterMode.NotSelected: return T.NotSelected;
                default: {
                    D.Err("Untranslated filter mode: {0}", filterMode);
                    return filterMode.ToString();
                }
            }
        }

        public static IOrderedEnumerable<TradeableItem> SortedBy(
                List<TradeableItem> input, TradeSortMode mode, bool isBuying) {
            switch (mode) {
                case TradeSortMode.ValueDesc: {
                    return input.OrderByDescending(i => i.Quantity * i.FinalPriceOfOne(isBuying));
                }
                case TradeSortMode.ValueAsc: {
                    return input.OrderBy(i => i.Quantity * i.FinalPriceOfOne(isBuying));
                }
                case TradeSortMode.CountsDesc: {
                    return input.OrderByDescending(i => i.Quantity);
                }
                case TradeSortMode.CountsAsc: {
                    return input.OrderBy(i => i.Quantity);
                }
                case TradeSortMode.Alphabetic: {
                    // ThenBy(Origin) keeps a def's New/Second-hand/Dead man's rows
                    // adjacent - TradeItemLister emits views in first-encounter
                    // order, so this is what keeps the three lists together.
                    return input.OrderBy(i => i.Name).ThenBy(i => i.Origin);
                }
                case TradeSortMode.Group : {
                    return input.OrderBy(i => i.GroupName).ThenBy(i => i.Name)
                        .ThenBy(i => i.Origin)
                        .ThenByDescending(i => i.FinalPriceOfOne(isBuying));
                }
            }
            D.Err("Unsupported Sort Mode: {0}", mode);
            return null;
        }

        public TradeableItem Take(int count) {
            var copy = new TradeableItem();
            copy.Id = Id;
            copy.MarketItemId = MarketItemId;
            copy.marketItem = marketItem;
            copy.Quantity = count;
            copy.PriceOfOne = PriceOfOne;
            copy.Heat = Heat;
            copy.Color = Color;

            Quantity -= count;
            return copy;
        }

        public override int GetHashCode() {
            if (MatType != null) {
                return Hashes.Quick((int) Owner, MatType.IdH);
            }
            if (OurObjData != null) {
                return (int) OurObjData.Id;
            }
            // Our being rows are rebuilt on every panel reload, and the new instance has to
            // find the selection made on the old one
            if (Owner == ItemOwner.Us && Being != null) {
                return Hashes.Quick((int) Being.Id, Primes.Prime002);
            }
            return base.GetHashCode();
        }

        public override bool Equals(object obj) {
            if (obj == null || obj is not TradeableItem ti) {
                return false;
            }
            if (MatType != null && ti.MatType != null) {
                return Owner == ti.Owner && MatType == ti.MatType;
            }
            if (OurObjData != null && ti.OurObjData != null) {
                return OurObjData == ti.OurObjData;
            }
            if (Owner == ItemOwner.Us && ti.Owner == ItemOwner.Us
                    && Being != null && ti.Being != null) {
                return Being == ti.Being;
            }
            return base.Equals(obj);
        }

        public override string ToString() {
            return $"TradeableItem[{Owner}|{MarketItem.Id}|{PriceOfOne}x{Quantity}]";
        }

        public Obj SpawnAt(GameState S, int goodsDropPos) {
            var item = this;
            if (IsBeing) {
                BeingTrade.Spawn(S, this, goodsDropPos);
                return null;
            }
            if (item.MatType == null) {
                return EntityUtils.SpawnNonMatObj(
                    Pos.ToVector(goodsDropPos) + Rng.UInsideUnitCircle() * 0.25f,
                        item.Def, item.Quality, item.Color);
            } else {
                //var storages = S.Sys.Inventory.Storages;
                var matType = item.MatType;
                var mat = new Mat {
                    Type = matType,
                    StackSize = item.QuantitySelected,
                    MaxStackSize = item.QuantitySelected,
                };
                return EntityUtils.SpawnRawMaterial(mat, goodsDropPos, 0.25f, true)?.Obj;
            }
        }
    }
}
