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
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public sealed class AIActionFunRideBot : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunRideBot());
        }
        public const string ActId = "ActFunRideBot";
        private AIActionFunRideBot() : base(ActId, () => T.RidingABot) {
            Preconditions = AIState.With(AIVarsH.HadFun, false);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            MarkAsQuiet();
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredAbilities(AbilityIdH.RideBot);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            agent.BB.TargetBeing = null;
            FunSession.Clear(agent);
        }

        private bool IsSafeAt(AIAgentComp agent, int pos, bool recurse = true) {
            // it could still be a door
            var wall = EntityUtils.WallAt(pos);
            // we won't go inside a wall anyway
            if (wall != null && wall.Door == null) {
                return true;
            }
            if (wall?.Door != null) {
                // does the door allow us through?
                if (!agent.Motion.CanGoThrough(wall.Passability.Deny)) {
                    return false;
                }
                // what's on the other side of the door?
                if (recurse
                        && IsSafeAt(agent, Pos.N(pos), false)
                        && IsSafeAt(agent, Pos.S(pos), false)
                        && IsSafeAt(agent, Pos.E(pos), false)
                        && IsSafeAt(agent, Pos.W(pos), false)) {
                    return true;
                }
                return false;
            }
            return agent.BeingData.Safety.IsActuallySafeAt(pos);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                }
            }
            var bot = agent.BB.TargetBeing;
            if (agent.Being.Graphics.IsRiding) {
                var data = agent.BeingData;
                var stats = data.StatsBeing;
                if (!FunSession.IsStarted(agent)) {
                    // the ride may last only what is left of the 2 hours
                    var left = Math.Max(0L, Consts.TicksPer2Hours - (ticks - goal.StateStartTick));
                    FunSession.Begin(agent, ticks, FunSession.FullNetGainPerTick(stats, 1f),
                        0, left);
                }
                var fun = stats.Fun;
                var novelty = FunNovelty.Of(data, FunSubjects.ForRide(bot?.Species), ticks);
                fun.AddToBaseWithCompoundLimit(FunSession.GainPerTick(stats, novelty, 1f));
                if (fun.Value > FunSession.SatisfiedAt ||
                        (G.Ticks - goal.StateStartTick) > Consts.TicksPer2Hours
                        || FunSession.IsOver(agent, ticks)) {
                    AddFunBonus(agent, ticks, bot);
                    FunSession.Finish(agent, ticks);
                    goal.WithWorld(AIVarsH.HadFun, true);
                    return Success;
                }

                var ba = bot.AIAgent;
                if (!ba.Motion.IsBusy && ba.Goal != null && ba.IsIdle) {
                    ba.ClearGoal();
                }

                // check if the bot is about to go where we don't want him to be
                var nextIdx = bot.Motion.Position.NextIdx;
                if (nextIdx > 0 && !IsSafeAt(agent, nextIdx)) {
                    // time to jump off
                    AddFunBonus(agent, ticks, bot);
                    FunSession.Clear(agent);
                    goal.WithWorld(AIVarsH.HadFun, true);
                    return Success;
                }

                return InProgress;
            }

            var catPos = agent.Motion.Position.Idx;
            if (bot == null) {
                bot = S.Beings.FindClosestMatching(catPos, (b) => {
                    return b.Species.IdH == SpeciesIdH.CleaningBot
                        && b != agent.Being
                        && !b.Graphics.HasRider
                        && b.IsAlive && !b.Motion.IsImmobilized
                        && S.Sys.Areas.IsSameArea(b.PosIdx, catPos);
                }, minDist: 0f, maxDist: 15f);
                if (bot != null) {
                    agent.BB.TargetBeing = bot;
                } else {
                    agent.BB.SetCooldown(IdH, ticks,
                        Consts.TicksPer3Hours, Consts.TicksPer6Hours);
                    return Failed(T.Bored);
                }
            }

            if (agent.BB.MoveProgress?.HasFailed == true) {
                // failed to catch a bot
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPerHour, Consts.TicksPer2Hours);
                return Failed(T.Bored);
            }

            if (Pos.SqrDistance(bot.PosIdx, catPos) > 2) {
                if (!agent.Motion.TryGoTowardsTarget(bot.PosIdx, bot,
                            1f, out agent.BB.MoveProgress)) {
                    agent.BB.SetCooldown(IdH, ticks,
                        Consts.TicksPer3Hours, Consts.TicksPer6Hours);
                    return Failed(T.Bored);
                }
                return InProgress;
            }


            // we are near the bot
            // start riding
            if (!agent.Being.Graphics.StartRiding(bot)) {
                return Failed(T.AdRejTargetUnavailable);
            }

            if (bot.Species.IdH == SpeciesIdH.CleaningBot
                    && agent.Being.Species.IdH == SpeciesIdH.Cat) {
                The.AchievementTracker.Unlock(
                    AchievementCatRidingCleaningBot.AchievementId);
            }

            return InProgress;

        }

        private void AddFunBonus(AIAgentComp agent, long ticks, Being bot) {
            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes) {
                var src = FunSubjects.ForRide(bot?.Species);
                var novelty = FunNovelty.Of(agent.BeingData, src, ticks);
                if (FunNovelty.TryScaleMood(+12, Consts.TicksPerDay, novelty,
                        out var effect, out var duration)) {
                    agent.AddMentalEffect(
                        MentalEffect.Create(agent.PosIdx, ticks, duration,
                            T.HadAFunRide, effect));
                }
                FunNovelty.Record(agent.BeingData, src, ticks);
            #if !PRODUCTION_BUILD
            } else {
                D.Warn("Ride was too short, no fun bonus: {0}", Units.TicksToTime(agent.TicksInCurrentAction));
            #endif
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            FunSession.Clear(agent);
            agent.HideThoughtBubble();
            agent.BB.MoveProgress = null;
            if (agent.Being.Graphics.IsRiding) {
                agent.Being.Graphics.StopRiding();
            }
            agent.BB.TargetBeing = null;
            agent.Being.Data.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
        }


        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.S.Beings.HasAliveBySpecies(SpeciesIdH.CleaningBot)) {
                return CachedFunCostWithCoolDown(agent, ticks);
            }
            return 0f;
        }

        protected override float PredictFunNovelty(AIAgentComp agent, long ticks) {
            if (!Species.Known.TryGetValue(SpeciesIdH.CleaningBot, out var botSpecies)) {
                return 1f;
            }
            return FunNovelty.Predict(agent.BeingData, FunSubjects.ForRide(botSpecies), ticks);
        }
    }
}
