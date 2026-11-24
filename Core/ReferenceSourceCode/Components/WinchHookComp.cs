using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class WinchHookComp : BaseComponent<WinchHookComp>, IRanged {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new WinchHookComp());
        }
        protected override void OnConfig() {
            Range = Config.GetFloat(PropertyIdH.Range, 100f);
        }

        public override string ToString() {
            return $" * WinchHookComp [Range {Range}]";
        }

        public float Range { get; private set; }
        public bool IsRangeUpgraded;

        public Color RadiusColor => Consts.EnergyGridConnectorReachColor;

        public bool RadiusSameArea => false;

        public ElectricNodeComp ENode => Tile.ENode;

        public bool CanConnectWith(WinchAnchorComp a) {
            if (!a.IsConstructed) { return false; }
            if (IsRangeUpgraded) { return true; }
            return Pos.SqrDistance(a.Entity.PosIdx, Tile.PosIdx) <= Range * Range;
        }

        public bool TryReelInToAnchor(WinchAnchorComp anch) {
            if (anch == null) {
                S.Sig.ShowErrorMessage.Send(T.NoWinchAnchor);
                return false;
            }
            if (!ENode.IsPowerable) {
                S.Sig.ShowErrorMessage.Send($"{T.EnergyNotConnected}: {Texts.LinkToTile(Tile)}");
                return false;
            }
            if (!ENode.IsReachable) {
                S.Sig.ShowErrorMessage.Send($"{T.EnergyOff_Grid}: {Texts.LinkToTile(Tile)}");
                return false;
            }
            var isReachable = CanConnectWith(anch);
            if (!isReachable) {
                S.Sig.ShowErrorMessage.Send(T.WarningTooFar);
                return false;
            }
            var hookIsland = S.Sys.Areas.IslandAt(Tile.PosIdx);
            var island = S.Sys.Areas.IslandAt(anch.Tile.PosIdx);
            EntityUtils.HideEntityLink();
            //return island >= 1 && island != hookIsland;
            if (hookIsland < 1 || island < 1 || island == hookIsland) {
                WPopup.Create(false, IconId.CWarning, T.OperationFailed, T.WinchErrorSections)
                    .WithAction2(T.Sections, () => {
                        S.Sig.ShowOverlay.Send(OverlayId.Islands);
                    })
                    .WithAction(T.Ok, null)
                    .Spawn();
                return false;
            }
            //S.Sig.CameraFocusRooted.Send(Entity, anch.Entity);
            if (!GameState.IsCurrent(S)) {
                D.Err("Game state not current when clicking Winch Hook reel in! {0}", S);
            }
            // deselect winch
            S.Toolbox.Clear();
            S.Toolbox.FreeSelect.Deactivate();
            S.CmdQ.Enqueue(new CmdWinchSections(
                Tile.PosIdx, anch.Tile.PosIdx, true));
            return true;
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false)) {
                    S.UIQuery.ShowTutorialPopup.Ask(WinchingPage.PageId, 1f);
                }
            }
        }

        public void InstallRangeUpgrade(bool add) {
            IsRangeUpgraded = add;
            if (IsRangeUpgraded) {
                Range = 0;
            } else {
                Range = Config.GetFloat(PropertyIdH.Range, 100f);
            }
        }
    }
}
