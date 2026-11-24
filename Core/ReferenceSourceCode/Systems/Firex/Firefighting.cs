using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Hints;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.Firex {
    public sealed class Firefighting {
        private readonly FirexSys firex;
        private readonly GameState S;
        private readonly Dictionary<Being, int> activeFirefighters = new();
        public Firefighting(FirexSys sys) {
            firex = sys;
            S = sys.S;
        }

        public void AfterProcessFire(long ticks) {
            // ignore fire for the first day in-game
            var timesOnFire = S.Situation.TimesOnFire;
            var startFirefightAt = S.Scenario.FiresToIgnore + 1;
            if (timesOnFire < startFirefightAt) {
                return;
            } else if (timesOnFire == startFirefightAt) {
                ShowFireHint();
            }
            foreach (var being in S.Beings.Alive) {
                var agent = being.AIAgent;
                if (!agent.CanWork) { continue; }
                if (!agent.BeingData.IsPlayer) { continue; }
                TryOrderFirefighting(ticks, agent, false, Pos.Invalid);
            }
        }

        public static bool TryOrderFirefighting(long ticks, AIAgentComp agent,
                bool force, int forcedPos) {
            if (!agent.Jobs.TryJob(JobTypeIdH.Firefighting, out var ffjob)) {
                return false;
            }
            if (ffjob.Priority < 1) {
                return false;
            }
            if (agent.BB.IsCoolingDown(JobTypeIdH.Firefighting, ticks)) {
                // D.Warn("Firefighting cooling down for: {0}", agent);
                return false;
            }
            if (agent.BB.TryNeedGoal(JobTypeIdH.Firefighting, out var ffgoal)) {
                if (AIGoal.IsActive(ffgoal.State)) {
                    ffgoal.GoalImportance = 1;
                    ffgoal.WithInterrupt(2, 0);
                    ffgoal.GoalPriority = ffjob.Priority;
                    return true;
                }
                // make firefighting available
                ffgoal.ResetWorldState();
                ffgoal.ClearCounters();
                ffgoal.ClearVars();
                ffgoal.ChangeStateTo(AIGoalState.Available, ticks, true);
            } else {
                ffgoal = AIGoal.CreatePersonal(JobTypeIdH.Firefighting, IconId.CFire,
                        T.JobTypeFirefighting, agent, ticks)
                    .WithWorldToDesired(AIVarsH.IsFireExtinguished, false, true);
                agent.BB.SetNeedGoal(JobTypeIdH.Firefighting, ffgoal);
            }
            ffgoal.GoalImportance = 1;
            ffgoal.WithInterrupt(2, 0);
            ffgoal.GoalPriority = ffjob.Priority;
            if (force) {
                var planner = agent.S.Query.GetAIPlanner.Ask();
                if (planner.TryFormulateFor(ffgoal, agent, ticks, out var plan)) {
                    agent.TryClearGoal();
                    ffgoal.WithVar(AIGoalVars.TargetPos, forcedPos);
                    agent.SetGoal(ffgoal, plan, ticks);
                    return true;
                // } else {
                //     D.Warn("Firefighter couldn't formulate a plan for goal: {0} -> {1}",
                //         agent, ffgoal);
                }
                return false;
            }
            return true;
        }

        public bool HasEnoughFirefightersFor(Being whosAsking, int fPos) {
            // if (S.Sys.Path.IsClaimed(fPos)) {
            //     return true;
            // }
            #if DISABLE_FIREX
            return true;
            #endif
            int ffCount = 0;
            foreach (var p in activeFirefighters) {
                if (p.Value == fPos && p.Key != whosAsking) {
                    ffCount++;
                }
            }
            if (ffCount > 0) {
                return Rng.UChance(Maths.Min(ffCount / 3f, 0.95f));
            }
            return false;
        }


        public void MarkFirefighting(Being who, int pos) {
            activeFirefighters[who] = pos;
        }
        public void EndFirefighting(Being who) {
            activeFirefighters.Remove(who);
        }

        private void ShowFireHint() {
            S.Sys.Hint.Show(HintFirefighting.Id, false);
        }

    }
}
