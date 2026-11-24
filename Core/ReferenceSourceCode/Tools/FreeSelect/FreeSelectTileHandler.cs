#if !PRODUCTION_BUILD
#define DEBUG_FLOOR_WAVES
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems;
using Game.Systems.AI;
using Game.Systems.Hints;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectTileHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectTile";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectTileHandler());
        }

        public override int Order => 15;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
            S.Sig.SelectEntity.AddListener(OnSelectEntity);
            S.Sig.SelectEntityUnfocused.AddListener(OnSelectEntityUnfocused);
        }

        private void OnSelectEntity(Entity entity) {
            // Clearing alone sends no selection signal, the panel and the bar would keep
            // the old selection
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            ctx.ClearSelectedTiles();
            if (entity is Tile t) {
                SelectTile(t, true);
            }
        }
        public void SelectTile(Tile tile, bool focusCamera) {
            ctx.ClearVFX();
            ctx.SelectTile(tile);
            S.Sig.TilesSelected.Send(ctx.SelectedTiles);
            if (focusCamera) {
                EntityUtils.CameraFocusOn(tile);
            }
        }
        private void OnSelectEntityUnfocused(Entity entity) {
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            ctx.ClearSelectedTiles();
            if (entity is Tile t) {
                SelectTile(t, false);
            }
        }

        public override void OnClickDown(Vector2 pos) {
            ctx.ClickDownTile = EntityUtils.GetTopmostParentTile(ctx.HoveredPos);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            // Alt and shift are obj and being selection modes
            if (ctx.IsAltPressed || ctx.IsShiftPressed) { return; }
            if (ctx.IsDragging) {
                if (!ctx.IsCtrlPressed) {
                    ctx.ClearSelectedTiles();
                }
                if (ctx.SelectedObjs.Count > 0) { return; }
                int layerId = WorldLayer.Objects;
                if (ctx.SelectedTiles.Count > 0) {
                    layerId = EntityUtils.ParentObj(ctx.SelectedTiles[0]).Definition.LayerId;
                }
                var found = S.Components.FindInArea<TileTransformComp>(startPos, pos, layerId: layerId);
                if (found != null) {
                    for (int i = 0; i < found.Count; i++) {
                        if (ctx.IsSelectionFull()) { break; }
                        TileTransformComp tt = found[i];
                        if (tt.IsChild) { continue; }
                        if (!tt.Entity.IsActive) { continue; }
                        ctx.SelectTile(tt.Tile);
                    }
                }
            } else {
                if (ctx.SelectedBeings.Count > 0) { return; }
                if (ctx.SelectedObjs.Count > 0) { return; }
                if (ctx.TargetBeing == null) {
                    // re-query hovered tile afterwards
                    ctx.LastHoverPos = Pos.Invalid;
                    // if not, we are trying to give commands to some being
                    QueryTiles(ctx, pos);
                }
            }
        }

        public override void OnHover(Vector2 pos) {
            if (ctx.HoveredPosChanged) {
                // A roof hides the tiles under it, and with them the hover outline
                var t = ctx.IsUnderRoof ? null : EntityUtils.GetTopmostParentTile(ctx.HoveredPos);
                SetHoverTile(t);
                // D.Warn("HoveredTile: {0}", ctx.HoveredTile);
                if (t?.IsConstructed == true && t.Definition.LayerId == WorldLayer.Floor) {
                    ctx.IsHoveringDirtyFloor = S.Sys.Dirt.IsDirty(t.PosIdx);
                } else {
                    ctx.IsHoveringDirtyFloor = false;
                }
            }
            if (ctx.HoveredTile?.IsConstructed == false) {
                var sb = ctx.BeginTooltipBlock();
                var mats = ctx.HoveredTile.Constructable.RawMaterials;

                #if DEBUG_FLOOR_WAVES
                var con = ctx.HoveredTile.Constructable;
                if (con.IsFloorWaveSeed) {
                    sb.AppendFormat("Floor wave seed\n");
                }
                if (con.FloorWaveId > 0) {
                    sb.AppendFormat("Floor wave id: {0}\n",
                        con.FloorWaveId);
                }
                if (con.Entity?.Goal?.AvailabilityGate != null) {
                    sb.AppendFormat("Available to build now: {0}\n",
                        con.Entity.Goal.IsAvailabilityGateOpen(S));
                }
                #endif

                if (!Arrays.IsEmpty(mats)) {
                    sb.Append(Texts.SubTitle(T.ContainsMaterials));
                    foreach (var mat in mats) {
                        string matAmt = Units.XOutOfY(mat.StackSize, mat.MaxStackSize);
                        if (mat.StackSize == mat.MaxStackSize) {
                            matAmt = Texts.Green(matAmt);
                        } else {
                            matAmt = Texts.Red(matAmt);
                        }
                        sb.AppendFormat("{0}: {1} \n", mat.Type.Def.CodexLink, matAmt);
                    }
                }
            }
            var health = ctx.HoveredTile?.Health;
            if (health?.IsDamaged == true) {
                var sb = ctx.BeginTooltipBlock();
                sb.AppendLine(Texts.Red(T.Damaged));
                // sb.Append(' ');
                // sb.Append(GlyphMap.ArrowRight);
                // sb.Append(' ');
                sb.Append(T.HealthBarInfo_V2.F((int) health.Health, (int) health.MaxHealth));
            }
            if (ctx.IsHoveringDirtyFloor) {
                var sb = ctx.BeginTooltipBlock();
                var dirtData = S.Sys.Dirt.At(ctx.HoveredPos);
                var count = Arrays.Count(dirtData);
                sb.AppendFormat("{0}: {1}", T.Dirt, count);
            }
        }

        private void SetHoverTile(Tile t) {
            ctx.HoveredTile = t;
            ctx.IsHoveringSelectableTile = IsSelectableTile(ctx, t);
        }

        private static bool IsSelectableTile(FreeSelectContext ctx, Tile t) {
            return t != null
                && (t.ENode != null
                    || t.Definition.LayerId == WorldLayer.Objects
                    || t.Goal != null
                    || t.Health.IsDamaged
                    || t.HasComponent<EmblemComp>()
                    || ctx.SelectedTiles.Contains(t));
        }

        internal static bool CanCycleToSelectableFloor(FreeSelectContext ctx, int pos) {
            if (ctx.SelectedTiles.Count != 1) {
                return false;
            }
            var selected = ctx.SelectedTiles[0];
            if (selected.PosIdx != pos) {
                return false;
            }
            var selectedRoot = EntityUtils.ParentObj(selected);
            if (selectedRoot.Definition.LayerId == WorldLayer.Floor) {
                return false;
            }
            var floor = EntityUtils.FloorAt(pos);
            return IsSelectableTile(ctx, floor);
        }

        /// <summary>
        /// Expected behavior:
        /// If CTRL is not pressed:
        /// 1. clicking on the floor does nothing
        /// 2. clicking a tile above the floor selects that tile
        /// 3. clicking the same position again selects the floor if it is selectable
        /// 4. clicking the same position again selects the tile above the floor
        /// If CTRL is pressed
        /// 1. clicking the tile twice will select the tile
        /// 2. clicking a selected tile again will
        /// 2.1 if only 1 tile is selected: select the floor underneath
        /// 2.2 if 2+ tiles are selected: deselect the clicked tile
        /// 3. clicking another tile when one is selected will add it to selection
        /// </summary>
        /// <param name="coords"></param>
        /// <returns></returns>
        public static bool QueryTiles(FreeSelectContext ctx, Vector2 coords) {
            var pos = EntityUtils.ToPos(coords);
            var S = ctx.S;
            if (!S.Grid.IsWithinBounds(pos)) { return false; }
            Tile selectedOnly = null;
            Tile selectedOnlyRoot = null;
            if (ctx.SelectedTiles.Count == 1)  {
                selectedOnly = ctx.SelectedTiles[0];
                if (selectedOnly.Transform.IsChild) {
                    selectedOnlyRoot = selectedOnly.Transform.Parent;
                } else {
                    selectedOnlyRoot = selectedOnly;
                }
            }
            var topmost = EntityUtils.GetTopmostTile(pos);
            var topmostRoot = EntityUtils.ParentObj(topmost);
            if (ctx.IsCtrlPressed && ctx.SelectedTiles.Count > 0
                    && !ctx.SelectedTiles.Contains(topmost)
                    && ctx.SelectedTiles[0].Definition?.LayerId == WorldLayer.Floor) {
                // for ctrl selecting floors under objects
                topmost = EntityUtils.FloorAt(pos);
                topmostRoot = topmost;
            }
            Tile selectedNow = null;
            if (topmostRoot?.IsActive == true) {
                if (topmostRoot.Definition.LayerId == WorldLayer.Floor) {
                    if (IsSelectableTile(ctx, topmostRoot)) {
                        selectedNow = topmost;
                    } else {
                        // boring floor
                        if (ctx.IsCtrlPressed || !topmost.IsConstructed) {
                            // only select a boring floor if ctrl is pressed and nothing else was selected before
                            selectedNow = topmost;
                        }
                    }
                } else {
                    selectedNow = topmost;
                }
            }

            // clicking the same tile more than once
            if (ctx.SelectedTiles.Count == 1 && selectedOnly.PosIdx == pos) {
                if (selectedOnlyRoot.Definition.LayerId == WorldLayer.Floor) {
                    var obj = EntityUtils.GetTopmostTile(pos);
                    if (obj?.IsActive == true) {
                        var parObj = EntityUtils.ParentObj(obj);
                        if (parObj.Definition.LayerId > WorldLayer.Floor) {
                            ctx.ClearSelectedTiles();
                            selectedNow = obj;
                        }
                    }
                } else {
                    var floor = EntityUtils.FloorAt(pos);
                    if (floor != null && (ctx.IsCtrlPressed || IsSelectableTile(ctx, floor))) {
                        ctx.ClearSelectedTiles();
                        selectedNow = floor;
                    }
                }
            }

            if (ctx.IsCtrlPressed) {
            } else {
                ctx.ClearSelectedTiles();
            }

            if (selectedNow != null) {
                bool doSelect = true;
                if (ctx.IsCtrlPressed) {
                    if (ctx.SelectedTiles.Count > 0) {
                        if (!EntityUtils.IsSameLayer(selectedNow, ctx.SelectedTiles[0])) {
                            doSelect = false;
                        } else {
                            // make sure we will be selecting the parent
                            selectedNow = EntityUtils.ParentObj(selectedNow);
                        }
                    }
                    if (doSelect && ctx.SelectedTiles.Count > 1) {
                        // perhaps we are clicking the same tile and we want to deselect it?
                        var selectedNowParent = EntityUtils.ParentObj(selectedNow);
                        if (ctx.SelectedTiles.Contains(selectedNowParent)) {
                            doSelect = false;
                            ctx.DeselectTile(selectedNowParent);
                        }
                    }
                }
                if (doSelect) {
                    if (ctx.IsCtrlPressed && ctx.SelectedTiles.Count == 1) {
                        // We're about to select an additional tile, let's make sure
                        // that the first tile is root if it's multi
                        if (selectedNow != ctx.SelectedTiles[0]) {
                            if (selectedNow.Transform.Parent == ctx.SelectedTiles[0]) {
                                // we will want to reselect
                                ctx.ClearSelectedTiles();
                            } else {
                                ctx.SelectedTiles[0] = EntityUtils.ParentObj(ctx.SelectedTiles[0]);
                            }
                        }
                    }
                    ctx.SelectTile(selectedNow);
                }
            }
            ctx.UpdateSelectedIndices(pos);
            return selectedNow != null;
        }

        public override void OnDoubleClick(Vector2 pos) {
            if (ctx.DoubleClicked || ctx.IsCtrlPressed || ctx.IsAltPressed || ctx.IsShiftPressed) {
                return;
            }
            if (!ctx.IsHoveringSelectableTile) {
                if (ctx.HoveredTile == null) {
                    return;
                }
                if (ctx.HoveredTile.Definition.LayerId == WorldLayer.Floor) {
                    if (ctx.HoveredTile.IsConstructed) {
                        // double-click selects a single boring floor tile without mass-selecting
                        ctx.ClearSelectedTiles();
                        ctx.SelectTile(ctx.HoveredTile);
                        S.Sig.TilesSelected.Send(ctx.SelectedTiles);
                        ctx.DoubleClicked = true;
                    }
                    return;
                }
            }
            if (ctx.SelectedCount != 1) { return; }

            if (!ctx.IsExclusivelySelected(ctx.HoveredTile)) { return; }

            if (ctx.HoveredTile.Definition.LayerId == WorldLayer.Walls
                    && ctx.HoveredTile.ENode == null) {
                var floor = EntityUtils.FloorAt(ctx.HoveredTile.PosIdx);
                if (floor != null) {
                    ctx.ClearSelectedTiles();
                    ctx.SelectTile(floor);
                    ctx.UpdateSelectedIndices(floor.PosIdx);
                    S.Sig.TilesSelected.Send(ctx.SelectedTiles);
                }
                ctx.DoubleClicked = true;
                return;
            }

            var def = ctx.HoveredTile.Definition;
            ctx.ClearSelectedTiles();
            // don't mass-select boring floors
            if (ctx.HoveredTile.Definition.LayerId == WorldLayer.Floor && ctx.HoveredTile.ENode == null) {
                if (ctx.HoveredTile.IsConstructed) {
                    D.Warn("Preventing double click on a floor: {0}", ctx.HoveredTile);
                    return;
                }
            }
            bool isConstructed = ctx.HoveredTile.IsConstructed;
            foreach (var e in S.Grids[def.LayerId].Data) {
                if (ctx.IsSelectionFull()) { break; }
                if (e?.IsActive == true && e.Definition == def) {
                    if (e.IsConstructed == isConstructed && RenderUtils.IsVisible(e.Position)) {
                        // D.Warn("Selecting tile on double click: {0}", e);
                        ctx.SelectTile(e);
                    }
                }
            }
            S.Sig.TilesSelected.Send(ctx.SelectedTiles);
            ctx.DoubleClicked = true;
        }

        public override bool AddRightClickContext(QuickChoice choice) {
            if (!ctx.IsHoveringDirtyFloor) { return false; }
            var hovTile = ctx.HoveredTile;
            if (hovTile?.IsConstructed != true) {
                return false;
            }
            choice.AddButton(T.OrderCleaningNow, () => {
                if (hovTile?.IsConstructed != true) {
                    UISounds.PlayActionDenied();

                    return;
                }
                if (hovTile.Goal != null) {
                    UISounds.PlayActionDenied();

                    return;
                }
                if (!hovTile.Constructable.CreateCleanGoal()) {
                    UISounds.PlayActionDenied();
                }

            });
            return true;
        }
    }
}
