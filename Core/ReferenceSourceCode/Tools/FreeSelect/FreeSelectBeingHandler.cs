#if !PRODUCTION_BUILD
// #define DEBUG_REGIONS
// #define DEBUG_SELECT_BEING
// #define PROFILE_REG_JOB
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Systems.Reg;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectBeingHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectBeing";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectBeingHandler());
        }

        private readonly HashSet<Being> cycledBeings = new();

        public override int Order => 5;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
            S.Sig.SelectEntity.AddListener(OnSelectEntity);
            S.Sig.SelectEntityUnfocused.AddListener(OnSelectEntityUnfocused);
            S.Query.SelectBeings.SetResponder(SelectBeings);
            S.Query.DeselectBeings.SetResponder(DeselectBeings);
            S.Query.GetSelectedBeings.SetResponder(GetSelectedBeings);
            S.Query.GetSelectedBeing.SetResponder(GetSelectedBeing);
        }

        private Being GetSelectedBeing(bool onlyOneMustBeSelected) {
            var cnt = ctx.SelectedBeings.Count;
            if (cnt == 0) { return null; }
            if (onlyOneMustBeSelected && cnt != 1) {
                return null;
            }
            return ctx.SelectedBeings[0];
        }

        private List<Being> GetSelectedBeings() {
            return ctx.SelectedBeings;
        }

        private int DeselectBeings(List<Being> beings) {
            int deselected = 0;
            foreach (var b in beings) {
                if (ctx.SelectedBeings.Contains(b)) {
                    ctx.DeselectBeing(b);
                    deselected++;
                }
            }
            S.Sig.BeingsSelected.Send(ctx.SelectedBeings);
            return deselected;
        }
        private int SelectBeings(List<Being> beings) {
            ctx.ClearSelectedTiles();
            ctx.ClearSelectedObjs();
            if (!The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                ctx.ClearSelectedBeings();
            }
            int selected = 0;
            if (beings != null) {
                foreach (var b in beings) {
                    if (b.IsActive) {
                        SelectBeing(b);
                        selected++;
                    }
                }
            }
            S.Sig.BeingsSelected.Send(ctx.SelectedBeings);
            return selected;
        }


        private void SelectBeing(Being b) {
            ctx.SelectBeing(b);
        }

        private void OnSelectEntity(Entity entity) {
            // Clearing alone sends no selection signal, the panel and the bar would keep
            // the old selection
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            // cannot use ctx.IsShiftPressed because it won't update when
            // mouse is over UI when shift was pressed
            // if (!ctx.IsShiftPressed) {
            bool isShiftPressed = The.Bindings.IsPressed(ActionType.ShiftModifier);
            if (!isShiftPressed) {
                ctx.ClearSelectedBeings();
            }
            if (entity is Being b) {
                SelectBeing(b, true, isShiftPressed);
            }
        }
        private void OnSelectEntityUnfocused(Entity entity) {
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            // cannot use ctx.IsShiftPressed because it won't update when
            // mouse is over UI when shift was pressed
            // if (!ctx.IsShiftPressed) {
            bool isShiftPressed = The.Bindings.IsPressed(ActionType.ShiftModifier);
            if (!isShiftPressed) {
                ctx.ClearSelectedBeings();
            }
            if (entity is Being b) {
                SelectBeing(b, false, isShiftPressed);
            }
        }

        private void SelectBeing(Being b, bool focusCamera, bool isShiftPressed) {
            ctx.ClearVFX();
            if (isShiftPressed && ctx.SelectedBeings.Contains(b)) {
                ctx.DeselectBeing(b);
            } else {
                SelectBeing(b);
                if (focusCamera) {
                    EntityUtils.CameraFocusOn(b);
                }
            }
            S.Sig.BeingsSelected.Send(ctx.SelectedBeings);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            // Ctrl and alt are tile and obj selection modes
            if (ctx.IsCtrlPressed || ctx.IsAltPressed) { return; }
            if (ctx.IsShiftPressed) {
                // if shift is pressed, we may be trying to connect something to something
                if (!ctx.IsDragging && ctx.SelectedTiles.Count > 0
                        && (ctx.HoveredBeing == null || ctx.HoveredTile?.ENode != null)) {
                    return;
                }
                ctx.ClearSelectedTiles();
                ctx.ClearSelectedObjs();
            }
            if (ctx.IsDragging) {
                if (!ctx.IsShiftPressed) {
                    // if shift is pressed we will be adding to the selection
                    ctx.ClearSelectedBeings();
                }
                Caches.BeingList.Clear();
                var found = S.Beings.FindVisualInRect(startPos, pos, Caches.BeingList, null);
                Caches.EntityProxyList.Clear();
                var numProxies = S.Query.GetEntityProxiesInRect.Ask(startPos, pos, Caches.EntityProxyList);
                if (numProxies > 0) {
                    found += numProxies;
                    foreach (var pr in Caches.EntityProxyList) {
                        if (pr.Entity is Being b) {
                            Caches.BeingList.Add(b);
                        }
                    }
                }
                if (found > 0) {
                    bool skipAnimals = false;
                    bool skipDead = false;
                    bool skipEnemies = false;
                    bool skipIncapacitated = false;
                    for (int i = Caches.BeingList.Count - 1; i >= 0; i--) {
                        Being b = Caches.BeingList[i];
                        if (b.Data.IsPlayer) {
                            skipEnemies = true;
                        }
                        if (!b.Data.Species.IsAnimal) {
                            skipAnimals = true;
                        }
                        if (!b.IsDead) {
                            skipDead = true;
                            if (!b.Data.Body.IsIncapacitated) {
                                skipIncapacitated = true;
                            }
                        }
                    }
                    for (int i = Caches.BeingList.Count - 1; i >= 0; i--) {
                        if (ctx.IsSelectionFull()) { break; }
                        Being b = Caches.BeingList[i];
                        if (skipEnemies && !b.Data.IsPlayer) {
                            continue;
                        }
                        // a chipped animal takes orders, so it joins the crew's selection
                        if (skipAnimals && b.Data.Species.IsAnimal
                                && !b.AIAgent.DirectControl.IsControllable) {
                            continue;
                        }
                        if (skipDead && b.IsDead) {
                            continue;
                        }
                        if (skipIncapacitated && b.Data.Body.IsIncapacitated) {
                            continue;
                        }

                        SelectBeing(b);
                    }
                }
            } else {
                if (!ctx.IsShiftPressed
                        && (ShouldContinueTileCycle(pos) || ShouldContinueObjCycle(pos))) {
                    return;
                }
                Being prevSelected = ctx.SelectedBeings.Count == 1 ? ctx.SelectedBeings[0] : null;
                // Every being here gets one turn before the click moves on to an item or tile.
                // Excluding only the previous one made two beings alternate forever, so an
                // item under them could never be selected (STA-1550)
                if (prevSelected == null || ctx.ClicksInCurrentPosIdx == 0) {
                    cycledBeings.Clear();
                }
                if (prevSelected != null) {
                    cycledBeings.Add(prevSelected);
                }
                var being = S.Beings.FindClosestVisual(pos, cycledBeings, boundsSize: 2);
                if (being == null && prevSelected != null) {
                    cycledBeings.Clear();
                    // if there's an object there, let's select it in the obj handler
                    if (S.Objs.FindClosestVisual(pos, boundsSize: 2) == null) {
                        if (FreeSelectTileHandler.QueryTiles(ctx, pos)) {
                            ctx.ClearSelectedBeings();
                            ResolveTargetBeing();
                            ctx.ClickUpConsumed = true;
                            return;
                        }
                        // still want to try again
                        being = S.Beings.FindClosestVisual(pos, boundsSize: 2);
                    } else {
                        return;
                    }
                }
                // Toggle same being
                if (being == null && !ctx.IsShiftPressed && ctx.SelectedBeings.Count > 0) {
                    ctx.ClearSelectedBeings();
                    // without this we won't select a tile if we're clicking on one
                    ResolveTargetBeing();
                }
                if (TrySelectBeing(being)) {
                    ctx.HoveredBeing = being;
                }
            }
        }

        private bool ShouldContinueTileCycle(Vector2 pos) {
            if (ctx.ClicksInCurrentPosIdx == 0 || ctx.SelectedTiles.Count != 1) {
                return false;
            }
            int posIdx = EntityUtils.ToPos(pos);
            if (!S.Grid.IsWithinBounds(posIdx)) {
                return false;
            }
            var selected = ctx.SelectedTiles[0];
            if (selected.PosIdx != posIdx) {
                return false;
            }
            // The floor is the bottom of the stack: the next click starts over at the top,
            // with a being standing there
            if (EntityUtils.ParentObj(selected).Definition.LayerId == WorldLayer.Floor) {
                return false;
            }
            if (S.Objs.FindClosestVisual(pos, boundsSize: 2) != null) {
                return true;
            }
            return FreeSelectTileHandler.CanCycleToSelectableFloor(ctx, posIdx);
        }

        // An item picked from a stack at this position hands the next click on to the tile
        // below it, not back to a being standing there, or a selectable floor under a being
        // and an item could never be reached (STA-1621)
        private bool ShouldContinueObjCycle(Vector2 pos) {
            if (ctx.ClicksInCurrentPosIdx == 0 || ctx.SelectedObjs.Count != 1
                    || ctx.SelectedBeings.Count > 0 || !ctx.IsHoveringSelectableTile) {
                return false;
            }
            // Only an item a click here picks (the box FindClosestVisual uses with boundsSize
            // 2), not one selected elsewhere, for example from a list
            var d = ctx.SelectedObjs[0].Position - pos;
            return Mathf.Abs(d.x) <= 1f && Mathf.Abs(d.y) <= 1f;
        }

        private bool TrySelectBeing(Being being) {
            if (being == null) { return false; }
            ctx.ClearSelectedTiles();
            ctx.ClearSelectedObjs();
            // ToolConnect.ClearShownTexts();
            if (ctx.IsShiftPressed) {
                if (ctx.SelectedBeings.Contains(being)) {
                    ctx.DeselectBeing(being);
                } else {
                    SelectBeing(being);
                }
            } else {
                // add if alt is not pressed
                ctx.ClearSelectedBeings();
                #if DEBUG_SELECT_BEING
                D.Warn("selecting being: {0}", being);
                #endif
                SelectBeing(being);
            }
            return true;
        }

        public override void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (ctx.SelectedBeings.Count == 0) { return; }
            if (ctx.IsCtrlPressed) {
                ctx.ClearSelectedBeings();
                ctx.PathPreview = null;
            }
        }

        public override void OnHover(Vector2 pos) {
            ResolveTargetBeing();
            ctx.HoveredBeing = S.Beings.FindClosestVisual(pos);
            ctx.TargetBeingCanGoToHoverPos = false;
            if (ctx.SelectedBeings.Count == 0) { return; }
            var targetOrSelected = ctx.TargetBeing ?? ctx.SelectedBeings[0];
            if (targetOrSelected?.IsDead == true) { return; }
            if (ctx.IsCtrlPressed && ctx.SelectedBeings.Count == 1
                    && ctx.SelectedBeings[0].Data.IsFriendly) {
                AddBeingTooltip(ctx.HoveredPos);
            } else {
                ctx.PathPreview = null;
            }
        }


        private void AddBeingTooltip(int pos) {
            if (ctx.TargetBeing == null) {
                return;
            }
            if (Pos.IsOOB(pos)) {
                return;
            }

            if (The.Bindings.IsJustPressedOrReleased(ActionType.CtrlModifier)) {
                ctx.BeingHintPos = Pos.Invalid;
            }
            if (ctx.BeingHintPos != pos || ctx.SelectedBeingLastPos != ctx.TargetBeing.PosIdx) {
                ShowPathPreviewTo(pos);
                ctx.BeingHintPos = pos;
                ctx.SelectedBeingLastPos = ctx.TargetBeing.PosIdx;
            }
            DoShowSelectedBeingHints(pos);
        }

        private void ResolveTargetBeing() {
            ctx.TargetBeing = null;
            ctx.TargetBeingIsControllable = false;
            ctx.TargetBeingIsUnderDirectControl = false;
            if (ctx.SelectedBeings.Count <= 0) { return; }
            foreach (var b in ctx.SelectedBeings) {
                if (!b.Data.Body.IsIncapacitated
                        && b.AIAgent.DirectControl.IsControllable) {
                    ctx.TargetBeing = b;
                    ctx.TargetBeingIsControllable = true;
                    ctx.TargetBeingIsUnderDirectControl = b.AIAgent.DirectControl.IsUnderDirectControl;
                    break;
                }
            }
            ctx.TargetBeing ??= ctx.SelectedBeings[0];
            if (ctx.TargetBeing?.IsDead == true) {
                ctx.TargetBeing = null;
            }
        }

        private void DoShowSelectedBeingHints(int pos) {
            var b = ctx.TargetBeing;
            var sb = ctx.BeginTooltipBlock();
            var name = b.Data.Mind.Name.DisplayName;
            // we no longer show tooltips for multi-selected beings
            // if (ctx.SelectedBeings.Count > 1) {
            //     name = $"{name} {Texts.Green($" + {ctx.SelectedBeings.Count - 1}")}";
            // }
            sb.Append(Texts.SubTitle(name));
            var ps = Texts.Accented2(Pos.String(pos));
            ctx.TargetBeingCanGoToHoverPos = false;
            #if DEBUG_REGIONS
            var dist = S.Query.RegionDist.Ask(
                RegPathReq.Near(b.PosIdx, pos, b.Motion.IsGrounded));
            if (dist < 0) {
                sb.AppendFormat("No Near Path via regions!\n");
            } else {
                sb.AppendFormat("Region Dist (near): {0}\n", dist);
            }
            #endif
            bool needsNewLine = false;
            var species = b.Species.Name;
            if (ctx.PathPreview != null) {
                // the path exists, but an immobilized being can't walk it
                if (ctx.PathPreview.Estimate.IsValid && ctx.PathPreview.Estimate.IsSafe
                        && !b.Motion.IsImmobilized) {
                    sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.BeingCanReach_V2.F(species, ps));
                    ctx.TargetBeingCanGoToHoverPos = true;
                } else {
                    sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight,
                        Texts.WithColor(T.BeingCannotReach_V2.F(species, ps),
                            Texts.TMPColorRed));
                }
                needsNewLine = true;
            }
            if (needsNewLine) {
                sb.AppendLine();
            }
            if (b.Data.Safety.IsActuallySafeAt(pos, out var threatT)) {
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.BeingThinksSafe_V2.F(species, ps));
            } else {
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.BeingThinksUnsafe_V2.F(species, Texts.Red(ps)));
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, Texts.Red(threatT));
            }
            if (!b.AIAgent.DirectControl.IsUnderDirectControl) {
                if (!b.AIAgent.DirectControl.CheckCanToggleDirectControl(out var tip)) {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, tip);
                }
            }
        }

        private void ShowPathPreviewTo(int pos) {
            var b = ctx.TargetBeing;
            if (b.Motion.Progress.IsTeleporting) {
                return;
            }
            int goalW = 0;
            int goalH = 0;
            if (ctx.HoveredTile?.Transform.IsMulti == true) {
                var tr = ctx.HoveredTile.Transform;
                goalW = tr.RotatedWidth;
                goalH = tr.RotatedHeight;
                pos = tr.PosIdx;
            }
            #if PROFILE_REG_JOB
            var regDist = S.Query.RegionDist.Ask(RegPathReq.To(b.Motion.Position.Idx, pos, b.Motion.IsGrounded));
            D.Warn("Profiled reg distance: {0}", regDist);
            #endif
            ctx.PathPreview = b.Motion;
            PathReqFlags flags = PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby
                | PathReqFlags.Estimate;
            CreatePathPreview(pos, goalW, goalH, flags);
            if (!ctx.PathPreview.Estimate.IsValid) {
                flags |= PathReqFlags.UnsafeOK;
                CreatePathPreview(pos, goalW, goalH, flags);
            }
        }

        private void CreatePathPreview(int pos, int goalW, int goalH, PathReqFlags flags) {
            var req = ctx.PathPreview.CreatePathReq(pos, flags, goalW, goalH);

            ctx.PathPreview.PathEstimate.Clear();
            ctx.PathPreview.Estimate = S.Sys.Path.ExecuteReq(ref req,
                ctx.PathPreview.PathEstimate);
        }

        public override bool AddSelectedBeingRightClickContext(
                int playerFaction, int canDirectControl, int underDirectControl,
                QuickChoice choice) {
            bool added = false;
            if (A.InstantBuild && underDirectControl == 0 && ctx.TargetBeing != null
                    && !Pos.IsOOB(ctx.HoveredPos)) {
                var teleportBeing = ctx.TargetBeing;
                var teleportPos = ctx.HoveredPosition;
                choice.AddButton("Teleport here", () => {
                    teleportBeing.Motion.TeleportTo(teleportPos);
                });
                added = true;
            }
            if (ctx.HoveredBeing == null) {
                return added;
            }
            if (ctx.SelectedBeings.Count != 1) { return added; }
            // don't finish off yourself
            if (ctx.IsSelected(ctx.HoveredBeing)) {
                return added;
            }
            if (ctx.HoveredBeing.TryGetComponent<BodyComp>(out var body)) {
                if (body.AddEuthanizeButton(choice, ctx.SelectedBeings[0])) {
                    return true;
                }
            }
            return added;
        }

        // True when the being is done with: it attacks, or the attack was refused. A refused
        // attack order is not a move order to the enemy's tile.
        private static bool TryOrderAttack(FreeSelectContext ctx, Being b, Being target,
                int targetPos, bool mayApproach, ref bool ordered, ref string refusal) {
            var targetUnit = target.AIAgent.CombatUnit;
            var kind = CombatOrders.KindFor(b.AIAgent.CombatUnit, targetUnit, mayApproach);
            if (CombatOrders.IsRefused(kind)) {
                refusal = CombatOrders.RefusalReason(kind);
                return true;
            }
            if (!FreeSelectCombatHandler.SetAttackTarget(ctx, targetUnit,
                    b.AIAgent.CombatUnit, kind)) {
                return false;
            }
            if (b.AIAgent.CombatUnit.BestAttack?.IsMelee == true) {
                if (!Pos.AreAdjacent(targetPos, b.PosIdx)) {
                    if (b.Motion.IsBusy) {
                        b.Motion.Fail(T.AdRejInterrupted);
                    }
                    b.Motion.TryGoTo(targetPos, PathReqFlags.AdjNearby);
                }
            } else if (b.Motion.IsBusy) {
                // An earlier move order (a forced walk keeps the AI off the unit) or a
                // walk toward the previous target would run to its end first. The
                // shooter's AI then walks it into range of this one.
                b.Motion.Clear(true);
            }
            ordered = true;
            PlayBeingSound(b);
            return true;
        }

        // The attack order a confirm held back (a calm guest), given as it would have been
        private static void OrderAttack(FreeSelectContext ctx, List<Being> selected, Being target,
                int targetPos, bool mayApproach) {
            bool ordered = false;
            string refusal = null;
            for (int i = 0; i < selected.Count; i++) {
                var b = selected[i];
                if (!b.IsActive || !b.AIAgent.DirectControl.IsControllable
                        || !b.AIAgent.DirectControl.IsUnderDirectControl) {
                    continue;
                }
                TryOrderAttack(ctx, b, target, targetPos, mayApproach, ref ordered, ref refusal);
            }
            if (!ordered) {
                if (refusal != null) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(refusal), true);
                }
                UISounds.PlayActionDenied();
            }
        }

        public static void OnRightClickGoToPos(FreeSelectContext ctx) {
            var count = ctx.SelectedBeings.Count;
            int numAssigned = 0;
            int pos = ctx.HoveredPos;
            if (count > 0) {
                // we will want to precalculate where to send whom
                numAssigned = Pos.MultipleAround(pos, Caches.PosIdx, count, true);
            }
            bool ordered = false;
            string refusal = null;
            var S = ctx.S;
            // A shooter that can't hit the target from where it stands walks into range only on
            // Shift or in a group order; alone, the order is refused
            bool mayApproach = ctx.IsShiftPressed || CountOrderable(ctx) > 1;
            if (ctx.HoveringAssignableAttackTarget && ctx.HoveredBeing != null) {
                var target = ctx.HoveredBeing;
                var targetPos = ctx.HoveredPos;
                var selected = new List<Being>(ctx.SelectedBeings);
                void Order() => OrderAttack(ctx, selected, target, targetPos, mayApproach);
                if (FreeSelectCombatHandler.ConfirmGuestBetrayal(ctx, target, Order)
                        || FreeSelectCombatHandler.ConfirmFriendlyAttack(ctx, target, Order)) {
                    return;
                }
            }
            for (int i = 0; i < ctx.SelectedBeings.Count; i++) {
                Being b = ctx.SelectedBeings[i];
                int bPos;
                if (numAssigned > 0) {
                    bPos = Caches.PosIdx[i];
                } else {
                    bPos = pos;
                }
                // D.Warn("Sending being #{0} to {1}", i, Pos.String(bPos));
                if (!b.IsActive) { continue; }
                if (!b.AIAgent.DirectControl.IsControllable) {
                    continue;
                }
                if (!b.AIAgent.DirectControl.IsUnderDirectControl) {
                    continue;
                }

                if (ctx.HoveringAssignableAttackTarget
                        && TryOrderAttack(ctx, b, ctx.HoveredBeing, ctx.HoveredPos, mayApproach,
                            ref ordered, ref refusal)) {
                    continue;
                }

                // Tell worker to go somewhere
                if (!DropTaskFor(b)) {
                    continue;
                }
                b.Motion.ClearNavFailureAt(bPos);

                PathReqFlags flags;
                if (S.Sys.Path.IsNavigable(bPos, b.Motion.IsGrounded)) {
                    flags = PathReqFlags.AdjOnTop;
                } else {
                    flags = PathReqFlags.AdjNearby;
                }

                if (b.Motion.IsBusy) {
                    b.Motion.Clear(true);
                }

                if (b.Motion.TryGoTo(bPos, flags, out var prog)) {
                    EndAttackOrder(b.AIAgent.CombatUnit);
                    b.Motion.SetForcedWalk(directOrder: true);
                    ordered = true;
                    PlayBeingSound(b);
                } else {
                    if ((flags & PathReqFlags.AdjNearby) != PathReqFlags.AdjNearby) {
                        if (b.Motion.TryGoTo(bPos, flags | PathReqFlags.AdjNearby, out prog)) {
                            EndAttackOrder(b.AIAgent.CombatUnit);
                            b.Motion.SetForcedWalk(directOrder: true);
                            ordered = true;
                            PlayBeingSound(b);
                        } else {
                            D.Warn("Could not go to (with retry): {0}", prog.FailReason);
                        }
                    } else {
                        D.Warn("Could not go to: {0}", prog.FailReason);
                    }
                }
            }
            if (ordered) {

                ctx.PathPreview = null;
            } else {
                if (refusal != null) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(refusal), true);
                }
                UISounds.PlayActionDenied();
            }
        }

        private static int CountOrderable(FreeSelectContext ctx) {
            int n = 0;
            foreach (var b in ctx.SelectedBeings) {
                if (b.IsActive && b.AIAgent.DirectControl.IsControllable
                        && b.AIAgent.DirectControl.IsUnderDirectControl) {
                    n++;
                }
            }
            return n;
        }

        // A move order replaces an attack order. A forced target kept after it was never
        // re-evaluated, so the unit could stand at its new spot facing a target it can't hit
        // while another one walked into range.
        private static void EndAttackOrder(CombatUnit cu) {
            if (cu.IsAttackTargetForced) {
                cu.SetAttackTarget(null);
            }
            cu.DirectControlAttackTargetId = 0;
        }

        private static void PlayBeingSound(Being b) {
            BeingUtils.PlaySelectSoundFor(b);
        }

        private static bool DropTaskFor(Being b) {
            var g = b.AIAgent.Goal;
            if (g != null) {
                if (!AIGoal.Interrupt(g, G.Ticks)) {
                    return false;
                }
            }
            b.Motion.Abort("player forced walk");
            return true;
        }
        public override void OnDoubleClick(Vector2 pos) {
            if (ctx.DoubleClicked || ctx.IsCtrlPressed || ctx.IsAltPressed || ctx.IsShiftPressed) {
                return;
            }

            if (ctx.SelectedCount != 1) { return; }
            if (ctx.HoveredBeing == null) { return; }
            // we're double clicking, let's make sure nothing else will remain
            // selected
            ctx.ClearSelectedObjs();

            // if (!ctx.IsExclusivelySelected(ctx.HoveredBeing)) {
            //     D.Warn("Not exclusively selected: {0}", ctx.HoveredBeing);
            //     return;
            // }

            var species = ctx.HoveredBeing.Species;
            var bAlive = ctx.HoveredBeing.IsAlive;
            ctx.ClearSelectedBeings();
            foreach (var o in S.Beings.All) {
                if (o.Species == species && o.IsAlive == bAlive) {
                    if (RenderUtils.IsVisible(o.Position)) {
                        SelectBeing(o);
                    }
                }
            }
            S.Sig.BeingsSelected.Send(ctx.SelectedBeings);
            ctx.DoubleClicked = true;
        }
    }
}
