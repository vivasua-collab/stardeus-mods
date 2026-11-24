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
using System.Collections.Generic;
using Game.Systems.Motion;
using Game.Systems.Mental;
using Game.Systems.Drama;
using Game.Systems.Reg;

namespace Game.Systems.AI {
    public sealed class AIActionPetAnimal : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionPetAnimal("ActPetAnimal",
                AIVarsH.IsAnimalPetted, () => $"{T.AbilityPetanimal} ({T.NounDirectOrder})", false));
            RegisterAction(new AIActionPetAnimal("ActPetAnimalFun",
                AIVarsH.HadFun, () => T.AbilityPetanimal, true));
        }
        private const int petDuration = Consts.TicksPer5Minutes;
        private int var;
        private readonly Dictionary<PettableComp, float> weightedPettables = new();
        private AIActionPetAnimal(string actId, int var, Func<string> name, bool forFun) : base(actId, name) {
            this.var = var;
            Preconditions = AIState.With(var, false);
            Outcomes = AIState.With(var, true);
            MarkAsQuiet();
            if (forFun) {
                WithRequiredNeed(NeedIdH.Fun);
                // we want our pets petted as much as possible!
                // WithRequiredJobType(JobTypeIdH.Social);
            }
            MarkAsRest();
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.PetAnimal);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            // pet
            agent.BB.TargetBeing = null;
            // when we end petting
            agent.BB.Long1NotSaved = 0;
            // punch animation in time
            agent.BB.Float1NotSaved = 0f;
            // punch animation out time
            agent.BB.Float2NotSaved = 0f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.TargetBeing != null) {
                if (agent.BB.TargetBeing.TryGetComponent<PettableComp>(out var pet)) {
                    pet.HideAllInfo();
                }
                agent.BB.TargetBeing.Motion.Immobilize(
                    ImmobilizeReasonIdH.Waiting, false);
                agent.BB.TargetBeing = null;
                agent.HideAllInfo();
            }
            agent.BB.MoveProgress = null;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Float1NotSaved = 0f;
            agent.BB.Float2NotSaved = 0f;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                } else {
                    return Failed(T.InfoUnsafeConditions);
                }
            }

            var agentPos = agent.PosIdx;
            if (agent.BB.TargetBeing == null) {
                if (goal.Type == AIGoalType.Colony) {
                    // we will have the animal selected already
                    if (goal.Target is PettableComp pettable) {
                        agent.BB.TargetBeing = pettable.Being;
                    }
                } else {
                    var allPettable = S.Components.AllByType<PettableComp>();
                    if (allPettable != null && allPettable.Count > 0) {
                        weightedPettables.Clear();
                        var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
                        foreach (PettableComp pettable in allPettable) {
                            if (pettable.Being == agent.Being) { continue; }
                            if (CanPet(agentPos, reach, pettable, out var w)) {
                                // lean toward species this being has not petted much lately
                                w *= FunNovelty.Predict(agent.BeingData,
                                    FunSubjects.ForPet(pettable.Being.Data.Species), ticks);
                                weightedPettables.Add(pettable, w);
                            }
                        }
                        if (weightedPettables.Count > 0) {
                            if (!Rng.Unseeded.TryWeightedFrom(weightedPettables,
                                    out var chosenPet)) {
                                return Failed(T.AdRejTargetUnavailable);
                            }
                            agent.BB.TargetBeing = chosenPet.Being;
                        }
                    }
                }
                if (agent.BB.TargetBeing == null) {
                    if (var == AIVarsH.HadFun) {
                        agent.BB.SetCooldown(IdH, ticks,
                            Consts.TicksPer3Hours, Consts.TicksPer6Hours);
                    }
                    return Failed(T.AdRejTargetUnavailable);
                }
            }

            int petPos;
            // we have a pet
            if (agent.BB.TargetBeing.TryGetComponent<PettableComp>(out var pet)) {
                petPos = pet.Being.PosIdx;
                pet.Being.Motion.Immobilize(ImmobilizeReasonIdH.Waiting, true);
            } else {
                return Error("Target is not Pettable: {0}", agent.BB.TargetBeing);
            }

            if (agent.BB.MoveProgress?.HasFailed == true) {
                // failed to catch the pet

                if (var == AIVarsH.HadFun) {
                    agent.BB.SetCooldown(IdH, ticks,
                        Consts.TicksPerHour, Consts.TicksPer2Hours);
                }
                return Failed("Could not reach the pet!");
            }

            // 2. go to the animal
            if (Pos.SqrDistance(petPos, agentPos) > 2) {
                if (!agent.Motion.TryGoTowardsTarget(petPos, pet.Being,
                            1f, out agent.BB.MoveProgress)) {

                    if (var == AIVarsH.HadFun) {
                        agent.BB.SetCooldown(IdH, ticks,
                            Consts.TicksPer3Hours, Consts.TicksPer6Hours);
                    }
                    return Failed("Could not reach the pet!");
                }
                return InProgress;
            }

            if (agent.BB.Long1NotSaved == 0) {
                // time when we will stop petting
                agent.BB.Long1NotSaved = ticks + petDuration;
                // hide task icon, if there was any
                pet.HideInfoIcon();
                // start petting
                // 3. stand near the animal and invoke some shit
                pet.ShowThoughtBubble(IconId.CHeart, T.IsBeingPetted);
                agent.ShowThoughtBubble(IconId.CHeart, T.ActPet);
                // move the agent to the right spot
                var petPosition = pet.Being.Position;
                var agentPosition = agent.PosExact;
                var dist = Vectors.Distance(petPosition, agentPosition);
                agent.Motion.TeleportTo(Vector2.MoveTowards(agentPosition,
                    petPosition, dist - 0.5f));
            } else if (agent.BB.Long1NotSaved < ticks) {
                // petted long enough
                pet.MarkPetted(ticks, agent.Being);
                goal.WithWorld(var, true);
                if (var == AIVarsH.HadFun) {
                    agent.BB.SetCooldown(IdH, ticks,
                        Consts.TicksPer4Hours, Consts.TicksPer12Hours);
                }

                AddMentalEffect(pet.Being.AIAgent, ticks, Consts.TicksPer4Hours, T.WasPetted, 5);
                var petSrc = FunSubjects.ForPet(pet.Being.Data.Species);
                var petNovelty = FunNovelty.Of(agent.BeingData, petSrc, ticks);
                if (FunNovelty.TryScaleMood(5, Consts.TicksPer4Hours, petNovelty,
                        out var effect, out var duration)) {
                    AddMentalEffect(agent, ticks, (int) duration, T.PettedAPet, effect);
                }
                FunNovelty.Record(agent.BeingData, petSrc, ticks);

                if (agent.BeingData.Mind.Health != null && pet.Being.Data.Mind.Health != null) {
                    var soc = SocialInteraction.Create(IconId.CPaw, T.PettedAPet,
                        T.ActPettedPet_V2.F(agent.BeingData.Mind.LinkToShortName,
                            pet.Being.Data.Mind.LinkToShortName))
                        .With(agent.BeingData)
                        .With(pet.Being.Data)
                        .WithAttitude(0.05f);
                    soc.OwnershipChange = 0.01f;

                    S.Query.RegisterSocialInteraction.Ask(soc);
                }

                TryWalkAway(agent);
                return Success;
            }

            float funBoost = 0f;
            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                if (!social.Incapable) {
                    funBoost += social.UseSkill(3f * SkillXp.Default) * 0.5f;
                }
            }
            if (pet.Being.Data.Mind.Jobs.TryJob(JobTypeIdH.Social,
                    out var petSocial)) {
                if (!petSocial.Incapable) {
                    funBoost += petSocial.UseSkill(3f * SkillXp.Default) * 0.5f;
                }
            }

            // petting
            FacePosition(agent, pet.Being.Position);
            pet.Being.Graphics.SetFacing(
                Facing.Opposite(agent.Being.Graphics.CurrentFacing));
            float dur = agent.BB.Long1NotSaved - ticks;
            float prog = 1f - dur / petDuration;
            Animate(agent, prog);
            // add fun
            if (pet.Being.Data.HasNeed(NeedIdH.Fun)) {
                // being petted is not the pet's own choice, so only the petter's memory counts
                IncreaseFun(pet.Being.Data.StatsBeing, funBoost, 1f);
            }
            if (agent.BeingData.HasNeed(NeedIdH.Fun)) {
                var novelty = FunNovelty.Of(agent.BeingData,
                    FunSubjects.ForPet(pet.Being.Data.Species), ticks);
                IncreaseFun(agent.BeingData.StatsBeing, funBoost, novelty);
            }

            return InProgress;
        }

        private void IncreaseFun(StatsBeing stats, float funBoost, float novelty) {
            float funRate = FunSession.GainPerTick(stats, novelty, 1f);
            if (funBoost > 1f) {
                funRate *= funBoost;
            }
            var speed = funRate + stats.FunDropRate.Value;
            stats.Fun.AddToBaseWithCompoundLimit(speed);
        }

        private void Animate(AIAgentComp agent, float progress) {
            agent.ShowInfoBar(Consts.ColorFuelBar, progress, "Petting!");
            if (agent.BB.Float1NotSaved == 0f) {
                var animInTime = Rng.URange(0.09f, 1.5f);
                var animOutTime = Rng.URange(0.09f, 1.5f);
                agent.BB.Float1NotSaved = animInTime;
                agent.BB.Float2NotSaved = animOutTime;
            }
            var punchTarget = agent.BB.TargetBeing.Position;
            agent.Being.Graphics.Punch(
                punchTarget - agent.PosExact, 0.05f,
                    agent.BB.Float1NotSaved,
                    agent.BB.Float2NotSaved);
        }

        protected override float PredictFunNovelty(AIAgentComp agent, long ticks) {
            if (var != AIVarsH.HadFun) { return 1f; }
            var allPettable = agent.S.Components.AllByType<PettableComp>();
            if (allPettable == null) { return 1f; }
            var data = agent.BeingData;
            float best = 0f;
            bool any = false;
            foreach (var c in allPettable) {
                var pettable = (PettableComp) c;
                var pet = pettable.Being;
                if (pet == null || pet == agent.Being || !pet.IsActive || pet.IsDead) { continue; }
                any = true;
                var novelty = FunNovelty.Predict(data, FunSubjects.ForPet(pet.Data.Species), ticks);
                if (novelty > best) {
                    best = novelty;
                    if (best >= 0.999f) { break; }
                }
            }
            return any ? best : 1f;
        }

        private bool CanPet(int agentPos, ReachQuery reach, PettableComp pet, out float w) {
            if (!pet.Being.IsActive || pet.Being.IsDead || pet.Being.Motion.IsImmobilized) {
                w = 0f;
                return false;
            }
            if (!pet.Being.Data.Species.IsPet && !pet.Being.Data.Mind.Species.IsPet) {
                w = 0f;
                return false;
            }
            if (pet.Being.Graphics.IsHidden) {
                w = 0f;
                return false;
            }
            var petPos = pet.Being.PosIdx;
            if (!reach.CanReach(petPos)) {
                w = 0f;
                return false;
            }
            var f = (1f - pet.Being.Data.StatsBeing.Fun.Value) * 100f;
            w = Pos.Distance(agentPos, petPos);
            w += f * f;
            return true;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (var == AIVarsH.HadFun) {
                if (agent.S.Components.CountByType<PettableComp>() == 0) {
                    return 0f;
                }
                // fun = cooldown
                return CachedFunCostWithCoolDown(agent, ticks);
            } else {
                return agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
            }
        }
    }
}
