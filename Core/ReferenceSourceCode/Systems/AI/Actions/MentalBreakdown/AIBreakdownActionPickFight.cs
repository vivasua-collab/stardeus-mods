using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Systems.Story;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.AI {
    // "Pick a fight" breakdown. The being walks up to whoever it hates the most
    // (or, lacking enemies, whoever is closest) and starts a fist fight.
    //
    // The fight itself is a regular friendly fight, run by CombatSys. It can't start
    // while the breakdown is still going: CombatSys has to interrupt the attacker's
    // goal, and a breakdown goal is non-interruptable. So the breakdown ends once
    // the being gets in the opponent's face, and the fight starts a tick later.
    public sealed class AIBreakdownActionPickFight : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionPickFight());
        }
        public const string ActId = "BreakdownPickFight";

        protected override int AiVar => AIVarsH.MentalPickedFight;

        private AIBreakdownActionPickFight() : base(ActId, () => T.BreakdownPickFight) {
            WithRequiredAbilities(AbilityIdH.Move);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownPickFightDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }

            var opponent = agent.BB.TargetBeing;
            if (opponent != null && !IsViableOpponent(agent.Being, opponent)) {
                agent.BB.AddToBlacklist(opponent.Id);
                agent.BB.TargetBeing = null;
                agent.BB.ClearMoveTarget();
                opponent = null;
            }
            if (opponent == null) {
                opponent = FindOpponent(S, agent);
                if (opponent == null) {
                    // Nobody to fight - end harmlessly
                    return EndBreakdown(agent, goal);
                }
                agent.BB.TargetBeing = opponent;
            }

            FacePosition(agent, opponent.Position);
            if (Vectors.SqrDistance(agent.Position, opponent.Position) > 2f) {
                if (!agent.Motion.TryGoTowards(opponent.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                        PathReqFlags.AdjOnTop | PathReqFlags.TempDestination
                        | PathReqFlags.Instant)) {
                    agent.BB.AddToBlacklist(opponent.Id);
                    agent.BB.TargetBeing = null;
                    agent.BB.ClearMoveTarget();
                }
                return InProgress;
            }

            // In their face. Bool1: the fight starts when the breakdown ends.
            agent.BB.Bool1 = true;
            return EndBreakdown(agent, goal);
        }

        // The most hated being that can be fought, or the closest one when the
        // being hates nobody in particular
        private static Being FindOpponent(GameState S, AIAgentComp agent) {
            Being best = null;
            var bonds = agent.BeingData.Mind.Bonds;
            if (bonds != null) {
                var mostHate = 0f;
                foreach (var op in bonds.Opinions.Values) {
                    if (op.Hate <= mostHate) { continue; }
                    var candidate = op.TargetMind?.Owner?.Being;
                    if (candidate == null || agent.BB.IsBlacklisted(candidate.Id)
                            || !IsViableOpponent(agent.Being, candidate)) {
                        continue;
                    }
                    best = candidate;
                    mostHate = op.Hate;
                }
            }
            if (best != null) { return best; }

            var closestDist = float.MaxValue;
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var candidate = alive[i];
                if (agent.BB.IsBlacklisted(candidate.Id)
                        || !IsViableOpponent(agent.Being, candidate)) {
                    continue;
                }
                var dist = Pos.SqrDistance(agent.PosIdx, candidate.PosIdx);
                if (dist < closestDist) {
                    best = candidate;
                    closestDist = dist;
                }
            }
            return best;
        }

        public static bool HasViableOpponent(GameState S, Being actor) {
            if (!A.IsCombatOn) { return false; }
            if (!FriendlyFightStoryEvent.IsFitToFight(actor, true)) { return false; }
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                if (IsViableOpponent(actor, alive[i])) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsViableOpponent(Being actor, Being candidate) {
            if (candidate == actor || !candidate.IsActive || !candidate.IsAlive) {
                return false;
            }
            // Punching a pet is a different kind of breakdown
            if (candidate.Data.Species.IsAnimal != actor.Data.Species.IsAnimal) {
                return false;
            }
            if (!FriendlyFightStoryEvent.IsFitToFight(candidate, false)) { return false; }
            return actor.Data.Safety.IsActuallySafeAt(candidate.PosIdx);
        }

        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return HasViableOpponent(agent.S, agent.Being);
        }

        private static void StartFightNextTick(GameState S, Being attacker, Being opponent) {
            S.Clock.OnNextTick(() => {
                // A lot can happen in a tick: re-check both with the full rule set,
                // the breakdown goal is over by now so nothing needs to be ignored
                if (!attacker.IsActive || !opponent.IsActive
                        || !FriendlyFightStoryEvent.CanFight(attacker, opponent)) {
                    return;
                }
                if (S.Query.StartFriendlyFightBetween.Ask(
                        attacker.AIAgent.CombatUnit, opponent.AIAgent.CombatUnit)) {
                    FriendlyFightStoryEvent.OnFightStarted(S, attacker, opponent, true);
                } else {
                    D.Warn("Breakdown fight did not start: {0} vs {1}", attacker, opponent);
                }
            });
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1 = false;
            agent.BB.Long1 = ticks + Consts.TicksPer4Hours;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            var opponent = agent.BB.TargetBeing;
            var isArmed = agent.BB.Bool1;
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1 = false;
            agent.BB.Long1 = 0;
            base.OnDeactivate(agent, goal, ticks);
            if (isArmed && opponent != null) {
                StartFightNextTick(agent.S, agent.Being, opponent);
            }
        }
    }
}
