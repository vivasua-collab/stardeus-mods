using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenPaintQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenPaintQuickSearchCommand());
        }

        public const string CmdId = "OpenPaint";
        public override string Id => CmdId;
        public override string Icon => IconId.CPaint;
        protected override string NameT => T.OverlayTipTint;
        public override string DescT => T.OverlayTipTint;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => A.S.BB.Get(BBKeyH.PaintUnlocked, false) || A.InstantBuild;

        public override bool Execute() {
            A.S.Sig.ToggleOverlayById.Send(OverlayId.Tint);
            return true;
        }
    }
}
