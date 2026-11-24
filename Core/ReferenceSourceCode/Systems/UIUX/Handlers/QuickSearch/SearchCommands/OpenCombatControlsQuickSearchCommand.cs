using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenCombatControlsQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenCombatControlsQuickSearchCommand());
        }

        public const string CmdId = "OpenCombatControls";
        public override string Id => CmdId;
        public override string Icon => IconId.CCombat;
        protected override string NameT => T.CombatControls;
        public override string DescT => T.CombatReaction;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleCombatControls.Ask(true);
            return true;
        }
    }
}
