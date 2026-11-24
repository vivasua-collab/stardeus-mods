using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class TopRightHandler : BaseWidgetHandler<TopRightWidget> {
        // Here rather than on the widget, which outlives a game load
        private readonly PauseNeglect pauseNeglect = new();

        public TopRightHandler(GameState s, TopRightWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.Sig.OnShowFullScreenWidget.AddListener(OnShowFSW);
            s.Sig.AIAddGoal.AddListener(OnAddGoal);
            s.Sig.TilesSelected.AddListener(OnTilesSelected);
            s.Sig.ObjSelected.AddListener(OnObjSelected);
            s.Sig.MultiSelect.AddListener(OnMultiSelect);
            s.Sig.ClickPressed.AddListener(OnClickPressed);
        }

        private void OnShowFSW(bool showing) {
            widget.ShowInFullScreenWidget(showing);
        }

        private bool IsPaused => S.Clock.Speed == ClockSpeed.Stopped;

        // Show Hints and Pause Frame are the opt-outs: a veteran who plans while paused turns
        // off one or the other. And a ring that cannot play right now would wait and play after
        // the player resumed
        private bool CanRingPauseButton() {
            return A.ShowHints
                && The.Prefs.GetBool(Pref.GPauseFrame, PrefH.GPauseFrame, true)
                && widget.CanRingPauseButton();
        }

        private void OnAddGoal(AIGoal goal) {
            // Sent for every goal the game adds, so the cheap check goes first
            if (!IsPaused || !Ready.Game) { return; }
            if (goal.Type != AIGoalType.Colony || !goal.IsByPlayer) { return; }
            pauseNeglect.OrderPlaced(G.Ticks, GameTimer.realtimeSinceStartupAsDouble);
        }

        private void OnTilesSelected(List<Tile> tiles) {
            if (!IsPaused || tiles == null) { return; }
            for (int i = 0; i < tiles.Count; i++) {
                if (IsOrderedThisPause(EntityUtils.ParentObj(tiles[i]))) {
                    RingOnInspect();
                    return;
                }
            }
        }

        private void OnObjSelected(List<Obj> objs) {
            if (!IsPaused || objs == null) { return; }
            for (int i = 0; i < objs.Count; i++) {
                if (IsOrderedThisPause(objs[i])) {
                    RingOnInspect();
                    return;
                }
            }
        }

        private void OnMultiSelect(List<Being> beings, List<Obj> objs, List<Tile> tiles) {
            OnTilesSelected(tiles);
            OnObjSelected(objs);
        }

        // Ticks are frozen while paused, so a goal created on the current tick was ordered
        // during this pause and has not had a single tick to progress
        private static bool IsOrderedThisPause(Entity entity) {
            var goal = entity?.Goal;
            return goal != null && goal.Type == AIGoalType.Colony && goal.IsByPlayer
                && goal.CreatedAt == G.Ticks;
        }

        private void RingOnInspect() {
            if (!CanRingPauseButton()) { return; }
            if (pauseNeglect.ShouldFocusOnInspect(GameTimer.realtimeSinceStartupAsDouble)) {
                widget.RingPauseButton();
            }
        }

        // Only clicks around the world in free select count: a click on the HUD may be the one
        // on Play, and a click with a build tool places the next order of a plan
        private void OnClickPressed() {
            if (!IsPaused || !S.Toolbox.IsFreeSelect) { return; }
            if (The.Bindings.Mouse.IsOverUI) { return; }
            if (!CanRingPauseButton()) { return; }
            if (pauseNeglect.ShouldFocusOnClick(G.Ticks,
                    GameTimer.realtimeSinceStartupAsDouble)) {
                widget.RingPauseButton();
            }
        }
    }
}
