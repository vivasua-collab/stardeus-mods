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
    public sealed class ProduceQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ProduceQuestStep(s, c, l));
        }
        public const string CheckId = "Produce";
        public const string Key_Cnt = "cnt";
        private readonly Def product;
        private readonly int count;
        private bool isSubscribed;
        // Not saved: after a load it counts the recent production again, which max() keeps
        // from counting twice
        private bool countedRecent;
        public ProduceQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var productDef = cfg.GetString(PropertyIdH.Product);
            count = cfg.GetInt(PropertyIdH.Count, 1);
            product = The.Defs.Get(productDef);
            if (product == null) {
                D.Throw("Product is null for quest step: {0} -> {1}",
                    step, productDef);
            }
            if (count < 1) {
                D.Throw("Quest build step has count < 1 in config: {0}", step);
                count = 1;
            }
            if (count == 1) {
                step.Title = T.QuestStepProduceOne_V1.F(product.CodexLink);
            } else {
                step.Title = T.QuestStepProduceMany_V2.F(Units.XNum(count), product.CodexLink);
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            var craftables = Craftable.FindForDef(product);
            if (craftables != null) {
                foreach (var cr in craftables) {
                    product.AddCraftingRecipe(cr, sb);
                }
            }
            var desc = T.QuestStepProduceTip_V2.F(product.CodexLink, sb);
            SetDesc(desc);
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        // What the colony already produces counts: a player who cooked before the quest came
        // waited for a meal nobody was making (STA-1764). The last day is the "+N/day" the
        // production overview shows. Done on a tick, not on activation, so it also runs once
        // after a load, when the statistics are in.
        public void Tick(long ticks) {
            if (countedRecent || !step.IsActive) { return; }
            countedRecent = true;
            var recent = S.Query.DefProductionRate.Ask(product, 24);
            if (recent <= 0) { return; }
            var key = step.Def.DataKey(Key_Cnt);
            var stored = step.Data.Data?.GetInt(key, 0) ?? 0;
            // max, not sum: after a load the stored count and the last day can overlap
            var produced = Math.Max(stored, recent);
            if (produced >= count) {
                step.SetState(QuestStepState.Completed);
                return;
            }
            step.Data.GetOrCreateData().SetInt(key, produced);
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.CrafterProducedItem.AddListener(OnObjProduced);
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.CrafterProducedItem.RemoveListener(OnObjProduced);
            }
        }
        private void OnObjProduced(CraftingOrder order, int incr) {
            if (order.Demand.Product != product) { return; }
            if (incr == 0) { return; }
            if (incr >= count) {
                step.SetState(QuestStepState.Completed);
                return;
            }
            var key = step.Def.DataKey(Key_Cnt);
            var producedCount = step.Data.Data?.GetInt(key, 0) ?? 0;
            producedCount += incr;
            if (producedCount >= count) {
                step.SetState(QuestStepState.Completed);
                return;
            }
            var d = step.Data.GetOrCreateData();
            d.SetInt(key, producedCount);
        }
    }
}
