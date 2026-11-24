#if UNITY_EDITOR
//#define DEBUG_GRID
#endif
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Visuals;
using KL.Utils;
using Unity.Collections;
using UnityEngine;

namespace Game.Systems.Electricity {
    public sealed class ElectricityUI {
        private readonly GameState S;
        private readonly ElectricitySys sys;
        private readonly ReusableNativeArray<ElectricConnection> arrConnections;
        private readonly ReusableNativeArray<ElectricNode> arrNodes;
        private readonly ElectricityVFX vfx;
        private float previousZoom;
        private bool isShowingOverlay;
        private GridDisplayMode gridDisplayMode;
        public void SetGridDisplayMode(int mode) {
            gridDisplayMode = (GridDisplayMode)mode;
            RefreshVFX();
        }
        private int selectedGrid;
        private bool hasSelectedGrid;
        public void SetSelectedGrid(int id, bool toggle) {
            if (toggle && hasSelectedGrid && id == selectedGrid) {
                hasSelectedGrid = false;
                selectedGrid = -1;
                RefreshVFX();
                return;
            }
            selectedGrid = id;
            hasSelectedGrid = id >= 0;
            RefreshVFX();
        }

        private enum GridDisplayMode {
            Auto,
            All,
            ConnectorsOnly,
            Teleporters,
        }

        public ElectricityUI(GameState s, ElectricitySys sys,
                ReusableNativeArray<ElectricNode> arrNodes,
                ReusableNativeArray<ElectricConnection> arrConnections,
                NativeList<ElectricGrid> grids) {
            S = s;
            this.sys = sys;
            this.arrConnections = arrConnections;
            this.arrNodes = arrNodes;
            vfx = s.Sys.Particles.ElectricityVFX;
            S.Sig.CamOrthoSizeChanged.AddListener(OnZoomChange);
        }

        private void OnZoomChange(float newZoom) {
            if (isShowingOverlay && gridDisplayMode == GridDisplayMode.Auto) {
                var cutoff = Tunables.ElectricityNodeViewCutoffHeight;
                //D.Log("Cutoff: {0}. Zoom: {1} -> {2}", cutoff, previousZoom, newZoom);
                if (previousZoom >= cutoff && newZoom < cutoff
                        || newZoom > cutoff && previousZoom <= cutoff) {
                    previousZoom = newZoom;
                    RefreshVFX();
                }
            }
            previousZoom = newZoom;
        }

        public void ToggleOverlay(OverlayInfo info, bool on) {
            if (isShowingOverlay) {
                if (!on || info.Id != OverlayId.Electricity) {
                    // The selected grid is not reset here: the Electricity
                    // panel owns it and may stay open after the overlay closes
                    isShowingOverlay = false;
                    S.Sig.ShowConnectorCountsNew.Send(false);
                }
            } else {
                if (on && info.Id == OverlayId.Electricity) {
                    isShowingOverlay = true;
                    S.Sig.ShowConnectorCountsNew.Send(true);
                }
            }
        }

        public void LoadGridVFX(VFXLinesBuffer vfx) {
            if (vfx == null) {
                D.Err("Trying to LoadGridVFX with null vfx lines buffer!");
                return;
            }
            int count = 0;
            bool skipLeaves = false;
            switch (gridDisplayMode) {
                case GridDisplayMode.All: {
                    skipLeaves = false;
                    break;
                }
                case GridDisplayMode.ConnectorsOnly: {
                    skipLeaves = true;
                    break;
                }
                case GridDisplayMode.Auto: {
                    if (previousZoom == 0) {
                        previousZoom = RenderUtils.CameraOrtho;
                    }
                    skipLeaves = previousZoom >
                        Tunables.ElectricityNodeViewCutoffHeight;
                    break;
                }
                case GridDisplayMode.Teleporters: {
                    var tps = S.Components.AllByType<TeleporterComp>();
                    if (tps != null) {
                        foreach (TeleporterComp tp in tps) {
                            count = AddTeleporterLink(vfx, count, tp);
                        }
                    }
                    vfx.Show(count);
                    return;
                }
            }
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                if (hasSelectedGrid && conn.GridId != selectedGrid) {
                    continue;
                }
                if (skipLeaves && (
                        conn.Type != ElectricConnectionType.TwoConnectors &&
                            conn.Type != ElectricConnectionType.ShipComputer)) {
                    continue;
                }
                var color = VfxColorFor(in conn);
                var p1 = conn.Pos1;
                var p2 = conn.Pos2;
                count = vfx.AddLink(p1, p2, color, count);
            }
            vfx.Show(count);
        }
        public int AddTeleporterLink(VFXLinesBuffer vfx, int count, TeleporterComp tp) {
            if (tp == null) { return count; }
            var tpOutbound = tp.Outbound;
            if (tpOutbound != null) {
                bool isBidirectional = tpOutbound.Outbound?.EntityId == tp.EntityId;
                bool isLinkActive = tp.CanTeleport && tpOutbound.CanTeleport;
                Color tpColor;
                if (isLinkActive) {
                    tpColor = isBidirectional ?
                        Consts.EnergyConnectionColorTeleporterBidirectional :
                            Consts.EnergyConnectionColorTeleporter;

                } else {
                    tpColor = Consts.EnergyConnectionColorConsumerDeficit;
                }

                count = vfx.AddLink(tp.Tile.Position, tpOutbound.Tile.Position,
                    tpColor, count);
            }
            return count;
        }

        public bool ShowTeleporterHover(TeleporterComp tp) {
            var count = AddTeleporterLink(vfx.VFX, 0, tp);
            if (count > 0) {
                vfx.VFX.Show(count);
                return true;
            }
            return false;
        }

        public void HideTeleporterHover() {
            vfx.VFX.Stop();
        }

        public int AddVFXFor(VFXLinesBuffer vfx, int nodeId, int count) {
            for (int i = 0; i < arrConnections.Count; i++) {
                var conn = arrConnections.Array[i];
                if (conn.Connection.Item1 == nodeId ||
                        conn.Connection.Item2 == nodeId) {
                    var color = VfxColorFor(in conn);
                    count = vfx.AddLink(conn.Pos1, conn.Pos2, color, count);
                }
            }
            return count;
        }

        private Color VfxColorFor(in ElectricConnection conn) {
            //if (!node.IsReachable) { return Consts.EnergyConnectionColorOffF4; }
            if (conn.Type == ElectricConnectionType.TwoConnectors) {
                return Consts.EnergyConnectionColorConnector;
            }
            if (arrNodes.TryGet(conn.NodeIdxs.Item1, out var node)) {
                if (!node.IsOn) {
                    return Consts.EnergyConnectionColorOff;
                }
                if (node.IsProducing) {
                    return Consts.EnergyConnectionColorProducer;
                }
                if (node.HasDeficit) {
                    return Consts.EnergyConnectionColorConsumerDeficit;
                }
                if (node.IsConsuming) {
                    return Consts.EnergyConnectionColorConsumer;
                }
            }
            return Consts.EnergyConnectionColorOff;
        }


        public void RefreshVFX() {
            vfx.RefreshView();
        }


        public void AfterRebuild() {
            vfx.RefreshView();
        }
    }
}
