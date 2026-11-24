using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.Systems.UIUX.Codex.Pages;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ShipCombatMiniWidget : BaseWidget {
        [SerializeField] private WText wTextHeader;
        [SerializeField] private Button mainButton;
        [SerializeField] private WText wTextBattleStations;
        [SerializeField] private WProgressText2Stacked wProgressFlux;
        [SerializeField] private RectTransform battleStationsContainer;
        [SerializeField] private GameObject battleStationsGO;
        [SerializeField] private GameObject redAlertBG;
        [SerializeField] private WText wBottomText;
        // The arrow in the header that opens the tactical view
        [SerializeField] private RectTransform viewArrow;
        private GameState S;
        private Lifetime lifetime;
        private Lifetime bsLife;

        private List<WBattleStationMiniIcon> spawnedBS = new();

        public RectTransform ViewArrowRect => viewArrow;

        private bool hasWeapons;
        private bool hasStations;
        private bool lacksStations;
        private bool lacksOperator;
        private bool inCombat;


        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.ShipCombatMini, this);
            mainButton.onClick.RemoveAllListeners();
            mainButton.onClick.AddListener(OnClick);
            wTextBattleStations.Load(T.NounBattleStationPlural);
            MiscUtils.DestroyChildren(battleStationsContainer);
            SetColors();
            S = s;
        }

        private void OnClick() {
            var wantsCombatMode = !UIShowing.ShipCombatMode;
            S.UIQuery.ToggleShipCombat.Ask(wantsCombatMode);
            Close();
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            if (show) {
                UIShowing.Set(UIShowingFlags.ShipCombatMini);
                Load();
                Rect.SetAsLastSibling();
                if (inCombat) {
                    A.S.UIQuery.ShowTutorialPopup.Ask(ShipCombatControlsPage.PageId, 3);
                }
            } else {
                UIShowing.Unset(UIShowingFlags.ShipCombatMini);
                Cleanup();
            }
        }

        public void Close() {
            Toggle(false);
        }

        public void RefreshHeader() {
            inCombat = IsPlayerShipCombatActive();
            UpdateHeader();
        }

        private void UpdateHeader() {
            if (inCombat) {
                wTextHeader.Load(T.ShipCombatOngoing);
                return;
            }
            if (S.Sys.Sec.HasActiveSpaceThreats) {
                wTextHeader.Load(T.ShipThreatDetected);
                return;
            }
            var flux = A.Player.Ship.CombatState.Flux;
            if (flux.State == ShipFluxState.Overload) {
                wTextHeader.Load(T.FluxOverload);
            } else if (flux.Hard > 0f || flux.Soft > 0f) {
                wTextHeader.Load(T.FluxVenting);
            } else {
                wTextHeader.Load(T.ShipThreatDetected);
            }
        }

        private void OnShipCombatChanged(bool nowInCombat) {
            inCombat = nowInCombat;
            UpdateHeader();
            RebuildBattleStations();
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("ShipCombatMini");
            inCombat = IsPlayerShipCombatActive();
            UpdateHeader();
            S.Sig.ShipCombatChanged.AddListener(OnShipCombatChanged, gameObject);
            UIUtils.AddTipFunc(lifetime, wProgressFlux, ShowFluxTooltip);
            var flux = A.Player.Ship.CombatState.Flux;
            flux.OnChanged.Bind(lifetime, () => {
                var fluxLabel = flux.State == ShipFluxState.Overload ? T.FluxOverload : T.NounFlux;
                wProgressFlux.Progress.Load(fluxLabel,
                    Units.XOutOfY(flux.Hard + flux.Soft, flux.Capacity), flux.TotalPercent);
                wProgressFlux.SetStackedProgress(flux.HardPercent);
                UpdateHeader();
            });
            RebuildBattleStations();
        }

        private bool IsPlayerShipCombatActive() {
            return A.Player.Ship.IsInCombat
                || S.Query.GetEnemyShips.Ask().Count > 0;
        }

        public void RebuildBattleStations() {
            if (bsLife != null) {
                bsLife.Dispose();
            }
            // we're not showing
            if (lifetime == null || lifetime.IsDisposed) {
                return;
            }
            spawnedBS.Clear();
            bsLife = lifetime.CreateChild("mini.bs");
            if (!inCombat) {
                battleStationsGO.SetActiveFast(false);
                redAlertBG.SetActiveFast(false);
                wBottomText.SetActiveFast(false);
                RebuildLayout();
                return;
            }
            int idx = 0;
            var allStations = S.Components.AllByType<BattleStationComp>();
            hasStations = false;
            lacksOperator = false;
            if (!Arrays.IsEmpty(allStations)) {
                foreach (BattleStationComp bs in allStations) {
                    if (!bs.IsConstructed) { continue; }
                    if (!bs.Tile.ENode.IsPowerable) { continue; }
                    var w = UISpawn.Widget<WBattleStationMiniIcon>(bsLife,
                        UIPrefabId.WBattleStationMiniIcon, battleStationsContainer, idx++);
                    w.Load(bs);
                    spawnedBS.Add(w);
                    if (!lacksOperator && !bs.IsOperatorPresent) {
                        lacksOperator = true;
                    }
                    hasStations = true;
                }
            }
            battleStationsGO.SetActiveFast(hasStations);
            var weapons = A.Player.Ship.CombatState.Weapons;
            hasWeapons = false;
            lacksStations = false;
            foreach (var wpn in weapons) {
                if (wpn.IsPointDefense) { continue; }
                hasWeapons = true;
                if (wpn.HasNoStation) {
                    lacksStations = true;
                }
            }
            RebuildAlert();
            RebuildLayout();
        }

        private void RebuildAlert() {
            bool needRedAlert = lacksStations || !hasWeapons || !hasStations || lacksOperator;
            if (!hasWeapons) {
                wBottomText.Load(T.NoShipWeapons);
            } else if (!hasStations) {
                wBottomText.Load(T.MustBuildBattleStationsTip_V1.F(
                    Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation)));
            } else if (lacksStations) {
                wBottomText.Load(T.MustBuildMoreBattleStationsTip_V1.F(
                    Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation,
                        T.NounBattleStationPlural)));
            } else if (lacksOperator) {
                wBottomText.Load(T.UnmannedBattleStations);
            }

            redAlertBG.SetActiveFast(needRedAlert);
            wBottomText.SetActiveFast(needRedAlert);
        }

        private void Cleanup() {
            if (lifetime != null) {
                S.Sig.ShipCombatChanged.RemoveListener(OnShipCombatChanged);
                lifetime.Dispose();
                lifetime = null;
            }
            spawnedBS.Clear();
        }

        public static string ShowFluxTooltip() {
            return ShipCombatWidgetUtils.GetFluxTip(A.Player.Ship, includeSettings: true);
        }

        public void SetColors() {
            var fillColor = The.UITheme.GetColor("ShipCombatFluxSoft", "ShipCombatMini");
            var fill2Color = The.UITheme.GetColor("ShipCombatFluxHard", "ShipCombatMini");
            ShipCombatWidgetUtils.FluxSoftColorTxt = Colors.ToHex3(fillColor, '#');
            ShipCombatWidgetUtils.FluxHardColorTxt = Colors.ToHex3(fill2Color, '#');
            ShipCombatWidgetUtils.FluxSoftColor = fillColor;
            ShipCombatWidgetUtils.FluxHardColor = fill2Color;

            wProgressFlux.Progress.SetFillColor(fillColor);
            wProgressFlux.SetStackedColor(fill2Color);
        }

        public void UpdateBattleStations() {

            var lacksOperatorNow = false;
            for (int i = spawnedBS.Count - 1; i >= 0; i--) {
                WBattleStationMiniIcon bs = spawnedBS[i];
                bs.UpdateState();
                if (!lacksOperatorNow && bs.IsUnmanned) {
                    lacksOperatorNow = true;
                }
            }
            if (lacksOperator != lacksOperatorNow) {
                lacksOperator = lacksOperatorNow;
                RebuildAlert();
                RebuildLayout();
            }
        }
    }
}
