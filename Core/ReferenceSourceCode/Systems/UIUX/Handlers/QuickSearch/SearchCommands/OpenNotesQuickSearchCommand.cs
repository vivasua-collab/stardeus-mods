using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenNotesQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenNotesQuickSearchCommand());
        }

        public const string CmdId = "OpenNotes";
        public override string Id => CmdId;
        public override string Icon => IconId.WPencil;
        protected override string NameT => T.OverlayTipNotes;
        public override string DescT => T.OverlayTipNotes;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleNotes.Ask(true);
            return true;
        }
    }
}
