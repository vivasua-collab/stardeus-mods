using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenDiplomacySearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenDiplomacySearchCommand());
        }

        public const string CmdId = "OpenDiplomacy";
        public override string Id => CmdId;
        public override string Icon => IconId.CScale;
        protected override string NameT => T.NounDiplomacy;
        public override string DescT => T.NounDiplomacy;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleDiplomacy.Ask(true);
            return true;
        }
    }
}
