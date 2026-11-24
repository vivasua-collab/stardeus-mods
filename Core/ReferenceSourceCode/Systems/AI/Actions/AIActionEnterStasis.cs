using Game.Components;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using Game.Systems.Creatures;
using KL.Utils;
using Game.Utils;

namespace Game.Systems.AI {
    // Direct-order action: walk to a specific StasisArray's InPort and get sent back into
    // cryo-stasis. Mirrors AIActionEnterShuttle's shape (walk to a port, mutate BeingData
    // on arrival, then defer the actual Being destruction by one tick).
    public sealed class AIActionEnterStasis : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionEnterStasis());
        }
        public const string ActId = "EnterStasis";
        public static readonly int ActIdH = Hashes.S(ActId);
        private AIActionEnterStasis() : base(ActId, () => T.ActEnterstasis) {
            Preconditions = AIState.With(AIVarsH.IsSentToStasis, false);
            Outcomes = AIState.With(AIVarsH.IsSentToStasis, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsWork();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            StasisArrayComp array = null;
            if (goal.TryGetVar("StasisArrayId", out int id)) {
                array = S.Components.Find<StasisArrayComp>(id);
            }
            if (array == null || !array.Tile.IsConstructed || !array.Tile.IsActive) {
                return Failed(T.AdRejTargetUnavailable);
            }
            var inPort = array.Tile.Transform.InPort;
            if (agent.PosIdx != inPort) {
                if (!TryGoToOnce(agent, inPort, false)) {
                    D.Err("Failed enter stasis: {0} | {1}",
                        agent.BB.MoveProgress, agent);
                    return FailedNavToTarget(agent, inPort);
                }
                return InProgress;
            }

            if (array.BeingCount >= array.Capacity) {
                // Every chamber holds a specific person now, so a full array cannot
                // quietly take one more
                return Failed(T.AdRejTargetUnavailable);
            }
            array.StoreColonist();
            StasisUtils.SendToStasis(S, agent.BeingData, array, ticks);
            goal.WithWorld(AIVarsH.IsSentToStasis, true);
            S.Clock.OnNextTick(() => {
                if (agent.BeingData.Being != null) {
                    S.Beings.Destroy(agent.Being);
                }
            });
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
