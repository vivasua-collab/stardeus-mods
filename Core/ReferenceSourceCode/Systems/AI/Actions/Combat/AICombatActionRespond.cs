using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.AI {
    // How the undrafted crew fights with the Fight reaction: a shooter fires
    // from where it stands at a target it can hit from there, a melee fighter walks over to an
    // enemy close by and hits it, and both go back to work once nobody is in reach. Nobody
    // goes looking for enemies or takes cover; drafting is how the player sends the crew in.
    public sealed class AICombatActionRespond : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionRespond());
        }
        public const string ActId = "Respond";

        private AICombatActionRespond() : base(ActId, () => T.CombatObjectiveDefend) {
            Preconditions = AIState.With(AIVarsH.HasResponded, false);
            Outcomes = AIState.With(AIVarsH.HasResponded, true);
            // a shooter fires without moving; a melee fighter that can't move hits what is next
            // to it
            WithoutRequiredAbilities();
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.Object2 = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.Object2 = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var cu = agent.CombatUnit;
            var target = cu.AttackTarget;
            if (target == null || cu.BestAttack == null || !cu.RespondsInPlace) {
                return Done(goal);
            }
            if (!cu.BestAttack.IsMelee) {
                // stand: the passive attack fires. A walk from the task it dropped would
                // otherwise carry it on, firing on the move
                if (agent.Motion.IsBusy) {
                    agent.Motion.Clear(resetFlags: true);
                }
                return InProgress;
            }
            if (cu.DistToAttackTarget > Tunables.CombatMeleeResponseRadius) {
                // it got away: back to work
                cu.SetAttackTarget(null);
                return Done(goal);
            }
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (!cu.IsSightFresh) {
                return InProgress;
            }
            if (agent.BB.MoveProgress?.HasFailed == true) {
                agent.BB.ClearMoveTarget();
                if (agent.BB.Byte1++ > 5) {
                    cu.SetAttackTarget(null);
                    return Failed("Can't reach the target!");
                }
            }
            switch (CombatApproach.Towards(S, agent, cu, target)) {
                case CombatApproach.Step.Unreachable:
                    cu.SetAttackTarget(null);
                    return Failed("Can't reach the target!");
                case CombatApproach.Step.Stuck:
                    agent.BB.Byte1 = 0;
                    cu.TryShuffle(S, target);
                    return InProgress;
                default:
                    return InProgress;
            }
        }

        private AIActionResult Done(AIGoal goal) {
            goal.WorldState.Set(AIVarsH.HasResponded, true);
            return Success;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
            agent.BB.Byte1 = 0;
        }
    }
}
