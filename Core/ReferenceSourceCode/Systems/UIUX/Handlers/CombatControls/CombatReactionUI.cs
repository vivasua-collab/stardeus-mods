using System;
using Game.Constants;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public static class CombatReactionUI {
        private static readonly CombatUnitConfig[] allConfigs =
            (CombatUnitConfig[]) Enum.GetValues(typeof(CombatUnitConfig));

        public static void Apply(CombatUnit unit, CombatUnitConfig cfg) {
            // SetConfig clears the current AttackTarget for Ignore and runs
            // ValidateConfig. Writing Mind.CombatConfig directly leaves a live
            // target engaged, because ValidateConfig's Ignore branch only acts
            // when AttackTarget is already null.
            unit.SetConfig(cfg);
            if (A.S.Sys.Combat.IsHostileCombatInProgress
                    && unit.State != CombatUnitState.Incapacitated) {
                AIGoal.Interrupt(unit.Agent.Goal, G.Ticks);
            }
        }

        public static void ShowChoice(RectTransform anchor, CombatUnit unit, Action onChanged) {
            var qc = QuickChoice.Create(T.CombatReaction);
            qc.Widget = anchor;
            var current = unit.Config;
            foreach (var cfg in allConfigs) {
                var name = CombatUnitComp.NameFor(cfg);
                if (cfg == current) {
                    name = $"{name} {Texts.GreyParentheses(T.AdjCurrent)}";
                }
                var chosen = cfg;
                qc.AddIconButton(CombatUnitComp.IconFor(cfg), name, () => {
                    // re-applying the current reaction would interrupt the unit's goal for nothing
                    if (unit.Config != chosen) {
                        Apply(unit, chosen);
                    }
                    onChanged?.Invoke();
                });
            }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }
    }
}
