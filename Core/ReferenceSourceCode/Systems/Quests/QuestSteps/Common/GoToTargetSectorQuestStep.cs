using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class GoToTargetSectorQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new GoToTargetSectorQuestStep(s, c, l));
        }
        public const string CheckId = "GoToTargetSector";
        public const string Key_SectorId = "SectorId";
        public override bool SaveLoadTexts => true;
        private readonly SpaceSector sector;
        public GoToTargetSectorQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (isLoading) {
                var sectorId = step.Data.Data.GetInt(DataKey(Key_SectorId), 0);
                if (sectorId == 0) {
                    D.Err("Didn't save sector id for step {0}", this);
                } else {
                    sector = S.Universe.Find(sectorId)?.Sector;
                }
            } else {
                var so = GetQuestVarValByKey<SpaceObject>(PropertyIdH.Var_Target);
                if (so == null) {
                    D.Throw("Couldn't get so for {0}", step);
                }
                sector = SOUtils.SectorFor(so);
                var d = step.Data.GetOrCreateData();
                d.SetInt(DataKey(Key_SectorId), sector.SO.Id);
            }
            SetTexts(step, isLoading);
        }

        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var path = sector.SO.ParentPath(fromPlayer: false);
            // var soLink = sector.SO.StarmapLink;

            step.Title = T.QuestStepReachSoType_V1.F(path);
            var desc = T.QuestStepReachSoTypeTip_V1.F(path);
            SetDesc(desc);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (config.HasKey(PropertyIdH.MakeHabitable)
                        && config.GetBool(PropertyIdH.MakeHabitable)) {
                    sector.SO.Flags |= SOFlags.Habitable;
                }
            }
        }

        public void Tick(long ticks) {
            if (!step.IsActive || sector == null || A.IsInHyperspace) { return; }
            if (A.PlayerSector.Sector != sector) { return; }
            OnArrived();
        }

        private void OnArrived() {
            CompleteStep();
            var msgKey = config.GetString(PropertyIdH.MessageOnReach, null);
            if (msgKey == null) { return; }
            var msg = msgKey.TOrNull();
            if (msg == null) {
                D.Err("Missing translation by key {0} for MessageOnReach in quest step {1}",
                    msgKey, this);
            } else {
                The.Dialog.ShowSimple(config.GetString(
                    PropertyIdH.MessageIcon, IconId.CComputing), msg);
            }
        }
    }
}
