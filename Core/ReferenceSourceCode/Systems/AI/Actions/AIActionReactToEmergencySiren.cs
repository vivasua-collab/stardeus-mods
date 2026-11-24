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
using Game.Systems.Reg;
using Game.Systems.Mental;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionReactToEmergencySiren : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionReactToEmergencySiren());
        }
        public StatModifier StatModRunToSafety = StatModifier.AddPercent(1.0f,
            T.ActMovetosafety);

        public override SocialChatContext ChatContext => SocialChatContext.NoChat;

        public const string ActId = "ReactToEmergencySiren";
        private AIActionReactToEmergencySiren() : base(ActId, () => T.EmergencySiren) {
            Preconditions = AIState.With(AIVarsH.ReactedToEmergencySiren, false);
            Outcomes = AIState.With(AIVarsH.ReactedToEmergencySiren, true);
            WithRequiredFlags(StatFlags.Biological);
            WithForbiddenFlags(StatFlags.Wild);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // D.Warn("Activating emergency siren for {0} | {1}", goal, agent);
            agent.BB.ClearMoveTarget();
            // closest siren
            agent.BB.Object1 = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var sec = S.Sys.Sec;
            // if (sec.NumActiveSirens == 0) {
            //     goal.WithWorld(AIVarsH.ReactedToEmergencySiren, true);
            //     return Success;
            // }
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.BB.MoveProgress == null) {
                // 1. get all active emergency sirens
                var bestSiren = RegUtils.FindClosestReachable(S, agent.PosIdx,
                    agent.Motion.IsGrounded, sec.ActiveSirens, sec.NumActiveSirens,
                        (siren) => {
                            var sirenPos = siren.Tile.PosIdx;
                            var s = Pos.S(sirenPos);
                            if (agent.IsFeelingSafeAt(s)) { return s; }
                            var e = Pos.E(sirenPos);
                            if (agent.IsFeelingSafeAt(e)) { return e; }
                            var w = Pos.W(sirenPos);
                            if (agent.IsFeelingSafeAt(w)) { return w; }
                            var n = Pos.N(sirenPos);
                            if (agent.IsFeelingSafeAt(n)) { return n; }
                            // not safe near this siren!
                            return Pos.Invalid;
                        }, false);

                if (bestSiren == null) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (!TryGoToOnce(agent, bestSiren.Tile, false,
                        PathReqFlags.AdjNearby)) {
                    return FailedNavToTarget(agent);
                }
                agent.BB.Object1 = bestSiren;
                return InProgress;
            }

            if (agent.BB.Object1 is EmergencySirenComp siren) {
                if (!siren.IsAlertActive) {
                    goal.WithWorld(AIVarsH.ReactedToEmergencySiren, true);
                    return Success;
                }
            }

            if (S.Sys.Areas.IsSameArea(agent.BB.MoveTargetPos, agent.PosIdx)) {
                if (AIActionIdleRoam.TryRoam(agent, false, 2)) {
                    agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                        IconId.CSiren, ticks,
                        Consts.TicksPer8Hours, T.EmergencySiren, -10));
                    return InProgress;
                } else {
                    goal.WithWorld(AIVarsH.ReactedToEmergencySiren, true);
                }

                return Success;
            }
            return FailedNavToTarget(agent);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // D.Warn("Deactivating emergency siren for {0} | {1}", goal, agent);
            agent.Being.Data.StatsBeing.MoveSpeed.RemoveModifier(StatModRunToSafety, false);
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
            agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx, ticks,
                Consts.TicksPer6Hours, T.EmergencySiren, -10));
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Go for it!!
            return 0.00001f;
        }
    }
}
