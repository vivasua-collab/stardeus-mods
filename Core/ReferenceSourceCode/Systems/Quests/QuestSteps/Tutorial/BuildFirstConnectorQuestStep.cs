using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class BuildFirstConnectorQuestStep : PowerTutorialQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new BuildFirstConnectorQuestStep(s, c, l));
        }
        public const string CheckId = "BuildFirstConnector";
        private readonly Def def;
        public BuildFirstConnectorQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Err("Def is null for quest step: {0} -> {1}", step, buildDef);
            }
            var defLink = def.CodexLink;

            var cpu = s.Components.FindFirstByType<ShipComputerComp>();
            string cpuLink;
            if (cpu == null) {
                D.Throw("No ship computer found in state for BuildFirstConnectorQuestStep");
            }
            cpuLink = Texts.LinkToTile(cpu.Tile);

            step.Title = T.QuestStepBuildCloseTo_V2.F(defLink, cpuLink);
            Init(isLoading);
        }

        protected override bool IsDone() {
            return PowerTutorialMatch.HasConnectorForCpu(S, def);
        }

        protected override void OnDone() {
            // Not from the quest creation pre-check, where the step is not active yet and
            // the quest itself has not been shown
            if (step.IsActive) {
                A.S.UIQuery.ShowTutorialPopup.Ask(ElectricityConnectPage.PageId, 2f);
            }
        }
    }
}
