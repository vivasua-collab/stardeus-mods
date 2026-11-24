using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Crafting;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class ConfigureCrafterQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ConfigureCrafterQuestStep(s, c, l));
        }
        public const string CheckId = "ConfigureCrafter";
        private readonly Def crafter;
        private readonly Def product;
        public ConfigureCrafterQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {

            var cfg = step.Def.Config;
            var crafterDef = cfg.GetString(PropertyIdH.Crafter);
            var productDef = cfg.GetString(PropertyIdH.Product);
            crafter = The.Defs.Get(crafterDef);
            product = The.Defs.Get(productDef);
            if (crafter == null) {
                D.Err("Crafter is null for quest step: {0} -> {1}",
                    step, crafterDef);
            }
            if (product == null) {
                D.Err("Product is null for quest step: {0} -> {1}",
                    step, productDef);
            }

            step.Title = T.QuestStepConfigureCrafter_V1.F(product.CodexLink);
            var desc = T.QuestStepConfigureCrafterTip_V2.F(
                crafter.CodexLink, product.CodexLink);
            SetDesc(desc);
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                A.S.UIQuery.ShowTutorialPopup.Ask(ConfigureCreafterPage.PageId, 2f);
            }
        }

        public bool CheckCompleted() {
            if (S.AllByDef(crafter, out var crafters)) {
                foreach (var cr in crafters) {
                    if (cr.Value.TryGetComponent<MultiCrafterComp>(out var crafterComp)) {
                        if (crafterComp.Orders == null) { continue; }
                        for (int i = crafterComp.Orders.Count - 1; i >= 0; i--) {
                            CraftingOrder order = crafterComp.Orders[i];
                            if (order.Demand.Product == product
                                    && order.State != CraftingOrderState.ProducedEnough) {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
