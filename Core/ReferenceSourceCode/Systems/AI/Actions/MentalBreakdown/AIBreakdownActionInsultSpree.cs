using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI {
    // "Insult spree" breakdown. The being marches from one crew member to the next,
    // rants at each of them for a few minutes and moves on. Every victim takes a
    // mood hit and starts liking the ranter a lot less.
    public sealed class AIBreakdownActionInsultSpree : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionInsultSpree());
        }
        public const string ActId = "BreakdownInsultSpree";

        public const int MinVictims = 3;
        public const int MaxVictims = 5;
        public const int VictimMoodEffect = -5;
        public const float VictimAttitudeChange = -0.3f;
        private const int RantTicks = Consts.TicksPer5Minutes;

        protected override int AiVar => AIVarsH.MentalWentOnInsultSpree;

        private AIBreakdownActionInsultSpree()
                : base(ActId, () => T.BreakdownInsultSpree) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Vocalize);
            WithForbiddenFlags(StatFlags.Animal);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownInsultSpreeDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            // Byte1: victims insulted so far, Int1: how many this spree is after
            if (ticks >= agent.BB.Long1 || agent.BB.Byte1 >= agent.BB.Int1) {
                return EndBreakdown(agent, goal);
            }

            var victim = agent.BB.TargetBeing;
            if (victim != null && !IsViableVictim(agent.Being, victim)) {
                StopRanting(agent);
                agent.BB.AddToBlacklist(victim.Id);
                agent.BB.TargetBeing = null;
                agent.BB.ClearMoveTarget();
                victim = null;
            }
            if (victim == null) {
                victim = FindVictim(S, agent);
                if (victim == null) {
                    // Ran out of people to offend
                    return EndBreakdown(agent, goal);
                }
                agent.BB.TargetBeing = victim;
            }

            FacePosition(agent, victim.Position);
            if (Vectors.SqrDistance(agent.Position, victim.Position) > 2f) {
                // The victim walked off mid-rant, chase them and start over
                StopRanting(agent);
                if (!agent.Motion.TryGoTowards(victim.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                        PathReqFlags.AdjOnTop | PathReqFlags.TempDestination
                        | PathReqFlags.Instant)) {
                    agent.BB.AddToBlacklist(victim.Id);
                    agent.BB.TargetBeing = null;
                    agent.BB.ClearMoveTarget();
                }
                return InProgress;
            }

            // Bool1NotSaved: is ranting, Long1NotSaved: when the rant started.
            // Neither is saved, so a rant interrupted by a load simply starts over.
            if (!agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = true;
                agent.BB.Long1NotSaved = ticks;
                S.Query.StartForcedChat.Ask(agent.BeingData, victim.Data, SocialContext._Insult);
            }
            var ranted = ticks - agent.BB.Long1NotSaved;
            if (ranted < RantTicks) {
                agent.ShowInfoBar(Consts.ColorHealthBar, ranted / (float) RantTicks,
                    T.BreakdownInsultSpreeRanting);
                return InProgress;
            }

            Insult(S, agent, victim, ticks);
            StopRanting(agent);
            agent.BB.AddToBlacklist(victim.Id);
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
            agent.BB.Byte1++;
            return InProgress;
        }

        private static void Insult(GameState S, AIAgentComp agent, Being victim, long ticks) {
            var ranter = agent.BeingData;
            victim.Data.Mind.Health?.AddEffect(S, MentalEffect.Create(
                victim.PosIdx, ticks, Consts.TicksPerDay,
                T.BreakdownInsultSpreeMood_V1.F(ranter.Name), VictimMoodEffect));
            if (ranter.Mind.Bonds == null || victim.Data.Mind.Bonds == null) {
                return;
            }
            // The victim takes it personally, the ranter barely remembers it
            var soc = SocialInteraction.Create(IconId.CStress, T.BreakdownInsultSpreeSocial,
                T.BreakdownInsultSpreeSocialDesc_V2.F(
                    ranter.Mind.LinkToShortName, victim.Data.Mind.LinkToShortName))
                .WithAttitude(VictimAttitudeChange)
                .With(ranter, 0.1f)
                .With(victim.Data, 1f);
            S.Query.RegisterSocialInteraction.Ask(soc);
        }

        private static void StopRanting(AIAgentComp agent) {
            if (agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = false;
                agent.HideInfoBar();
                if (agent.BB.TargetBeing != null) {
                    agent.S.Query.EndForcedChat.Ask(agent.BeingData, agent.BB.TargetBeing.Data);
                }
            }
        }

        // Closest first, so the spree tears through whoever is around
        private static Being FindVictim(GameState S, AIAgentComp agent) {
            Being closest = null;
            var closestDist = float.MaxValue;
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var candidate = alive[i];
                if (agent.BB.IsBlacklisted(candidate.Id)
                        || !IsViableVictim(agent.Being, candidate)) {
                    continue;
                }
                var dist = Pos.SqrDistance(agent.PosIdx, candidate.PosIdx);
                if (dist < closestDist) {
                    closest = candidate;
                    closestDist = dist;
                }
            }
            return closest;
        }

        public static bool HasViableVictim(GameState S, Being actor) {
            // No mouth, no insults: a mindjar without a Conversation Module stays quiet
            if (!actor.Data.HasAbility(AbilityIdH.Vocalize)) { return false; }
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                if (IsViableVictim(actor, alive[i])) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsViableVictim(Being actor, Being candidate) {
            if (candidate == actor || !candidate.IsActive || !candidate.IsAlive) {
                return false;
            }
            if (candidate.Carrier != null) { return false; }
            // Only those who can be offended are worth insulting
            if (candidate.Data.Mind.Health == null) { return false; }
            if (candidate.Data.Species.IsAnimal) { return false; }
            // An insult has to be heard (BeingChat.WillChat: Vocalize and Hear)
            if (!candidate.Data.HasAbility(AbilityIdH.Hear)) { return false; }
            if (candidate.Data.IsFriendly != actor.Data.IsFriendly) { return false; }
            if (candidate.Data.HasCondition(ConditionIdH.Sleeping)) { return false; }
            return actor.Data.Safety.IsActuallySafeAt(candidate.PosIdx);
        }

        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return HasViableVictim(agent.S, agent.Being);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = Rng.URange(MinVictims, MaxVictims + 1);
            agent.BB.Long1 = ticks + Consts.TicksPer6Hours;
            agent.BB.Long1NotSaved = 0;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            StopRanting(agent);
            agent.HideInfoBar();
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = 0;
            agent.BB.Long1 = 0;
            agent.BB.Long1NotSaved = 0;
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
