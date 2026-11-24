using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI {
    // Heavy mental breakdown: the being walks to the nearest available shuttle,
    // launches it (permanently - it never comes back), and deserts the colony.
    public sealed class AIBreakdownActionAbandonShip : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionAbandonShip());
        }

        public const string ActId = "BreakdownAbandonShip";

        // How far (in region depth) the search looks for an available shuttle.
        private const int SearchDepth = 30;

        protected override int AiVar => AIVarsH.MentalAbandonedShip;

        private AIBreakdownActionAbandonShip()
                : base(ActId, () => T.ActBreakdownabandonship) {
            // Piloting a shuttle away is not something an animal can do.
            WithRequiredAbilities(AbilityIdH.Move);
            WithForbiddenFlags(StatFlags.Animal);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownabandonshipDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                // Couldn't reach a shuttle in time - give up quietly.
                return EndBreakdown(agent, goal);
            }

            var shuttle = agent.BB.TargetTile?.GetComponent<ShuttleComp>();
            if (shuttle == null || !shuttle.IsAvailableForExpedition) {
                agent.BB.ClearMoveTarget();
                agent.BB.TargetTile = null;
                shuttle = FindShuttle(S, agent);
                if (shuttle == null) {
                    return EndBreakdown(agent, goal);
                }
                agent.BB.TargetTile = shuttle.Tile;
                return InProgress;
            }

            if (agent.PosIdx != shuttle.InPort) {
                if (!TryGoToOnce(agent, shuttle.InPort, false)) {
                    // Can't reach this one right now - drop it and look for another.
                    agent.BB.ClearMoveTarget();
                    agent.BB.TargetTile = null;
                }
                return InProgress;
            }

            Abandon(S, agent, shuttle, ticks);
            return EndBreakdown(agent, goal);
        }

        private static ShuttleComp FindShuttle(GameState S, AIAgentComp agent) {
            var tile = S.Query.FindTileNearby.Ask(
                agent.PosIdx, agent.Motion.IsGrounded, SearchDepth,
                WorldLayer.Objects, (t) => {
                    if (!t.IsConstructed) { return false; }
                    if (!t.TryGetComponent<ShuttleComp>(out var sh)
                            || !sh.IsAvailableForExpedition) {
                        return false;
                    }
                    // Don't send the being somewhere it could actually die reaching it.
                    return agent.BeingData.Safety.IsActuallySafeAt(sh.InPort);
                });
            return tile?.GetComponent<ShuttleComp>();
        }

        // Selection-time check: only assign this breakdown if a shuttle is actually
        // free and safely reachable right now.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            // Deserting means flying off in the stolen shuttle - a being that can't survive
            // vacuum (no spacesuit, and not a species that's immune to begin with) can't do this.
            if (agent.BeingData.StatsBeing.O2Min.Value > 0f) { return false; }
            return FindShuttle(agent.S, agent) != null;
        }

        private static void Abandon(GameState S, AIAgentComp agent, ShuttleComp shuttle, long ticks) {
            var data = agent.BeingData;
            // Remember who took it, so a "the shuttle flies itself back" return event can
            // later find this specific shuttle and this specific deserter (see DeserterUtils).
            shuttle.SaveLastCrew(new List<BeingData> { data });
            shuttle.LaunchShuttle(true, permanent: true);
            data.Mind.Role = ColonyRole.Deserter;
            data.Mind.DesertedTick = ticks;
            DeserterUtils.ApplyColonyMoodEffect(S, data, IconId.CStress, ticks,
                T.MoodCrewMemberDeserted, -6);

            var text = T.BeingAbandonedTheShip_V2.F(
                Texts.LinkToBeing(data), Texts.LinkToTile(shuttle.Tile));
            The.Dialog.ShowSimple(shuttle.Tile.Definition.Preview, text);
            S.Sig.ShowFanfare.Send(Texts.Red(text));
            S.Sys.Log.AddLine(agent.Being, text);

            // Defer the actual removal so this tick's OnDeactivate (notification
            // cleanup, AfterBreakdown) runs against a still-existing being first.
            S.Clock.OnNextTick(() => {
                if (data.Being != null) {
                    // A deserter takes its gear along and comes back wearing it
                    // (DeserterUtils.ReturnDeserter), the way stasis keeps it
                    BeingUtils.RemoveFromShip(S, data, BeingState.Uninitialized,
                        keepInventory: true);
                }
            });
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Long1 = ticks + Consts.TicksPer8Hours;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Long1 = 0;
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
