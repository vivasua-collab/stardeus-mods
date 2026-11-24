using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionCrawlerAttached : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionCrawlerAttached());
        }
        public override Priority Importance => Priority.Second;

        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.TickAfterDeath;
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            ConditionState.CreateFor(The.R.State,
                ConditionIdH.CrawlerInfected, cond.Owner, Severity.Irrelevant, true);
            var b = cond.Owner.Body;
            AddWoundWithChance(BodyPartSlotIdH.Head, 1f, b, ticks, Severity.Mild, Severity.Moderate, Severity.Severe);
            AddWoundWithChance(BodyPartSlotIdH.Jaw, 0.5f, b, ticks, Severity.Mild, Severity.Moderate);
            AddWoundWithChance(BodyPartSlotIdH.Eye_L, 0.05f, b, ticks, Severity.Mild, Severity.Moderate);
            AddWoundWithChance(BodyPartSlotIdH.Eye_R, 0.05f, b, ticks, Severity.Mild, Severity.Moderate);
            AddWoundWithChance(BodyPartSlotIdH.Brain, 0.3f, b, ticks, Severity.Mild);
            // The.R.State.Situation.AddTension(1f, "Crawler attached");
        }

        private void AddWoundWithChance(int bodyPartSlotIdH, float chance, Body body,
                long ticks, params Severity[] severities) {
            if (!Rng.UChance(chance)) { return; }
            if (body.TryGetSlot(bodyPartSlotIdH, out var part)) {
                part.TryAddWound(NamedEntity.For(T.BeingsCrawler01), DamageType.Piercing,
                    Rng.UFrom(severities), ticks, 1f, out _, conditionId: IdH);
            }
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (ticks % Consts.TicksPer10Minutes != 1) { return; }
            var dur = ticks - cond.StartTick;
            if (dur > Consts.TicksPer8Hours) {
                var chance = 0.01f * ((dur / Consts.TicksPerHour) - 7);
                // D.Warn("Remove chance: {0}", chance);
                if (Rng.UChance(chance)) {
                    // detach the crawler
                    if (cond.Owner.Inventory.Equipment.TryGetValue(
                            InventorySlotIdH.Hat, out var hat)) {
                        hat.RemoveItem();
                    }
                }
            }
            var agent = cond.Owner.Being.AIAgent;
            if (!agent.IsDisabled && agent.CanWork && agent.Goal?.IsNonInterruptable != true) {
                if (agent.CurrentAction?.IdH != AIBreakdownActionPanic.ActIdH) {
                    AIGoal.Interrupt(agent.Goal, ticks, T.ActPanic);
                    D.Warn("Trying to panic with crawler attached: {0}. Current action: {1}. Goal: {2}",
                        agent, agent.CurrentAction, agent.Goal);
                    // panic
                    var g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CStress,
                        T.ActPanic, agent, G.Ticks)
                            .WithWorldToDesired(AIVarsH.MentalPanicked, false, true)
                            .NonInterruptable()
                            .ForceAssigned()
                            .WithoutRetry();
                    g.WithInterrupt(3, 0);
                    g.SetAgent(agent);
                    if (!s.Query.ForceGoal.Ask(g, false)) {
                        D.Err("Failed inducing panic from crawler attachment for {0} | {1}",
                            agent, agent.CurrentAction);
                    }
                }
            }
        }

        private ConditionCrawlerAttached() : base(ConditionId.CrawlerAttached,
                GraphicsId.BeingsHatsHumanCrawlerFacehugCrawlerFacehug) {
            PreventsColonyWork = true;
            PreventsControl = true;
        }
    }
}
