using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class AssignBattleStationQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new AssignBattleStationQuestStep(s, c, l));
        }
        public const string CheckId = "AssignBattleStation";

        public AssignBattleStationQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (!string.IsNullOrEmpty(titleKey)) {
                step.Title = titleKey.T();
            }
            SetDesc(T.QuestMakeshipcombatreadyAssignBattlestationTip_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShredderCannon, T.NounShredderCannonsPlural)));
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                S.UIQuery.ShowTutorialPopup.Ask(BattleStationsPage.PageId, 3f);
            }
        }


        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return S.Components.CountMatching<BattleStationComp>(
                bs => bs.AssignedWeapons.Value > 0, 1) > 0;
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
