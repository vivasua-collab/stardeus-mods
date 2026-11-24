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
using Game.Systems.Motion;
using Game.Achievements;
using Game.Systems.Mental;
using System.Collections.Generic;

namespace Game.Systems.AI {
    public sealed class AIActionFunAdmireObject : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunAdmireObject());
        }
        public const string ActId = "ActFunAdmireObject";
        public static readonly int ActIdH = Hashes.S(ActId);
        private AIActionFunAdmireObject() : base(ActId, () => T.VerbAdmiring) {
            Preconditions = AIState.With(AIVarsH.HadFun, false);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            MarkAsQuiet();
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredAbilities(AbilityIdH.Move);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // target admirable
            agent.BB.Object1 = null;
            // admiration time left
            agent.BB.Int1 = 0;
            FunSession.Clear(agent);
        }

        private readonly AdmirableComp[] admirables = new AdmirableComp[64];
        private readonly float[] admirableNovelties = new float[64];

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                }
            }
            if (agent.BB.Object1 is not AdmirableComp target) {
                target = FindAdmirableObject(S, agent);

                if (target == null) {
                    agent.BB.SetCooldown(ActIdH, ticks, Consts.TicksPer2Hours, Consts.TicksPer6Hours);
                    return Failed(T.Bored);
                }
                agent.BB.Object1 = target;
                agent.BB.Int1 = Rng.URange(Consts.TicksPer30Minutes, Consts.TicksPerHour);
                agent.ShowThoughtBubble(target.AdmireIcon, target.AdmiringName(true));
            }

            // 2. go near
            if (!EntityUtils.IsNearObj(agent.PosIdx, target.Entity)) {
                var pos = target.AdmirePosIdx(agent);
                if (pos != Pos.Invalid) {
                    if (!TryGoToOnce(agent, pos, true)) {
                        return FailedNavToTarget(agent);
                    }
                } else if (!TryGoToOnce(agent, target.Entity, true)) {
                    return FailedNavToTarget(agent);
                }
                return InProgress;
            } else {
                FaceTarget(target, agent);
            }

            var data = agent.BeingData;
            var stats = data.StatsBeing;
            var src = FunSubjects.ForAdmirable(target);
            if (!FunSession.IsStarted(agent)) {
                // starts once the being is admiring, the walk there does not count
                FunSession.Begin(agent, ticks,
                    FunSession.FullNetGainPerTick(stats, target.FunMultiplier),
                    0, Maths.Max(agent.BB.Int1, 0));
            }
            var fun = stats.Fun;
            var novelty = FunNovelty.Of(data, src, ticks);
            fun.AddToBaseWithCompoundLimit(
                FunSession.GainPerTick(stats, novelty, target.FunMultiplier));

            if (fun.Value > FunSession.SatisfiedAt || agent.BB.Int1-- < 0
                    || FunSession.IsOver(agent, ticks)) {
                AddFunBonus(agent, ticks, target, novelty);
                FunNovelty.Record(data, src, ticks);
                FunSession.Finish(agent, ticks);
                agent.BB.SetCooldown(ActIdH, ticks, Consts.TicksPer30Minutes, Consts.TicksPer2Hours);
                goal.WithWorld(AIVarsH.HadFun, true);
                return Success;
            }

            // still admiring
            return InProgress;
        }

        public AdmirableComp FindAdmirableObject(GameState S, AIAgentComp agent,
                float chanceWindows = 0.25f, float chanceNonWindows = 0.9f) {
            var safety = agent.BeingData.Safety;
            // 1. find something to admire
            int found = 0;
            var agentPos = agent.PosIdx;
            var ticks = G.Ticks;
            var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
            if (Rng.UChance(chanceNonWindows)) {
                found = S.Components.FindAllMatching(admirables, (a) => {
                    if (a.Entity.Definition.LayerId < WorldLayer.Objects)  {
                        return false;
                    }
                    if (Pos.SqrDistance(a.Entity.PosIdx, agentPos) > 10 * 10) {
                        return false;
                    }
                    if (!S.Sys.Areas.IsSameArea(agentPos, a.Entity.PosIdx)) {
                        return false;
                    }
                    return a.CanSafelyAdmire(agent, reach);
                });
                if (found == 0) {
                    chanceWindows = 1f;
                } else if (FunNovelty.Predict(agent.BeingData, FunSource.View, ticks)
                        > BestNovelty(agent, ticks, found) + FunNovelty.PreferenceTolerance) {
                    // the view is clearly fresher than any object here, look at everything
                    found = 0;
                    chanceWindows = 1f;
                }
            } else {
                chanceWindows = 1f;
            }

            if (found == 0) {
                if (Rng.UChance(chanceWindows)) {
                    found = S.Components.FindAllMatching(admirables, (a) => {
                        if (Pos.SqrDistance(agentPos, a.Entity.PosIdx) > 10 * 10) {
                            return false;
                        }
                        if (a.IsWindow) {
                            var area = S.Sys.Areas;
                            var pos = a.Entity.PosIdx;
                            if (a.Entity.Definition.LayerId == WorldLayer.Walls) {
                                if (!area.IsEnclosed(Pos.N(pos))
                                        && !area.IsEnclosed(Pos.S(pos))
                                        && !area.IsEnclosed(Pos.E(pos))
                                        && !area.IsEnclosed(Pos.W(pos))) {
                                    return false;
                                }
                            } else if (!area.IsEnclosed(pos)) {
                                return false;
                            }
                        }
                        if (a.Entity.Definition.LayerId == WorldLayer.Floor)  {
                            // obstructed admirable floor
                            if (!EntityUtils.IsEmptyFloorAt(a.Entity.PosIdx)) {
                                return false;
                            }
                        }
                        return a.CanSafelyAdmire(agent, reach);
                    });
                }
            }
            if (found > 0) {
                found = KeepFreshest(agent, ticks, found);
                AdmirableComp closest = null;
                float closestDist = float.MaxValue;
                var aPos = agent.PosExact;
                Arrays.Shuffle(Rng.Unseeded, admirables, found);
                for (int i = 0; i < found; i++) {
                    var adm = admirables[i];
                    var dist = Vectors.SqrDistance(adm.Entity.Position, aPos);
                    // close enough, stop searching
                    if (dist < 5 * 5) {
                        return adm;
                    }
                    if (dist < closestDist) {
                        closest = adm;
                        closestDist = dist;
                    }
                }
                return closest;
            }
            return null;
        }

        // Fills admirableNovelties for the first found candidates and returns the best
        private float BestNovelty(AIAgentComp agent, long ticks, int found) {
            var data = agent.BeingData;
            float best = -1f;
            for (int i = 0; i < found; i++) {
                var novelty = FunNovelty.Predict(data, FunSubjects.ForAdmirable(admirables[i]), ticks);
                admirableNovelties[i] = novelty;
                if (novelty > best) {
                    best = novelty;
                }
            }
            return best;
        }

        // Drops candidates that are clearly more worn out than the freshest one
        private int KeepFreshest(AIAgentComp agent, long ticks, int found) {
            var min = BestNovelty(agent, ticks, found) - FunNovelty.PreferenceTolerance;
            int kept = 0;
            for (int i = 0; i < found; i++) {
                if (admirableNovelties[i] >= min) {
                    admirables[kept++] = admirables[i];
                }
            }
            for (int i = kept; i < found; i++) {
                admirables[i] = null;
            }
            return kept;
        }

        private void AddFunBonus(AIAgentComp agent, long ticks, AdmirableComp target,
                float novelty) {
            if (!FunNovelty.TryScaleMood(target.Admirability, Consts.TicksPer4Hours, novelty,
                    out var effect, out var duration)) {
                return;
            }
            agent.AddMentalEffect(
                MentalEffect.Create(target.Entity.PosIdx, ticks, duration,
                    target.AdmiringName(false), effect));
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // target admirable
            agent.BB.Object1 = null;
            // admiration time left
            agent.BB.Int1 = 0;
            Array.Clear(admirables, 0, admirables.Length);
            FunSession.Clear(agent);
            agent.HideThoughtBubble();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            var sight = agent.BeingData.StatsBeing.Sight.Value;
            if (sight < 0.05f) {
                // can't admire without ability to see
                return 0f;
            }
            if (sight > 1f) {
                // better sight won't help admire more
                sight = 1f;
            }
            if (agent.S.Components.CountByType<AdmirableComp>() == 0)  {
                return 0f;
            }

            return CachedFunCostWithCoolDown(agent, ticks) / sight;
        }

        protected override float PredictFunNovelty(AIAgentComp agent, long ticks) {
            var all = agent.S.Components.AllByType<AdmirableComp>();
            if (all == null) { return 1f; }
            var data = agent.BeingData;
            var agentPos = agent.PosIdx;
            var areas = agent.S.Sys.Areas;
            float best = 0f;
            bool any = false;
            foreach (var c in all) {
                var a = (AdmirableComp) c;
                var pos = a.Entity.PosIdx;
                // same limits FindAdmirableObject uses, cheapest check first
                if (Pos.SqrDistance(pos, agentPos) > 10 * 10) { continue; }
                if (!a.Entity.IsActive || !a.IsAdmirableNow) { continue; }
                if (!areas.IsSameArea(agentPos, pos)) { continue; }
                any = true;
                var novelty = FunNovelty.Predict(data, FunSubjects.ForAdmirable(a), ticks);
                if (novelty > best) {
                    best = novelty;
                    if (best >= 0.999f) { break; }
                }
            }
            return any ? best : 1f;
        }
    }
}
