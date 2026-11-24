using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Systems.UIUX {
    // Shows the Scanning For Resources tutorial the first time the player runs short of a
    // material that space deposits hold, while a radar is working: the lesson is "how do I
    // find more of this", so it lands when the player is asking exactly that. It used to fire
    // when a radar connected, which is also when the BuildARadar quest completes and the
    // expedition quest is offered, so it interrupted reading the new quest.
    public sealed class ScanningTutorialTrigger {
        private readonly GameState S;
        // One attempt per session: the popup queue rejects a page that was already seen or
        // learned (it remembers across saves), so a later deficit has nothing left to do
        private bool isDone;

        public ScanningTutorialTrigger(GameState s) {
            S = s;
            s.Sig.MatDeficitChanged.AddListener(OnDeficitChanged);
        }

        private void OnDeficitChanged(MatDeficitEventHolder holder, bool hasDeficit) {
            if (isDone || !hasDeficit) { return; }
            var mat = holder?.DeficitType;
            if (mat == null) { return; }
            if (!S.Sys.Sec.IsRadarActive()) { return; }
            if (!HasSpaceDeposits(mat)) { return; }
            isDone = true;
            S.UIQuery.ShowTutorialPopup.Ask(ScanningForResourcesPage.PageId, 3f);
        }

        private bool HasSpaceDeposits(MatType mat) {
            var map = S.Query.GetSpaceMap.Ask();
            if (map?.Regions == null) { return false; }
            foreach (var reg in map.Regions) {
                if (reg.Biome != null && reg.Biome.HasDepositsFor(mat)) {
                    return true;
                }
            }
            return false;
        }
    }
}
