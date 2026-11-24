using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class NukeSectorQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new NukeSectorQuestStep(s, c, l));
        }
        public const string CheckId = "NukeSector";
        private const string Key_SectorId = "SectorId";
        public override bool SaveLoadTexts => true;
        private readonly SpaceSector sector;
        private bool isSubscribed;

        public NukeSectorQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (isLoading) {
                var sectorId = step.Data.Data.GetInt(DataKey(Key_SectorId), 0);
                if (sectorId == 0) {
                    D.Err("Didn't save sector id for step {0}", this);
                } else {
                    sector = S.Universe.Find(sectorId)?.Sector;
                }
            } else {
                var target = GetQuestVarValByKey<SpaceObject>(PropertyIdH.Var_Target);
                sector = SOUtils.SectorFor(target);
                if (sector == null) {
                    D.Throw("NukeSectorQuestStep requires IQuestVar<SpaceObject>, but {0} is not that! {1}",
                        GetQuestVarByKey(PropertyIdH.Var_Target), this);
                }
                var d = step.Data.GetOrCreateData();
                d.SetInt(DataKey(Key_SectorId), sector.SO.Id);
            }
            SetTexts(step, isLoading);
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }
        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var path = sector.SO.ParentPath(fromPlayer: false);
            var soLink = sector.SO.StarmapLink;

            step.Title = T.QuestStepNukeSectorTitle_V1.F(soLink);
            var nukeDef = The.Defs.FastGet(DefIdsH.ObjectsDevicesSectorNuke);
            var desc = T.QuestStepNukeSectorDesc_V2.F(nukeDef.CodexLink, path);
            SetDesc(desc);
        }
        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (!CheckIfAlreadyNuked()) {
                    Subscribe();
                }
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.SpaceObjectNuked.AddListener(OnSONuked);
            }
        }

        private bool CheckIfAlreadyNuked() {
            if (sector.SO.Type.IdH == SpaceObjectTypeIdH.Space) {
                CompleteStep();
                return true;
            }
            return false;
        }

        private void OnSONuked(SpaceObject so) {
            if (so == sector.SO) {
                CompleteStep();
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.SpaceObjectNuked.RemoveListener(OnSONuked);
            }
        }
    }
}
