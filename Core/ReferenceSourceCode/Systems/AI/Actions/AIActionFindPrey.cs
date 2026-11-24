using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Systems.AI.Combat;
using System.Collections.Generic;

namespace Game.Systems.AI {
    public sealed class AIActionFindPrey : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindPrey());
        }
        public const string ActId = "FindPrey";
        private AIActionFindPrey() : base(ActId, () => T.VerbSearchingFor_V1.F(T.SpeciesHuman)) {
            Preconditions = AIState.With(AIVarsH.IsHungry, true);
            Outcomes = AIState.With(AIVarsH.IsHungry, false);
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.FaceHug);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
        }


        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            goal.WorldState.Set(AIVarsH.IsHungry, false);
            return Success;
        }

        private bool TryFindPrey(AIAgentComp agent, out Being prey) {
            prey = null;
            // The action also deactivates when the hunter goes down, and a downed one
            // starting a fight drafts the whole crew against whatever enemy is near
            if (agent.BeingData.Body.IsIncapacitated
                    || agent.CombatUnit.State == CombatUnitState.Incapacitated) {
                return false;
            }
            var s = agent.S;
            // Guests are counted separately from Enemies, but predators hunt anyone, so a
            // colony holding only guests still has to be scanned.
            if (s.Situation.Colonists <= 0 && s.Situation.Enemies <= 0
                    && s.Situation.Guests <= 0) {
                return false;
            }
            var areaSys = agent.S.Sys.Areas;
            var aPos = agent.PosIdx;
            if (areaSys.IsSpace(aPos)) { return false; }
            var fac = agent.BeingData.EffectiveFaction;
            var agentIsPlayer = agent.BeingData.IsPlayer;
            foreach (var b in s.Beings.Alive) {
                if (b.Data.Body.IsIncapacitated) { continue; }
                if (!b.Species.IsBiological) {
                    continue;
                }
                if (fac == b.Data.EffectiveFaction) { continue; }
                // Domesticated (player-aligned) crawlers never hunt guest passengers.
                if (agentIsPlayer && b.Data.IsGuest) { continue; }
                if (areaSys.IsSameArea(aPos, b.PosIdx)) {
                    prey = b;
                    return true;
                }
            }
            return false;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks,
                Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
            // if (agent.BeingData.StatsBeing.AIAlignment.Value >= 0.5f) { return; }
            if (TryFindPrey(agent, out var prey)) {
                D.Warn("Combat#X: Crawler wants to eat: {0} -> {1}", agent, prey);
                agent.S.Query.StartCombatBetween.Ask(agent.CombatUnit, prey.AIAgent.CombatUnit);
                agent.CombatUnit.SetState(CombatUnitState.Aggressive);
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) { return 0f; }
            if (!TryFindPrey(agent, out _)) { return 0f; }
            return CachedRandomCostWithCoolDown(agent.BB, ticks);
        }
    }
}
