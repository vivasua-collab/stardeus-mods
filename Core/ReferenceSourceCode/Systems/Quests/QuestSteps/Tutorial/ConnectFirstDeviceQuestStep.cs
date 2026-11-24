using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ConnectFirstDeviceQuestStep : PowerTutorialQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ConnectFirstDeviceQuestStep(s, c, l));
        }
        public const string CheckId = "ConnectFirstDevice";
        private readonly Def def;
        private readonly Def connDef;
        public ConnectFirstDeviceQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Misconfigured step, no def by id: {0} | {1}", buildDef, step);
            }
            var connDefId = cfg.GetString(PropertyIdH.ConnectorDefinition);
            connDef = The.Defs.Get(connDefId);
            if (connDef == null) {
                D.Err("Connector Def is null for quest step: {0} -> {1}", step, connDefId);
            }
            Init(isLoading);
        }

        protected override void UpdateTexts(bool onActive) {
            string connText;
            string devText;
            string connToolText = Texts.TitleWithShortcutGlyph(
                The.Toolbox.ConnectTool.NameT,
                ActionType.ShortcutEnergyConnect);
            if (onActive) {
                // No device is legitimate here: the one BuildFirstDevice counted may have
                // been deconstructed since, and the Codex link then says what to build
                PowerTutorialMatch.FindLinkTargets(S, def, connDef, out var dev, out var conn);
                connText = conn == null ? connDef.CodexLink : Texts.LinkToTile(conn.Tile);
                devText = dev == null ? def.CodexLink : Texts.LinkToTile(dev.Tile);
            } else {
                connText = connDef.CodexLink;
                devText = def.CodexLink;
            }
            step.Title = T.QuestStepConnectToSpecific_V3.F(
                connToolText, devText, connText);
        }

        // On the CPU grid, not merely linked to a connector: a pair on another island
        // (survivor area, wreck) powers nothing the player owns, and ConfigureMatterReactor
        // after this step needs the reactor on the CPU grid anyway
        protected override bool IsDone() {
            return PowerTutorialMatch.HasDeviceOnCpuGrid(S, def);
        }
    }
}
