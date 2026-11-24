using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Systems.UIUX {
    // Shows the Drafting tutorial when the first hostile combat of a game starts. A playtester
    // new to colony sims got lost in their first fight: nothing told them that colonists can be
    // drafted and ordered around.
    public sealed class DraftingTutorialTrigger {
        private readonly GameState S;
        // One attempt per session: the popup queue rejects a page that was already seen or
        // learned (it remembers across saves), and the signal repeats for every raid army
        private bool isDone;

        public DraftingTutorialTrigger(GameState s) {
            S = s;
            s.Sig.CombatChanged.AddListener(OnCombatChanged);
        }

        private void OnCombatChanged(bool started, CombatFlags flags) {
            if (isDone || !started || (flags & CombatFlags.Hostile) == 0) { return; }
            isDone = true;
            S.UIQuery.ShowTutorialPopup.Ask(CombatDraftingPage.PageId, 3f);
        }
    }
}
