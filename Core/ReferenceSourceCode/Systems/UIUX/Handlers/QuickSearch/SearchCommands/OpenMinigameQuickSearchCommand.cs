using Game.Components.Upgrades;
using Game.Constants;
using Game.UI;

namespace Game.Systems.UIUX {
    public abstract class OpenMinigameQuickSearchCommand : BaseSearchCommand {
        public abstract string MinigameId { get; }
        protected abstract string GameNameT { get; }

        protected override string NameT => GameNameT;
        public override string DescT => GameNameT;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => GameCartridgeUpgrade.IsUnlocked(MinigameId);

        public override bool Execute() {
            if (!IsEnabled) {
                A.S.Sig.ShowErrorMessage.Send($"{T.NotAvailable}: {GameNameT}");
                return false;
            }
            A.S.UIQuery.ToggleMinigame.Ask(MinigameId);
            return true;
        }
    }
}
