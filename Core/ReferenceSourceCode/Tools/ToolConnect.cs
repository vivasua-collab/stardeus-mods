using Game.Constants;
using Game.Commands;
using Game.Data;
using KL.Grid;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Randomness;
using KL.Utils;
using Game.UI;
using Game.Systems.UIUX;
using Game.Components;
using System;
using Game.Input;
using static UnityEngine.ParticleSystem;
using Game.Systems;
using Unity.Mathematics;
using Game.Visuals;
using Game.Systems.Electricity;
using Game.Procgen;

namespace Game.Tools {
    public sealed class ToolConnect : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolConnect());
        }

        public override string MenuIcon => IconId.WOverlayEnergy;
        public override ActionType Shortcut => ActionType.ShortcutEnergyConnect;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/e" };

        public override string Id => ToolId;
        public const string ToolId = "connect";
        public override int Cursor => CursorTypeH.CursorBolt;
        private const int cursorInvalid = CursorTypeH.CursorCancel;

        public override bool IsConfigurable => false;
        public override bool IsClickedDown => wasClickedDown;

        private Tile currentTile;
        private Tile endTile;
        private readonly HashSet<ElectricNodeComp> showingConnectableTexts = new();

        private Vector2 lastDragStart;
        private Vector2 lastDragEnd;
        private Vector2 hoverPosition;

        // For buttons that send the player from a device to the tool
        public static void Start() {
            A.S.Toolbox.SetActive(A.S.Toolbox.ConnectTool);
        }

        public void ClearShownTexts() {
            foreach (var node in showingConnectableTexts) {
                node.ShowConnectableInfoText(false);
            }
            showingConnectableTexts.Clear();
        }

        private Tile hoveredTile;
        private ElectricNodeComp hovered;
        private ElectricityVFX vfx;
        private SelectionVFX selVFX;
        public void ShowStats(ElectricNodeComp node) {
            if (node != null && node.CanShowConnectableInfo) {
                if (showingConnectableTexts.Add(node)) {
                    node.ShowConnectableInfoText(true);
                }
            }
        }
        public override void UpdateVFX(ParticlesSys ps) {
            vfx ??= ps.ElectricityVFX;
            selVFX ??= ps.SelectionVFX;
            vfx.ShowConnectionDrag(
                currentTile, hoveredTile, lastDragStart, lastDragEnd,
                    hoverPosition, isDragging);
            if (!Consts.DisableHoverEffects) {
                selVFX.UpdateHoverTile(hoveredTile?.ENode != null,
                    hoveredTile);
            } else {
                selVFX.UpdateHoverTile(false, null);
            }
        }
        private void ClearVFX() {
            vfx?.ShowConnectionDrag(null, null,
                Consts.V2Zero, Consts.V2Zero, Consts.V2Zero, false);
            selVFX?.UpdateHoverTile(false, null);
        }

        public override void Activate() {
            S.Sig.ShowConnectorCountsNew.Send(true);
            RemoveTooltip();
        }

        private void ClearHovered() {
            if (hovered != null) {
                if (showingConnectableTexts.Remove(hovered)) {
                    hovered.ShowConnectableInfoText(false);
                }
                hovered = null;
            }
        }

        // UpdateVFX drops the hover outline and the hovered node's connections
        // once nothing is hovered
        protected override void HideHover() {
            base.HideHover();
            ClearHovered();
            hoveredTile = null;
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            var pos = EntityUtils.ToPosSafe(coords);
            hoverPosition = coords;
            if (pos != lastHoverPos) {
                ClearHovered();
                if (ToolGrid.IsWithinBounds(pos)) {
                    hoveredTile = EntityUtils.GetTopmostParentTile(pos);
                    hovered = hoveredTile?.ENode;
                    if (hoveredTile != null && hoveredTile.Definition.LayerId != WorldLayer.Floor) {
                        // Maybe we are hovering above a electrical floor tile
                        var floorBelow = EntityUtils.FloorAt(pos);
                        if (floorBelow?.ENode?.IsConnectableNow(out _) == true) {
                            if (hovered?.IsConnectableNow(out _) != true) {
                                hoveredTile = floorBelow;
                                hovered = floorBelow.ENode;
                            }
                        }
                    }
                    if (hovered != null) {
                        ShowStats(hovered);
                    }
                } else {
                    hoveredTile = null;
                    hovered = null;
                }
            }

            if (isDragging) {
                OnHoverDrag();
                ValidateUpcomingClickUp(pos);
                return;
            }

            if (!ToolGrid.IsWithinBounds(pos)) {
                return;
            }
            if (hovered != null) {
                ShowHoveredTint(hovered, pos);
            } else {
                ClearPreview();
            }
            lastHoverPos = pos;
        }

        private void ShowHoveredTint(ElectricNodeComp hovered, int pos) {
            if (hovered != null && hovered.IsConnectableNow(out _)) {
                ShowGreenTile(pos);
            } else {
                ShowRedTile(pos);
            }
        }

        private void ExecuteClickUp(int pos) {
            if (endTile == null) { return; }
            ElectricNodeComp startNode = currentTile?.ENode;
            if (startNode == null) { return; }
            // Could be null
            ElectricNodeComp endNode = endTile?.ENode;
            if (startNode == endNode) {
                return;
            }

            if (!A.InstantBuild) {
                if (!startNode.IsReachable && endNode?.IsReachable == false) {
                    if (!startNode.IsConnector && endNode?.IsConnector != true) {
                        A.S.Sig.ShowErrorMessage.Send(T.ElNotAConnector);
                    } else {
                        if (startNode?.IsConstructed == true && endNode?.IsConstructed == true) {
                            ShowUnreachable();
                        }
                    }
                    return;
                }
            }

            if (startNode.IsConnected && endNode?.IsConnected == true) {
                // Check if there is by chance two teleporters
                var t1 = startNode.Tile.Teleporter;
                var t2 = endNode.Tile.Teleporter;
                if (t1 != null && t2 != null) {
                    if (t1.Outbound == t2) {
                        // Disconnect
                        t1.UnsetOutbound();
                        Sounds.PlayEnergyOff(t2.Tile.Position);
                        return;
                    } else {
                        // Connect
                        t1.SetOutbound(t2);
                        Sounds.PlayEnergyOn(t2.Tile.Position);
                        return;
                    }
                }
            }

            // We are dragging from a connector
            if (endNode == null) {
                // 9. we are trying to build a connector?
                if (endTile?.Definition.LayerId < WorldLayer.Objects) {
                    if (The.Prefs.GetBool(Pref.GDragRelBuildCon,
                            PrefH.GDragRelBuildCon,
                            Consts.DefaultDragRelBuildCon)) {
                        if (startNode.Tile.HasComponent<WinchHookComp>()) {
                            return;
                        }
                        ShowBuildConnectorChoice(startNode, endTile);
                        return;
                    }
                }
            }

            if (startNode.IsConnectedTo(endNode)) {
                // we will try to connect
                if (!startNode.TryDisconnectFrom(endNode, out var err)) {
                    D.Warn("Failed disconnecting {0} and {1}: {2}", startNode, endTile, err);
                    S.Sig.ShowErrorMessage.Send(err);
                } else {
                    Sounds.PlayEnergyOff(endNode.Node.Position);
                    A.Grid.RebuildGrid(false);
                }
            } else {
                // we will try to connect
                if (!startNode.TryConnectTo(endNode, out var err)) {
                    // is it a winch hook and winch anchor?
                    if (TryWinch(currentTile, endTile)) {
                        return;
                    }
                    D.Warn("Failed connecting {0} and {1}: {2}", startNode, endTile, err);
                    The.SysSig.ShowTooltip.Send(Texts.Red(err), false);
                    S.Sig.ShowErrorMessage.Send(err);
                } else {
                    Sounds.PlayEnergyOn(endNode.Node.Position);
                    A.Grid.RebuildGrid(false);
                }
            }
        }

        private bool TryWinch(Tile a, Tile b) {
            if (!a.TryGetComponent(out WinchHookComp hook)) {
                return false;
            }
            b.TryGetComponent(out WinchAnchorComp anchor);
            // anchor can be null, it's ok
            hook.TryReelInToAnchor(anchor);
            return true;
        }

        private void ShowUnreachable() {
            S.Sig.ShowErrorMessage.Send(UnreachableInfo());
        }

        public string UnreachableInfo() {
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                c => c.Tile.IsConstructed);
            string cpuName = null;
            if (cpu != null) {
                cpuName = Texts.LinkToTile(cpu.Tile);
            } else {
                cpuName = The.Defs.FastGet(DefIdsH.ObjectsDevicesShipComputer).CodexLink;
            }
            return T.ConnectorShipCpuTip_V1.F(cpuName);
        }

        public void OnDragInFreeSelect(int lastHoverPos, Vector2 dragStart, Vector2 dragEnd,
                Tile startTile) {
            this.lastHoverPos = lastHoverPos;
            this.currentTile = startTile;
            hoveredTile = EntityUtils.GetTopmostParentTile(EntityUtils.ToPosSafe(dragEnd));
            endTile = hoveredTile;
            wasClickedDown = true;
            OnDrag(dragStart, dragEnd, Vectors.Distance(dragStart, dragEnd));
            ShowHoveredTint(startTile.ENode, EntityUtils.ToPos(dragStart));
        }

        private void ValidateUpcomingClickUp(int pos) {
            ElectricNodeComp startNode = currentTile?.ENode;
            if (startNode == null) { return; }
            // Could be null
            ElectricNodeComp endNode = endTile?.ENode;

            if (startNode == endNode) {
                RemoveTooltip();
                return;
            }

            if (startNode.Tile.HasComponent<WinchHookComp>()) {
                if (endNode?.IsConnector != true) {
                    UpdateTooltip(T.ReelIn,
                        endTile?.HasComponent<WinchAnchorComp>() == true);
                    return;
                }
            }

            if (startNode.IsConnected && endNode?.IsConnected == true) {
                // Check if there is by chance two teleporters
                var t1 = startNode.Tile.Teleporter;
                var t2 = endNode.Tile.Teleporter;
                if (t1 != null && t2 != null) {
                    if (t1.Outbound == t2) {
                        // Disconnect
                        UpdateTooltip(T.Unlink, true);
                        //t1.Outbound = null;
                        //Sounds.PlayEnergyOff(t2.Tile.Position);
                        return;
                    } else {
                        // Connect
                        //t1.Outbound = t2;
                        UpdateTooltip(T.LinkVerb, true);
                        //Sounds.PlayEnergyOn(t2.Tile.Position);
                        return;
                    }
                }
            }

            // We are dragging from a connector
            if (endNode == null) {
                // 9. we are trying to build a connector?
                if (endTile?.Definition.LayerId < WorldLayer.Objects) {
                    if (The.Prefs.GetBool(Pref.GDragRelBuildCon,
                            PrefH.GDragRelBuildCon,
                            Consts.DefaultDragRelBuildCon)) {
                        if (startNode.Tile.HasComponent<WinchHookComp>()) {
                            return;
                        }
                        UpdateTooltip(T.ElBuildConnectorHere, true);
                        return;
                    }
                }
            } else if (!startNode.IsConnectedTo(endNode)) {
                // we will try to connect
                if (!startNode.CanConnectTo(endNode, out var failReason)) {
                    UpdateTooltip(failReason, false, Texts.TMPColorRed);
                    return;
                }
            }

            RemoveTooltip();
        }

        private string lastTooltip;
        private bool lastActionValid;
        private void UpdateTooltip(string tip, bool isActionValid, string color = null) {
            if (lastActionValid != isActionValid) {
                lastActionValid = isActionValid;
                The.SysSig.SetCursor.Send(isActionValid ? Cursor : cursorInvalid);
            }
            if (lastTooltip != tip) {
                if (color == null) {
                    The.SysSig.ShowTooltip.Send(tip, false);
                } else {
                    The.SysSig.ShowTooltip.Send(Texts.WithColor(tip, color), false);
                }
                //D.Log("Setting tooltip: {0}", lastTooltip);
                lastTooltip = tip;
            }
        }

        private void RemoveTooltip() {
            if (!lastActionValid) {
                lastActionValid = true;
                The.SysSig.SetCursor.Send(Cursor);
            }
            if (lastTooltip != null) {
                //D.Log("Removing tooltip: {0}", lastTooltip);
                The.SysSig.HideTooltip.Send();
                lastTooltip = null;
            }
        }

        public override void OnClickDown(Vector2 coords) {
            var pos = EntityUtils.ToPosSafe(coords);
            currentTile = EntityUtils.GetTopmostParentTile(pos);
            wasClickedDown = true;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            OnDragCommon(start, end);
            lastHoverPos = dragEnd;
            // isDragging = true;
            lastDragStart = start;
            lastDragEnd = end;
            endTile = hoveredTile;
        }

        public override bool Cancel() {
            lastHoverPos = Pos.Invalid;
            isDragging = false;
            currentTile = null;
            hoveredTile = null;
            endTile = null;
            return false;
        }

        public override void Deactivate() {
            //D.Log("Cancelling connect tool");
            ClearVFX();
            isDragging = false;
            currentTile = null;
            lastHoverPos = Pos.Invalid;
            hoveredTile = null;
            endTile = null;
            ClearShownTexts();
            // The electricity overlay outlives the tool, and connector slot
            // labels are part of it
            S.Sig.ShowConnectorCountsNew.Send(
                UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Electricity);
            base.Deactivate();
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!isDragging) {
                EndClickUp();
                return;
            }
            var ctxPos = EntityUtils.ToPos(position);
            if (Pos.IsOOB(ctxPos)) {
                EndClickUp();
                return;
            }
            // Check if maybe end tile has two connections
            if (HasMultipleConnectablesAt(ctxPos) &&
                    currentTile?.ENode?.IsConnector == true) {
                ShowConnectionChoice(currentTile.ENode, ctxPos);
                EndClickUp();
                return;
            }
            ExecuteClickUp(ctxPos);
            EndClickUp();
        }

        private void EndClickUp() {
            wasClickedDown = false;
            currentTile = null;
            endTile = null;
            ClearDragPositions();
            isDragging = false;
            RemoveTooltip();
        }

        public override string ToString() {
            return $"{Id}{GetHashCode()}";
        }

        private bool HasMultipleConnectablesAt(int posIdx) {
            var node1 = EntityUtils.ParentObjAt(posIdx);
            int count = 0;
            if (node1?.IsConstructed == true && node1.ENode != null) {
                count++;
            }
            node1 = EntityUtils.FloorAt(posIdx);
            if (node1?.IsConstructed == true && node1.ENode != null) {
                count++;
            }
            node1 = EntityUtils.WallAt(posIdx);
            if (node1?.IsConstructed == true && node1.ENode != null) {
                count++;
            }
            return count > 1;
        }

        private void ShowConnectionChoice(ElectricNodeComp connector, int posIdx) {
            if (connector == null) {
                return;
            }
            var qc = QuickChoice.Create(T.MultipleNodes);
            AddConnectButton(qc, connector, EntityUtils.ParentObjAt(posIdx), posIdx);
            AddConnectButton(qc, connector, EntityUtils.WallAt(posIdx), posIdx);
            AddConnectButton(qc, connector, EntityUtils.FloorAt(posIdx), posIdx);
            if (qc.Count > 0) {
                S.UIQuery.ShowQuickChoice.Ask(qc);
            }
        }

        private string ConnectButtonText(ElectricNodeComp con, Tile node) {
            if (con == null) { return node.Definition.NameT; }
            var isConnected = con.IsConnectedTo(node.ENode);
            if (isConnected) {
                return $"{T.EnergyNodeDisconnect} {Texts.Accented(GlyphMap.ArrowRight)} {node.Definition.NameT}";
            } else {
                return $"{T.EnergyNodeConnect} {Texts.Accented(GlyphMap.ArrowRight)} {node.Definition.NameT}";
            }
        }

        private void AddConnectButton(QuickChoice qc, ElectricNodeComp con, Tile node,
                int posIdx) {
            if (node?.IsConstructed != true || node.Health.IsHeavilyDamaged
                    || node.ENode == null) {
                return;
            }
            qc.AddIconButton(ConnectButtonText(con, node), new QuickChoice.IconButton() {
                Icon = node.Definition.PreviewSprite,
                OnClick = () => {
                    ToggleConnection(con, node);
                    ShowConnectionChoice(con, posIdx);
                },
                AutoClose = false,
            });
        }

        private void ToggleConnection(ElectricNodeComp con, Tile node) {
            if (con.IsConnectedTo(node.ENode)) {
                if (A.Grid.TryDisconnect(con, node.ENode, out var failR)) {
                    Sounds.PlayEnergyOff(node.Position);
                } else {
                    D.Err("Failed toggling connection (disconnect): {0}", failR);
                }
            } else {
                if (A.Grid.TryConnect(con, node.ENode, out var failR)) {
                    Sounds.PlayEnergyOn(node.Position);
                } else {
                    D.Err("Failed toggling connection (connect): {0}", failR);
                }
            }
        }

        private void ShowBuildConnectorChoice(ElectricNodeComp connector, Tile targetTile) {
            if (targetTile == null || connector == null) {
                return;
            }
            bool isWall = targetTile.Definition.LayerId == WorldLayer.Walls;
            var qc = QuickChoice.Create(T.BuildConnector);
            foreach (var d in The.Defs.Defs) {
                var def = d.Value;
                if (def.IsAbstract) { continue; }
                if (isWall && d.Value.LayerId != WorldLayer.Walls) { continue; }
                if (def.HasComponent(ComponentIdH.ElectricNode) &&
                        def.ComponentConfigFor(ComponentIdH.ElectricNode, true)
                            .GetBool(PropertyIdH.IsConnector, false) && S.Research.IsDefUnlocked(def)) {
                    qc.AddIconButton(def.NameT, new QuickChoice.IconButton() {
                        Icon = def.PreviewSprite,
                        OnClick = () => {
                            OrderConnectorAt(targetTile, connector, def);
                        },
                        AutoClose = true,
                    });
                }
            }
            if (qc.Count > 0) {
                S.UIQuery.ShowQuickChoice.Ask(qc);
            }
        }

        private void OrderConnectorAt(Tile targetTile, ElectricNodeComp connector, Def def) {
            var conProto = EntityUtils.CreatePrototype<Tile>(def);
            // used to be for tutorial
            // if (The.Toolbox.BuildTool.Prevalidate?.Invoke(
            //         targetTile.PosIdx, conProto) == false) {
            //     UISounds.PlayActionDenied();
            //     return;
            // }
            S.CmdQ.Enqueue(new CmdPlaceTile(
                targetTile.PosIdx, conProto, warn: true, instant: A.InstantBuild));
            conProto.ENode.TryConnectPosAfterBuild = connector.PosIdx;
        }
    }
}
