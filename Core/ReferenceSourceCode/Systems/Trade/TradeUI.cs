using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Systems.Creatures;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using Game.Systems.Items;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.BodyParts;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Notify;

namespace Game.Systems.Trade {
    public sealed class TradeUI {
        private NotificationHandle tradeEvent;
        public Action<TradeUI> OnEnd;
        public bool HasBoughtSomething;
        public readonly Trader Trader;
        private readonly Wallet ourWallet;
        public OnlineOrder OnlineOrder;
        public bool IsComplete { get; private set; }
        public bool IsExecuted { get; private set; }
        public double SaleValue;
        private int goodsDropPos;
        private TradePortalComp tradePortal;
        /// <summary>
        /// For faction impact only
        /// </summary>
        public float SaleImpact => (float) (SaleValue / Math.Max(Math.Max(ourWallet.Amount, Trader.Wallet.Amount), 50000));
        public bool IsRemote;
        private readonly GameState S;
        private readonly TradeSys tradeSys;
        private Lifetime tradePanelLife;
        private WTradePanel tradePanel;
        private readonly List<TradeableItem> ourItems = new();
        public List<TradeableItem> OurItems => ourItems;
        private double expectedTradeIncome;
        private static TradeSortMode sortMode {
            get {
                var mode = CachedPlayerPrefs.GetInt("trade.sort.mode", (int) TradeSortMode.Group);
                return (TradeSortMode) mode;
            }
            set {
                CachedPlayerPrefs.SetInt("trade.sort.mode", (int) value);
            }
        }

        private TradeSortMode? _sortMode;
        public TradeSortMode SortMode {
            get {
                _sortMode ??= sortMode;
                return _sortMode.Value;
            }
            set {
                sortMode = value;
                _sortMode = value;
            }
        }

        public TradeFilterMode FilterMode;

        private static readonly List<string> sortOptionNames = new();
        private static readonly List<string> filterOptionNames = new();

        private string searchStr;
        public bool HasSearchStr => !string.IsNullOrEmpty(searchStr);
        public List<string> SortOptionNames {
            get {
                if (sortOptionNames.Count == 0) {
                    foreach (TradeSortMode val in Enum.GetValues(typeof(TradeSortMode))) {
                        sortOptionNames.Add(TradeableItem.SortText(val));
                    }
                }
                return sortOptionNames;
            }
        }

        public List<string> FilterOptionNames {
            get {
                if (filterOptionNames.Count == 0) {
                    foreach (TradeFilterMode val in Enum.GetValues(typeof(TradeFilterMode))) {
                        filterOptionNames.Add(TradeableItem.FilterText(val));
                    }
                }
                return filterOptionNames;
            }
        }

        // public for tests
        public readonly Dictionary<TradeableItem, int> Selling = new();
        public readonly Dictionary<TradeableItem, int> Buying = new();

        public TradeUI(Trader merchant, int goodsDropPos, TradeSys sys) {
            if (merchant == null) {
                D.Err("PANIC! Trying to start a trade with null merchant! More errors will likely follow");
                return;
            }
            tradeSys = sys;
            S = The.R.State;
            IsRemote = goodsDropPos < 1;
            this.goodsDropPos = goodsDropPos;
            ourWallet = S.Wallet;
            this.Trader = merchant;
            foreach (var item in merchant.Inventory) {
                // this may have been modified when spawning gifts
                item.QuantitySelected = 0;
            }
            tradeEvent = Notification.Build(IconId.CTrade,
                    $"{T.OngoingTrade} ({merchant.Name})", 0, NotificationType.Immediate)
                .WithOnClick(Show)
                .Group("ong_trade", T.OngoingTrade)
                .Sticky()
                .PulseOnly()
                .Show();
            S.Sig.TradeTeleporterSelected.AddListener(OnTradeTeleporterSelected);
            S.Sig.ShipCombatEngagedWith.AddListener(OnEngageShip);
        }

        private void OnEngageShip(NPCShip ship, bool isInCombat) {
            if (isInCombat && ship.SO.IsTraderCreated && Trader == ship.SO.Trader) {
                D.Warn("Aborting trade due to ship combat with: {0}", ship);
                EndTrade();
            }
        }

