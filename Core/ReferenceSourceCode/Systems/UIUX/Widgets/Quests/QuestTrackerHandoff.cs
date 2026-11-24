using Game.CodeGen;
using Game.Data;
using Game.Systems.Quests;

namespace Game.Systems.UIUX {
    // The Quests window is for choosing a quest, the HUD quest tracker is for doing it.
    // Players on small screens kept the window open while playing because it repeated the
    // tracker's objectives and nothing pointed at the tracker, so the hand-off moments
    // (accepting from the offer popup, pressing Show in the details) ring the tracker to show
    // where the quest went. Nothing rings while the Quests window is open (see UIFocusRing).
    public static class QuestTrackerHandoff {
        private static QuestWidget Tracker =>
            (QuestWidget)Registries.UIWidgets.FastGet(UIWidgetIdH.Quest);

        public static bool IsTrackerVisible() {
            var tracker = Tracker;
            return tracker != null && tracker.IsVisibleToPlayer;
        }

        // Only when the tracker shows this quest: accepting while another quest is tracked
        // keeps the old one there, and ringing it would point at the wrong quest
        public static void FocusTracker(Quest q) {
            if (!A.S.Query.IsTrackedQuest.Ask(q)) { return; }
            var tracker = Tracker;
            if (tracker == null) { return; }
            UIFocusRing.Request(tracker.Rect, waitForQuestsWindow: true);
        }
    }
}
