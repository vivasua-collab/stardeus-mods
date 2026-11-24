using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStageDone : BaseExpeditionStage {
        public override int StageType => ExpeditionStageTypeH.Done;
        public override bool CanCallBack(Expedition exp) {
            return false;
        }
        public override bool IsReturning => true;

        public override string Title => T.ExpStageDone;

        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.Done,
                () => new ExpeditionStageDone());
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            exp.IsDone = true;
            if (exp.Shuttle.IsLost) { return null; }
            exp.DumpLoot();

            if (exp.QuestObserver == null) {
                var target = exp.InitialTarget ?? exp.Target;
                var questTargets = exp.S.Query.GetQuestStepTargets.Ask(target);
                exp.QuestObserver = questTargets.First?.ExpeditionObserver;
            }

            if (exp.S.Sys.Expedition.Expeditions.Count < 2) {
                var shuttleName = exp.Shuttle?.Name;
                string targetName;
                // we set the player as target when removing POI
                if (exp.Target == exp.S.Universe.Player) {
                    targetName = T.SpaceObjectPlanetpoi;
                } else {
                    targetName = exp.Target.DisplayName;
                }
                var icon = exp.Shuttle != null ?  exp.Shuttle.Entity.Definition.Preview :
                    GraphicsId.ObjectsDevicesShuttle_D;

                exp.S.Sys.Log.AddLine(T.ExpeditionOver_V2.F(shuttleName, targetName));
            }

            return null;
        }
    }
}
