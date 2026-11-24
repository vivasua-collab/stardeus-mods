using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // "Streaking" breakdown. The being throws all of its equipment on the floor and
    // wanders around naked. The breakdown goal is non-interruptable, so the being
    // can't re-equip until it is over - after that the regular equipment need picks
    // the gear back up, and the Naked mood effect takes care of the consequences.
    public sealed class AIBreakdownActionStreaking : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionStreaking());
        }
        public const string ActId = "BreakdownStreaking";

        protected override int AiVar => AIVarsH.MentalStreaked;

        private AIBreakdownActionStreaking() : base(ActId, () => T.BreakdownStreaking) {
            WithRequiredAbilities(AbilityIdH.Move);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownStreakingDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }
            // has stripped
            if (!agent.BB.Bool1) {
                // The world may have changed since the breakdown was selected.
                // Taking a spacesuit off in vacuum would be a death sentence.
                if (!CanStripSafely(agent)) {
                    return EndBreakdown(agent, goal);
                }
                agent.BB.Bool1 = true;
                Strip(agent);
                return InProgress;
            }

            agent.BB.ClearMoveTarget();
            if (Rng.UChance(0.5f)) {
                return InProgress;
            }
            // Safe roaming only: the being has no protective gear on anymore
            if (!AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), false)) {
                var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                if (pos != Pos.Invalid) {
                    agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop);
                }
            }
            return InProgress;
        }

        private static void Strip(AIAgentComp agent) {
            foreach (var slot in agent.BeingData.Inventory.Equipment) {
                if (slot.Value.Item != null) {
                    slot.Value.RemoveItem();
                }
            }
        }

        public static bool HasEquipment(BeingData being) {
            var inv = being.Inventory;
            if (inv == null) { return false; }
            foreach (var slot in inv.Equipment) {
                if (slot.Value.Item != null) {
                    return true;
                }
            }
            return false;
        }

        private static bool CanStripSafely(AIAgentComp agent) {
            return HasEquipment(agent.BeingData)
                && agent.BeingData.Safety.IsActuallySafeAtWithoutEquipment(agent.PosIdx);
        }

        // Selection-time check: there must be something to take off, and the being
        // must survive where it stands without it.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return CanStripSafely(agent);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Bool1 = false;
            // breakdown deadline
            agent.BB.Long1 = ticks + Rng.URange(
                Consts.TicksPerHour, Consts.TicksPerHour * 2);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Bool1 = false;
            agent.BB.Long1 = 0;
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
