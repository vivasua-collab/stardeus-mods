using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using KL.Grid;
using Game.Components;

namespace Game.Systems.Areas {
    public sealed class IslandsContextPopup : IOverlayLegendPopup {
        private static readonly int popupHash = Hashes.S("IslandsOverlay");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => true;

        private readonly GameState S;
        private readonly AreaSys sys;
        private bool isOpen;

        public IslandsContextPopup(GameState s, AreaSys sys) {
            S = s;
            this.sys = sys;
        }

        public void Show() => S.UIQuery.ShowContextPopup.Ask(this, true);
        public void Hide() => S.UIQuery.ShowContextPopup.Ask(this, false);

        public void Refresh() {
            if (isOpen) {
                Show();
            }
        }

        public void OnPopupOpened() {
            isOpen = true;
        }

        public void OnPopupClosed() {
            isOpen = false;
            if (!sys.IsSwitchingOverlay) {
                A.S.Sig.HideTargetOverlay.Send(OverlayId.Islands);
            }
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("Islands");
            var block = UISpawn.Block(life, wContent, 0);
            int idx = 0;
            int fIdx = 0;

            var canSellSection = S.Sys.Ship.FirstReachableNearby(SpaceObjectTypeIdH.Port) != null;

            // NoBuildZone legend row (footer)
            var noBuild = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, wFooter.Container, fIdx++);
            noBuild.Load(IconId.CStripeZone, T.NoBuildZone, null);
            noBuild.SetTint(Consts.ColorNoBuildZone);
            UIUtils.AddTip(life, noBuild, T.NoBuildZoneTip);
            UIUtils.AddHover(life, noBuild, on => {
                if (on) {
                    S.Sig.CameraFocus.Send(S.Map.Grid.Center, S.Map.Grid.Size, 0);
                }
            });

            // NoFlyZone legend row (footer)
            var noFly = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, wFooter.Container, fIdx++);
            noFly.Load(IconId.CStripeZone, T.NoFlyZone, null);
            noFly.SetTint(Consts.ColorNoFlyZone);
            UIUtils.AddTip(life, noFly, T.NoFlyZoneTip);
            UIUtils.AddHover(life, noFly, on => {
                if (on) {
                    S.Sig.CameraFocus.Send(S.Map.Grid.Center,
                        S.Map.Grid.Size *
                            ((S.Map.Grid.Height - (Consts.MapMargin * 2)) /
                                (float) S.Map.Grid.Height), 0);
                }
            });

            var singleIsland = sys.IslandCount == 1;
            // can be null!
            var cpus = S.Components.AllByType<ShipComputerComp>();

            // Island list
            foreach (var isle in sys.Islands.OrderByDescending(a => a.Value.Count)) {
                var capturedIsle = isle;
                var iconTint = sys.ColorForArea(isle.Key);
                var sizeText = $"{T.Size}: {Units.Num(isle.Value.Count)}";

                var hasCpu = singleIsland ||
                    (cpus != null && cpus.Any(
                        c => c is ShipComputerComp cpu
                        && isle.Value.Contains(cpu.Tile.PosIdx)));
                if (hasCpu) {
                    var isleBtn = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    isleBtn.Load(IconId.CSection, T.IslandNr_V1.F(isle.Key), sizeText, () => {
                        S.Sig.CameraFocusTiles.Send(capturedIsle.Value);
                    });
                    isleBtn.SetTint(iconTint);
                    UIUtils.AddHover(life, isleBtn, on => {
                        if (on) {
                            S.Sig.CameraShowTilesLink.Send(null, capturedIsle.Value,
                                CameraFocus.FlashColor());
                        } else {
                            S.Query.CameraHideLink.Ask();
                        }
                    });
                } else {
                    var isleRow = UISpawn.Widget<WContentButton2Text2>(life,
                        UIPrefabId.WContent2Button2Text2, block.Container, idx++);
                    isleRow.Item.Load(IconId.CSection, T.IslandNr_V1.F(isle.Key), sizeText, () => {
                        S.Sig.CameraFocusTiles.Send(capturedIsle.Value);
                    });
                    isleRow.Item.SetTint(iconTint);
                    UIUtils.AddHover(life, isleRow.Item, on => {
                        if (on) {
                            S.Sig.CameraShowTilesLink.Send(null, capturedIsle.Value,
                                CameraFocus.FlashColor());
                        } else {
                            S.Query.CameraHideLink.Ask();
                        }
                    });
                    isleRow.Button.Load(canSellSection ? T.Sell : T.Abandon, () => {
                        sys.TrySellOrAbandonSection(capturedIsle.Value);
                    });
                }
            }

            wFooter.SetActiveFast(true);

            return $"{T.Sections} ({sys.IslandCount})";
        }
    }
}
