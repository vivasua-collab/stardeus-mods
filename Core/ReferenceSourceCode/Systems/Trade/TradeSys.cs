using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Story.Events;
using Game.Systems.BodyParts;
using Game.Systems.Crafting;
using Game.Systems.Items;
using Game.Systems.Notify;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Microsoft.CodeAnalysis.Operations;
using UnityEngine;

namespace Game.Systems.Trade {
    public sealed class TradeSys : GameSystem, ISaveableSpecial {
        public const string SysId = "TradeSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TradeSys());
        }
        private Dictionary<uint, Trader> traders;
        /// <summary>
        /// Key = space object id
        /// </summary>
        private Dictionary<int, MatAvailability> matAvailability;

        private List<OnlineOrder> onlineOrders;
        private List<OnlineOrder> onlineOrdersCompleted;

        private static readonly Curve bodyPartQualityCurve = new float[] {
            0.0f, 0.1f,
            0.1f, 0.3f,
            0.2f, 0.5f,
            0.3f, 0.7f,
            0.4f, 0.75f,
            0.5f, 0.8f,
            0.6f, 0.85f,
            0.8f, 0.9f,
            1.0f, 1.0f,
        };
        private static readonly Curve itemQualityCurve = new float[] {
            0.0f, 0.5f,
            0.1f, 0.6f,
            0.5f, 0.8f,
            0.8f, 0.9f,
            1.0f, 1.0f,
        };

        protected override void OnInitialize() {
            Trader.LastId = 0;
            matAvailability = new();
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Sig.SetMatAvailability.AddListener(OnSetMatAvailability);
        }

        private void OnLoad(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.CreateTrader.SetResponder(CreateTrader);
            state.Query.GetOnlineOrders.SetResponder(GetOnlineOrders);
            state.Query.OnlinePriceFor.SetResponder(GetOnlinePrice);
            state.Query.GetOnlineOrderForCapsule.SetResponder(GetOnlineOrderForCapsule);
            state.Query.GetOngoingTrades.SetResponder(GetOngoingTrades);
            state.Query.GetMatAvailabilityFor.SetResponder(GetMatAvailabilityFor);
            state.Query.GetScarcityAdjustment.SetResponder(GetScarcityAdjustment);
            state.Query.FindTrader.SetResponder(FindTrader);
            state.Sig.RemoveTrader.AddListener(RemoveTrader);
            state.Query.CreateRandomTrade.SetResponder(CreateRandomTrade);
            state.Query.CreateOnlineTrade.SetResponder(CreateOnlineTrade);
            state.Query.StartTrade.SetResponder(StartTrade);
            state.Query.StartTradeLocal.SetResponder(StartTrade);
            state.Sig.LoadOurTradeableItemsFor.AddListener(LoadOurItems);
            state.Sig.MarketDayChanged.AddListener(OnMarketDayChanged);
            state.Sig.SpaceObjectNearby.AddListener(OnSONearby);
            state.Sig.TradeExecuted.AddListener(OnTradeExecuted);
        }

        private OnlineOrder GetOnlineOrderForCapsule(int capsuleId) {
            if (onlineOrdersCompleted == null) {
                return null;
            }
            foreach (OnlineOrder order in onlineOrdersCompleted) {
                if (order.CapsuleId == capsuleId) {
                    return order;
                }
            }
            return null;
        }

        private List<OnlineOrder> GetOnlineOrders(bool completed) {
            if (completed) {
                return onlineOrdersCompleted;
            } else {
                return onlineOrders;
            }
        }

        private readonly Dictionary<OnlineOrder, NotificationHandle> pendingOrderNotifications = new();

        private void OnTick(long ticks) {
            if (Arrays.IsEmpty(onlineOrders)) { return; }
            for (int i = onlineOrders.Count - 1; i >= 0; i--) {
                OnlineOrder order = onlineOrders[i];
                if (order.DeliveryTick < ticks) {
                    DeliverOrder(order);
                } else {
                    if (!pendingOrderNotifications.TryGetValue(order, out var ev)) {
                        CreateNotificationFor(order);
                    }
                    order.UpdateDeliveryText(S);
                }
            }
        }

