using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class MoveShipSearchComand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new MoveShipSearchComand());
        }
        public const string CmdId = "MoveShip";
        public override string Id => CmdId;
        public override string Icon => IconId.CPropulsion;
        protected override string NameT => T.VerbMoveShip;
        public override string DescT => T.ResearchLocalmaneuvering;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            return MoveShipAction.TryLocalManeuver();
        }
    }
}
