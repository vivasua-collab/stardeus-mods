using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Electricity;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class BuildFirstDeviceQuestStep : PowerTutorialQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new BuildFirstDeviceQuestStep(s, c, l));
        }
        public const string CheckId = "BuildFirstDevice";
        private readonly Def def;
        private readonly Def connDef;
        public BuildFirstDeviceQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Err("Def is null for quest step: {0} -> {1}", step, buildDef);
            }
            var connDefId = cfg.GetString(PropertyIdH.ConnectorDefinition);
            connDef = The.Defs.Get(connDefId);
            if (connDef == null) {
                D.Err("Connector Def is null for quest step: {0} -> {1}", step, connDefId);
            }
            step.TitleInactive = T.TutStepPlaceTile_V1.F(def.CodexLink);
            Init(isLoading);
        }



        protected override void UpdateTexts(bool onActive) {
            string connText = null;
            if (onActive) {
                var conn = PowerTutorialMatch.FindConnectorOnCpuGrid(S, connDef);
                if (conn != null) {
                    connText = Texts.LinkToTile(conn.Tile);
                }
            }
            connText ??= connDef.CodexLink;
            step.Title = T.QuestStepBuildCloseTo_V2.F(def.CodexLink, connText);
        }

        protected override bool IsDone() {
            return PowerTutorialMatch.HasBuiltDevice(S, def, connDef);
        }
    }
}
