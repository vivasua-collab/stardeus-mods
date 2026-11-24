using Game.Constants;
using Game.Data;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Components;
using Game.Input;
using KL.Utils;
using Game.Systems.AI;
using KL.Grid;

namespace Game.Tools {
    public abstract class ToolTasksBase : TileTool {
        protected const int MaxPriority = 5;
        private const int NearbyRadius = 30;
        private const float RefreshInterval = 0.5f;
        private const float MaxZoomForPriorities = 20f;
        private const int MarkerDeltas = MaxPriority * 2 + 1;
        // Cached so that refreshing a large drag rect does not allocate a string per task
        private static readonly string[] changeMarkers = BuildChangeMarkers();
        private static readonly string[] cancelMarkers = BuildCancelMarkers();
        // Target -> text this tool shows on it. Swapped on every refresh, so only the
        // texts that are no longer wanted get hidden
        private Dictionary<IComponent, string> shownTexts = new();
        private Dictionary<IComponent, string> nextShownTexts = new();
        // Goals a click would act on right now: the hovered tile or the drag rect
        private readonly List<AIGoal> markedGoals = new();
        private readonly HashSet<AIGoal> markedSet = new();
        private int centerPos = Pos.Invalid;
        private PosRect markedRect;
        private int modifierBits;
        private float nextRefreshAt;
        private bool needsRefresh = true;
        private bool hasAdAtCurrentPos;
        public override bool IsConfigurable => false;
        protected override void Initialize() {
            showDragRectSize = false;
        }

        public string ShownPriorityText(IComponent target) {
            if (target == null) { return null; }
            return shownTexts.TryGetValue(target, out var text) ? text : null;
        }

        private static string FormatChangeMarker(int priority, int delta) {
            var sign = delta > 0 ? "+" : "-";
            return $"{priority}<size=50%>[{sign}{Mathf.Abs(delta)}]</size>";
        }

        private static string FormatCancelMarker(int priority) {
            return $"{priority}<size=50%>[x]</size>";
        }

        private static string[] BuildChangeMarkers() {
            var res = new string[(MaxPriority + 1) * MarkerDeltas];
            for (int p = 0; p <= MaxPriority; p++) {
                for (int d = -MaxPriority; d <= MaxPriority; d++) {
                    res[p * MarkerDeltas + d + MaxPriority] = FormatChangeMarker(p, d);
                }
            }
            return res;
        }

        private static string[] BuildCancelMarkers() {
            var res = new string[MaxPriority + 1];
            for (int p = 0; p <= MaxPriority; p++) {
                res[p] = FormatCancelMarker(p);
            }
            return res;
        }

        protected static string ChangeMarker(int priority, int delta) {
            if (priority < 0 || priority > MaxPriority
                    || delta < -MaxPriority || delta > MaxPriority) {
                return FormatChangeMarker(priority, delta);
            }
            return changeMarkers[priority * MarkerDeltas + delta + MaxPriority];
        }

        protected static string CancelMarker(int priority) {
            if (priority < 0 || priority > MaxPriority) {
                return FormatCancelMarker(priority);
            }
            return cancelMarkers[priority];
        }

        // Text for goals that a click would act on. Nearby goals always show the plain number
        protected virtual string MarkedPriorityText(AIGoal goal) {
            return Units.Num(goal.GoalPriority);
        }

        private void ClearShownTaskPriorities() {
            if (shownTexts.Count > 0) {
                foreach (var target in shownTexts.Keys) {
                    target.HideInfoText();
                }
                shownTexts.Clear();
            }
        }

        private void ShowText(IComponent target, string text) {
            // Marked goals are shown first, a nearby goal on the same target must not override
            if (nextShownTexts.ContainsKey(target)) { return; }
            // Always pass it on: the info system may have dropped the text on its own
            target.ShowInfoText(text, string.Empty);
            nextShownTexts[target] = text;
        }

        private void RefreshShownPriorities() {
            needsRefresh = false;
            nextRefreshAt = Time.unscaledTime + RefreshInterval;
            var zoom = S.BB.Get(BBKeyH.CameraZoom, 4f);
            if (zoom > MaxZoomForPriorities) {
                ClearShownTaskPriorities();
                return;
            }
            markedSet.Clear();
            for (int i = 0; i < markedGoals.Count; i++) {
                var g = markedGoals[i];
                if (g.Target == null) { continue; }
                markedSet.Add(g);
                ShowText(g.Target, MarkedPriorityText(g));
            }
            if (centerPos != Pos.Invalid) {
                ShowNearbyPriorities();
            }
            markedSet.Clear();
            foreach (var target in shownTexts.Keys) {
                if (!nextShownTexts.ContainsKey(target)) {
                    target.HideInfoText();
                }
            }
            (shownTexts, nextShownTexts) = (nextShownTexts, shownTexts);
            nextShownTexts.Clear();
        }