        private void CreateNotificationFor(OnlineOrder order) {
            var notif = Notification.Build(order.Icon, order.Title, 0, NotificationType.Info)
                .Group("on_shop", title: T.ResearchEconomyOnlineshop, icon: IconId.CStarcreds)
                .Sticky()
                .WithOnClick(S.Sig.ToggleOnlineShopUI.Send)
                .Show();
            pendingOrderNotifications.Add(order, notif);
        }

        private void DeliverOrder(OnlineOrder order) {
            onlineOrders.Remove(order);
            onlineOrdersCompleted ??= new();
            onlineOrdersCompleted.Add(order);
            order.CapsuleId = -1;
            if (order.SpaceObjectId == S.Universe.Player.Parent.Id) {
                var evt = StoryEventDef.Get("Space/StorageCapsule");
                var data = StoryEventData.CreateFor(evt);
                if (S.Query.StartStoryEvent.Ask(data)) {
                    if (data.TargetObj != null) {
                        order.CapsuleId = data.TargetObj.Obj.Id;
                        S.Sig.ShopOrderDelivered.Send(order);
                    }
                }
            }
            S.Sig.ShopOrderDelivered.Send(order);
            if (pendingOrderNotifications.TryGetValue(order, out var ev)) {
                Notification.Remove(ref ev);
                pendingOrderNotifications.Remove(order);
            }
        }

        private void OnTradeExecuted(TradeUI trade) {
            if (trade.HasBoughtSomething && trade.OnlineOrder != null) {
                onlineOrders ??= new();
                onlineOrders.Add(trade.OnlineOrder);
                S.Sig.ShopOrderCreated.Send(trade.OnlineOrder);
            }
        }

        private Dictionary<Trader, TradeUI> GetOngoingTrades() {
            return OngoingTrades;
        }

        private double GetOnlinePrice(TradeableItem item) {
            float adj = 1f;
            var so = S.Universe.Player.Parent;
            var avail = GetMatAvailabilityFor(so);
            if (avail.Availabilities != null) {
                var coreMat = GetCoreMatFor(item);
                if (coreMat != null) {
                    adj = avail.PriceAdjustmentFor(coreMat);
                }
            }
            var mi = item.MarketItem;
            var price = S.Market.Adjusted(mi).Value;
            price *= adj;

            return price;
        }

        private void OnSONearby(SpaceObject so, bool added) {
            if (added || OngoingTrades.Count == 0) { return; }
            if (!added) {
                bool removedTrade;
                int attempts = 10;
                do {
                    removedTrade = false;
                    foreach (var tr in OngoingTrades) {
                        var trader = tr.Key;
                        if (trader.SOId > 0 && trader.SOId == so.Id) {
                            // we are no longer near the trader
                            tr.Value.EndTrade();
                            removedTrade = true;
                            break;
                        }
                    }
                } while (removedTrade && attempts-- > 0);
            }
        }

        private float GetScarcityAdjustment(Trader trader, TradeableItem item) {
            if (item.MatType == null) {
                return 1f;
            }
            SpaceObject so = null;
            if (trader.SOId > 0) {
                so = S.Universe.Find(trader.SOId);
            }
            if (so == null) {
                so = S.Universe.Player.Parent;
            }
            var avail = GetMatAvailabilityFor(so);
            if (avail.Availabilities == null) {
                return 1f;
            }

            var coreMat = GetCoreMatFor(item);
            if (coreMat == null) {
                return 1f;
            }
            var adj = avail.PriceAdjustmentFor(coreMat);
            // if (adj != 1f) {
            //     D.Warn("Scarcity adjustment for mat {0} ({1}): {2}", item.Id, coreMat.NameT, adj);
            // }
            return adj;
        }

        private MatType GetCoreMatFor(TradeableItem item) {
            if (item.MatType.Deposit != null) {
                // this can be directly modified
                return item.MatType;
            }

            var craftables = Craftable.FindForDef(item.Def);
            if (craftables == null) {
                return null;
            }

            // see if there is a crafting recipe that uses one of the modified material
            foreach (var cr in craftables) {
                if (cr.IgnoreMarket) { continue; }
                // FIXME make it nicer some day
                if (cr.Type == "Ore") {
                    return cr.Ingredients[0].Type;
                }
            }

            return null;
        }

        private void OnSetMatAvailability(SpaceObject so, MatAvailability avail) {
            if (avail.Availabilities == null) {
                matAvailability.Remove(so.Id);
                return;
            }
            // D.Warn("Setting mat availability for {0}: {1}", so.Biome, avail);
            matAvailability[so.Id] = avail;
        }

