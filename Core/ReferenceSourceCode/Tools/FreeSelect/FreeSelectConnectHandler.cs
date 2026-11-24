using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.Hints;
using KL.Collections;
using KL.Grid;
using Game.Utils;
using UnityEngine;
using KL.Utils;
using Game.UI;
using Game.Components;
using System.Linq;
using Game.CodeGen;

namespace Game.Tools {
    public sealed class FreeSelectConnectHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectConnect";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectConnectHandler());
        }

        public override int Order => 10;

        private ToolConnect connectTool => S.Toolbox.ConnectTool;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }
        private bool WantsConnectOnDrag(Tile t) {
            if (t == null) { return false; }
            if (t.HasComponent<WinchHookComp>()) {
                return true;
            }
            if (t.ENode == null) { return false; }
            if (!t.ENode.IsConnector && t.ENode.IsConnected) {
                // don't want to switch into tool without good reason
                return false;
            }
            return t.IsConstructed;
        }
        private bool IsConnectable(Tile t) {
            if (t == null) { return false; }
            if (t.ENode == null) { return false; }
            return t.IsConstructed;
        }

        private bool IsConnectableSelected(out bool hasConnector) {
            hasConnector = false;
            bool hasConnectable = false;
            for (int i = ctx.SelectedTiles.Count - 1; i >= 0; i--) {
                Tile d = ctx.SelectedTiles[i];
                if (IsConnectable(d)) {
                    if (d.ENode?.IsConnector == true) {
                        hasConnector = true;
                    }
                    hasConnectable = true;
                    if (hasConnectable && hasConnector) {
                        break;
                    }
                }
            }
            return hasConnectable;
        }

        public override void OnClickDown(Vector2 pos) {
            if (ctx.IsShiftPressed && ctx.SelectedCount > 0
                    && ctx.SelectedTiles.Count == ctx.SelectedCount) {
                if (ctx.CanToggleConnect) {
                    TryToggleConnect(ctx.HoveredPos);
                } else {
                    if (ctx.HoveredBeing == null) {
                        UISounds.PlayActionDenied();
                    }
                }
            }
        }

        private bool StartedConnectorDrag(float dragDistance) {
            if (ctx.ClickDownTile == ctx.HoveredTile) { return false; }
            if (!WantsConnectOnDrag(ctx.ClickDownTile)) {
                if (ctx.SelectedTiles.Count > 0
                        && EntityUtils.ParentObj(ctx.SelectedTiles[0]) == ctx.ClickDownTile) {
                    // we are dragging from a selected connectable tile, let's switch
                } else {
                    // nothing is selected
                    if (ctx.ClickDownTile?.Teleporter != null) {
                        // we will allow this for unselected teleporters
                    } else {
                        return false;
                    }
                }
            }
            // see if we are hovering a connectable tile
            if (dragDistance < 5f && !WantsConnectOnDrag(ctx.HoveredTile)) {
                if (ctx.ClickDownTile.ENode?.IsConnector == true && IsConnectable(ctx.HoveredTile)) {
                    // we will want to connect
                } else {
                    return false;
                }
            }
            var hovPos = EntityUtils.ToPos(ctx.DragStart);
            var dragStart = ctx.DragStart;
            var startTile = ctx.ClickDownTile;
            var dragEnd = ctx.DragEnd;
            // Switch the tool
            S.Sys.Hint.MarkNextActionAsAccidental(HintDragConnect.Id);
            S.Sig.ShortcutPressed.Send(ActionType.ShortcutEnergyConnect);
            connectTool.OnDragInFreeSelect(hovPos, dragStart, dragEnd, startTile);
            return true;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            if (!ctx.ReadyForClickUp) { return; }
            // This is called to deselect the object that is currently selected
            // TODO make this more efficient and don't call this over and over
            if (dragDistance > 0.5f) {
                if (ctx.IsAltPressed || ctx.IsCtrlPressed || ctx.IsShiftPressed) {
                    return;
                }
                if (StartedConnectorDrag(dragDistance)) {
                    return;
                }
            }
        }

        public override void OnHover(Vector2 pos) {
            ctx.CanToggleConnect = false;
            ctx.PreventDragging = false;
            if (ctx.HoveredTile?.IsConstructed == true && ctx.HoveredTile.ENode?.IsConnector == true) {
                if (ctx.SelectedTiles.Count == 0) {
                    var glyph = The.Bindings.GetControlName(ActionType.Click);
                    var sb = ctx.BeginTooltipBlock();
                    sb.Append(T.ConnectorTipClickDrag_V1.F(glyph));
                }
            }
            if (ctx.IsExclusivelySelected(ctx.HoveredTile)) {
                // don't show hints to connect to itself
                return;
            }
            if (ctx.IsShiftPressed) {
                ctx.PreventDragging = true;
                var hint = CheckIfConnectable(ctx.HoveredPos);
                if (hint != null) {
                    // we are able to connect if we click
                    if (ctx.CanToggleConnect) {
                        ctx.WantedCursor = connectTool.Cursor;
                        hint = Texts.Green(hint);
                    }
                    var sb = ctx.BeginTooltipBlock();
                    sb.Append(Texts.SubTitle(T.ToolConnect));
                    sb.Append(hint);
                }
            } else if (IsConnectableSelected(out var hasConnector)) {
                if (hasConnector || ctx.HoveredTile?.ENode?.IsConnector == true) {
                    var sb = ctx.BeginTooltipBlock();
                    sb.Append(Texts.SubTitle(T.ToolConnect));
                    var hint = T.HoldButtonForMoreInfo_V1.F(
                        The.Bindings.GetControlName(ActionType.ShiftModifier));
                    hint = Texts.Accented2(hint);
                    sb.Append(hint);
                }
            }
        }

        // returns true if we need to show a tooltip about connection
        private string CheckIfConnectable(int pos) {
            if (ctx.SelectedTiles.Count == 0) { return null; }
            bool isOnlyConnectorsSelected = true;
            bool isOnlyTeleportersSelected = true;
            bool isOnlyENodesSelected = true;
            if (ctx.SelectedTiles.Count > 0) {
                for (int i = ctx.SelectedTiles.Count - 1; i >= 0; i--) {
                    Tile t = ctx.SelectedTiles[i];
                    t = EntityUtils.ParentObj(t);
                    if (t.ENode == null) {
                        isOnlyConnectorsSelected = false;
                        isOnlyTeleportersSelected = false;
                        isOnlyENodesSelected = false;
                        break;
                    } else {
                        if (t.ENode?.Node.IsConnector != true) {
                            isOnlyConnectorsSelected = false;
                        }
                        if (t.Teleporter == null) {
                            isOnlyTeleportersSelected = false;
                        }
                    }
                }
            }
            if (!isOnlyConnectorsSelected && !isOnlyTeleportersSelected &&
                    !isOnlyENodesSelected) {
                return null;
            }
            var obj = ctx.HoveredTile;
            if (obj == null || !obj.IsConstructed) {
                //nothing to do
            } else if (isOnlyConnectorsSelected && obj.ENode != null) {
                if (obj.ENode.IsConnector) {
                    connectTool.ShowStats(obj.ENode);
                }
                MarkConnectable();
            } else if (isOnlyENodesSelected && obj.ENode?.Node.IsConnector == true) {
                connectTool.ShowStats(obj.ENode);
                MarkConnectable();
            } else if (isOnlyTeleportersSelected && obj.Teleporter != null) {
                MarkConnectable();
            }
            if (isOnlyConnectorsSelected) {
                return T.MassConnectTipConn_V1.F(
                    The.Bindings.GetControlName(ActionType.Click));
            } else if (isOnlyTeleportersSelected) {
                var tip1 = T.MassConnectTipTeleporters_V1.F(
                    The.Bindings.GetControlName(ActionType.Click));
                var tip2 = T.MassConnectTipEnodes_V1.F(
                    The.Bindings.GetControlName(ActionType.Click));
                return $"{tip1}\n * {tip2}";
            } else if (isOnlyENodesSelected) {
                return T.MassConnectTipEnodes_V1.F(
                    The.Bindings.GetControlName(ActionType.Click));
            } else {
                D.Err("Unhandled case: IsOnlyConSel: {0}, IsOnlyTPSel: {1}, IsOnlyEnodesSel: {2}");
            }
            return null;
        }
        private void TryToggleConnect(int pos) {
            var target = EntityUtils.GetTopmostParentTile(pos);
            if (ctx.SelectedTiles.Contains(target)) {
                UISounds.PlayActionDenied();
                return;
            }
            if (target?.ENode == null && target?.Teleporter == null) {
                UISounds.PlayActionDenied();
                return;
            }
            bool toggledConnection = false;
            string failReason = null;
            // one on one connector to tile
            if (ctx.SelectedTiles.Count == 1) {
                var t = EntityUtils.ParentObj(ctx.SelectedTiles[0]);
                if (EntityUtils.ParentObj(t).ENode?.IsConnector == true) {
                    var node1 = t.ENode;
                    var node2 = target.ENode;
                    if (A.Grid.AreConnected(t.Id, target.Id)) {
                        toggledConnection = ToggleConnectionBetween(node1, node2, false, out failReason);
                    } else {
                        if (t.ENode?.CanConnectTo(target.ENode, out failReason) == true) {
                            toggledConnection = ToggleConnectionBetween(node1, node2, true, out failReason);
                        }
                    }
                    if (!toggledConnection) {
                        if (failReason != null) {
                            S.Sig.ShowErrorMessage.Send(failReason);
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    } else {
                        A.Grid.RebuildGrid(false);
                        S.Sig.TilesSelected.Send(ctx.SelectedTiles);
                    }
                    return;
                }
            }
            // If all selected tiles are already linked to this target, disconnect.
            // Otherwise connect the ones that are not. "Connected" is pairwise
            // against the hovered target, so a device already linked to connector A
            // can still be linked to connector B.
            bool wantsConnect = false;
            for (int i = ctx.SelectedTiles.Count - 1; i >= 0; i--) {
                Tile t = ctx.SelectedTiles[i];
                t = EntityUtils.ParentObj(t);
                if (!A.Grid.AreConnected(t.Id, target.Id)) {
                    wantsConnect = true;
                    break;
                }
            }
            bool toggledElectricConnection = false;
            for (int i = ctx.SelectedTiles.Count - 1; i >= 0; i--) {
                Tile t = ctx.SelectedTiles[i];
                t = EntityUtils.ParentObj(t);
                if (A.Grid.AreConnected(t.Id, target.Id) ||
                        t.ENode?.CanConnectTo(target.ENode, out _) == true) {
                    var node1 = t.ENode;
                    var node2 = target.ENode;
                    if (ToggleConnectionBetween(node1, node2, wantsConnect, out failReason)) {
                        toggledConnection = true;
                        toggledElectricConnection = true;
                    }
                } else if (t.Teleporter?.CanTeleport == true
                            && target.Teleporter?.CanTeleport == true) {
                    if (t.Teleporter.Outbound == target.Teleporter) {
                        t.Teleporter.UnsetOutbound();
                        Sounds.PlayEnergyOff(target.Position);
                        toggledConnection = true;
                    } else {
                        t.Teleporter.SetOutbound(target.Teleporter);
                        Sounds.PlayEnergyOn(target.Position);
                        toggledConnection = true;
                    }
                }
            }

            if (!toggledConnection) {
                if (failReason != null) {
                    S.Sig.ShowErrorMessage.Send(failReason);
                } else {
                    UISounds.PlayActionDenied();
                }
            } else {
                if (toggledElectricConnection) {
                    A.Grid.RebuildGrid(false);
                }
                S.Sig.TilesSelected.Send(ctx.SelectedTiles);
            }
        }

        private static bool ToggleConnectionBetween(ElectricNodeComp con,
                ElectricNodeComp node, bool connect, out string failReason) {
            if (!con.IsReachable && !node.IsReachable) {
                failReason = The.Toolbox.ConnectTool.UnreachableInfo();
                return false;
            }
            failReason = null;
            if (con.IsConnectedTo(node)) {
                if (!connect) {
                    // Notifies ElectricNodeConnectionChanged on both sides when it flips
                    if (A.Grid.TryDisconnect(con, node, out failReason)) {
                        Sounds.PlayEnergyOff(node.Node.Position);
                        return true;
                    } else {
                        D.Warn("Could not disconnect {0} and {1}: {2}",
                            con, node, failReason);
                    }
                }
            } else {
                if (connect) {
                    // Notifies ElectricNodeConnectionChanged on both sides when it flips
                    if (A.Grid.TryConnect(con, node, out failReason)) {
                        Sounds.PlayEnergyOn(node.Node.Position);
                        return true;
                    } else {
                        D.Warn("Could not connect {0} and {1}: {2}",
                            con, node, failReason);
                    }
                }
            }
            return false;
        }

        private void MarkConnectable() {
            ctx.CanToggleConnect = true;
        }
    }
}
