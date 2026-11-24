using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Research;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class TerraformQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new TerraformQuestStep(s, c, l));
        }
        public const string CheckId = "Terraform";
        private readonly SpaceObject planet;
        private readonly TerraformType type;
        public TerraformQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var terraformType = config.GetString(PropertyIdH.TerraformType);
            type = TerraformType.Get(terraformType);
            if (type == null) {
                D.Throw("TerraformType {0} not found for {1}", terraformType, this);
            }
            planet = GetQuestVarValByKey<SpaceObject>(PropertyIdH.Var_Target);
            step.Title = T.QuestStepTerraform_V1.F(type.NameT);

            Def terraformerDef = null;
            foreach (var d in The.Defs.Defs) {
                var def = d.Value;
                if (def.IsAbstract) { continue; }
                if (Researchable.TreeNode(def)) { continue; }
                var tcConf = def.ComponentConfigFor(ComponentIdH.Terraformer, false);
                if (tcConf == null) { continue; }
                var tcType = tcConf.GetString(PropertyIdH.Type, null);
                if (type.Id == tcType) {
                    //BINGO!
                    terraformerDef = def;
                    break;
                }
            }

            var defName = terraformerDef?.CodexLink ??
                "[BUG: NO TERRAFORMER FOR THIS TYPE!]";

            SetDesc(T.QuestStepTerraformTip_V2.F(type.NameT, defName));
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            if (planet.TerraformState == null) { return false; }
            if (planet.TerraformState.TryGetValue(type, out var amt)) {
                return amt >= 0.999999f;
            }
            return false;
        }
    }
}
