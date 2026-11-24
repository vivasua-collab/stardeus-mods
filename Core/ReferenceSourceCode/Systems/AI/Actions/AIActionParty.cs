using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using KL.Grid;
using Game.Systems.Drama;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionParty : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionParty());
        }
        public const string ActId = "Party";
        public static readonly int ActIdH = Hashes.S(ActId);
        // The middle of the party (PartyVenue.Center), set on every guest's goal
        public const string PartyPosVar = "PartyPos";
        private AIActionParty() : base(ActId, () => T.ActSocialize) {
            Preconditions = AIState.With(AIVarsH.HadParty, false);
            Outcomes = AIState.With(AIVarsH.HadParty, true);
            WithRequiredAbilities(AbilityIdH.Intelligence);
            RequireMentalHealth();
            MarkAsRest();
        }

        // BB.Int1: the party's center. BB.Long1NotSaved: when to choose where to stand next.
        // BB.Object1: the music the party gathers at, once BB.Bool1NotSaved says it was looked up.
        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill();
            }
            if (agent.TicksInCurrentAction > Consts.TicksPer4Hours) {
                goal.WithWorld(AIVarsH.HadParty, true);
                return Success;
            }
            var bb = agent.BB;
            if (bb.Int1 == 0 && !goal.TryGetVar(PartyPosVar, out bb.Int1)) {
                return Error("PartyPos variable not set on goal: {0}", goal);
            }
            var center = bb.Int1;
            if (bb.MoveProgress != null) {
                // A walk to a spot is over: stay a while there. One that failed, or stopped
                // short because somebody took the spot first, chooses again at once.
                if (bb.MoveProgress.HasFailed && bb.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(bb.MoveTargetPos);
                    bb.Long1NotSaved = 0;
                } else if (agent.PosIdx != bb.MoveTargetPos) {
                    bb.Long1NotSaved = 0;
                } else {
                    bb.Long1NotSaved = ticks + StayTicks();
                }
                bb.ClearMoveTarget();
            }
            if (ticks < bb.Long1NotSaved) {
                PartyGroups.Stand(S, agent, center, Music(S, agent, center));
                return InProgress;
            }
            // Walks that stay (not TempDestination) claim the tile on arrival: the party used
            // to send everyone to 5 tiles with temporary walks, and they piled up there
            var spot = PartyGroups.PickSpot(S, agent, center);
            if (spot != Pos.Invalid && spot != agent.PosIdx) {
                if (TryGoToOnce(agent, spot, true)) {
                    return InProgress;
                }
                agent.Motion.StoreNavFailureAt(spot);
                bb.ClearMoveTarget();
            }
            bb.Long1NotSaved = ticks + StayTicks();
            return InProgress;
        }

        private static int StayTicks() {
            return Rng.URange(Consts.TicksPer20Minutes, Consts.TicksPer40Minutes);
        }

        private static JukeboxComp Music(GameState S, AIAgentComp agent, int center) {
            var bb = agent.BB;
            if (!bb.Bool1NotSaved) {
                bb.Bool1NotSaved = true;
                bb.Object1 = PartyVenue.MusicFor(S, center);
            }
            return bb.Object1 as JukeboxComp;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            Reset(agent);
            agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                ticks, Consts.TicksPerDay, T.NounParty, +15));
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            Reset(agent);
            PartyGroups.Clear(agent);
        }

        private static void Reset(AIAgentComp agent) {
            var bb = agent.BB;
            bb.ClearMoveTarget();
            bb.Int1 = 0;
            bb.Long1NotSaved = 0;
            bb.Object1 = null;
            bb.Bool1NotSaved = false;
        }
    }
}
