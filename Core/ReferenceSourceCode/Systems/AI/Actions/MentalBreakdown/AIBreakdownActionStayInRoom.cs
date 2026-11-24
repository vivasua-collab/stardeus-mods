using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.AI;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionStayInRoom : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionStayInRoom());
        }
        public const string ActId = "BreakdownStayInRoom";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownstayinroomDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalStayedInRoom;
        private AIBreakdownActionStayInRoom() : base(ActId, () => T.BreakdownBreakdownstayinroom) {
            WithRequiredAbilities(AbilityIdH.Move);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.TicksInCurrentAction < Consts.TicksPer12Hours
                    && agent.BeingData.StatsBeing.Sanity.Value < agent.BB.Float1) {
                agent.BB.ClearMoveTarget();
                var roomId = S.Sys.Areas.PersonalQuartersAreaOf(agent.BeingData);
                if (roomId <= 0) {
                    // Room was lost mid-breakdown (bed removed, reassigned, etc.) — end gracefully
                    return EndBreakdown(agent, goal);
                }
                var inRoom = S.Sys.Areas.At(agent.PosIdx) == roomId;
                // When already inside, mostly idle; occasionally pace to another spot in the room
                if (inRoom && Rng.UChance(0.8f)) {
                    return InProgress;
                }
                if (TryGoToRoomTile(S, agent, roomId)) {
                    agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                        ticks, Consts.TicksPer4Hours, NameT, 100));
                    return InProgress;
                }
                // Can't reach room and not inside — don't trap the being, end early
                if (!inRoom) {
                    return EndBreakdown(agent, goal);
                }
            }
            return EndBreakdown(agent, goal);
        }

        private static bool TryGoToRoomTile(GameState S, AIAgentComp agent, int roomId) {
            var tiles = S.Sys.Areas.AreaById(roomId);
            if (tiles == null || tiles.Count == 0) { return false; }
            var path = S.Sys.Path;
            var isG = agent.Motion.IsGrounded;
            for (int i = 0; i < 8; i++) {
                var p = S.Rng.From(tiles);
                if (p == agent.PosIdx || !path.IsRoamable(p, isG)) { continue; }
                if (agent.Motion.TryGoTo(p, PathReqFlags.AdjOnTop)) { return true; }
            }
            return false;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // Random sanity recovery target, same range as BreakdownRoam
            agent.BB.Float1 = Rng.URange(0.5f, 0.6f);
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Float1 = 0f;
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
