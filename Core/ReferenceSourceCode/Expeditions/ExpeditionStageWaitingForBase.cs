using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStageWaitingForBase : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.WaitingForBase, () => {
                return new ExpeditionStageWaitingForBase();
            });
        }
        public override bool CanCallBack(Expedition exp) {
            return false;
        }
        public override bool IsReturning => true;

        public override string Title => T.ExpStageWaitingForBase;
        public override int StageType => ExpeditionStageTypeH.WaitingForBase;

        public override string MiniWidgetStatusIcon => IconId.CTime;

        public ExpeditionStageWaitingForBase() {
            MiniWidgetText.Set(T.Waiting);
        }

        private bool hasLoggedPlayerSO;
        private bool hasLoggedShuttleSO;

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            D.Ass(exp.Shuttle != null, "Expedition has null shuttle! {0}", exp);
            var playerSO = PlayerSO(exp.S);
            if (playerSO == null) {
                if (!hasLoggedPlayerSO) {
                    D.Err("Player has no space object! {0}", exp);
                    hasLoggedPlayerSO = true;
                }
                return this;
            }
            var shuttleSO = exp.Shuttle.SpaceObject;
            if (shuttleSO == null) {
                if (!hasLoggedShuttleSO) {
                    D.Err("Shuttle has no space object! Expedition: {0}, Target: {1}. Player parent: {2}, Target parent: {3}",
                        exp, exp.Target, playerSO.Parent, exp.Target?.Parent);
                    hasLoggedShuttleSO = true;
                }
                // Let's recover the shuttle by landing it on the ship instead of being stuck here
                return ExpeditionStages.Find(ExpeditionStageTypeH.LandingOnBase);
            }
            if (playerSO.Parent != shuttleSO.Parent) {
                //D.Warn("Expedition shuttle is in a different parent than player: {0}",
                    //shuttleSO);
                return this;
            }
            if (SOUtils.CanShuttleReachInSpace(exp, playerSO.Location, out _)) {
                var expStageType = ExpeditionStageTypeH.TravelToBase;
                return ExpeditionStages.Find(expStageType);
            }
            return this;
        }
    }
}
