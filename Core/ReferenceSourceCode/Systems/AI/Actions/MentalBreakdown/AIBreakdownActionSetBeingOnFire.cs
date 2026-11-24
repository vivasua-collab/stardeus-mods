using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Systems.Path;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionSetBeingOnFire : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSetBeingOnFire());
        }

        public const string ActId = "BreakdownSetBeingOnFire";

        private const byte PhaseSeekVictim = 0;
        private const byte PhaseRetreat = 1;
        private const byte PhaseRoam = 2;

        private const int RetreatDistance = 10;
        private const int RetreatDistanceSqr = RetreatDistance * RetreatDistance;
        private const int RetreatSearchDepth = 6;

        protected override int AiVar => AIVarsH.MentalSetBeingOnFire;

        private AIBreakdownActionSetBeingOnFire()
                : base(ActId, NameForAction) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate);
        }

        private static string NameForAction() {
            return T.SettingSomeoneOnFire;
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.TryingToSetSomeoneOnFire_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            switch (agent.BB.Byte1) {
                case PhaseRetreat:
                    return Retreat(S, agent, goal, ticks);
                case PhaseRoam:
                    return Roam(S, agent, goal, ticks);
                default:
                    return SeekVictim(S, agent, goal, ticks);
            }
        }

        private AIActionResult SeekVictim(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }

            var victim = agent.BB.TargetBeing;
            if (victim != null && !IsViableVictim(agent.Being, victim)) {
                agent.BB.AddToBlacklist(victim.Id);
                agent.BB.TargetBeing = null;
                agent.BB.ClearMoveTarget();
                victim = null;
            }

            if (victim == null) {
                victim = FindVictim(S, agent);
                if (victim == null) {
                    return EndBreakdown(agent, goal);
                }
                agent.BB.TargetBeing = victim;
            }

            var distSqr = Vectors.SqrDistance(agent.Position, victim.Position);
            if (distSqr > 0.5f) {
                FacePosition(agent, victim.Position);
                if (!agent.Motion.TryGoTowards(victim.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                        PathReqFlags.AdjOnTop |
                        PathReqFlags.TempDestination |
                        PathReqFlags.Instant)) {
                    agent.BB.AddToBlacklist(victim.Id);
                    agent.BB.TargetBeing = null;
                    agent.BB.ClearMoveTarget();
                }
                return InProgress;
            }

            FacePosition(agent, victim.Position);
            if (S.Sys.Firex.TrySetOnFire(
                    victim.Flammable, Fire.CreateBalanced(S))) {
                agent.BB.Bool1 = true;
                agent.BB.Int1 = victim.PosIdx;
                agent.BB.TargetBeing = null;
                agent.BB.Byte1 = PhaseRetreat;
                agent.BB.Long1 = ticks + Consts.TicksPerHour * 2;
                agent.BB.ClearMoveTarget();
                The.Dialog.ShowSimple(IconId.CFire, T.BeingSetBeingOnFire_V2.F(
                    Texts.LinkToBeing(agent.BeingData),
                    Texts.LinkToBeing(victim.Data)));
                return InProgress;
            }

            agent.BB.AddToBlacklist(victim.Id);
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
            return InProgress;
        }

        private AIActionResult Retreat(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (Pos.SqrDistance(agent.PosIdx, agent.BB.Int1)
                    >= RetreatDistanceSqr || ticks >= agent.BB.Long1) {
                BeginRoaming(agent, ticks);
                return InProgress;
            }

            var isGrounded = agent.Motion.IsGrounded;
            var destination = S.Query.FindNearbyRandom.Ask(
                agent.PosIdx, isGrounded, RetreatSearchDepth, (p) => {
                    return Pos.SqrDistance(p, agent.BB.Int1) >= RetreatDistanceSqr
                        && S.Sys.Path.IsRoamable(p, isGrounded)
                        && agent.BeingData.Safety.IsActuallySafeAt(p);
                });

            if (destination != Pos.Invalid) {
                agent.BB.ClearMoveTarget();
                agent.Motion.TryGoTo(destination,
                    PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK,
                    out agent.BB.MoveProgress);
                return InProgress;
            }

            agent.BB.ClearMoveTarget();
            AIActionIdleRoam.TryRoam(agent, false, Rng.URange(4, 8), true);
            return InProgress;
        }

        private AIActionResult Roam(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }

            agent.BB.ClearMoveTarget();
            if (!AIActionIdleRoam.TryRoam(
                    agent, false, Rng.URange(1, 4), true)) {
                var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                if (pos != Pos.Invalid) {
                    agent.Motion.TryGoTo(pos,
                        PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK);
                }
            }
            return InProgress;
        }

        private static void BeginRoaming(AIAgentComp agent, long ticks) {
            agent.BB.Byte1 = PhaseRoam;
            agent.BB.Long1 = ticks + Rng.URange(
                Consts.TicksPerHour, Consts.TicksPerHour * 2);
            agent.BB.ClearMoveTarget();
        }

        private static Being FindVictim(GameState S, AIAgentComp agent) {
            Being closest = null;
            var closestDist = float.MaxValue;
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var candidate = alive[i];
                if (!IsViableVictim(agent.Being, candidate)
                        || agent.BB.IsBlacklisted(candidate.Id)) {
                    continue;
                }
                var dist = Pos.SqrDistance(agent.PosIdx, candidate.PosIdx);
                if (dist < closestDist) {
                    closest = candidate;
                    closestDist = dist;
                }
            }
            return closest;
        }

        public static bool HasViableVictim(GameState S, Being actor) {
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                if (IsViableVictim(actor, alive[i])) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsViableVictim(Being actor, Being candidate) {
            if (candidate == actor || !candidate.IsActive || !candidate.IsAlive) {
                return false;
            }
            var flammable = candidate.Flammable;
            if (flammable == null || !flammable.IsActive) {
                return false;
            }
            return !flammable.IsOnFire && !flammable.IsTemperatureResistant;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Byte1 = PhaseSeekVictim;
            agent.BB.Bool1 = false;
            agent.BB.Int1 = Pos.Invalid;
            agent.BB.Long1 = ticks + Consts.TicksPer8Hours;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetBeing = null;
            agent.BB.Byte1 = PhaseSeekVictim;
            agent.BB.Bool1 = false;
            agent.BB.Int1 = Pos.Invalid;
            agent.BB.Long1 = 0;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
