using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;

namespace Game.Systems.AI {
    // "Lights out" breakdown. The being walks around the ship flipping power
    // switches off. Nothing breaks - the player has to notice and switch the
    // devices back on.
    public sealed class AIBreakdownActionLightsOut : AIBreakdownActionDeviceSpree {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionLightsOut());
        }
        public const string ActId = "BreakdownLightsOut";

        // Scratch list for target picking. Only used during selection / retargeting,
        // never in a hot per-tick path.
        private static readonly List<Tile> targetCandidates = new();

        protected override int AiVar => AIVarsH.MentalSwitchedDevicesOff;
        protected override int ActTicks => Consts.TicksPerMinute;

        private AIBreakdownActionLightsOut() : base(ActId, () => T.BreakdownLightsOut) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate);
            WithForbiddenFlags(StatFlags.Animal);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownLightsOutDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        // Random rather than closest, so the player has to look around the ship
        protected override Tile FindTarget(AIAgentComp agent) {
            targetCandidates.Clear();
            var nodes = agent.S.Components.AllByType<ElectricNodeComp>();
            if (nodes == null) { return null; }
            foreach (var comp in nodes) {
                var t = (comp as ElectricNodeComp)?.Tile;
                if (IsValidTarget(agent, t)) {
                    targetCandidates.Add(t);
                }
            }
            var target = targetCandidates.Count > 0 ? Rng.UFrom(targetCandidates) : null;
            targetCandidates.Clear();
            return target;
        }

        protected override bool IsSpreeTarget(AIAgentComp agent, Tile t) {
            if (!t.TryGetComponent<ElectricNodeComp>(out var node)) { return false; }
            if (!node.IsSwitchable || !node.IsSwitchedOn) { return false; }
            return !IsLifeSupport(t);
        }

        // This is a Light band breakdown, it must stay a nuisance. Devices that
        // keep someone alive are off limits. A device missing from this list is
        // not a disaster: the perpetrator and the device are announced, and
        // switching it back on is a single click.
        public static bool IsLifeSupport(Tile t) {
            return t.TryGetComponent<OxygenProducerComp>(out _)
                || t.TryGetComponent<HeaterComp>(out _)
                || t.TryGetComponent<VentComp>(out _)
                || t.TryGetComponent<StasisPodComp>(out _)
                || t.TryGetComponent<StasisArrayComp>(out _)
                || t.TryGetComponent<AutoDocComp>(out _)
                || t.TryGetComponent<TubeFeederComp>(out _)
                || t.TryGetComponent<ShipComputerComp>(out _)
                // an unpowered door can lock someone in
                || t.TryGetComponent<DoorComp>(out _);
        }

        protected override void ActOn(AIAgentComp agent, Tile target, long ticks) {
            if (!target.TryGetComponent<ElectricNodeComp>(out var node)) { return; }
            node.Switch(false);
            Announce(agent, T.BreakdownLightsOutHit_V2.F(
                Texts.LinkToBeing(agent.BeingData), Texts.LinkToTile(target)));
        }
    }
}
