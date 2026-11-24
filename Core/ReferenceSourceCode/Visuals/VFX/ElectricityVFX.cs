using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Tools;
using Game.UI;
using KL.Grid;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Visuals {
    public sealed class ElectricityVFX {
        private GameState S;
        public VFXLinesBuffer VFX;
        public readonly VFXLine ConnectVFX;
        public SelectionVFX SelVFX;

        private bool isShowingHover;
        private bool isShowingGrid;
        private bool isShowingSelected;
        private Tile lastHoveredTile;
        private List<Tile> lastSelection;
        public int MaxGridSize;

        private int lastBuildTileHoverPos;

        public void ShowBuildTile(Tile tilePreview, int hoverPos) {
            if (tilePreview == null) {
                // The build tool drops its preview after every placement and
                // rotation, and makes a new one next frame. Redrawing the grid
                // for that one frame would flash it and cost a full rebuild.
                lastBuildTileHoverPos = -1;
                //ConRangeVFX.Stop();
                if (!isShowingGrid) {
                    VFX.Stop();
                }
                return;
            }
            if (tilePreview.ENode == null) {
                // Nothing electric to preview, give the lines back to the
                // grid overlay if it is on
                EndBuildPreview();
                return;
            }
            var tileCenter = tilePreview.Transform.RotatedCenter(hoverPos);
            // Can't use tilePreview.EnergyNode;
            var node = tilePreview.GetComponent<ElectricNodeComp>();
            if (node == null) {
                //ConRangeVFX.Stop();
                return;
            } else {
                var range = node.Node.ConnectorRange;
                int cons = A.Grid.GetAutoConnectCandidatesForPosition(
                    tileCenter, range, Caches.IntArray128, false);
                if (cons > 0) {
                    var rc = 0;
                    for (int i = 0; i < cons; i++) {
                        var conn = Caches.IntArray128[i];
                        var connComp = A.Grid.GetCompByIdx(conn);
                        if (connComp != null) {
                            rc = VFX.AddLink(connComp.Node.Position,
                                tileCenter, Consts.EnergyGridConnectorReachColor, rc);
                        }
                    }
                    // Reach lines replace the grid lines in the shared buffer
                    isShowingGrid = false;
                    if (lastBuildTileHoverPos != hoverPos) {
                        lastBuildTileHoverPos = hoverPos;
                        VFX.Show(rc);
                    }
                } else {
                    lastBuildTileHoverPos = -1;
                    isShowingGrid = false;
                    VFX.Stop();
                }
            }
        }

        // The build tool is done with the shared line buffer
        public void EndBuildPreview() {
            lastBuildTileHoverPos = -1;
            if (!TryShowGrid()) {
                VFX.Stop();
            }
        }

        public void ShowConnectionDrag(Tile currentTile, Tile hoveredTile,
                Vector2 lastDragStart, Vector2 lastDragEnd, Vector2 hoverPos,
                    bool isDragging) {
            var count = 0;
            ElectricNodeComp hovCon = hoveredTile?.ENode;
            ElectricNodeComp curCon = currentTile?.ENode;
            if (hoveredTile != lastHoveredTile) {
                lastHoveredTile = hoveredTile;
                isShowingHover = false;
            }
            var rangeCount = 0;
            if (hovCon != null) {
                count = A.Grid.UI.AddVFXFor(VFX, hovCon.EntityId, count);
                rangeCount = SelVFX.AddRange(rangeCount, hoveredTile, hoveredTile.PosIdx, 0.5f);
            }
            if (isDragging && currentTile != hoveredTile && curCon != null) {
                bool canReach = curCon != null || hovCon != null;
                if (canReach && curCon != null) {
                    canReach = Vectors.SqrDistance(lastDragStart, lastDragEnd)
                        <= curCon.RangeSqr;
                }
                if (canReach && hovCon != null) {
                    canReach = Vectors.SqrDistance(lastDragStart, lastDragEnd)
                        <= hovCon.RangeSqr;
                }
                float4 conColor;
                if (canReach) {
                    // will we connect or disconnect?
                    if (curCon.IsConnectedTo(hovCon)) {
                        conColor = Consts.EnergyConnectionColorCreateDisconnectF4;
                    } else {
                        conColor = Consts.EnergyConnectionColorCreateConnectF4;
                    }
                } else {
                    conColor = Consts.EnergyConnectionColorCreateF4;
                }
                var fromTo = new float4(
                    lastDragStart.x, lastDragStart.y,
                    lastDragEnd.x, lastDragEnd.y);
                ConnectVFX.Update(fromTo, conColor);
                rangeCount = SelVFX.AddRange(rangeCount, currentTile, currentTile.PosIdx);
            } else {
                ConnectVFX.Stop();
            }
            SelVFX.ShowRange(rangeCount);
            if (count > 0) {
                isShowingGrid = false;
                if (!isShowingHover) {
                    isShowingHover = true;
                    VFX.Show(count);
                }
            } else if (isDragging || !TryShowGrid()) {
                VFX.Stop();
                isShowingHover = false;
                isShowingGrid = false;
            }
        }

        public ElectricityVFX(GameState s, float4 bounds) {
            S = s;
            MaxGridSize = 1024 * 16;
            VFX = new VFXLinesBuffer("ElectricityGridNew", MaxGridSize,
                Color.magenta, bounds);
            ConnectVFX = new VFXLine("ElectricityGridConnect", bounds);

            S.Sig.TilesSelected.AddListener(ShowSelected);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.HideOverlay.AddListener(OnHideOverlay);
            S.Sig.MultiSelect.AddListener(OnMultiSelect);
        }

        private void OnHideOverlay() {
            if (isShowingGrid) {
                VFX.Stop();
                isShowingSelected = false;
                isShowingGrid = false;
            }
        }

        private void OnMultiSelect(List<Being> b, List<Obj> o, List<Tile> t) {
            ShowSelected(t);
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            var isEnergy = info.Id == OverlayId.Electricity;
            if (!isEnergy) {
                if (on && isShowingGrid) {
                    isShowingGrid = false;
                    VFX.Stop();
                }
                return;
            }
            if (on) {
                isShowingGrid = true;
                A.Grid.UI.LoadGridVFX(VFX);
            } else if (isShowingSelected) {
                ShowSelected(lastSelection);
            } else {
                VFX.Stop();
                isShowingSelected = false;
                isShowingGrid = false;
            }
        }

        public void ShowSelected(List<Tile> selectedTiles) {
            lastSelection = selectedTiles;
            var stc = selectedTiles == null ? 0 : selectedTiles.Count;
            var count = 0;
            for (int i = 0; i < stc; i++) {
                var tile = selectedTiles[i];
                if (tile.Transform.IsChild) {
                    tile = tile.Transform.Parent;
                }
                if (tile.ENode != null) {
                    count = A.Grid.UI.AddVFXFor(VFX, tile.Id, count);
                    S.Toolbox.ConnectTool.ShowStats(tile.ENode);
                }
                count = A.Grid.UI.AddTeleporterLink(VFX, count, tile.Teleporter);
            }

            if (count > 0) { // || conRangeOffset > 0) {
                VFX.Show(count);
                isShowingSelected = true;
                isShowingGrid = false;
                // TODO
                //conRange.ShowParticles(conRangeOffset, false);
            } else if (!TryShowGrid()) {
                VFX.Stop();
                isShowingSelected = false;
                isShowingGrid = false;
            }
        }


        public bool TryShowGrid(bool force = false) {
            //D.Warn("WillShowGrid: showing:{0}, force:{1}", isShowingGrid, force);
            if (force && isShowingGrid) {
                isShowingGrid = false;
            }
            if (UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Electricity) {
                if (!isShowingGrid) {
                    A.Grid.UI.LoadGridVFX(VFX);
                    S.Sig.ShowConnectorCountsNew.Send(true);
                    isShowingSelected = false;
                }
                isShowingHover = false;
                isShowingGrid = true;
                return true;
            }
            return false;
        }

        public void Destroy() {
            VFX?.Destroy();
            ConnectVFX?.Destroy();
        }

        public void RefreshView() {
            if (isShowingGrid) {
                TryShowGrid(true);
            } else {
                var wasShowingGrid = isShowingSelected;
                ShowSelected(lastSelection);
                if (!isShowingSelected && wasShowingGrid) {
                    TryShowGrid();
                }
            }
        }
    }
}
