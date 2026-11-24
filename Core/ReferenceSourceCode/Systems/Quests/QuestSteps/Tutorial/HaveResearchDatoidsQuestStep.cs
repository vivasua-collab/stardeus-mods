using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Crafting;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    // Asks for the datoids a research costs, counted against the difficulty-adjusted cost.
    // A fixed count in JSON is only right on one difficulty. Checks stock rather than
    // production so it completes exactly when the research node becomes unlockable.
    public sealed class HaveResearchDatoidsQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HaveResearchDatoidsQuestStep(s, c, l));
        }
        public const string CheckId = "HaveResearchDatoids";
        private readonly Def research;
        private readonly MatType mat;
        private readonly int count;
        public HaveResearchDatoidsQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var researchDef = cfg.GetString(PropertyIdH.Research);
            research = The.Defs.Get(researchDef);
            if (research?.Researchable == null) {
                D.Throw("Incorrectly configured datoids step: {0} [Missing research: {1}]",
                    step, researchDef);
            }
            mat = MatType.Get(cfg.GetString(PropertyIdH.MatType));
            if (mat == null) {
                D.Throw("Incorrectly configured datoids step: {0} [Missing MatType]", step);
            }
            foreach (var cost in research.Researchable.AdjustedDatoidCosts(s.Difficulty)) {
                if (cost.Type == mat) {
                    count = cost.StackSize;
                    break;
                }
            }
            if (count < 1) {
                D.Throw("Research {0} does not cost {1} for quest step: {2}",
                    researchDef, mat, step);
            }
            step.Title = T.QuestStepProduceMany_V2.F(Units.XNum(count), mat.Def.CodexLink);
            var sb = Caches.StringBuilder;
            sb.Clear();
            var craftables = Craftable.FindForDef(mat.Def);
            if (craftables != null) {
                foreach (var cr in craftables) {
                    mat.Def.AddCraftingRecipe(cr, sb);
                }
            }
            var why = T.QuestStepResearchDatoidsTip_V3.F(Texts.LinkToResearch(research),
                Units.XNum(count), mat.Def.CodexLink);
            var how = T.QuestStepProduceTip_V2.F(mat.Def.CodexLink, sb);
            SetDesc($"{why}{GlyphMap.BRHR}{how}");
        }

        private bool IsDone() {
            if (S.Research.Get(research.IdH).InternalUnlocked) {
                return true;
            }
            return S.Research.DatoidCounts.TryGetValue(mat, out var have)
                && have.Value >= count;
        }

        public void Tick(long ticks) {
            if (step.IsActive && IsDone()) {
                CompleteStep();
            }
        }

        public void PreCheck() {
            if (IsDone()) {
                step.SetHidden();
                CompleteStep();
            }
        }
    }
}