        public void Show() {
            if (Trader.SOId != 0) {
                var so = S.Universe.Find(Trader.SOId);
                if (so.IsShip || so.Type.IsConsumable) { // if not, it is likely a merchant pod
                    var isSameParent = so.Parent == S.Universe.Player.Parent;
                    if (!isSameParent || !S.Sys.Ship.IsNearby(so)) {
                        if (!isSameParent) {
                            TradeTooFar();
                            return;
                        }
                        A.Player.Ship.NearbyPosFrom(so.Location, out var isNear, 0f);
                        if (!isNear) {
                            TradeTooFar();
                            return;
                        }
                        // close enough
                    }
                }
            }
            if (tradePanelLife != null) {
                tradePanelLife.Dispose();
            }
            tradePanelLife = Lifetime.Create("TradePanel");
            tradePanel = WTradePanel.SpawnFor(this, tradePanelLife, searchStr);
            if (Buying.Count > 0 || Selling.Count > 0) {
                UpdateOutcome();
            }
        }

        private void TradeTooFar() {
            UISounds.PlayActionDenied();
            Notification.Remove(ref tradeEvent);
            EndTrade();
        }

        private void OnTradeTeleporterSelected(TeleporterComp teleporter) {
            ReloadPanel();
        }

        private List<TradeableItem> LoadOurItems() {
            ourItems.Clear();
            S.Sig.LoadOurTradeableItemsFor.Send(Trader, ourItems);
            return ourItems;
        }
        public void LoadOurItems(Lifetime life, WGroup container) {
            var vals = LoadOurItems();
            ShowItemSelectContext(life, container, vals, Selling, false);
        }
        public void LoadTheirItems(Lifetime life, WGroup container) {
            var vals = Trader.Inventory;
            foreach (var item in vals) {
                Trader.UpdatePriceFor(S, item);
            }
            ShowItemSelectContext(life, container, vals, Buying, true);

        }

        private void ShowItemSelectContext(Lifetime life, WGroup container,
                List<TradeableItem> choice,
                Dictionary<TradeableItem, int> target, bool isBuying) {

            var orderedChoice = TradeableItem.SortedBy(choice, sortMode, isBuying);

            string searchQuery = null;
            if (searchStr != null) {
                searchQuery = searchStr.Trim().ToLowerInvariant()
                    .Replace(" ", string.Empty);
                if (searchQuery == string.Empty) {
                    searchQuery = null;
                }
            }

            List<TradeableItem> filteredItems = null;

            int added = 0;
            var isBlackMarketUnlocked = S.Research.IsUnlocked(DefIdsH.ResearchEconomyBlackMarket);
            var addGroupHeaders = sortMode == TradeSortMode.Group;
            foreach (var val in orderedChoice) {
                if (!isBlackMarketUnlocked && val.MarketItem.IsBlackMarket) {
                    continue;
                }
                // not available
                if (val.Quantity == 0) { continue; }
                //if (target.Contains(val)) { continue; }

                if (target.TryGetValue(val, out var q)) {
                    val.QuantitySelected = q;
                }

                if (!FilterItem(val)) {
                    continue;
                }

                if (searchQuery != null && !MatchesSearch(val, searchQuery)) {
                    continue;
                }
                added++;
                filteredItems ??= new();
                filteredItems.Add(val);
            }

            if (added == 0) {
                var noItems = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container.Container, -1);
                noItems.Load(IconId.CInfo, T.Empty);
                return;
            }

            TradeItemLister.BuildList(life, this, container,
                target, filteredItems, isBuying, addGroupHeaders, sortMode);
        }

        // Being rows show the individual's name, not the species, so both have to match
        public static bool MatchesSearch(TradeableItem val, string searchQuery) {
            if (val.Def.SearchNameT.Contains(searchQuery)) { return true; }
            return val.Being != null && val.BeingSearchName.Contains(searchQuery);
        }

