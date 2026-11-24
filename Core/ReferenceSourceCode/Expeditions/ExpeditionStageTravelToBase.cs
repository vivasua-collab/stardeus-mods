using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStageTravelToBase : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.TravelToBase, () => {
                return new ExpeditionStageTravelToBase();
            });
        }
        public override bool CanCallBack(Expedition exp) {
            return false;
        }
        public override int StageType => ExpeditionStageTypeH.TravelToBase;
        public override bool IsReturning => true;
        public override string Title => T.ExpStageTravelToBase;

        public override string MiniWidgetStatusIcon => IconId.CPropulsion;

        public ExpeditionStageTravelToBase() {
            MiniWidgetText.Set(T.ExpStageTravelToBase);
        }

        // If you are wondering why / how the shuttle moves in space, the logic
        // is handled in ExpeditionSystem.MoveShuttles, here we only check if shuttle
        // has arrived
        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            var player = exp.S.Universe.Player;
            var shuttleSO = exp.Shuttle.SpaceObject;
            int expStageType;
            if (shuttleSO == null) {
                D.Err("We don't have shuttle space object! Assuming shuttle is on the base already? {0}",
                    exp.Shuttle);
                expStageType = ExpeditionStageTypeH.LandingOnBase;
                return ExpeditionStages.Find(expStageType);
            }
            if (player.Parent != shuttleSO.Parent) {
                expStageType = ExpeditionStageTypeH.WaitingForBase;
            } else if (SOUtils.CanShuttleReachInSpace(exp, player.Location,
                    out var hasReached, 1.1f)) {
                if (hasReached) {
                    expStageType = ExpeditionStageTypeH.LandingOnBase;
                } else {
                    return this;
                }
            } else {
                expStageType = ExpeditionStageTypeH.WaitingForBase;
            }
            return ExpeditionStages.Find(expStageType);
        }
    }
}
