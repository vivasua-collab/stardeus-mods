using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStageTravelToDest : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.TravelToDest, () => {
                return new ExpeditionStageTravelToDest();
            });
        }
        public override int StageType => ExpeditionStageTypeH.TravelToDest;
        public override string Title => T.ExpStageTravelToDest;

        public override string MiniWidgetStatusIcon => IconId.CPropulsion;

        public ExpeditionStageTravelToDest() {
            MiniWidgetText.Set(T.VerbFlying);
        }

        // If you are wondering why / how the shuttle moves in space, the logic
        // is handled in ExpeditionSystem.MoveShuttles, here we only check if shuttle
        // has arrived
        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            var shuttleSO = exp.Shuttle.SpaceObject;
            if (shuttleSO == null) {
                D.Err("Expedition has no shuttle SO! {0}", exp);
            }
            int expStageType;
            var landSite = exp.Target.Location + exp.TargetOffset;
            if (SOUtils.CanShuttleReachInSpace(exp, landSite, out var hasReached, 1.1f)) {
                if (!hasReached) {
                    return this;
                }
            } else {
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.AbortingExpeditionTargetTooFar_V1.F(exp.Target.StarmapLink));
                expStageType = ExpeditionStageTypeH.WaitingForBase;
                exp.MiningOrders.IsAutoUnload = false;
                return ExpeditionStages.Find(expStageType);
            }

            var questTargets = exp.S.Query.GetQuestStepTargets.Ask(exp.Target);
            exp.QuestObserver = questTargets.First?.ExpeditionObserver;

            if (questTargets.Length > 0 && questTargets.First.ExpeditionHandler != null) {
                expStageType = ExpeditionStageTypeH.Quest;
            // We have reached the target
            } else if (exp.Target.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                expStageType = ExpeditionStageTypeH.IdentifyResource;
            } else if (exp.Target.Type.IdH == SpaceObjectTypeIdH.Resource) {
                // let's see if we can mine the resource here
                // TODO are we installing some hardware?
                expStageType = ExpeditionStageTypeH.MineResource;
            } else if (exp.Target.Type.IdH == SpaceObjectTypeIdH.Anomaly) {
                expStageType = ExpeditionStageTypeH.Anomaly;
            } else if (exp.Target.Type.IdH == SpaceObjectTypeIdH.PlanetPOI) {
                expStageType = ExpeditionStageTypeH.PlanetPOI;
            } else {
                // If the expedition was launched before v0.8, it may attempt to go to a planet
                // and this won't work anymore
                D.ErrSoft("Unknown target type for expedition: {0}",
                    exp.Target.TypeName);
                expStageType = ExpeditionStageTypeH.PrepForReturn;
            }
            return ExpeditionStages.Find(expStageType);
        }
    }
}
