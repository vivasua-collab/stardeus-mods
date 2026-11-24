using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStagePrepForReturn : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.PrepForReturn, () => {
                return new ExpeditionStagePrepForReturn();
            });
        }
        public override int StageType => ExpeditionStageTypeH.PrepForReturn;
        public override bool CanCallBack(Expedition exp) {
            return false;
        }
        public override bool IsReturning => true;
        public override string Title => T.ExpStagePrepReturn;

        public override string MiniWidgetStatusIcon => IconId.CPropulsion;

        public ExpeditionStagePrepForReturn() {
            MiniWidgetText.Set(T.ExpStageTravelToBase);
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            exp.EndTick = ticks;
            // if (SOUtils.CanShuttleReachInSpace(exp, exp.S.Universe.Player.Location, out _)) {
            var expStageType = ExpeditionStageTypeH.TravelToBase;
            return ExpeditionStages.Find(expStageType);
            // }
            // return ExpeditionStages.Find(ExpeditionStageTypeH.WaitingForBase);
        }
    }
}