        private void ShowNearbyPriorities() {
            Pos.ToXY(centerPos, out var x, out var y);
            var area = new PosRect(
                Mathf.Max(0, x - NearbyRadius),
                Mathf.Max(0, y - NearbyRadius),
                Mathf.Min(S.GridWidth - 1, x + NearbyRadius),
                Mathf.Min(S.GridHeight - 1, y + NearbyRadius));
            Caches.Goals.Clear();
            S.Query.AIGoalsInArea.Ask(area, Caches.Goals);
            const int sqrRadius = NearbyRadius * NearbyRadius;
            for (int i = 0; i < Caches.Goals.Count; i++) {
                var g = Caches.Goals[i];
                if (g.Target == null || markedSet.Contains(g)) { continue; }
                if (Pos.SqrDistance(g.Target.Entity.PosIdx, centerPos) > sqrRadius) {
                    continue;
                }
                ShowText(g.Target, Units.Num(g.GoalPriority));
            }
            Caches.Goals.Clear();
        }

        private static int ReadModifierBits() {
            var bits = 0;
            if (The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                bits |= 1;
            }
            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                bits |= 2;
            }
            return bits;
        }

        protected virtual void OnHover() {}

        protected virtual Color dragRectColor => Consts.GreenRectColor;

        // The numbers are about the tasks around the cursor, they go with it
        protected override void HideHover() {
            base.HideHover();
            ResetShownPriorities();
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            OnHover();
            var pos = EntityUtils.ToPosSafe(coords);
            if (pos != centerPos) {
                centerPos = pos;
                needsRefresh = true;
            }
            var bits = ReadModifierBits();
            if (bits != modifierBits) {
                modifierBits = bits;
                needsRefresh = true;
            }
            if (isDragging) {
                OnHoverDrag();
                // OnDrag runs after OnHover and refreshes with this frame's drag rect
                return;
            }
            // The periodic refresh picks up tasks added, finished, moved or
            // reprioritized elsewhere while the cursor rests
            if (needsRefresh || Time.unscaledTime >= nextRefreshAt) {
                markedGoals.Clear();
                hasAdAtCurrentPos = S.Query.AIGoalsAtPos.Ask(pos, markedGoals) > 0;
                RefreshShownPriorities();
            }
            lastHoverPos = pos;
            if (hasAdAtCurrentPos) {
                ShowColorTile(pos, dragRectColor);
            } else {
                ClearPreview();
            }
        }

        public override void OnClickDown(Vector2 pos) {
            base.OnClickDown(pos);
            // A drag that starts now must query its rect even if it equals the last one
            needsRefresh = true;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var rect = DragRect(start, end);
            HandleDragRect(rect, dragRectColor);
            if (needsRefresh || rect.Min != markedRect.Min || rect.Max != markedRect.Max
                    || Time.unscaledTime >= nextRefreshAt) {
                markedRect = rect;
                markedGoals.Clear();
                hasAdAtCurrentPos = S.Query.AIGoalsInArea.Ask(rect, markedGoals) > 0;
                RefreshShownPriorities();
            }
        }

        public override bool CancelDrag() {
            if (!base.CancelDrag()) { return false; }
            needsRefresh = true;
            return true;
        }

        protected virtual void OnCancel() { }

        private void ResetShownPriorities() {
            ClearShownTaskPriorities();
            markedGoals.Clear();
            centerPos = Pos.Invalid;
            needsRefresh = true;
            hasAdAtCurrentPos = false;
        }

        public override bool Cancel() {
            ResetShownPriorities();
            OnCancel();
            return false;
        }

        public override void Deactivate() {
            ResetShownPriorities();
            OnCancel();
            base.Deactivate();
        }

        protected abstract void OnClickUp(List<AIGoal> goals);
        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            int count;
            Caches.Goals.Clear();
            if (isDragging) {
                count = S.Query.AIGoalsInArea.Ask(
                    DragRect(startPos, position), Caches.Goals);
            } else {
                count = S.Query.AIGoalsAtPos.Ask(
                    EntityUtils.ToPosSafe(position), Caches.Goals);
            }
            if (count > 0) {
                OnClickUp(Caches.Goals);
            }
            ClearDragPositions();
            Caches.Goals.Clear();
            isDragging = false;
            needsRefresh = true;
        }
    }
}