        private MatAvailability GetMatAvailabilityFor(SpaceObject so) {
            if (so == null) {
                return default;
            }
            var typeIdH = so.Type.IdH;
            if (typeIdH == SpaceObjectTypeIdH.Region) {
                if (matAvailability.TryGetValue(so.Id, out var res)) {
                    return res;
                }
            }
            return GetMatAvailabilityFor(so.Parent);
        }

        private void OnMarketDayChanged() {
            if (traders == null) { return; }
            foreach (var trader in traders.Values) {
                trader.OnMarketDayChanged();
            }
        }

        private void LoadOurItems(Trader trader, List<TradeableItem> items) {
            if (trader.Inventory == null) {
                D.Err("Trying to load out items for trader without inventory! {0}", trader);
                return;
            }
            Caches.MatCounts.Clear();
            S.Sys.Inventory.LoadRemainingMaterials(Caches.MatCounts);
            foreach (var m in Caches.MatCounts) {
                // we don't have this material right now
                if (m.Value == 0) { continue; }
                if (m.Key.IgnoreMarket) { continue; }
                if (m.Key.Def.IgnoreMarket) { continue; }
                var mi = MarketItem.FindFor(m.Key.Def);
                if (mi == null) { continue; }
                if (!trader.TradeableGroups.Keys.ContainsAny(mi.GroupsH)) {
                    continue;
                }
                var item = TradeableItem.Create(mi, m.Value,
                    TradeableItem.ItemOwner.Us);
                trader.UpdatePriceFor(S, item);
                items.Add(item);
            }
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in objs.Values) {
                if (!obj.CanSell) { continue; }
                var mi = MarketItem.FindFor(obj.Def);
                if (mi == null) { continue; }
                if (!trader.TradeableGroups.Keys.ContainsAny(mi.GroupsH)) {
                    continue;
                }
                if (obj.BodyPartState != null && obj.BodyPartState.Condition == 0f) {
                    continue;
                }
                var item = TradeableItem.Create(mi, 1,
                    TradeableItem.ItemOwner.Us);
                item.Color = obj.Color;
                item.OurObjData = obj;
                if (item.IsInventoryItem) {
                    item.Quality = obj.Quality;
                } else if (item.IsBodyPart) {
                    if (obj.BodyPartState != null) {
                        item.Quality = obj.BodyPartState.Quality;
                        item.Condition = obj.BodyPartState.Condition;
                    } else {
                        item.Quality = obj.Quality;
                        item.Condition = 1f;
                    }
                }
                trader.UpdatePriceFor(S, item);
                items.Add(item);
            }
            BeingTrade.LoadOurBeings(S, trader, items);
        }

        private void RemoveTrader(Trader tr) {
            if (tr == null) {
                D.Err("Trying to remove a null trader!");
                return;
            }
            if (traders == null) {
                D.Err("Trying to remove a trader while traders is null!", traders);
                return;
            }
            traders.Remove(tr.Id);
        }

        private TradeUI CreateRandomTrade(Rng rng, FactionDef fac) {
            var req = new TraderRequest();
            req.HasNoSO = true;
            req.WealthFactor = TraderRequest.WealthCurveMid.Evaluate(rng.Value);
            req.Faction = fac;
            req.Rng = rng;
            var trader = CreateTrader(req);
            return StartTrade(trader);
        }

        private TradeUI CreateOnlineTrade() {
            if (onlineTrader == null) {
                CreateOnlineTrader();
            }
            onlineTrader.Markup = Tunables.OnlineShopPriceMarkup * S.Difficulty.Level;
            if (G.Ticks > onlineTrader.InventoryRefreshTick) {
                GenInventoryForOnlineTrader();
            }
            return StartTrade(onlineTrader);
        }

        private Trader onlineTrader;


        private void CreateOnlineTrader() {
            // D.Warn("Creating trader: {0}", req);
            var trader = new Trader();
            trader.Id = ++Trader.LastId;
            traders ??= new();
            AddToTraderList(trader);
            trader.Faction = FactionDef.FastGet(FactionIdH.Capitalist);
            trader.Name = T.ResearchEconomyOnlineshop;
            trader.TradeableGroups = new();
            trader.Inventory = new();
            trader.Wallet = new Wallet();
            trader.Wealth = 0f;
            trader.IsOnline = true;
            onlineTrader = trader;

            GenInventoryForOnlineTrader();
        }

        private void GenInventoryForOnlineTrader() {
            onlineTrader.Inventory.Clear();
            foreach (var mi in MarketItem.CFDs.Values) {
                if (mi.OnlineShopQuantity < 1) { continue; }
                var q = mi.OnlineShopQuantity * Rng.URange(0.9f, 1.1f);
                // adjust by scarcity
                var matItem = TradeableItem.Create(mi, Mathf.RoundToInt(q),
                    TradeableItem.ItemOwner.Them);
                onlineTrader.Inventory.Add(matItem);
            }
            onlineTrader.UpdateInventoryRefreshTick(G.Ticks, Rng.Unseeded);
        }

        private void AddToTraderList(Trader trader) {
            traders ??= new();
            if (!traders.TryAdd(trader.Id, trader)) {
                D.Err("Traders already contain someone with ID: {0}. Possible Trader.LastId mismatch! Auto-fixing Trader.LastID", trader.Id);
                Trader.LastId = traders.Keys.Max();
                trader.Id = ++Trader.LastId;
                if (!traders.TryAdd(trader.Id, trader)) {
                    D.Err("PANIC! Can't insert trader with id: {0}. {1}", trader.Id,
                        D.Dump(traders));
                }
            }
        }

        private Trader FindTrader(uint id) {
            if (traders == null) {
                D.Err("Trying to find a trader by id {0} while traders is null!", id);
                return null;
            }
            if (traders.TryGetValue(id, out var tr)) {
                // let's see if inventory has to refresh
                if (tr.InventoryRefreshTick == 0) {
                    D.Err("Inventory refresh tick was 0 for trader: {0}", tr);
                    tr.UpdateInventoryRefreshTick(G.Ticks, Rng.Unseeded);
                } else if (G.Ticks > tr.InventoryRefreshTick) {
                    RefreshInventoryFor(tr);
                }
                return tr;
            }
            return null;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd?.Trade != null) {
                traders = sd.Trade.Traders ?? new();
                Trader.LastId = sd.Trade.LastTraderId;
                if (sd.Trade.MatAvailability != null) {
                    matAvailability = sd.Trade.MatAvailability;
                }
                foreach (var t in traders) {
                    if (t.Value.IsOnline) {
                        if (onlineTrader != null) {
                            D.Err("Trying to overwrite online trader: {0} -> {1}",
                                onlineTrader, t.Value);
                        }
                        onlineTrader = t.Value;
                    }
                    if (t.Value.Markup == 0f) {
                        t.Value.Markup = Rng.UCurve(Tunables.TraderMarkupCurve);
                    }
                }
                onlineOrders = sd.Trade.OnlineOrders;
                onlineOrdersCompleted = sd.Trade.OnlineOrdersCompleted;
            } else {
                traders = new();
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Trade ??= new();
            sd.Trade.LastTraderId = Trader.LastId;
            sd.Trade.Traders = traders;
            sd.Trade.MatAvailability = matAvailability;
            sd.Trade.OnlineOrders = onlineOrders;
            sd.Trade.OnlineOrdersCompleted = onlineOrdersCompleted;
        }

        public readonly Dictionary<Trader, TradeUI> OngoingTrades = new();

        private TradeUI StartTrade(Trader trader) {
            return StartTrade(trader, 0);
        }

        private TradeUI StartTrade(Trader trader, int pos) {
            // GenMerchant.RefreshInventory(S, merchant);
            UIUtils.CloseFullScreenUI();
            if (OngoingTrades.TryGetValue(trader, out var ongoing)) {
                if (!ongoing.IsComplete) {
                    ongoing.Show();
                    return ongoing;
                }
                // A completed trade unregisters itself via TradeEnded, so finding
                // one here means it ended before it was registered. Drop it.
                OngoingTrades.Remove(trader);
            }
            var trade = new TradeUI(trader, pos, this);
            // Register before Show(), which can end the trade synchronously
            // (trader too far, ship combat) and needs TradeEnded to find it.
            OngoingTrades.Add(trader, trade);
            trade.Show();
            // S.Sig.StartTrade.Send(trade);
            if (!trade.IsComplete) {
                S.Clock.Pause();
            }
            return trade;
        }

        private int traderMinCash => Tunables.TraderMinCash;
        private int traderMaxCash => Tunables.TraderMaxCash;
        private int traderMinItems => Tunables.TraderMinItems;
        private int traderMinGroups => Tunables.TraderMinGroups;
        private Curve marketGroupAdjCurve => Tunables.TraderMarketGroupCostAdjustmentCurve;
        private Curve markupCurve => Tunables.TraderMarkupCurve;

        private void RefreshInventoryFor(Trader trader) {
            // D.Warn("Refreshing trader inventory: {0}", trader);
            GenCashFor(trader, Rng.Unseeded);
            GenInventoryFor(trader, Rng.Unseeded);
            trader.UpdateInventoryRefreshTick(G.Ticks, Rng.Unseeded);
        }

        private Trader CreateTrader(TraderRequest req) {
            if (!req.Validate()) {
                return null;
            }
            // D.Warn("Creating trader: {0}", req);
            var trader = new Trader();
            trader.Id = ++Trader.LastId;
            if (!req.HasNoSO) {
                trader.SOId = req.SO?.Id ?? 0;
            }
            AddToTraderList(trader);
            var diff = S.Universe.CurrentBiome?.Difficulty ?? req.Rng.Value;

            trader.Markup = markupCurve.Evaluate(diff);
            trader.Markup *= req.Rng.Range(0.9f, 1.1f);
            trader.Markup += req.Rng.Range(0.01f, 0.03f);

            trader.Faction = req.Faction;
            trader.GenerateName(S, req.Rng);
            var isBlackMarket = req.Rng.Chance(req.Faction.ChanceBlackMarket);
            if (isBlackMarket && req.SO?.Ship?.IsPolice == true) {
                // 20% chance of corrupt cop
                if (req.Rng.Chance(0.8f)) {
                    isBlackMarket = false;
                }
            }
            trader.TradeableGroups = new();
            trader.Inventory = new();
            trader.UpdateInventoryRefreshTick(G.Ticks, req.Rng);
            trader.Wallet = new Wallet();
            if (req.WealthFactor == 0f) {
                req.WealthFactor = req.Rng.Range(0.1f, 0.2f);
            }
            trader.Wealth = req.WealthFactor;
            var minCash = req.MinCash;
            if (minCash == 0f) {
                var damp = CashDampeningFor(S.Wallet.Amount);
                minCash = (float)(S.Wallet.Amount * req.WealthFactor * damp);
            }
            if (minCash < traderMinCash) {
                minCash = traderMinCash;
            }
            trader.MinCash = minCash;
            GenCashFor(trader, req.Rng);

            var maxGroups = MarketGroup.All.Count;
            if (req.MinGroups == 0) {
                req.MinGroups = traderMinGroups;
            }
            if (req.MinItems == 0) {
                req.MinItems = traderMinItems;
            }
            trader.MinItems = req.MinItems;
            var numGroups = req.Rng.Range(req.MinGroups, req.MinGroups + 2);
            if (numGroups > maxGroups) {
                numGroups = maxGroups;
            }

            var w = Caches.WeightedInts;
            w.Clear();

            foreach (var group in MarketGroup.All.Values) {
                if (group.TraderQuantity == 0) {
                    // D.Warn("Skipping group with trader quantity 0: {0}", group.Id);
                    continue;
                }
                if (group.IdH == MarketGroupIdH.BlackMarket) {
                    if (isBlackMarket) {
                        trader.TradeableGroups.Add(group.IdH,
                            req.Rng.Curve(marketGroupAdjCurve));
                    }
                    continue;
                }
                w.Add(group.IdH, group.Rarity);
            }

            while (numGroups-- > 0) {
                if (req.Rng.TryWeightedFrom(w, out var gId)) {
                    trader.TradeableGroups.Add(gId,
                        req.Rng.Curve(marketGroupAdjCurve));
                    w.Remove(gId);
                } else {
                    // included all the groups
                    break;
                }
            }


            GenInventoryFor(trader, req.Rng);
            return trader;
        }

        private int GenInventoryFor(Trader trader, Rng rng) {
            var numItems = rng.Range(trader.MinItems, trader.MinItems + 5);
            var w = Caches.WeightedInts;
            var traderRng = new Rng(Hashes.Quick(trader.Id, Primes.Prime024));
            w.Clear();
            var alwaysInclude = Caches.IntList;
            alwaysInclude.Clear();
            var d = S.Difficulty.Level;
            var cantTradeBodyParts = !S.Research.IsUnlocked(DefIdsH.ResearchRoboticsAssemblyHub);
            SpaceObject so;
            if (trader.SOId == 0) {
                so = A.PlayerSector;
            } else {
                so = S.Universe.Find(trader.SOId);
                if (so == null) {
                    D.Err("Couldn't find Trader's SO by id: {0} | {1}",
                        trader.SOId, trader);
                    so = A.PlayerSector;
                }
            }
            var sectorDiff = SOUtils.SectorDifficultyOf(so, A.PlayerSector);

            bool allowExceptions = rng.Chance(0.05f);
            bool canHaveAsimov = trader.Faction?.IdH == FactionIdH.Virus && S.Research.IsUnlocked(DefIdsH.ResearchEconomyBlackMarket);

            var sectorDiffAdj = 0.1f;
            if (traderRng.Chance(0.05f)) {
                // super extremely cool shit trader
                sectorDiffAdj = 0.66f;
            } else if (traderRng.Chance(0.25f)) {
                sectorDiffAdj = 0.33f;
            }

            foreach (var item in MarketItem.CFDs.Values) {
                var adjSecDiff = rng.Range(sectorDiff, sectorDiff + sectorDiffAdj);
                if (item.MinSectorDifficulty > adjSecDiff) {
                    // D.Warn("Skipping item with min sec dif: {0}: {1}> {2}", item.Id, item.MinSectorDifficulty, adjSecDiff);
                    if (!allowExceptions) {
                        continue;
                    }
                    // still leave a slight chance to encounter the item
                    var chanceAllowException = Mathf.Max(item.Rarity * item.Rarity, 0.05f);
                    if (!rng.Chance(chanceAllowException)) {
                        continue;
                    }
                }
                // no point in selling body parts when we can't trade them
                if (cantTradeBodyParts && Arrays.ContainsSafe(item.GroupsH, MarketGroupIdH.BodyPart)) {
                    continue;
                }
                if (InventoryItemDef.TryForDef(item.Def.IdH, out var invItem)) {
                    if (invItem.SpawnRequirements != null &&
                            !S.Situation.Matches(invItem.SpawnRequirements, d)) {
                        continue;
                    }
                }
                if (canHaveAsimov) {
                    if (item.Def.IdH == DefIdsH.ObjUpgradesAsimovOverrideUpgrade) {
                        EnsureItemAvailable(trader, rng, w, item);
                        continue;
                    } else if (item.Def.IdH == DefIdsH.ObjPartsImplantObeyChip) {
                        EnsureItemAvailable(trader, rng, w, item);
                        continue;
                    }
                }
                if (item.CustomChanceInStock > 0f && rng.Chance(item.CustomChanceInStock)) {
                    // 1000% that this item gets picked up
                    EnsureItemAvailable(trader, rng, w, item);
                    continue;
                }
                bool canTrade = false;
                foreach (var g in item.GroupsH) {
                    if (trader.TradeableGroups.ContainsKey(g)) {
                        canTrade = true;
                        break;
                    }
                }
                if (!canTrade) { continue; }
                w.Add(item.IdH, item.Rarity);
            }
            trader.Inventory.Clear();
            var avail = GetMatAvailabilityFor(so);
            // D.Warn("Mat availability for trader: {0}: {1}: {2}", trader, so, avail);

            if (numItems > w.Count) {
                D.Warn("Reducing num items: {0} > {1}", numItems, w.Count);
                numItems = w.Count;
            }

            int attempts = 10000;
            while (numItems > 0 && attempts-- > 0) {
                if (rng.TryWeightedFrom(w, out var itemIdH)) {
                    w.Remove(itemIdH);
                    var mi = MarketItem.CFDs[itemIdH];
                    var q = mi.TraderStock(S, rng, sectorDiff, trader.Wealth);
                    if (q == 0) {
                        continue;
                    }
                    numItems--;
                    // D.Warn("Trader stock for {0}: {1}", mi.Id, q);
                    if (mi.Def.MatType == null) {
                        if (InventoryItemDef.HasForDef(mi.Def.IdH)
                                || BodyPartDef.HasForDef(mi.Def.IdH)) {
                            if (q > Tunables.TraderQualityItemMaxStock) {
                                q = rng.Range(Tunables.TraderQualityItemMaxStock - 5,
                                    Tunables.TraderQualityItemMaxStock + 1);
                            }
                        }
                        for (int i = 0; i < q; i++) {
                            var objItem = TradeableItem.Create(mi,
                                1, TradeableItem.ItemOwner.Them);
                            if (Craftable.CanHaveColor(mi.Def)) {
                                objItem.Color = Craftable.RandomColorFor(mi.Def, rng);
                            }
                            if (objItem.IsInventoryItem) {
                                objItem.Quality = rng.Curve(itemQualityCurve);
                            } else if (objItem.IsBodyPart) {
                                objItem.Quality = rng.Curve(bodyPartQualityCurve);
                                objItem.Condition = 1f;
                            }
                            // D.Warn("Generating trader's item: {0}", mi.Def);
                            trader.Inventory.Add(objItem);
                        }
                    } else {
                        var matItem = TradeableItem.Create(mi,
                            q, TradeableItem.ItemOwner.Them);
                        var coreMat = GetCoreMatFor(matItem);
                        if (coreMat != null) {
                            var newQ = avail.AdjustTraderQuantityFor(coreMat, q);
                            if (newQ != q) {
                                // D.Warn("Adjusting mat quantity due to scarcity: {0}: {1} => {2}",
                                //     mi.Id, q, newQ);
                                matItem.Quantity = newQ;
                            }
                        }
                        trader.Inventory.Add(matItem);
                    }
                    // don't add more than one copy of same mat. items are ok though
                    w.Remove(itemIdH);
                } else {
                    D.ErrSoft("Could not generate all items, {0} left", numItems);
                    break;
                }
            }
            // Last, so that the regular stock rolls exactly as it would without beings
            BeingTrade.GenStockFor(S, trader, rng);

            return numItems;
        }

        private void EnsureItemAvailable(Trader trader, Rng rng, Dictionary<int, float> w, MarketItem item) {
            w.Add(item.IdH, 10f);
            if (trader.AdditionalGroupCosts == null) {
                trader.AdditionalGroupCosts = new();
            }
            if (!trader.AdditionalGroupCosts.ContainsKey(item.FirstGroup.IdH)) {
                trader.AdditionalGroupCosts[item.FirstGroup.IdH] = rng.Curve(marketGroupAdjCurve);
            }
        }

        /// <summary>
        /// Dampens trader cash generation as the player's wallet grows, so that
        /// rich players increasingly encounter traders with less cash than them.
        /// Does not affect <see cref="Trader.Wealth"/>/inventory generation.
        /// </summary>
        private float CashDampeningFor(double playerCash) {
            var parity = Tunables.TraderWealthParityCash;
            if (playerCash <= parity) {
                // not rich enough yet, keep current behavior
                return 1f;
            }
            // log-scale player richness from parity ($15K, t=0) to $1M (t=1)
            var t = Mathf.InverseLerp(
                Mathf.Log10(parity),
                Mathf.Log10(traderMaxCash),
                Mathf.Log10((float)playerCash));
            return CurveDef.FastGet(CurveIdH.Economy_TraderWealth_PlayerDampening).Evaluate(t);
        }

        private void GenCashFor(Trader trader, Rng rng) {
            var minCash = trader.MinCash;
            var maxCash = minCash * 2f;
            if (maxCash > traderMaxCash) {
                maxCash = traderMaxCash;
            }
            if (minCash > maxCash) {
                minCash = maxCash * 0.8f;
            }
            trader.Wallet.Amount = rng.Range(minCash, maxCash);
        }

        public override void Unload() {
            OngoingTrades.Clear();
        }

        public void TradeEnded(TradeUI trade) {
            if (trade == null || trade.Trader == null) {
                D.Err("Trying to end a null trade or a trade with null trader: {0} | {1}",
                    trade, trade?.Trader);
                return;
            }
            if (!OngoingTrades.Remove(trade.Trader)) {
                D.Err("Trade ended but it wasn't in list of OngoingTrades: {0} | {1}",
                    trade, D.Dump(OngoingTrades));
            }
        }
    }
}
