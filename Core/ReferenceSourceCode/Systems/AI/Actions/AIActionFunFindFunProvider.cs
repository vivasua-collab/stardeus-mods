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
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Reg;

namespace Game.Systems.AI {
    public sealed class AIActionFunFindFunProvider : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunFindFunProvider());
        }
        public const string ActId = "ActFunFindFunProvider";
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private readonly int nextActId;
        private static readonly IFunProvider[] providers = new IFunProvider[64];
        private static readonly float[] novelties = new float[64];
        private AIActionFunFindFunProvider() : base(ActId, () => T.VerbSearchingFor_V1.F(T.StatFun)) {
            // Preconditions = AIState.With(AIVarsH.IsNearJukebox, false);
            Outcomes = AIState.With(AIVarsH.IsNearFunProvider, true);
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredAbilities(AbilityIdH.Move);
            nextActId = Hashes.S(AIActionFunUseFunProvider.ActId);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            // carry this over to the next action
            agent.BB.TargetEntity = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var provider = agent.BB.TargetEntity;
            if (provider == null) {
                return FindFunProvider(S, agent, ticks, ref provider);
            }

            // check if we are near
            if (provider.TryGetComponent<IFunProvider>(out var box)) {
                if (Pos.SqrDistance(agent.Motion.Position.Idx, box.WorkSpot) < 2f) {
                    // we have arrived
                    goal.WithWorld(AIVarsH.IsNearFunProvider, true);
                    return Success;
                }
                if (!TryGoToOnce(agent, box.WorkSpot, false)) {
                    // Remembered at the work spot, which is what the search skips by. Kept at
                    // the device's tile, the search picked the same device again on every
                    // tick and the being stood still for hours (CQGKQ)
                    return FailedNavToTarget(agent, box.WorkSpot);
                }
                return InProgress;
            }

            D.Err("FunProvider comp not available on {0}", provider);
            return Failed(T.Error);
        }

        private AIActionResult FindFunProvider(GameState S, AIAgentComp agent,
                long ticks, ref Entity device) {

            var op = S.Query.FindSocialOpportunity.Ask(
                agent.BeingData, SocialOpportunityIdH.FunDevice);

            // find the entity and try to go near
            var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
            var prov = FindBestFunProvider(S, agent, ticks, reach, out var bestNovelty);
            if (op?.FunProvider != null && IsCandidate(op.FunProvider, agent, reach)) {
                // join others unless it means using something clearly more worn out
                var opNovelty = FunNovelty.Predict(agent.BeingData,
                    FunSubjects.ForDevice(op.FunProvider.FunTile), ticks);
                if (prov == null || opNovelty >= bestNovelty - FunNovelty.PreferenceTolerance) {
                    prov = op.FunProvider;
                }
            }
            if (prov == null) {
                // there is no jukebox available
                // save the cooldown for the action that wants us to go
                // and use a jukebox
                agent.BB.SetCooldown(nextActId,
                    ticks, Consts.TicksPer3Hours, Consts.TicksPer6Hours);
                return Failed(T.RequiredDeviceIsNotAvailable);
            }
            agent.BB.TargetEntity = prov.FunTile;
            return InProgress;
        }

        // Prefers the device this being has used the least lately, and the closest one
        // among devices that are about as fresh.
        private IFunProvider FindBestFunProvider(GameState S, AIAgentComp agent, long ticks,
                ReachQuery reach, out float bestNovelty) {
            bestNovelty = -1f;
            var count = S.Components.FindAllByInterface(providers);
            var data = agent.BeingData;
            var agentPos = agent.Motion.Position.Exact;
            for (int i = 0; i < count; i++) {
                var p = providers[i];
                if (!IsCandidate(p, agent, reach)) {
                    novelties[i] = -1f;
                    continue;
                }
                var novelty = FunNovelty.Predict(data, FunSubjects.ForDevice(p.FunTile), ticks);
                novelties[i] = novelty;
                if (novelty > bestNovelty) {
                    bestNovelty = novelty;
                }
            }
            IFunProvider best = null;
            float bestDist = float.MaxValue;
            if (bestNovelty >= 0f) {
                var minNovelty = bestNovelty - FunNovelty.PreferenceTolerance;
                for (int i = 0; i < count; i++) {
                    if (novelties[i] < 0f || novelties[i] < minNovelty) { continue; }
                    var dist = Vectors.SqrDistance(providers[i].Entity.Position, agentPos);
                    if (dist < bestDist) {
                        bestDist = dist;
                        best = providers[i];
                    }
                }
            }
            Array.Clear(providers, 0, count);
            return best;
        }

        // A device this being can use and walk to by a route it may take (BeingReach)
        private static bool IsCandidate(IFunProvider p, AIAgentComp agent, ReachQuery reach) {
            if (!p.Entity.IsActive || !p.CanAddUser || !p.CanUse(agent.Being)) {
                return false;
            }
            var ws = p.FunTile.Transform.WorkSpot;
            return agent.IsFeelingSafeAt(ws) && !agent.Motion.HadNavFailureAt(ws)
                && reach.CanReach(ws);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // do not do this for non-failed goal, this will be carried over to next action!
            if (AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal)) {
                agent.BB.TargetEntity = null;
            }

            if (!agent.BB.IsCoolingDown(nextActId, ticks)) {
                agent.BB.SetCooldown(nextActId,
                    ticks, Consts.TicksPer2Hours, Consts.TicksPer4Hours);
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.S.Components.CountByInterface<IFunProvider>(max: 1) == 0) {
                return 0f;
            }
            return 0.0001f;
        }
    }
}
