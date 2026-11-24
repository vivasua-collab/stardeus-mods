using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI {
    // "Catatonic" breakdown. The being drops to the floor where it stands and stays
    // there, unresponsive, until the breakdown runs out. It needs no abilities, so it
    // is also one of the few breakdowns an immobilized being can have.
    //
    // The Sleeping condition is deliberately not applied: MentalHealth freezes the
    // sanity glide for sleeping beings, and a catatonic being should still recover.
    public sealed class AIBreakdownActionCatatonic : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionCatatonic());
        }
        public const string ActId = "BreakdownCatatonic";

        protected override int AiVar => AIVarsH.MentalWentCatatonic;
        private Curve duration;

        private AIBreakdownActionCatatonic() : base(ActId, () => T.BreakdownCatatonic) {
            WithoutRequiredAbilities();
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownCatatonicDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }
            // Not saved on purpose: graphics state is lost on load, so the being
            // has to lie down again after the game is loaded mid-breakdown.
            if (!agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = true;
                agent.Being.Graphics.SetVertical(false);
                agent.Being.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
            }
            return InProgress;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            duration ??= CurveDef.FastGet(CurveIdH.MentalBreakdown_CatatonicDurationHours);
            agent.BB.ClearMoveTarget();
            agent.BB.Bool1NotSaved = false;
            // breakdown deadline
            agent.BB.Long1 = ticks + (long) (Rng.Unseeded.Curve(duration)
                * Consts.TicksPerHour);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Long1 = 0;
            agent.Being.Graphics.SetVertical(true);
            agent.Being.Graphics.SetRotation(0f, true);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
