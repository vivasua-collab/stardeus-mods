using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class KillNPCShipQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new KillNPCShipQuestStep(s, c, l));
        }
        public const string CheckId = "KillNPCShip";
        public override bool SaveLoadTexts => true;
        private bool isSubscribed;
        private NPCShip targetShip;
        private readonly QuestVar shipVar;

        public KillNPCShipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            shipVar = GetQuestVarByKey(PropertyIdH.Var_Ship);
            targetShip = shipVar.GetValue<NPCShip>();

            SetTexts(s, step, isLoading);

            if (isLoading && step.IsActive) {
                Subscribe();
                CheckIfAlreadyKilled();
            }
        }

        private void SetTexts(GameState s, QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }

            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T();
            } else {
                step.Title = $"{T.Destroy}: {targetShip.NameLinked}";
            }

            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null && targetShip != null) {
                step.Desc = descKey.T(targetShip.NameLinked);
            } else if (targetShip != null) {
                step.Desc = $"{T.NounTarget}: {targetShip.NameLinked}";
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (!CheckIfAlreadyKilled()) {
                    Subscribe();
                }
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ShipDefeatedInCombat.AddListener(OnShipDefeated);
            }
        }

        private bool CheckIfAlreadyKilled() {
            // Refresh ship reference in case it changed
            targetShip = shipVar.GetValue<NPCShip>();

            if (targetShip != null && targetShip.IsDerelict) {
                CompleteStep();
                return true;
            }
            return false;
        }

        private void OnShipDefeated(NPCShip attacker, NPCShip defeated) {
            if (defeated == targetShip) {
                CompleteStep();
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ShipDefeatedInCombat.RemoveListener(OnShipDefeated);
            }
        }
    }
}
