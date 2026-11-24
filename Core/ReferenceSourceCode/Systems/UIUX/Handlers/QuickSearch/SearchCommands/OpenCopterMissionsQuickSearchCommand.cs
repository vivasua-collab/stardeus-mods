using Game.Constants;
using Game.Systems.Copters;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenCopterMissionsQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenCopterMissionsQuickSearchCommand());
        }

        public const string CmdId = "OpenCopterMissions";
        public override string Id => CmdId;
        public override string Icon => IconId.CCopter;
        protected override string NameT => T.CopterMissions;
        public override string DescT => T.CopterMissions;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.Sig.ShowOverlay.Send(CoptersSys.Overlay);
            return true;
        }
    }
}
