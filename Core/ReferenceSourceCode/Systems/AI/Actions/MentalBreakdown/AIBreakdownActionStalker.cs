using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI {
    // "Clingy" breakdown. The being picks someone at random and shadows them for a
    // few hours, staring. The one being followed gets a small mood debuff for as
    // long as it lasts.
    public sealed class AIBreakdownActionStalker : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionStalker());
        }
        public const string ActId = "BreakdownStalker";

        // Start walking again once the victim gets this far away (in tiles)
        public const float FollowDistance = 3f;
        private const float FollowDistanceSqr = FollowDistance * FollowDistance;
        public const int VictimMoodEffect = -4;

        // Scratch list for victim picking. Only used during selection / retargeting,
        // never in a hot per-tick path.
        private static readonly List<Being> victimCandidates = new();

        protected override int AiVar => AIVarsH.MentalStalked;

        private AIBreakdownActionStalker() : base(ActId, () => T.BreakdownStalker) {
            WithRequiredAbilities(AbilityIdH.Move);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownStalkerDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }

            var victim = agent.BB.TargetBeing;
            if (victim != null && !IsViableVictim(agent, victim)) {
                agent.BB.TargetBeing = null;
                agent.BB.ClearMoveTarget();
                victim = null;
            }
            if (victim == null) {
                victim = FindVictim(S, agent);
                if (victim == null) {
                    // Nobody left to follow - end harmlessly
                    return EndBreakdown(agent, goal);
                }
                agent.BB.TargetBeing = victim;
                // make the victim notice right away
                agent.BB.Long1NotSaved = 0;
            }

            FacePosition(agent, victim.Position);
            if (Vectors.SqrDistance(agent.Position, victim.Position) > FollowDistanceSqr) {
                if (!agent.Motion.TryGoTowards(victim.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby
                        | PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    // Can't get to this one - follow somebody else
                    agent.BB.AddToBlacklist(victim.Id);
                    agent.BB.TargetBeing = null;
                    agent.BB.ClearMoveTarget();
                }
                return InProgress;
            }

            // Close enough to be creepy. The effect is short and re-added with the
            // same reason, so it never stacks and fades soon after the stalker leaves.
            if (ticks >= agent.BB.Long1NotSaved) {
                agent.BB.Long1NotSaved = ticks + Consts.TicksPerHour;
                victim.Data.Mind.Health?.AddEffect(S, MentalEffect.Create(
                    victim.PosIdx, ticks, Consts.TicksPer2Hours,
                    T.BreakdownStalkerMood_V1.F(agent.BeingData.Name),
                    VictimMoodEffect));
            }
            return InProgress;
        }

        private static Being FindVictim(GameState S, AIAgentComp agent) {
            victimCandidates.Clear();
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var candidate = alive[i];
                if (agent.BB.IsBlacklisted(candidate.Id)) { continue; }
                if (!IsViableVictim(agent, candidate)) { continue; }
                victimCandidates.Add(candidate);
            }
            var victim = victimCandidates.Count > 0 ? Rng.UFrom(victimCandidates) : null;
            victimCandidates.Clear();
            return victim;
        }

        public static bool HasViableVictim(GameState S, Being actor) {
            var agent = actor.AIAgent;
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                if (IsViableVictim(agent, alive[i])) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsViableVictim(AIAgentComp agent, Being candidate) {
            if (candidate == agent.Being || !candidate.IsActive || !candidate.IsAlive) {
                return false;
            }
            if (candidate.Carrier != null) { return false; }
            // Shadowing a raider would just get the stalker killed
            if (candidate.Data.IsFriendly != agent.BeingData.IsFriendly) { return false; }
            // Don't follow someone into a place that could kill the stalker
            return agent.BeingData.Safety.IsActuallySafeAt(candidate.PosIdx);
        }

        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return HasViableVictim(agent.S, agent.Being);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            // breakdown deadline
            agent.BB.Long1 = ticks + Rng.URange(
                Consts.TicksPer2Hours, Consts.TicksPer4Hours);
            // next victim mood refresh
            agent.BB.Long1NotSaved = 0;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Long1 = 0;
            agent.BB.Long1NotSaved = 0;
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
