using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Visuals;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Plays the focus ring from a selected entity onto its actions in the bottom bar when
    // EntityActionsHint asks for it. Owned by BottomBarHandler rather than the widget, which
    // outlives a game load. See Docs/Architecture/Systems/EntityActionsHint.md.
    public sealed class EntityActionsHintHandler {
        // Lets a test move the hint's real-time clock forward
        public static double DebugClockOffset;
        private readonly GameState S;
        private readonly BottomBarWidget bar;
        private readonly EntityActionsHint hint = new();
        // The single entity whose actions the bar shows
        private Entity shown;
        // Its actions are on their way in: the swap lands them halfway through its slide
        private bool isSettling;

        [ExecuteOnReload]
        private static void CleanupStatics() {
            DebugClockOffset = 0;
        }

        public EntityActionsHintHandler(GameState s, BottomBarWidget bar) {
            S = s;
            this.bar = bar;
            s.UISig.SelectionActionsShown.AddListener(OnShown);
            bar.ActionPressed = OnActionPressed;
        }

        private static double Now => GameTimer.realtimeSinceStartupAsDouble + DebugClockOffset;
        private RectTransform Target => bar.TempGroup.Rect;
        // Global: once a player has used the actions, no save or launch teaches them again
        private static int Uses => The.Prefs.GetInt(Pref.GEntityActionsUsed,
            PrefH.GEntityActionsUsed, 0);

        private void OnShown(Entity entity) {
            // The bar built the same selection again: nothing new to point at
            if (entity != null && entity == shown) { return; }
            // A ring on its way points at the old actions
            UIFocusRing.Cancel(Target);
            if (shown != null) {
                hint.Clear(Now, !S.Query.IsAnythingSelected.Ask());
            }
            shown = entity;
            isSettling = entity != null;
        }

        private void OnActionPressed() {
            // The ring did its job
            UIFocusRing.Cancel(Target);
            hint.ActionUsed();
            var uses = Uses;
            if (uses < EntityActionsHint.LearnedAfterUses) {
                The.Prefs.SetInt(Pref.GEntityActionsUsed, uses + 1);
            }
        }

        public void Update() {
            if (shown == null || !Ready.Game) { return; }
            if (!shown.IsActive) {
                // Removed while selected, the selection clears itself right after
                hint.Clear(Now, false);
                shown = null;
                isSettling = false;
                return;
            }
            if (bar.IsAnimating) { return; }
            var now = Now;
            if (isSettling) {
                isSettling = false;
                if (hint.Select(shown.Id, now, S.SessionTime, Uses, CanRing())) {
                    Ring();
                }
                return;
            }
            if (hint.IsKeptDue(now) && CanRing() && hint.Keep(now, S.SessionTime, Uses)) {
                Ring();
            }
        }

        // Show Hints is the opt-out. A ring that cannot play right now would wait behind the
        // covering screen and then point at a selection that may have changed
        private bool CanRing() {
            return A.ShowHints && UIFocusRing.CanShowNow(Target);
        }

        // In the color of the selection outline it lifts off, but opaque: the outline's alpha
        // suits its world effect, and a thin line at half alpha would fade into the floor
        private static Color RingColor(Entity entity) {
            var color = SelectionVFX.OutlineColor(entity);
            color.a = 1f;
            return color;
        }

        private void Ring() {
            UIFocusRing.RequestFrom(Target, shown, RingColor(shown));
        }
    }
}
