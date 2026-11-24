using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenu_FlightCapability : StarmapMenuBlock {
        private readonly MatType fuelSectorType;
        private readonly MatType fuelRegionType;
        private readonly BInt fuelSector;
        private readonly BInt fuelRegion;
        public override int Order => 1;
        private bool checklistExpanded;
        private Lifetime lifetime;

        public StarmapMenu_FlightCapability(StarmapMenuWidget parent, GameState s, StarmapContext ctx,
                MatType fuelSectorType, MatType fuelRegionType, BInt fuelSector, BInt fuelRegion)
                : base(parent, ctx, s) {
            this.fuelSectorType = fuelSectorType;
            this.fuelRegionType = fuelRegionType;
            this.fuelSector = fuelSector;
            this.fuelRegion = fuelRegion;
        }

        public override void Load(int siblingIdx) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("FlightCapability");
            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.ShipNavFlightCapability);

            var layerState = S.Sys.ShipNav.EffectiveHyperspaceStateFor(ctx.ParentSO);

            AddFlightCondition(block, idx++);
            AddHyperjumpCharge(block, idx++, layerState);
            AddAvailableJumps(block, idx++, layerState);
            AddHyperdrivesRunning(block, idx++, layerState);
            AddFuel(block, idx++);
        }

        private void AddFuel(WContentBlock block, int idx) {
            var w4 = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2,
                block.Container, idx);

            MatType fuelType;
            BInt fuelAmount;

            string hsLayer = SOUtils.CurrentStarmapLayer(S, true);
            if (hsLayer == UniverseLayer.Universe) {
                fuelType = fuelRegionType;
                fuelAmount = fuelRegion;
            } else {
                fuelType = fuelSectorType;
                fuelAmount = fuelSector;
            }
            w4.Load(fuelType.IconId, fuelType.NameT);
            fuelAmount.Bind(lifetime, (v) => {
                w4.SetText2(Units.NumColoredAbbr(v));
            });
            UIUtils.AddCodexTooltip(lifetime, w4.gameObject, w4, fuelType.Def);
        }

        private void AddHyperdrivesRunning(WContentBlock block, int idx, HyperspaceLayerState layerState) {
            var w3 = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2,
                block.Container, idx);
            w3.SetText(T.HyperdrivesRunning);
            new C2<int, int>(lifetime,
                layerState.EnginesReady,
                layerState.Engines,
                (ready, total) => {
                    w3.SetImage(ready < 1 ? IconId.CCross : IconId.CCheck);
                    w3.SetText2(Units.XOutOfY(ready, total));
                }
            );
            UIUtils.AddTipFunc(lifetime, w3, () => {
                return DrivesRunningTip(layerState);
            });
        }

        private void AddAvailableJumps(WContentBlock block, int idx, HyperspaceLayerState layerState) {
            var w2 = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2,
                block.Container, idx);
            w2.SetText(T.HyperjumpsAvailable);
            var b2_1 = new C2<int, int>(
                lifetime,
                layerState.Jumps,
                layerState.Engines,
                (jumps, maxJumps) => {
                    w2.SetImage(jumps < 1 ? IconId.CCross : IconId.CCheck);
                    w2.SetText2(Units.XOutOfY(jumps, maxJumps));
                }
            );
            UIUtils.AddTipFunc(lifetime, w2, AvailableJumpsTip);
        }

        private void AddHyperjumpCharge(WContentBlock block, int idx, HyperspaceLayerState layerState) {
            var w1 = UISpawn.Widget<WIconText2>(lifetime,UIPrefabId.WContentText2,
                block.Container, idx);
            w1.SetText(T.NounHyperjumpCharge);
            layerState.MaxCharge.Bind(lifetime, (v) => {
                w1.SetImage(v < 1f ? IconId.CCross : IconId.CCheck);
                w1.SetText2(Units.Percentage(v));
            });
            UIUtils.AddTipFunc(lifetime, w1, () => {
                return HyperjumpChargeTip(layerState);
            });
        }

        private void AddBoolItem(RectTransform rt, int idx, string name, BBool binding) {
            var w1 = UISpawn.Widget<WIconText2>(lifetime,UIPrefabId.WContentText2, rt, idx);
            w1.SetText(name);
            var b = binding.Bind(lifetime, (v) => {
                w1.SetImage(v ? IconId.CCheck : IconId.CCross);
                w1.SetText2(v ? T.Yes : T.No);
            });
        }

        private void AddFlightCondition(WContentBlock block, int idx) {
            var flightCap = S.Query.GetShipFlightCondition.Ask();
            var w0 = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContentListButton, block.Container, idx);
            w0.Load(IconId.CCheck, T.NounChecklist);
            var listButton = w0.GetComponent<WContentListButton>();

            BBool engineBinding = null;
            switch (ctx.Layer) {
                case UniverseLayer.Universe:
                    engineBinding = flightCap.HasEnginesUniverse;
                    break;
                case UniverseLayer.Region:
                    engineBinding = flightCap.HasEnginesRegion;
                    break;
                case UniverseLayer.Sector:
                    engineBinding = flightCap.HasEnginesSector;
                    break;
                default:
                    D.Throw("Unknown starmap layer when getting flight condition: {0}", ctx.Layer);
                    break;
            }
            listButton.LoadOnToggle(lifetime, (l, rt, b1, b2) =>
                ToggleReadiness(flightCap, engineBinding, rt, b1, b2), checklistExpanded);

            var b4 = new B4<bool, bool, bool, bool, int>(
                lifetime,
                flightCap.IsInOnePiece,
                flightCap.HasBridge,
                flightCap.HasBridgeOperator,
                engineBinding,
                (onePiece, hasBridge, hasOperator, hasEngines) => {
                    int total = 0;
                    if (onePiece) { total++; }
                    if (hasBridge) { total++; }
                    if (hasOperator) { total++; }
                    if (hasEngines) { total++; }
                    return total;
                }
            );
            // lifetime is null because b4 already bound
            var b4b = b4.Bind(null, (countOK) => {
                w0.SetImage(countOK == 4 ? IconId.CCheck : IconId.CCross);
                w0.SetText2(Units.XOutOfY(countOK, 4));
            }, allowNullLifetime: true);
        }

        private bool ToggleReadiness(ShipFlightCondition cond, BBool engineBinding,
                RectTransform rt, bool willShow, bool wasLoaded) {
            checklistExpanded = willShow;
            if (wasLoaded || !willShow) { return wasLoaded; }
            D.Warn("Toggling flight readiness checklist: {0}", willShow);
            int idx = 0;
            AddBoolItem(rt, idx++, T.ShipInOnePiece, cond.IsInOnePiece);
            AddBoolItem(rt, idx++, T.ShipHasBridgeControls, cond.HasBridge);
            AddBoolItem(rt, idx++, T.ShipHasBridgeControlsOperator, cond.HasBridgeOperator);
            AddBoolItem(rt, idx++, T.ShipEnginesReady, engineBinding);
            return true;
        }

        private string DrivesRunningTip(HyperspaceLayerState layerState) {
            string hsLayer = SOUtils.CurrentStarmapLayer(S, true);
            if (!S.Sys.ShipNav.CanMoveInHyperspace(true, hsLayer, out var failR)) {
                return failR;
            }
            if (layerState.EnginesReady < 1) {
                return T.HyperdrivesNotRunningTip;
            }
            return null;
        }

        private string AvailableJumpsTip() {
            string hsLayer = SOUtils.CurrentStarmapLayer(S, true);
            if (!S.Sys.ShipNav.CanMoveInHyperspace(true, hsLayer, out var failR)) {
                return failR;
            }
            return null;
        }

        private string HyperjumpChargeTip(HyperspaceLayerState layerState) {
            if (A.IsInHyperspace && layerState.MaxCharge < 1f) {
                return T.HyperdrivesChargeHyperspaceTip;
            }
            string hsLayer = SOUtils.CurrentStarmapLayer(S, true);
            if (!S.Sys.ShipNav.CanMoveInHyperspace(true, hsLayer, out var failR)) {
                return failR;
            }
            return T.Ready;
        }
    }
}
