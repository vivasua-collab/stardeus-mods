using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ConnectFirstConnectorQuestStep : PowerTutorialQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ConnectFirstConnectorQuestStep(s, c, l));
        }
        public const string CheckId = "ConnectFirstConnector";
        private readonly Def def;
        public ConnectFirstConnectorQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Misconfigured step, no def by id: {0} | {1}", buildDef, step);
            }
            Init(isLoading);
        }

        protected override void UpdateTexts(bool onActive) {
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            string connText;
            string cpuText = Texts.LinkToTile(cpu.Tile);
            string connToolText = Texts.TitleWithShortcutGlyph(
                The.Toolbox.ConnectTool.NameT,
                ActionType.ShortcutEnergyConnect);
            if (onActive) {
                // we expect to have a connector within the range of the CPU
                var conn = S.Components.FindFirstMatching<ElectricNodeComp>(
                    n => n.Tile.Definition == def && n.IsConnectedTo(cpu.Tile.ENode));
                conn ??= S.Components.FindFirstMatching<ElectricNodeComp>(
                    n => n.Tile.Definition == def && n.Node.NumConnections == 0
                        && n.CanConnectTo(cpu.Tile.ENode, out _));
                connText = conn == null ? def.CodexLink : Texts.LinkToTile(conn.Tile);
            } else {
                connText = def.CodexLink;
            }
            step.Title = T.QuestStepConnectToSpecific_V3.F(
                connToolText, connText, cpuText);
        }

        protected override bool IsDone() {
            return PowerTutorialMatch.HasConnectorLinkedToCpu(S, def);
        }

        protected override void OnDone() {
            // So that the next step's pre-check would work immediately
            A.Grid.RebuildGrid(true);
        }
    }
}
