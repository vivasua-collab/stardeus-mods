using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Systems.Motion;
using Game.Systems.Mental;
using Game.Systems.Drama;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using System.Collections.Generic;

namespace Game.Systems.AI {
    public sealed class AIActionStab : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStab());
        }
        public const string ActId = "Stab";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionStab() : base(ActId, () => T.VerbStalking) {
            Preconditions = AIState.With(AIVarsH.Stabbed, false);
            Outcomes = AIState.With(AIVarsH.Stabbed, true);
            MarkAsWork();
            RequireMentalHealth();
            WithoutRequiredAbilities();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // prey
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.StatusNemesis}");
            }

            var prey = agent.BB.TargetBeing;

            var distSqr = Vectors.SqrDistance(agent.Position, prey.Position);
            if (distSqr < 0.25f * 0.25f) {
                if (prey.Motion.IsBusy) {
                    prey.Motion.Abort(T.StabbedBySomeone_V1.F(
                        agent.Being.Data.Mind.LinkToShortName));
                }
                // push the prey a little away from us
                prey.Motion.TeleportTo(agent.Position + (prey.Position - agent.Position).normalized * 0.33f);
                ApplyStabbing(S, agent, prey, ticks);
                goal.WithWorld(AIVarsH.Stabbed, true);
                return Success;
            }
            FacePosition(agent, prey.Position);

            if (distSqr > 0.5f) {
                if (!agent.Motion.TryGoTowards(prey.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                            PathReqFlags.AdjOnTop |
                            PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    return Failed($"{T.AdRejUnreachable}: {prey.Data.Mind.LinkToShortName}");
                }
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer8Hours) {
                // can't catch the prey
                return Failed($"{T.AdRejUnreachable}: {T.StatusNemesis}");
            }

            return InProgress;
        }

        private readonly Dictionary<BodyPartSlot, float> weightedOrgans = new();

        private void ApplyStabbing(GameState s, AIAgentComp agent, Being prey, long ticks) {
            prey.Data.Body.BodySlot.TryAddOrUpdateWound(agent.Being, DamageType.Piercing,
                s.Rng.OneOf(Severity.Severe, Severity.Critical), ticks, 0.6f);

            weightedOrgans.Clear();
            foreach (var part in prey.Data.Body.Slots) {
                if (part.InstalledDef == null) { continue; }
                if (part.AttachedToPart?.Type.IdH != BodyPartSlotIdH.Body) { continue; }
                if (part.InstalledDef.Flags.HasFlag(BodyPartFlags.Internal)) {
                    weightedOrgans.Add(part, part.Size);
                }
            }
            if (s.Rng.TryWeightedFrom(weightedOrgans, out var organ)) {
                organ.TryAddOrUpdateWound(agent.Being, DamageType.Piercing,
                    s.Rng.OneOf(Severity.Critical, Severity.Irreparable), ticks, 0.6f);
            }
            weightedOrgans.Clear();
        }

        public static bool EnsureTargetBeing(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BB.TargetBeing != null) {
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.BeingId_Uint, out uint dateBeingId)) {
                var prey = S.Query.FindBeingData.Ask(dateBeingId);
                if (prey?.Being == null) {
                    return false;
                }
                agent.BB.TargetBeing = prey.Being;
                return true;
            }
            D.Err("Stab goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
