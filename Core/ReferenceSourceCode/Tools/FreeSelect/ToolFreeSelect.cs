#if !PRODUCTION_BUILD
// #define DEBUG_SELECT
// #define DEBUG_SELECT_HANDLERS
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Tools {
    public sealed class ToolFreeSelect : Tool, IsDoubleClickableTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolFreeSelect());
        }
        public const string ToolId = "free_select";
        public override string Id => ToolId;
        public override string MenuIcon => IconId.WCursor;
        public override bool IsConfigurable => false;
        public override bool SkipMenu => true;
        public override bool IsClickedDown => ctx.ReadyForClickUp;
        public override int HoverPos => ctx.HoveredPos;
        private int numHandlers;
        private FreeSelectContext ctx;
        private FreeSelectHandler[] handlers;
        protected override void Initialize() {
            handlers = Registries.FreeSelectHandlers.All.Values
                .OrderBy(h => h.Order).ToArray();
            ctx = new FreeSelectContext(S, handlers);
            numHandlers = handlers.Length;
            for (int i = 0; i < numHandlers; i++) {
                FreeSelectHandler h = handlers[i];
                h.Initialize(ctx);
            }
            ctx.Clear();
            // S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            // S.Sig.SelectEntity.AddListener(OnSelectEntity);
        }


        public override void UpdateVFX(ParticlesSys ps) {
            ctx.UpdateVFX(ps);
        }

        public override void AddParticles(Dictionary<int, XParticles> x) {
            AddDragRectParticles(x);
        }

        public override void OnClickDown(Vector2 pos) {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: OnClickDown({0})", pos);
            #endif
            ctx.BeginClickDown(pos);
            for (int i = 0; i < numHandlers; i++) {
                if (IsHiddenByRoof(handlers[i])) { continue; }
                #if DEBUG_SELECT_HANDLERS
                D.Warn("FreeSelect: {0}.OnClickDown({1})",
                    handlers[i].Id, pos);
                #endif
                handlers[i].OnClickDown(pos);
            }
            ctx.EndClickDown();
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: OnClickUp({0}->{1})", startPos, pos);
            #endif
            if (!ctx.ReadyForClickUp) {
                return;
            }
            ctx.ReadyForClickUp = false;
            ctx.ClickUpConsumed = false;
            // Drags still box select through the roofs
            bool isRoofClick = ctx.IsUnderRoof && !ctx.IsDragging;
            for (int i = 0; i < numHandlers; i++) {
                if (isRoofClick && !handlers[i].WorksOverRoofs) { continue; }
                #if DEBUG_SELECT_HANDLERS
                D.Warn("FreeSelect: {0}.OnClickUp({1}->{2},{3})",
                    handlers[i].Id, startPos, pos, dragDistance);
                #endif
                handlers[i].OnClickUp(startPos, pos, dragDistance);
                if (ctx.ClickUpConsumed) { break; }
            }
            if (isRoofClick && !ctx.ClickUpConsumed) {
                ClickOnRoof();
            }
            ctx.ClickUpConsumed = false;
            // have to do this for the Tool class
            base.isDragging = false;
            if (ctx.WasDragging) {
                ctx.EndDrag();
            } else {
                ctx.EndClickUp();
            }
        }

        public override void OnHoverUI() {
            ctx.HideHover();
        }

        public override void OnHover(Vector2 pos) {
            // using (D.Timer("Hover", 0.0001f)) {
                ctx.BeginOnHover(pos);
                for (int i = 0; i < numHandlers; i++) {
                    var h = handlers[i];
                    if (IsHiddenByRoof(h)) {
                        // What the roof hides gets no tooltip. The handler still runs: it
                        // keeps state the right click orders need (the target being, the
                        // attack target), which would go stale if it were skipped
                        var tooltipLength = ctx.Tooltip.Length;
                        h.OnHover(pos);
                        ctx.Tooltip.Length = tooltipLength;
                    } else {
                        h.OnHover(pos);
                    }
                }
                ctx.EndOnHover();
            // }
        }

        private bool IsHiddenByRoof(FreeSelectHandler h) {
            return ctx.IsUnderRoof && !h.WorksOverRoofs;
        }

        // A click on a roof that no marker took is a click on nothing: it clears the selection,
        // unless a modifier asks to add to it
        private void ClickOnRoof() {
            if (ctx.IsCtrlPressed || ctx.IsShiftPressed || ctx.IsAltPressed) { return; }
            ctx.ClearSelectedBeings();
            ctx.ClearSelectedObjs();
            ctx.ClearSelectedTiles();
        }

        public bool OnDoubleClick(Vector2 pos) {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: DblClick({0})", pos);
            #endif
            ctx.DoubleClicked = false;
            for (int i = 0; i < numHandlers; i++) {
                if (IsHiddenByRoof(handlers[i])) { continue; }
                #if DEBUG_SELECT_HANDLERS
                D.Warn("FreeSelect: {0}.OnDoubleClick({1})",
                    handlers[i].Id, pos);
                #endif
                handlers[i].OnDoubleClick(pos);
            }
            return ctx.DoubleClicked;
        }

        public override void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (!ctx.ReadyForClickUp) { return; }
            if (!ctx.WasDragging) {
                if (ctx.PreventDragging) {
                    return;
                }
                ctx.HasStartedDragging = true;
            } else if (ctx.DragEnd == pos) {
                // still same pos
                return;
            }
            #if DEBUG_SELECT
            D.Warn("FreeSelect: OnDrag({0}->{1},{2})", startPos, pos, dragDistance);
            #endif
            ctx.IsDragging = true;
            isDragging = true;
            ctx.DragStart = startPos;
            ctx.DragEnd = pos;
            for (int i = 0; i < numHandlers; i++) {
                handlers[i].OnDrag(startPos, pos, dragDistance);
            }
            ctx.WasDragging = true;
        }

        public bool ClickCancelReleased() {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: ClickCancelReleased");
            #endif
            if (ctx.OnClickCancelReleased()) {
                return true;
            }
            // if (!ctx.IsHoveringSelectableEntity) {
            //     return false;
            // }
            // if (!FreeSelectUtils.IsHoveringCloseEnough
            //         || !FreeSelectUtils.TryShowHoveredEntityContextMenuOnRMB) {
            //     return false;
            // }
            // // A popup might have opened while we were hovering something
            // if (The.Bindings.Mouse.IsOverUI) {
            //     return false;
            // }

            // // Handle one or more selected tiles if we are hovering any
            // if (ctx.IsHoveringSelectableTile
            //         && ctx.SelectedObjs.Count == 0
            //         && ctx.SelectedBeings.Count == 0) {
            //     if (ctx.SelectedTiles.Count > 1) {
            //         if (ctx.SelectedTiles.Contains(ctx.HoveredTile)) {
            //             S.Sig.ShowMultiTileContextMenu.Send(ctx.SelectedTiles);
            //             return true;
            //         }
            //     } else {
            //         if (ctx.SelectedTiles.Count == 0) {
            //             S.Sig.ToggleContextMenu.Send(ctx.HoveredTile);
            //             return true;
            //         } else {
            //             var selTile = EntityUtils.ParentObj(ctx.SelectedTiles[0]);
            //             if (ctx.HoveredTile == selTile) {
            //                 S.Sig.ToggleContextMenu.Send(ctx.HoveredTile);
            //                 return true;
            //             }
            //         }
            //     }
            // }
            // if (ctx.HoveredBeing != null
            //         && ctx.SelectedObjs.Count == 0
            //         && ctx.SelectedTiles.Count == 0) {
            //     if (ctx.SelectedBeings.Count > 1) {
            //         if (ctx.SelectedBeings.Contains(ctx.HoveredBeing)) {
            //             S.Sig.ShowMultiBeingContextMenu.Send(ctx.SelectedBeings);
            //             return true;
            //         }
            //     } else if (ctx.SelectedBeings.Count == 1) {
            //         if (ctx.SelectedBeings.Contains(ctx.HoveredBeing)) {
            //             S.Sig.ToggleContextMenu.Send(ctx.HoveredBeing);
            //             return true;
            //         }
            //     } else {
            //         S.Sig.ToggleContextMenu.Send(ctx.HoveredBeing);
            //         return true;
            //     }
            // }
            // if (ctx.HoveredObj != null
            //         && ctx.SelectedBeings.Count == 0
            //         && ctx.SelectedTiles.Count == 0) {
            //     if (ctx.SelectedObjs.Count > 1) {
            //         if (ctx.SelectedObjs.Contains(ctx.HoveredObj)) {
            //             S.Sig.ShowMultiObjContextMenu.Send(ctx.SelectedObjs);
            //             return true;
            //         }
            //     } else if (ctx.SelectedObjs.Count == 1) {
            //         if (ctx.SelectedObjs.Contains(ctx.HoveredObj)) {
            //             S.Sig.ToggleContextMenu.Send(ctx.HoveredObj);
            //             return true;
            //         }
            //     } else {
            //         S.Sig.ToggleContextMenu.Send(ctx.HoveredObj);
            //         return true;
            //     }
            // }

            return false;
        }

        public override bool Cancel() {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: Cancel");
            #endif
            return ctx.Clear();
        }

        public override bool CancelDrag() {
            if (dragParticles != null) {
                dragParticles.ShowParticles(0, false);
            }
            if (ctx.IsDragging) {
                ctx.Clear();
                return true;
            }
            return false;
        }

        public override void Activate() {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: Activate");
            #endif
            for (int i = 0; i < numHandlers; i++) {
                handlers[i].Activate();
            }
        }

        public override void Deactivate() {
            #if DEBUG_SELECT
            D.Warn("FreeSelect: Deactivate");
            #endif
            ctx.Clear();
        }
        private XParticles dragParticles;
        private void AddDragRectParticles(Dictionary<int, XParticles> x) {
            if (!ctx.IsDragging) { return; }
            if (x.TryGetValue(ParticlesIdH.MouseRect, out var mr)) {
                var count = LoadDragRectParticles(mr.Particles);
                mr.ShowParticles(count, false);
                dragParticles = mr;
            }
        }

        public override int LoadDragRectParticles(ParticleSystem.Particle[] particles) {
            if (!ctx.IsDragging) { return 0; }
            var mid = (ctx.DragStart + ctx.DragEnd) / 2f;
            particles[0] = ParticlesSys.ParticleAt(mid, Consts.FreeSelectRectColor,
                Vectors.Abs(ctx.DragEnd - ctx.DragStart), 0.1f);
            return 1;
        }
        public override string ToString() {
            return T.ToolFree_Select;
        }

    }
}
