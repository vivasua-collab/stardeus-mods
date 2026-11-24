using Game.Constants;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenSurgeriesSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenSurgeriesSearchCommand());
        }

        public const string CmdId = "OpenSurgeries";
        public override string Id => CmdId;
        public override string Icon => IconId.CHealth;
        public override ActionType Shortcut => ActionType.OpenSurgeries;
        protected override string NameT => T.Health;
        public override string DescT => T.Health;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleSurgeries.Ask(true);
            return true;
        }
    }
}
