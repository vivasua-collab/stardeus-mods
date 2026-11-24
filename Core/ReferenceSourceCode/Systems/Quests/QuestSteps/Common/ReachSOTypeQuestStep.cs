using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ReachSOTypeQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ReachSOTypeQuestStep(s, c, l));
        }
        public const string CheckId = "ReachSOType";
        private const string Key_StartId = "StartId";
        private readonly SpaceObjectType destinationType;
        private readonly HashSet<int> compatibleTypes;
        private bool checkedStart;
        private SpaceObject startObj;
        public ReachSOTypeQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var destinationTypeId = config.GetString(PropertyIdH.SOType);
            destinationType = SpaceObjectType.Find(destinationTypeId);
            if (destinationType == null) {
                D.Throw("Incorrectly configured ReachSOType step: {0}. [SOType unknown: {1}]",
                    step, destinationTypeId);
            }
            var compatibleTypeIds = config.GetStringSet(PropertyIdH.CompatibleSOTypes, null);
            if (!Arrays.IsEmpty(compatibleTypeIds)) {
                compatibleTypes = new HashSet<int>();
                foreach (var t in compatibleTypeIds) {
                    var type = SpaceObjectType.Find(t);
                    if (type != null) {
                        compatibleTypes.Add(type.IdH);
                    } else {
                        D.Throw("Incorrectly configured quest step: {0}. Unknown SOType in CompatibleSOTypes: {1}",
                            step, t);
                    }
                }
            }
            step.Title = T.QuestStepReachSoType_V1.F(destinationType.NameT);
            var desc = T.QuestStepReachSoTypeTip_V1.F(destinationType.NameT);
            SetDesc(desc);
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        private bool CheckCompleted() {
            var near = S.Sys.Ship.Nearby;
            if (!checkedStart) {
                checkedStart = true;
                var data = step.Data.GetOrCreateData();
                var startId = data.GetInt(DataKey(Key_StartId));
                if (startId == 0) {
                    // we have never set it!
                    for (int i = near.Count - 1; i >= 0; i--) {
                        if (near[i].Type == destinationType) {
                            startObj = near[i];
                            break;
                        }
                    }
                    data.SetInt(DataKey(Key_StartId), startObj?.Id ?? -1);
                } else if (startId > 0) {
                    startObj = S.Universe.Find(startId);
                }
            }
            for (int i = near.Count - 1; i >= 0; i--) {
                var nearSO = near[i];
                if (nearSO == startObj) { continue; }
                if (compatibleTypes != null) {
                    if (compatibleTypes.Contains(nearSO.Type.IdH)) {
                        return true;
                    }
                } else {
                    if (nearSO.Type == destinationType) {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
