using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Systems;
using Game.Systems.AI;
using Game.Systems.Motion;
using Game.Systems.Reg;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectContext {
        public readonly GameState S;
        public readonly FreeSelectHandler[] Handlers;
        public readonly int NumHandlers;
        public bool IsCtrlPressed;
        public bool IsAltPressed;
        public bool IsShiftPressed;
        public Vector2 DragStart;
        public Vector2 DragEnd;
        public bool ReadyForClickUp;
        public int ClickDownPos { get; private set; }
        public int HoveredPos;
        public Vector2 HoveredPosition;
        public bool HoveredPosChanged;
        public int LastHoverPos;
        // Set while the cursor is over UI, so the hover outline and the path
        // preview do not stay where the cursor left the map
        public bool IsHoverHidden;
        // Set while a hull view roof covers the cursor: only the handlers that work over roofs
        // (FreeSelectHandler.WorksOverRoofs) show a tooltip or take a click there
        public bool IsUnderRoof;
        private bool isDragging;
        public bool IsDragging {
            get => isDragging;
            set {
                isDragging = G.IsClickDrag = value;
            }
        }
        public bool WasDragging;
        public bool PreventDragging;
        public bool HasStartedDragging;
        public bool IsHoveringSelectableTile;
        public bool IsHoveringDirtyFloor;
        public Tile HoveredTile;
        public Obj HoveredObj;
        public bool SelectionChanged;
        public int ClicksInCurrentPosIdx;
        public readonly StringBuilder Tooltip = new();
        public readonly List<Being> SelectedBeings = new();
        public readonly List<Tile> SelectedTiles = new();
        public readonly List<Obj> SelectedObjs = new();
        private readonly FreeSelectRightClickUI rightClickUI;
        public int SelectedCount => SelectedBeings.Count + SelectedObjs.Count + SelectedTiles.Count;

        public bool IsHoveringSelectableEntity => IsHoveringSelectableTile
            || HoveredBeing != null || HoveredObj != null;

        public bool CanToggleConnect;

        public MotionControl PathPreview;
        public SelectionVFX SelVFX;
        public int WantedCursor;
        public int CurrentCursor;
        public Being TargetBeing;
        public Being HoveredBeing;
        // the hovered being's conditions already went under its info lines this hover
        public bool BeingConditionsShown;
        public int BeingHintPos;
        public int SelectedBeingLastPos;
        public bool TargetBeingCanGoToHoverPos;
        public bool TargetBeingIsControllable;
        public bool TargetBeingIsUnderDirectControl;
        public bool HoveredPosOnFire;
        public bool HoveringAssignableAttackTarget;
        public Tile ClickDownTile;
        public bool DoubleClicked;
        public bool ClickUpConsumed;
        public readonly List<AIGoal> HoveredGoals = new();
        public readonly EntityInfoItems[] InfoItems = new EntityInfoItems[4];
        public OverlayInfo CurrentOverlay;

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            //showAllAds = on && info.Id == OverlayId.Tasks;
            CurrentOverlay = on ? info : null;
        }

        public FreeSelectContext(GameState s, FreeSelectHandler[] handlers) {
            S = s;
            Handlers = handlers;
            NumHandlers = handlers.Length;
            rightClickUI = new FreeSelectRightClickUI(S, rightClickContext, this);
            S.Query.IsAnythingSelected.SetResponder(IsAnythingSelected);
            S.Query.IsSelectionFull.SetResponder(IsSelectionFull);
            S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            S.Query.IsSelected.SetResponder(IsSelected);
            S.Query.IsExclusivelySelected.SetResponder(IsExclusivelySelected);
            S.Query.DeselectIfSelected.SetResponder(DeselectIfSelected);
            S.Query.DeselectAny.SetResponder(DeselectAny);
            S.Query.GetSelectedParentTile.SetResponder(GetSelectedParentTile);
            S.Query.StartBuildACopy.SetResponder(StartBuildACopy);
            S.Query.StartRelocate.SetResponder(StartRelocate);
            S.Query.GetSelectedTiles.SetResponder(GetSelectedTiles);
            S.Query.GetSelectedObjs.SetResponder(GetSelectedObjs);
            S.Query.GetSelectedObj.SetResponder(GetSelectedObj);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Query.SelectBeingCount.SetResponder(GetSelectedBeingCount);
            S.Query.ClearSelectedBeings.SetResponder(OnClearSelectedBeings);
            S.Query.OrderRemovingSelectedTiles.SetResponder(OrderRemovingSelectedTiles);
            S.Query.IsDisposableSelected.SetResponder(IsDisposableSelected);
            S.Query.TryDisposeSelected.SetResponder(TryDisposeSelected);
            S.Query.TryCancelSelectedTasks.SetResponder(TryCancelSelectedTasks);
            S.Query.HasSelectedTasks.SetResponder(HasSelectedTasks);
        }

        // Availability of the cancel tasks hotkey: must agree with what
        // TryCancelSelectedTasks would cancel, or the key gets swallowed
        // instead of falling through to the cancel tool
        private bool HasSelectedTasks() {
            for (int i = SelectedBeings.Count - 1; i >= 0; i--) {
                if (SelectedBeings[i]?.Goal != null) { return true; }
            }
            for (int i = SelectedObjs.Count - 1; i >= 0; i--) {
                if (SelectedObjs[i]?.Goal != null) { return true; }
            }
            for (int i = SelectedTiles.Count - 1; i >= 0; i--) {
                if (EntityUtils.ParentObj(SelectedTiles[i])?.Goal != null) { return true; }
            }
            return false;
        }

        private bool TryCancelSelectedTasks() {
            if (SelectedCount == 0) { return false; }

            var goals = Caches.Goals;
            var goalSet = Caches.GoalSet;
            goals.Clear();
            goalSet.Clear();

            AddSelectedGoals(SelectedBeings, goals, goalSet);
            AddSelectedGoals(SelectedObjs, goals, goalSet);
            for (int i = SelectedTiles.Count - 1; i >= 0; i--) {
                AddSelectedGoal(EntityUtils.ParentObj(SelectedTiles[i]), goals, goalSet);
            }

            bool cancelled = ToolTasksCancel.CancelGoals(S, goals);
            goals.Clear();
            goalSet.Clear();
            if (cancelled) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cancelled;
        }

        private static void AddSelectedGoals<T>(
                List<T> entities, List<AIGoal> goals, HashSet<AIGoal> goalSet) where T : Entity {
            for (int i = entities.Count - 1; i >= 0; i--) {
                AddSelectedGoal(entities[i], goals, goalSet);
            }
        }

        private static void AddSelectedGoal(
                Entity entity, List<AIGoal> goals, HashSet<AIGoal> goalSet) {
            var goal = entity?.Goal;
            if (goal != null && goalSet.Add(goal)) {
                goals.Add(goal);
            }
        }

        private bool IsDisposableSelected() {
            return DisposeAction.OrderDispose(SelectedBeings, SelectedObjs, true,
                out _, out _, out _);
        }

        private bool TryDisposeSelected(bool nextFrame) {
            if (nextFrame) {
                Coroutines.OnNextFrame(() => TryDisposeSelected(false));
                return true;
            }
            return DisposeAction.OrderDispose(SelectedBeings, SelectedObjs, false,
                out _, out _, out _);
        }

        private bool OrderRemovingSelectedTiles() {
            if (SelectedTiles.Count > 0) {
                bool ordered = false;
                foreach (var t in SelectedTiles) {
                    var parent = EntityUtils.ParentObj(t);
                    if (parent.Constructable.TryOrderDeconstruct()) {
                        ordered = true;
                    }
                }
                if (ordered) {
                    UIUtils.RefreshSelectedEntityUI();
                }
                return ordered;
            }
            return false;
        }

        private int GetSelectedBeingCount() {
            return SelectedBeings.Count;
        }

        private readonly QuickChoice rightClickContext = QuickChoice.Create();

        private bool QuickGoToCheckFor(int pos, Being b) {
            if (Pos.IsOOB(pos)) { return false; }
            if (b.Motion.IsImmobilized) { return false; }
            if (b.IsDead || b.Data.Body.IsIncapacitated) { return false; }
            var obj = EntityUtils.ParentObjAt(pos);
            int dist;
            if (obj != null && obj.Transform.IsMulti) {
                var tr = obj.Transform;
                dist = S.Query.RegionDist.Ask(
                    RegPathReq.Near(b.PosIdx, tr.PosIdx, b.Motion.IsGrounded,
                        tr.RotatedWidth, tr.RotatedHeight));
            } else {
                dist = S.Query.RegionDist.Ask(
                    RegPathReq.Near(b.PosIdx, pos, b.Motion.IsGrounded));
            }
            return dist != -1;
        }


        public bool OnClickCancelReleased() {
            if (SelectedBeings.Count == 0) {
                return HandleRightClickWithoutSelectedBeings();
            }
            int canDC = 0;
            int underDC = 0;
            int playerFaction = 0;
            Being bestBeing = null;
            for (int i = SelectedBeings.Count - 1; i >= 0; i--) {
                Being b = SelectedBeings[i];
                if (!b.IsAlive || !b.Data.IsPlayer) {
                    continue;
                }
                playerFaction++;
                if (!b.Data.Body.IsIncapacitated && !b.Motion.IsImmobilized
                        && b.AIAgent.DirectControl.IsControllable) {
                    canDC++;
                    if (bestBeing == null || (!bestBeing.Motion.IsFlying && b.Motion.IsFlying)) {
                        bestBeing = b;
                    }
                }
                if (b.AIAgent.DirectControl.IsUnderDirectControl) {
                    underDC++;
                }
            }
            rightClickContext.Clear();
            bool didSomething = false;
            if (!TargetBeingCanGoToHoverPos && !IsCtrlPressed && bestBeing != null) {
                // we need a rough estimate here
                TargetBeingCanGoToHoverPos = QuickGoToCheckFor(HoveredPos, bestBeing);
            }
            foreach (var h in Handlers) {
                // Nothing to offer for what a roof hides; drafted units still take the move
                // or attack order below
                if (IsUnderRoof && !h.WorksOverRoofs) { continue; }
                var countBefore = rightClickContext.Count;
                var hasIt = h.AddSelectedBeingRightClickContext(playerFaction, canDC, underDC,
                    rightClickContext);
                if (hasIt && rightClickContext.Count == countBefore) {
                    hasIt = false;
                    D.Err("Ctx Handler lying about having it! {0}", h.Id);
                }
                didSomething |= hasIt;
            }
            if (didSomething) {
                rightClickUI.Show(true);
            }
            if (underDC > 0 && !didSomething) {
                // try to order going somewhere
                FreeSelectBeingHandler.OnRightClickGoToPos(this);
                didSomething = true;
            }
            return didSomething;
        }

        private bool HandleRightClickWithoutSelectedBeings() {
            rightClickContext.Clear();
            bool didSomething = false;
            foreach (var h in Handlers) {
                if (IsUnderRoof && !h.WorksOverRoofs) { continue; }
                var countBefore = rightClickContext.Count;
                var hasIt = h.AddRightClickContext(rightClickContext);
                if (hasIt && rightClickContext.Count == countBefore) {
                    hasIt = false;
                    D.Err("Ctx Handler lying about having it 2! {0}", h.Id);
                }
                didSomething |= hasIt;
            }
            if (didSomething) {
                rightClickUI.Show(false);
            }
            return didSomething;
        }

        public Entity SelectedEntity {
            get {
                if (SelectedBeings.Count > 0) { return SelectedBeings[0]; }
                if (SelectedTiles.Count > 0) { return EntityUtils.ParentObj(SelectedTiles[0]); }
                if (SelectedObjs.Count > 0) { return SelectedObjs[0]; }
                return null;
            }
        }

        private List<Tile> GetSelectedTiles() {
            return SelectedTiles;
        }
        private List<Obj> GetSelectedObjs() {
            return SelectedObjs;
        }
        private Obj GetSelectedObj(bool isOnlyOne) {
            if (SelectedObjs.Count == 0) {
                return null;
            }
            if (isOnlyOne) {
                return SelectedObjs.Count == 1 ? SelectedObjs[0] : null;
            } else {
                return SelectedObjs[0];
            }
        }

        private Tile GetSelectedParentTile(bool isOnlyOne) {
            var cnt = SelectedTiles.Count;
            if (cnt == 0 || (isOnlyOne && cnt != 1)) { return null; }
            var t = SelectedTiles[0];
            return EntityUtils.ParentObj(t);
        }
        private Tile GetSelectedTile(bool isOnlyOne) {
            var cnt = SelectedTiles.Count;
            if (cnt == 0 || (isOnlyOne && cnt != 1)) { return null; }
            return SelectedTiles[0];
        }
        public bool DeselectNonBeings() {
            bool deselected = SelectedCount > SelectedBeings.Count;
            ClearSelectedTiles();
            ClearSelectedObjs();
            if (deselected) {
                S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
            }
            return deselected;
        }
        public bool DeselectAny() {
            bool deselected = SelectedCount > 0;
            ClearSelectedTiles();
            ClearSelectedObjs();
            ClearSelectedBeings();
            if (deselected) {
                S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
            }
            return deselected;
        }
        public bool DeselectIfSelected(Entity entity) {
            if (entity is Tile t) {
                if (SelectedTiles.Count == 0) {
                    return false;
                }
                // The selected entry whose parent is this tile: the first selected tile used to
                // be deselected instead, and selected child tiles of this one were missed
                t = EntityUtils.ParentObj(t);
                for (int i = SelectedTiles.Count - 1; i >= 0; i--) {
                    var selT = SelectedTiles[i];
                    if (EntityUtils.ParentObj(selT) == t) {
                        DeselectTile(selT);
                        S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
                        return true;
                    }
                }
                return false;
            } else if (entity is Being b) {
                if (SelectedBeings.Contains(b)) {
                    DeselectBeing(b);
                    S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
                    return true;
                }
                return false;
            } else if (entity is Obj o) {
                if (SelectedObjs.Contains(o)) {
                    DeselectObj(o);
                    S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
                    return true;
                }
                return false;
            }
            return false;
        }

        public bool IsSelected(Entity entity) {
            if (entity is Tile t) {
                t = EntityUtils.ParentObj(t);
                for (int i = SelectedTiles.Count - 1; i >= 0; i--) {
                    Tile st = SelectedTiles[i];
                    var selT = EntityUtils.ParentObj(st);
                    if (t == selT) {
                        return true;
                    }
                }
                return false;
            } else if (entity is Being b) {
                return SelectedBeings.Contains(b);
            } else if (entity is Obj o) {
                return SelectedObjs.Contains(o);
            }
            return false;
        }
        public bool IsExclusivelySelected(Entity entity) {
            if (entity is Tile t) {
                if (SelectedCount != 1 || SelectedTiles.Count != 1) {
                    return false;
                }
                t = EntityUtils.ParentObj(t);
                var selT = EntityUtils.ParentObj(SelectedTiles[0]);
                return t == selT;
            } else if (entity is Being b) {
                return SelectedBeings.Contains(b) && SelectedCount == 1;
            } else if (entity is Obj o) {
                return SelectedObjs.Contains(o) && SelectedCount == 1;
            }
            return false;
        }

        public bool DeselectTile(Tile tile) {
            if (SelectedTiles.Remove(tile)) {
                SelectionChanged = true;
                TransmitToTile(tile, MsgIdH.EntityDeselected);
                return true;
            }
            return false;
        }

        private void OnEntityRemoved(Entity entity) {
            var wasSelected = false;
            if (entity is Obj obj) {
                wasSelected = SelectedObjs.Remove(obj);
            } else if (entity is Tile tile) {
                if (DeselectTile(tile)) {
                    SelVFX?.ShowSelectedTiles(SelectedTiles);
                    wasSelected = true;
                }
            } else if (entity is Being b) {
            }
            UpdateVFX(S.Sys.Particles);
            // The selection UI shares these lists and was not told: the bar kept the removed
            // entity's actions, and its outdated-goals check rebuilt it every 0.25 s
            if (wasSelected) {
                S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
            }
        }

        public bool DeselectBeing(Being b) {
            if (SelectedBeings.Remove(b)) {
                b.Transmit(null, MsgIdH.EntityDeselected);
                SelectionChanged = true;
                return true;
            }
            return false;
        }


        private bool IsAnythingSelected() {
            return SelectedBeings.Count > 0 ||
                SelectedObjs.Count > 0 ||
                SelectedTiles.Count > 0;
        }

        private int selectionLimitFrame = -1;

        /// <summary>
        /// Mass selects can gather thousands of entities, which the multi entity
        /// panel and the action bar cannot handle. Every mass select loop must
        /// break on this.
        /// </summary>
        public bool IsSelectionFull() {
            if (SelectedCount < Consts.MaxSelectedEntities) { return false; }
            // Tile, obj and being handlers can all hit the cap on the same click,
            // and the message plays a denied sound, so warn once per frame
            if (selectionLimitFrame != Time.frameCount) {
                selectionLimitFrame = Time.frameCount;
                S.Sig.ShowErrorMessage.Send(
                    T.UiSelectionLimit_V1.F(Consts.MaxSelectedEntities));
            }
            return true;
        }

        public void ClearSelectedObjs() {
            // D.Warn("Clearing selected objs: {0}", D.Dump(SelectedObjs));
            var cnt = SelectedObjs.Count;
            if (cnt == 0) { return; }
            for (int i = 0; i < cnt; i++){
                var o = SelectedObjs[i];
                o.Transmit(null, MsgIdH.EntityDeselected);
            }
            SelectedObjs.Clear();
            SelectionChanged = true;
        }

        public bool DeselectObj(Obj o) {
            if (SelectedObjs.Remove(o)) {
                o.Transmit(null, MsgIdH.EntityDeselected);
                SelectionChanged = true;
                return true;
            }
            return false;
        }

        private bool OnClearSelectedBeings() {
            if (SelectedBeings.Count > 0) {
                ClearSelectedBeings();
                return true;
            }
            return false;
        }

        public void ClearSelectedBeings() {
            var cnt = SelectedBeings.Count;
            if (cnt == 0) { return; }
            for (int i = 0; i < cnt; i++) {
                Being b = SelectedBeings[i];
                if (b.IsActive) {
                    b.Transmit(null, MsgIdH.EntityDeselected);
                    b.Motion.HidePathViz();
                }
            }
            SelectionChanged = true;
            SelectedBeings.Clear();
        }
        public void SelectBeing(Being b) {
            if (IsSelectionFull()) { return; }
            // if we could remove, let's keep it removed
            if (!SelectedBeings.Contains(b)) {
                b.Transmit(null, MsgIdH.EntitySelected);
                SelectedBeings.Add(b);
                SelectionChanged = true;
            }
        }
        public void SelectTile(Tile t) {
            // D.Warn("selecting tile: {0}", t);
            if (IsSelectionFull()) { return; }
            if (!t.IsActive) {
                D.Warn("Trying to select an inactive tile: {0}", t);
                return;
            }
            if (SelectedTiles.Contains(t)) { return; }
            TransmitToTile(t, MsgIdH.EntitySelected);
            SelectionChanged = true;
            SelectedTiles.Add(t);
        }

        private static void TransmitToTile(Tile t, int msg) {
            if (t.Transform.IsChild) {
                t.Transform.Parent.Transmit(null, msg);
            } else {
                t.Transmit(null, MsgIdH.EntitySelected);
            }
        }

        public void ClearSelectedTiles() {
            var cnt = SelectedTiles.Count;
            if (cnt == 0) { return; }
            S.Toolbox.ConnectTool.ClearShownTexts();
            S.Sys.Particles.ElectricityVFX.ShowSelected(null);
            for (int i = 0; i < cnt; i++) {
                Tile t = SelectedTiles[i];
                TransmitToTile(t, MsgIdH.EntityDeselected);
            }
            SelectedTiles.Clear();
            SelectionChanged = true;
            UpdateSelectedIndices();
        }

        public void ClearVFX() {
            SelVFX?.HideAll();
        }

        public bool Clear() {
            Tooltip.Clear();
            BeingConditionsShown = false;
            HoveredGoals.Clear();
            ReadyForClickUp = false;
            for (int i = InfoItems.Length - 1; i >= 0; i--) {
                InfoItems[i] = null;
            }
            SelectionChanged = false;
            TargetBeingIsUnderDirectControl = false;
            TargetBeingIsControllable = false;
            ClickDownTile = null;
            WantedCursor = 0;
            CurrentCursor = 0;
            The.SysSig.SetCursor.Send(CursorTypeH.Cursor);
            The.SysSig.SetCursorOverride.Send(0);
            IsDragging = false;
            PreventDragging = false;
            ClickDownPos = Pos.Invalid;
            LastHoverPos = Pos.Invalid;
            HoveredPos = Pos.Invalid;
            IsHoveringSelectableTile = false;
            PathPreview = null;
            HoveredTile = null;
            HoveredBeing = null;
            HoveredObj = null;
            TargetBeing = null;
            The.SysSig?.HideTooltip.Send();
            SelVFX?.HideAll();
            return TryDeselect();
        }

        public bool TryDeselect() {
            bool deselected = false;
            if (SelectedBeings.Count > 0) {
                ClearSelectedBeings();
                deselected = true;
            }
            if (SelectedObjs.Count > 0) {
                ClearSelectedObjs();
                deselected = true;
            }
            if (SelectedTiles.Count > 0) {
                ClearSelectedTiles();
                deselected = true;
            }
            if (deselected) {
                S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
            }
            UpdateSelectedIndices();
            return deselected;
        }

        internal void EndDrag() {
            WasDragging = false;
            HasStartedDragging = false;
            DragStart = Consts.V2Zero;
            DragEnd = Consts.V2Zero;
            IsDragging = false;
            S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
        }
        public void UpdateVFX(ParticlesSys ps) {
            SelVFX ??= ps.SelectionVFX;
            SelVFX.UpdateSelectedObj(SelectedObjs);
            SelVFX.UpdateSelectedBeings(SelectedBeings);
            if (PathPreview != null && !IsHoverHidden) {
                if (!PathPreview.Being.IsActive) {
                    PathPreview = null;
                    SelVFX.HidePreviewPath();
                } else {
                    SelVFX.ShowPreviewPath(PathPreview.PathEstimate,
                        PathPreview.Estimate.IsSafe ?
                            Consts.BeingPathPreviewColor :
                                Consts.BeingPathPreviewUnsafeColor);
                }
            } else {
                SelVFX.HidePreviewPath();
            }
            SelVFX.UpdateLinks(SelectedTiles, SelectedObjs, SelectedBeings);
            if (!Consts.DisableHoverEffects && !IsHoverHidden) {
                SelVFX.UpdateHoverTile(IsHoveringSelectableTile, HoveredTile);
            } else {
                SelVFX.UpdateHoverTile(false, null);
            }
        }

        public void UpdateSelectedIndices(int pos = 0) {
            if (SelectedTiles.Count == 1) {
                var t = EntityUtils.ParentObj(SelectedTiles[0]);
                var layer = t.Definition.LayerId;
                if (layer == WorldLayer.Floor) {
                    Shader.SetGlobalInt(ShaderVars._SelectedFloorIndex, pos);
                    Shader.SetGlobalInt(ShaderVars._SelectedWallIndex, -1);
                } else if (layer == WorldLayer.Walls) {
                    Shader.SetGlobalInt(ShaderVars._SelectedFloorIndex, -1);
                    Shader.SetGlobalInt(ShaderVars._SelectedWallIndex, pos);
                } else { // object is selected
                    Shader.SetGlobalInt(ShaderVars._SelectedFloorIndex, -1);
                    Shader.SetGlobalInt(ShaderVars._SelectedWallIndex, -1);
                }
            } else {
                Shader.SetGlobalInt(ShaderVars._SelectedFloorIndex, -1);
                Shader.SetGlobalInt(ShaderVars._SelectedWallIndex, -1);
            }
        }

        public void EndClickDown() {
            if (IsCtrlPressed || IsShiftPressed || IsAltPressed) {
                return;
            }
            // deselect everything if we had multiple things selected
            // otherwise we will want to cycle
            if (SelectedCount > 1) {
                // D.Warn("Clearing all on click down");
                ClearSelectedBeings();
                ClearSelectedObjs();
                ClearSelectedTiles();
            }
        }

        public void EndClickUp() {
            // D.Warn("Selected: {0} | {1} | {2}",
            //     D.Dump(SelectedBeings),
            //     D.Dump(SelectedObjs),
            //     D.Dump(SelectedTiles));
            ClicksInCurrentPosIdx++;
            if (SelectionChanged) {
                UpdateSelectedIndices(HoveredPos);
                S.Sig.MultiSelect.Send(SelectedBeings, SelectedObjs, SelectedTiles);
            }
        }

        public void BeginClickDown(Vector2 pos) {
            ReadyForClickUp = true;
            SelectionChanged = false;
            // The tile handler sets it again, unless a roof keeps the click from reaching it
            ClickDownTile = null;
            if (EntityUtils.IsWithinBounds(pos)) {
                var p = EntityUtils.ToPos(pos);
                UpdateClickDownPos(p);
            } else {
                UpdateClickDownPos(Pos.Invalid);
            }
        }

        private void UpdateClickDownPos(int pos) {
            if (ClickDownPos != pos) {
                // D.Warn("Updating pos: {0}", Pos.StringFromTo(ClickDownPos, pos));
                ClickDownPos = pos;
            }
        }

        // The hovered entities stay known, only what shows them goes away
        public void HideHover() {
            if (IsHoverHidden) { return; }
            IsHoverHidden = true;
            // An attack or connect cursor would stay on over the UI
            if (CurrentCursor != 0) {
                CurrentCursor = 0;
                The.SysSig.SetCursor.Send(CursorTypeH.Cursor);
            }
        }

        public void BeginOnHover(Vector2 pos) {
            IsHoverHidden = false;
            IsCtrlPressed = The.Bindings.IsPressed(ActionType.CtrlModifier);
            IsShiftPressed = The.Bindings.IsPressed(ActionType.ShiftModifier);
            IsAltPressed = The.Bindings.IsPressed(ActionType.AltModifier);
            // D.Warn("FreeSelect: OnHover({0})", pos);
            var wasUnderRoof = IsUnderRoof;
            if (EntityUtils.IsWithinBounds(pos)) {
                HoveredPosition = pos;
                HoveredPos = EntityUtils.ToPos(pos);
                IsUnderRoof = MapRenderer.Current?.HullView?.Covers(pos) == true;
            } else {
                HoveredPosition = new Vector2(-1f, -1f);
                HoveredPos = Pos.Invalid;
                IsUnderRoof = false;
            }
            WantedCursor = 0;
            // A roof fading in or out under a cursor that stays put changes what is hovered too
            HoveredPosChanged = HoveredPos != LastHoverPos || IsUnderRoof != wasUnderRoof;
            if (HoveredPosChanged) {
                ClicksInCurrentPosIdx = 0;
            }
            Tooltip.Clear();
            BeingConditionsShown = false;
        }

        public void EndOnHover() {
            if (WantedCursor != CurrentCursor) {
                if (WantedCursor != 0) {
                    The.SysSig.SetCursor.Send(WantedCursor);
                } else {
                    The.SysSig.SetCursor.Send(CursorTypeH.Cursor);
                }
                CurrentCursor = WantedCursor;
            }
            LastHoverPos = HoveredPos;
            S.Vars.IsHoveringEntity =
                HoveredTile != null || HoveredBeing != null || HoveredObj != null;
            if (Tooltip.Length > 0) {
                The.SysSig.ShowTooltip.Send(Tooltip.ToString(), false);
            } else {
                The.SysSig.HideTooltip.Send();
            }
        }

        public StringBuilder BeginTooltipBlock() {
            if (Tooltip.Length > 0) {
                // D.Warn("Adding tip separator after: {0}", Tooltip.ToString());
                Tooltip.Append(GlyphMap.BRHR);
            }
            return Tooltip;
        }

        private bool StartBuildACopy() {
            if (SelectedTiles.Count == 1) {
                var tile = EntityUtils.ParentObj(SelectedTiles[0]);
                if (tile.Constructable?.IsReachableForCommonAction == true) {
                    var buildTool = ToolPlaceTiles.StartBuilding(tile.DefinitionId);
                    if (tile.Transform.IsRotatable) {
                        buildTool.SetRotation(tile.Transform.Rotation);
                    }
                } else {
                    var sb = Caches.StringBuilder2;
                    sb.Clear();
                    sb.Append(tile.Definition.CodexLink);
                    if (!tile.Definition.IsInSeason) {
                        sb.Append(' ');
                        sb.Append(GlyphMap.ArrowRight);
                        sb.Append(' ');
                        sb.Append(Texts.Red(T.ResearchSeasonalLocked_V1.F(tile.Definition.Seasonal.RangeText)));
                    } else if (!Researchable.IsEmptyPrereq(tile.Definition)) {
                        sb.Append(' ');
                        sb.Append(GlyphMap.ArrowRight);
                        sb.Append(' ');
                        sb.AppendFormat(Texts.Red("{0}: {1}"), T.CodexMResearch, T.ResearchLocked);
                        sb.Append(GlyphMap.BRHR);
                        sb.Append(S.Research.TreePathFor(tile.Definition));
                    } else {
                        sb.Append(GlyphMap.BRHR);
                        sb.Append(T.DeviceLootableOnly);
                    }
                    S.Sig.ShowErrorMessage.Send($"{T.BuildACopy}: {T.OperationFailed}: {sb.ToString()}");
                }
                return true;
            } else if (SelectedTiles.Count > 1) {
                BuildACopyAction.BuildACopy(SelectedTiles, false, out _);
                return true;
            }
            return false;
        }

        private bool StartRelocate() {
            if (SelectedTiles.Count == 1) {
                return GetSelectedParentTile(false).Reinstallable?.BeginRelocation() == true;
            } else if (SelectedTiles.Count > 1) {
                return RelocateUtils.Relocate(SelectedTiles, false, out _);
            } else {
                return RelocateUtils.Relocate(SelectedObjs, SelectedBeings);
            }
        }
    }
}
