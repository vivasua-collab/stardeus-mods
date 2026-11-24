using System;
using System.Collections;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Trade;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WTradePanel : WBase, ICloseableWidget, IWithSearch, ICustomPoolable {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WEntityHeader wHeaderUs;
        [SerializeField] private WEntityHeader wHeaderThem;
        [SerializeField] private WDropdown wDropFilter;
        [SerializeField] private WDropdown wDropSort;
        [SerializeField] private WText wDropSortText;
        [SerializeField] private WText wDropFilterText;
        [SerializeField] private WInput wSearch;
        [SerializeField] private WText wTxtMarkup;
        [SerializeField] private WGroup wContentUs;
        [SerializeField] private WGroup wContentThem;
        [SerializeField] private WButtonIconText wButtonTrade;
        [SerializeField] private WButtonText wButtonCancel;
        [SerializeField] private WText wOutcomeText;
        [SerializeField] private ScrollRect wScroll;
        [SerializeField] private GameObject wTradePortalBlock;
        [SerializeField] private WImage wTradePortalImage;
        [SerializeField] private WText wTradePortalMissingText;
        [SerializeField] private WButtonText wTradePortalMissingButton;

        private Lifetime life;
        private Lifetime parentLife;
        private TradeUI trade;
        private Trader trader;
        private GameState S;
        private string lastSearchStr;
        private Coroutine updateSearchCrt;
        private Coroutine portalCheckCrt;
        private string extraWarning;

        public static WTradePanel SpawnFor(TradeUI trade, Lifetime lifetime, string searchQ) {
            var w = UISpawn.Widget<WTradePanel>(lifetime, UIPoolRoot.PopupContainer);
            w.Load(trade, lifetime, searchQ);
            w.RebuildLayout();
            UIUtils.AfterShowPopup(w.Rect, true, randShift: false);
            UIUtils.MoveIntoScreenBounds(w.Rect);
            return w;
        }

        private void Load(TradeUI trade, Lifetime lifetime, string searchQ) {
            wScroll.verticalNormalizedPosition = 1f;
            this.trade = trade;
            trader = trade.Trader;
            this.parentLife = lifetime;
            if (life != null) {
                life.Dispose();
            }
            life = parentLife.CreateChild("WTradePanel.Load");
            S = A.S;
            lastSearchStr = searchQ;
            updateSearchCrt = null;
            UIUtils.LoadSearch(wSearch, FilterItems, SubmitFilterItems, value: searchQ);
            HideTradePortalUnavailable();
            CheckTradePortal();
            // Real time, not TickB: this panel force-stops the clock, so no tick comes while
            // it is open and a portal switched on meanwhile was never noticed (STA-1883)
            if (portalCheckCrt != null) {
                StopCoroutine(portalCheckCrt);
            }
            portalCheckCrt = gameObject.activeInHierarchy
                ? StartCoroutine(CheckTradePortalLoop()) : null;
            wDropFilter.Load(trade.FilterOptionNames, (int) trade.FilterMode, trade.SetFilterMode);
            wDropFilterText.Load($"{T.Filter}: {trade.FilterOptionNames[(int) trade.FilterMode]}");
            wDropSort.Load(trade.SortOptionNames, (int) trade.SortMode, trade.SetSortMode);
            wDropSortText.Load($"{T.Sort}: {trade.SortOptionNames[(int) trade.SortMode]}");
            wButtonCancel.Load(T.TradeCancel, trade.EndTrade);
            wButtonTrade.Load(IconId.CStarcreds, T.TradeExecute, trade.Execute);
            UIUtils.AddTipFunc(life, wButtonTrade,
                () => extraWarning == null ? null : Texts.Red(extraWarning));
            trade.LoadOurItems(life, wContentUs);
            trade.LoadTheirItems(life, wContentThem);
            wOutcomeText.Load(null);
            wPanelHeader.Load(T.Trade, Close);
            wHeaderThem.SetFactionIcon(null);
            wHeaderUs.SetFactionIcon(FactionDef.Player);
            wHeaderThem.Load(trader.Faction.FlagIcon, trader.Name);
            wHeaderUs.Load(GraphicsId.ObjectsDevicesShipComputer, T.OurWallet);
            wHeaderThem.SetText2(Texts.Green(Wallet.ToValueString(trader.Wallet.Amount)));
            wHeaderUs.SetText2(Texts.Green(Wallet.ToValueString(S.Wallet.Amount)));
            if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyMarketPrediction)) {
                // doesn't need to be func since clock will be force stopped
                UIUtils.AddTip(life, wHeaderThem, trader.RefreshLine);
            }
            if (S.Research.IsUnlocked(DefIdsH.ResearchEconomyWallet)) {
                // EffectiveMarkup, not Markup - otherwise an allied trader's displayed
                // percentage contradicts the prices right next to it
                wTxtMarkup.Load(
                    T.MerchantRipoff_V1.F(
                        Units.PercentageWithFraction1(trader.EffectiveMarkup,
                            clamp: false)));
            } else {
                wTxtMarkup.Load(null);
            }
            RebuildLayout();
            // LoadHeader(wHeaderUs, a.OwnerMind.Owner);
            // LoadHeader(wHeaderThem, b.OwnerMind.Owner);
        }

        private void SubmitFilterItems(string q) {
            var searchQ = Strings.ToSearchString(q, allowNull: true);
            if (updateSearchCrt != null) {
                StopCoroutine(updateSearchCrt);
            }
            lastSearchStr = searchQ;
            trade.SetSearchStr(searchQ);
        }

        private void FilterItems(string q) {
            var searchQ = Strings.ToSearchString(q, allowNull: true);
            if (lastSearchStr == searchQ) { return; }
            lastSearchStr = searchQ;
            if (updateSearchCrt != null) {
                StopCoroutine(updateSearchCrt);
            }
            updateSearchCrt = StartCoroutine(UpdateSearch(searchQ));
        }

        private IEnumerator UpdateSearch(string searchQ) {
            yield return Consts.Wait500Ms;
            if (gameObject.activeSelf) {
                trade.SetSearchStr(searchQ);
            }
        }

        private IEnumerator CheckTradePortalLoop() {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (trade != null) {
                yield return wait;
                if (trade == null) { yield break; }
                CheckTradePortal();
            }
            portalCheckCrt = null;
        }

        private void CheckTradePortal() {
            if (trade.Trader.IsOnline) { return; }
            if (!trade.IsRemote) { return; }

            var tradePortal = TradeUI.FindTradePortal(S);
            if (tradePortal != null) {
                if (tradePortal.IsUsable) {
                    HideTradePortalUnavailable();
                } else {
                    ShowTradePortalUnavailable(tradePortal, NotReadyText);
                }
                return;
            }
            // A portal that is built but switched off or unpowered is not missing: point
            // at it instead of offering to build another one
            var unpowered = TradeUI.FindAnyConstructedTradePortal(S);
            ShowTradePortalUnavailable(unpowered, unpowered == null ? null : NotReadyText);
        }

        private static string NotReadyText =>
            $"{T.ObjectsDevicesTradeportal}: {Texts.Red(T.NotReady)}";

        private void ShowTradePortalUnavailable(TradePortalComp portal, string reason) {
            var text = Texts.AutoLink(T.TradePortalRequired, DefIdsH.ObjectsDevicesTradePortal);
            if (reason == null) {
                reason = text;
            } else {
                reason = $"{reason}\n{text}";
            }
            wTradePortalMissingText.Load(reason);
            wTradePortalImage.Load(GraphicsId.ObjectsDevicesTradePortal);
            if (portal != null) {
                wTradePortalMissingButton.Load(T.Show, () => {
                    EntityUtils.SelectOrFocus(portal.Entity);
                    EntityUtils.CameraFocusOn(portal.Entity);
                    Close();
                });
            } else {
                var portalDef = The.Defs.FastGet(DefIdsH.ObjectsDevicesTradePortal);
                if (S.Research.IsDefUnlocked(portalDef)) {
                    wTradePortalMissingButton.Load(T.VerbBuild, () => {
                        ToolPlaceTiles.StartBuilding(portalDef.Id);
                        Close();
                    });
                } else {
                    wTradePortalMissingButton.Load(T.ResearchNodeLocked, () => {
                        A.S.UIQuery.ShowInResearchTree.Ask(portalDef);
                        Close();
                    });
                }
            }
            wTradePortalBlock.SetActiveFast(true);
        }

        private void HideTradePortalUnavailable() {
            wTradePortalBlock.SetActiveFast(false);
        }



        private void LoadHeader(WEntityHeader h, BeingData b) {
            b.Avatar.Load(h.Image);
            h.SetText(b.Mind.Name.DisplayName);
            h.SetText2(b.UI.GetBio(false, false));
            h.SetFactionIcon(b.EffectiveFaction);
        }


        public bool TryCloseWidget() {
            if (!gameObject.activeSelf) {
                return false;
            }
            Close();
            return true;
        }

        private void Close() {
            if (portalCheckCrt != null) {
                StopCoroutine(portalCheckCrt);
                portalCheckCrt = null;
            }
            if (trade != null && trader.IsOnline) {
                // closing the panel cancels an Online Shop trade
                trade.EndTrade();
            }
            life?.Dispose();
            life = null;
            parentLife?.Dispose();
            parentLife = null;
            trade = null;
            trader = null;
            S = null;
        }

        public bool TryFocusSearch() {
            return wSearch.Focus();
        }

        public void Reload(string searchQ) {
            Load(trade, parentLife, searchQ);
        }

        public void PoolDeactivate() {
            A.S.Clock.ForceResume(KL.Clock.ForceStopReason.Trade);
            gameObject.SetActiveFast(false);
        }

        public void PoolActivate() {
            A.S.Clock.ForceStop(KL.Clock.ForceStopReason.Trade);
            gameObject.SetActiveFast(true);
        }

        public void SetOutcomeText(string outcomeText, string extraWarning) {
            wOutcomeText.Load(outcomeText);
            this.extraWarning = extraWarning;
            if (string.IsNullOrWhiteSpace(extraWarning)) {
                wButtonTrade.Load(IconId.CStarcreds, T.TradeExecute, trade.Execute);
            } else {
                wButtonTrade.Load(IconId.CWarning, T.TradeExecute, trade.Execute);
            }
        }
    }
}