        private bool FilterItem(TradeableItem val) {
            switch (FilterMode) {
                case TradeFilterMode.Selected: {
                        if (val.QuantitySelected < 1) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.NotSelected: {
                        if (val.QuantitySelected > 0) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.Equipment: {
                        if (val.MatType != null) {
                            return false;
                        }
                        if (!val.IsInventoryItem) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.BodyParts: {
                        if (val.MatType != null) {
                            return false;
                        }
                        if (!val.IsBodyPart) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.Materials: {
                        if (val.MatType == null) {
                            return false;
                        }
                        // group will be 0 only when it's not assigned
                        if (val.MatType.GroupH != 0) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.Food: {
                        if (val.MatType == null) {
                            return false;
                        }
                        // group will be 0 only when it's not assigned
                        int gid = val.MatType.GroupH;
                        if (gid != MatGroupIdH.Food && gid != MatGroupIdH.RawFood &&
                                gid != MatGroupIdH.Drink) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.Upgrades: {
                        if (val.MatType == null) {
                            return false;
                        }
                        // group will be 0 only when it's not assigned
                        int gid = val.MatType.GroupH;
                        if (gid != MatGroupIdH.Upgrade) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.Beings: {
                        if (!val.IsBeing) {
                            return false;
                        }
                        break;
                    }
                case TradeFilterMode.All:
                default: break;
            }

            return true;
        }

        // Our wallet limits what we buy, the trader's wallet what they buy from us. Selling past
        // the trader's wallet still goes through, so that side only warns (STA-1825)
        internal static string PriceText(string text, double price, bool isBuying,
                GameState s, Trader trader) {
            if (isBuying) {
                return price > s.Wallet.Amount ? Texts.Red(text) : Texts.Green(text);
            }
            return price > trader.Wallet.Amount ? Texts.Yellow(text) : Texts.Green(text);
        }

        public static string ShowSellableDetails(GameState S, Trader trader,
                TradeableItem val, bool isBuying, bool isGifting = false) {
            if (val.Being != null && !isGifting) {
                return BeingTrade.Tooltip(S, trader, val, isBuying);
            }
            var sb = Caches.StringBuilder4;
            sb.Clear();
            if (isBuying && val.MatType != null) {
                var owned = S.Sys.Inventory.CountOf(val.Def);
                sb.AppendFormat("{0}: {1}\n", T.OwnedCount, Texts.Accented(owned));
            }
            if (!isGifting) {
                var totalVal = val.Quantity * val.FinalPriceOfOne(isBuying);
                var total = Wallet.ToValueString(totalVal , true);
                string totalText;
                if (isBuying) {
                    totalText = PriceText(total, totalVal, true, S, trader);
                    total = T.CanBuyFor_V2.F(Texts.Accented(val.Quantity), totalText);
                    sb.AppendLine(total);
                } else if (val.MatType != null) {
                    totalText = PriceText(total, totalVal, false, S, trader);
                    total = T.CanSellFor_V2.F(Texts.Accented(val.Quantity), totalText);
                    sb.AppendLine(total);
                }
                if (isBuying && val.Heat > 1f) {
                    var change = Units.PercentageWithFraction4(val.Heat - 1f, false);
                    change = Texts.Red($"+{change}");
                    sb.AppendFormat("{0} ({1})\n", T.TradedItemTooHot, change);
                } else if (!isBuying && val.Heat < 1f) {
                    var change = Units.PercentageWithFraction4(1f - val.Heat, false);
                    change = Texts.Red($"-{change}");
                    sb.AppendFormat("{0} ({1})\n", T.TradedItemTooCold, change);
                }
            }
            if (val.IsInventoryItem) {
                sb.AppendFormat("{0}: {1}", T.NounQuality, Units.Percentage(val.Quality, clamp: false));
                // Names the deduction ItemOwnership.PriceMulFor already applied to
                // PriceOfOne, so this can never disagree with what was charged.
                var originLabel = ItemOwnership.OriginLabel(val.OurObjData, null);
                if (originLabel != null) {
                    var mul = ItemOwnership.PriceMulFor(val.Origin);
                    if (mul < 1d) {
                        var change = Texts.Red(Units.PercentageSigned((float) (mul - 1d)));
                        sb.AppendFormat("\n{0} ({1}: {2})", originLabel, T.NounPrice, change);
                    } else {
                        sb.AppendFormat("\n{0}", originLabel);
                    }
                }
            } else if (val.IsBodyPart) {
                if (val.OurObjData != null) {
                    val.OurObjData.AddBodyPartData(sb, true);
                } else {
                    sb.AppendFormat("{0}: {1}", T.NounQuality, Units.Percentage(val.Quality, clamp: false));
                    sb.AppendFormat("\n{0}: {1}", T.PartCondition, Units.Percentage(val.Condition));
                    if (BodyPartDef.TryForDef(val.Def.IdH, out var bp)) {
                        bp.AddInfo(sb, val.OurObjData?.Mind?.Species, val.Quality);
                    }
                }
            }

            if (isGifting) {
                sb.Append(val.Def.ExtendedTooltip(S, "\n", skipCount: true));
            }
            return sb.ToString();
        }

        public double ValueOf(Dictionary<TradeableItem, int> items, bool isBuying) {
            var val = 0d;
            foreach (var s in items) {
                val += s.Key.FinalPriceOfOne(isBuying) * s.Value;
            }
            return val;
        }

        public void UpdateOutcome() {
            var sellingIncome = ValueOf(Selling, false);
            var buyingCost = ValueOf(Buying, true);
            // TOTAL transaction in either direction
            SaleValue = sellingIncome + buyingCost;
            var isTraderShortOnCash = false;
            var isYouShortOnCash = false;

            var finalIncome = sellingIncome - buyingCost;
            if (finalIncome > 0.0001f && Trader.Wallet.Amount < finalIncome) {
                isTraderShortOnCash = true;
                finalIncome = Trader.Wallet.Amount;
            }
            else if (finalIncome < 0.0001f && !S.Wallet.CanAfford(-finalIncome)) {
                isYouShortOnCash = true;
            }
            expectedTradeIncome = finalIncome;
            var incomeText = Wallet.ToValueString(Math.Abs(finalIncome));

            if (finalIncome > 0) {
                incomeText = Texts.WithColor(incomeText, Texts.TMPColorGreen);
            } else if (-finalIncome > ourWallet.Amount) {
                incomeText = Texts.WithColor(incomeText, Texts.TMPColorRed);
            } else {
                incomeText = Texts.WithColor(incomeText, Texts.TMPColorYellow);
            }
            incomeText = Texts.WithSize(incomeText, 200);

            string outcomeText;
            string extraWarning = null;
            if (finalIncome < 0) {
                outcomeText = $"{Texts.WithVOffset($"{T.YouWillPay}:", "0.35em")} {incomeText}";
            } else {
                outcomeText = $"{Texts.WithVOffset($"{T.YouWillReceive}:", "0.35em")} {incomeText}";
            }
            if (isTraderShortOnCash) {
                extraWarning = T.TradeMerchantNotEnoughCash;
            } else if (isYouShortOnCash) {
                extraWarning = T.TradeNotEnoughCash;
            }

            tradePanel.SetOutcomeText(outcomeText, extraWarning);
        }

        public void Execute() {
            int step = 0;
            try {
                var ourWallet = S.Wallet;
                SaleValue = 0;

                bool isBuyingOrSelling = false;
                bool cannotSellSelected = false;
                // Validate every selected item, not just the first one: a being can die or
                // leave the map between selecting it and executing the trade
                List<TradeableItem> unsellable = null;
                foreach (var s in Selling) {
                    s.Key.QuantitySelected = s.Value;
                    if (s.Key.QuantitySelected > 0) {
                        if (!TrySell(s.Key, true, out var inc)) {
                            S.Sys.Log.AddLine($"{T.Missing}: {s.Key.Def.CodexLink} [x{s.Key.QuantitySelected}]",
                                Texts.TMPColorRed);
                            cannotSellSelected = true;
                            s.Key.QuantitySelected = 0;
                            unsellable ??= new();
                            unsellable.Add(s.Key);
                        }
                        isBuyingOrSelling = true;
                    }
                }
                if (unsellable != null) {
                    foreach (var item in unsellable) {
                        Selling[item] = 0;
                    }
                }
                if (cannotSellSelected && isBuyingOrSelling) {
                    // WPopup.Create(false, IconId.CWarning, T.TradeFailed, T.TradeModified).Spawn();
                    S.Sig.ShowErrorMessage.Send($"{T.TradeFailed}: {T.TradeModified}");
                    return;
                }
                if (!isBuyingOrSelling) {
                    foreach (var b in Buying) {
                        b.Key.QuantitySelected = b.Value;
                        if (b.Key.QuantitySelected > 0) {
                            isBuyingOrSelling = true;
                            break;
                        }
                    }
                }
                if (!isBuyingOrSelling) {
                    // WPopup.Create(false, IconId.CWarning, T.TradeFailed, T.TradeNoOffer).Spawn();
                    S.Sig.ShowErrorMessage.Send(T.TradeNoOffer);
                    return;
                }
                step = 1;

                if (IsRemote && !Trader.IsOnline) {
                    // We need a teleporter
                    tradePortal = FindTradePortal(S);
                    if (tradePortal != null) {
                        goodsDropPos = tradePortal.Tile.Transform.OutPort;
                    } else {
                        // WPopup.Create(false, IconId.CWarning, T.TradeFailed, TradePortalComp.NotAvailable).Spawn();
                        S.Sig.ShowErrorMessage.Send(TradePortalComp.NotAvailable);
                        return;
                    }
                    // need to check if what we buy will end up in safe env
                    if (Buying.Count > 0 && !S.BB.Get(BBKeyH.HasAsimovOverride, false)) {
                        bool isBuyingBiological = false;
                        foreach (var b in Buying) {
                            b.Key.QuantitySelected = b.Value;
                            if (b.Key.QuantitySelected > 0 && b.Key.Def.IsBeing) {
                                var species = Species.ForDef(b.Key.Def.IdH);
                                isBuyingBiological |= species.IsBiological;
                            }
                        }

                        if (isBuyingBiological) {
                            bool isAtmoSafe = EntityUtils.IsLiveableAtmosphereAt(S, goodsDropPos);
                            if (!isAtmoSafe) {
                                goodsDropPos = 0;
                                WPopup.Create(false, IconId.CWarning, T.TradeFailed, T.TradeUnsafeEnvironment).Spawn();
                                return;
                            }
                        }
                    }
                }
                if (goodsDropPos == 0 && !Trader.IsOnline) {
                    D.Err("Did not set goodsDropsPos!");
                    // WPopup.Create(false, IconId.CWarning, T.TradeFailed, T.TradeNoTeleporter).Spawn();
                    S.Sig.ShowErrorMessage.Send(T.TradeNoTeleporter);
                    return;
                }

                step = 2;

                if (expectedTradeIncome < 0) {
                    if (!ourWallet.CanAfford(-expectedTradeIncome)) {
                        S.Sig.ShowErrorMessage.Send(T.TradeNotEnoughCash);
                        ReloadPanel();
                        return;
                    }
                }
                step = 3;

                var partial = false;
                var totalBalance = 0d;
                int tryingToSellCount = 0;
                int soldCount = 0;
                if (!partial && Selling.Count > 0) {
                    foreach (var s in Selling) {
                        TradeableItem sell = s.Key;
                        sell.QuantitySelected = s.Value;
                        if (sell.QuantitySelected < 1) { continue; }
                        tryingToSellCount++;
                        if (TrySell(sell, false, out var income)) {
                            soldCount++;
                            totalBalance += income;
                        } else {
                            expectedTradeIncome -= income;
                        }
                    }

                    if (tryingToSellCount > soldCount) {
                        partial = true;
                    }
                    if (soldCount > 0) {
                        IsExecuted = true;
                    }
                }
                step = 5;

                if (partial) {
                    S.Sys.Log.AddLine(T.TradeModified, Texts.TMPColorRed);
                    //UIPopupWidget.Spawn(IconId.CWarning, T.TradeFailed, T.TradeModified);
                    //listHeaderOur.NeedsListRebuild = true;
                    //return;
                }

                if (Trader.IsOnline) {
                    OnlineOrder = new();
                    OnlineOrder.Items = new();
                    OnlineOrder.OrderTick = G.Ticks;
                    OnlineOrder.DeliveryTick = G.Ticks +
                        Rng.URange(Consts.TicksPer7Hours, Consts.TicksPer9Hours);
                    OnlineOrder.SpaceObjectId = S.Universe.Player.Parent.Id;
                }

                foreach (var b in Buying) {
                    TradeableItem buy = b.Key;
                    buy.QuantitySelected = b.Value;
                    if (buy.QuantitySelected < 1) { continue; }
                    var buyValue = OnBuy(buy);
                    totalBalance -= buyValue;
                    HasBoughtSomething = true;
                }
                step = 6;

                if (expectedTradeIncome > 0) {
                    if (!Trader.Wallet.TryPay(expectedTradeIncome, ourWallet)) {
                        D.Err("Trader failed to pay the amount!");
                    }
                } else if (expectedTradeIncome < 0) {
                    if (!ourWallet.TryPay(-expectedTradeIncome, Trader.Wallet)) {
                        D.Err("We failed to pay the amount!");
                    }
                }
                step = 7;

                if (expectedTradeIncome < 0) {
                    if (!Maths.Approximately((float) (totalBalance - expectedTradeIncome), 0f)) {
                        D.Err("Unexpected balanced after sale, wanted: {0}, got {1}.",
                            expectedTradeIncome, totalBalance);
                    }
                }
                if (HasBoughtSomething) {
                    IsExecuted = true;
                }
                SaleValue = math.abs(expectedTradeIncome);
                EndTrade();
                if (IsExecuted) {
                    S.Sig.TradeExecuted.Send(this);
                    UISounds.Play("FX/CashRegisterSFX", 0.3f);
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Exception in TradeUI.Execute. step {0}. Clock speed: {1}",
                    step, G.ClockSpeed);
                Selling.Clear();
                Buying.Clear();
                ReloadPanel();
                WPopup.Create(false, IconId.CWarning, T.TradeFailed, T.TradeModified).Spawn();
            }
        }

        // IsPowered as well as IsConsuming: Consumed only updates on an energy tick, so a
        // portal switched on while the clock is stopped (the trade panel stops it) would
        // otherwise not count until the game runs again (STA-1883)
        public static TradePortalComp FindTradePortal(GameState S) {
            return S.Components.FindFirstMatching<TradePortalComp>(
                (t) => t.Tile.IsConstructed
                    && (t.Tile.ENode.IsConsuming || t.Tile.ENode.IsPowered));
        }

        // Any built portal, working or not: to point the player at the one that is off
        public static TradePortalComp FindAnyConstructedTradePortal(GameState S) {
            return S.Components.FindFirstMatching<TradePortalComp>(
                (t) => t.Tile.IsConstructed);
        }

        public void EndTrade() {
            if (IsComplete) { return; }
            IsComplete = true;
            S.Sys.Diplomacy.AddTrade(this);
            if (HasBoughtSomething && !Trader.IsOnline) {
                if (The.Prefs.GetBool(Pref.GFocusOnItemsAfterTrade,
                        PrefH.GFocusOnItemsAfterTrade, true)) {
                    EntityUtils.CameraFocusOn(goodsDropPos);
                }
                UISounds.PlayShowDialog();
            }
            Notification.Remove(ref tradeEvent);
            S.Sig.TradeTeleporterSelected.RemoveListener(OnTradeTeleporterSelected);
            S.Sig.ShipCombatEngagedWith.RemoveListener(OnEngageShip);
            tradeSys.TradeEnded(this);
            OnEnd?.Invoke(this);
            tradePanelLife?.Dispose();
            tradePanelLife = null;
        }

        private bool TrySell(TradeableItem item, bool dryRun, out double income) {
            int soldCount = 0;
            if (item.Being != null) {
                if (item.QuantitySelected == 1 && BeingTrade.CanSellNow(S, Trader, item.Being)) {
                    soldCount = 1;
                    if (!dryRun) {
                        BeingTrade.Sell(S, Trader, item.Being);
                    }
                }
                // Beings never join the trader's stock (only seeded offers do) and have no
                // market heat, see BeingTrade
                income = item.FinalPriceOfOne(false) * soldCount;
                return soldCount > 0;
            }
            if (item.MatType == null) {
                if (item.OurObjData == null) {
                    D.Err("Item should have OurObjData! {0}", item);
                    income = 0;
                    return false;
                }
                if (item.QuantitySelected != 1) {
                    D.Err("Trying to sell Obj with QuantitySelected != 1! {0} | {1}",
                        item.QuantitySelected, item);
                    income = 0;
                    return false;
                }
                if (item.OurObjData.CanSell) {
                    soldCount += item.QuantitySelected;
                    if (!dryRun) {
                        RemoveObjData(S, item.OurObjData, item);
                    }
                }
            } else {
                var matType = item.MatType;
                if (S.Sys.Inventory.TrySell(matType, item.QuantitySelected, !dryRun)) {
                    soldCount += item.QuantitySelected;
                } else {
                    if (!dryRun) {
                        D.ErrSoft("Failed to sell required mat: {0} x{1}!",
                            item.MatType, item.QuantitySelected);
                    }
                }
            }

            if (!dryRun) {
                if (item.OurObjData != null) {
                    // let's not add sold objs into trader's inventory
                    // this could lead to wiping out wounds and states from obj data
                } else {
                    var tradersItem = Trader.Inventory.FirstOrDefault(i => i.MarketItemId == item.MarketItemId);
                    if (tradersItem == null) {
                        // D.Err("Trader stack is null by id: {0}. SHOULD NEVER HAPPEN",
                        //     item.Def.Id);
                        // NO! it's fine, we didn't have any items of this kind in trader's inventory
                        tradersItem = item.Take(soldCount);
                        Trader.Inventory.Add(tradersItem);
                    } else {
                        tradersItem.Quantity += soldCount;
                    }
                }
                Trader.ItemHeat ??= new();
                var so = S.Universe.Find(Trader.SOId);
                var sectorDiff = SOUtils.SectorDifficultyOf(so, S.Universe.Player.Parent);
                item.MarketItem.AdjustHeat(S, sectorDiff, Trader.ItemHeat, soldCount);
            }
            income = item.FinalPriceOfOne(false) * soldCount;
            return soldCount > 0;
        }

        public static void RemoveObjData(GameState S, ObjData objData, object ctx) {
            if (objData.State == ObjState.Stored) {
                if (!S.Query.DestroyStoredObj.Ask(objData)) {
                    D.Err("Failed destroying sold obj: {0}", ctx);
                }
            } else {
                objData.State = ObjState.Destroyed;
                objData.StorageId = 0;
                if (objData.Obj != null) {
                    S.Objs.Destroy(objData.Obj);
                } else {
                    S.Query.DeactivateObjData.Ask(objData, ObjState.Destroyed);
                }
            }
        }

        private double OnBuy(TradeableItem item) {
            int quantitySelected = item.QuantitySelected;
            var totalValue = item.FinalPriceOfOne(true) * quantitySelected;
            var took = item.Take(quantitySelected);
            if (Trader.IsOnline) {
                OnlineOrder.Items.Add(took);
                OnlineOrder.TotalPrice += totalValue;
            } else {
                item.SpawnAt(S, goodsDropPos);
            }
            if (item.IsBeing) {
                // The individual now lives aboard, the offer must not linger in stock
                Trader.Inventory.Remove(item);
                item.QuantitySelected = 0;
                return totalValue;
            }
            Trader.ItemHeat ??= new();
            var so = S.Universe.Find(Trader.SOId);
            var sectorDiff = SOUtils.SectorDifficultyOf(so, S.Universe.Player.Parent);
            item.MarketItem.AdjustHeat(S, sectorDiff, Trader.ItemHeat, -quantitySelected);
            item.QuantitySelected = 0;
            return totalValue;
        }

        public void SetSearchStr(string str) {
            searchStr = str;
            ReloadPanel();
        }

        public void SetFilterMode(int mode) {
            FilterMode = (TradeFilterMode) mode;
            ReloadPanel();
        }

        public void SetSortMode(int mode) {
            SortMode = (TradeSortMode) mode;
            ReloadPanel();
        }

        public void ReloadPanel() {
            tradePanel.Reload(searchStr);
            UpdateOutcome();
        }
    }
}
