using System;
using Game.CodeGen;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class InstallUpgradeQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new InstallUpgradeQuestStep(s, c, l));
        }
        public const string StepId = "InstallUpgrade";
        private bool isSubscribed;
        private readonly Upgrade upgrade;
        private readonly Def deviceDef;
        private readonly bool allowAnywhere;
        public InstallUpgradeQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            allowAnywhere = config.GetBool(PropertyIdH.AllowAnyDevice, false);
            var upgradeId = config.GetString(PropertyIdH.UpgradeId);
            upgrade = UpgradeRegistry.Get(upgradeId);
            if (upgrade == null) {
                D.Throw("Upgrade not available by id {0} for {1}", upgradeId, this);
            }
            var defId = config.GetString(PropertyIdH.TargetDevice);
            deviceDef = The.Defs.Get(defId);
            if (deviceDef == null) {
                D.Throw("TargetDevice def not found by id: {0} for {1}", defId, this);
            }
            var upgradeName = MatType.Get(upgrade.MatTypeId)?.Def.CodexLink ?? upgrade.Title;
            step.Title = T.QuestStepInstallUpgradeTitle_V1.F(upgradeName);
            step.Desc = T.QuestStepInstallUpgradeDesc_V2.F(upgradeName, deviceDef.CodexLink);
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (CheckInstalled()) {
                    CompleteStep();
                    return;
                }
                Subscribe();
            }
        }

        private bool CheckInstalled() {
            var upId = upgrade.UpgradeId;
            foreach (var up in S.Sys.Upg.Upgraded) {
                for (int i = up.NumSlots - 1; i >= 0; i--) {
                    if (up.Upgrades[i] == upId) {
                        if (allowAnywhere || up.Tile.Definition == deviceDef) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.TileUpgradeChanged.AddListener(OnUpgradeChange);
            }
        }

        private void OnUpgradeChange(Upgrade up, Tile tile, bool added) {
            if (added && up == upgrade) {
                if (allowAnywhere || tile.Definition == deviceDef) {
                    CompleteStep();
                }
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.TileUpgradeChanged.RemoveListener(OnUpgradeChange);
            }
        }

    }
}
