using System;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public abstract class DevMenuTab {
        protected GameState S;

        protected DevMenuTab(GameState s) {
            S = s;
        }

        public abstract string TabId { get; }
        public abstract string TabName { get; }

        /// <summary>
        /// Populate the tab's content area. Called each time the tab is shown or reloaded.
        /// subPanel is available for tabs that need a detail panel (StoryEvents, Drama).
        /// reload can be called to rebuild the current tab in place.
        /// </summary>
        public abstract void LoadContent(Lifetime life, WGroup group, ref int gIdx,
            SubPanelWidget subPanel, Action reload);

        public virtual void OnClose() { }
    }
}
