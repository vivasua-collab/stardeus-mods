using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // "Lockdown" breakdown. The being walks around the ship locking doors. The
    // player has to find and unlock them before the crew gets cut off from
    // food, beds and work.
    public sealed class AIBreakdownActionLockdown : AIBreakdownActionDeviceSpree {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionLockdown());
        }
        public const string ActId = "BreakdownLockdown";

        // Scratch list for target picking. Only used during selection / retargeting,
        // never in a hot per-tick path.
        private static readonly List<Tile> targetCandidates = new();

        protected override int AiVar => AIVarsH.MentalLockedDoors;
        protected override int MinTargets => 2;
        protected override int MaxTargets => 4;
        protected override int ActTicks => Consts.TicksPerMinute;

        private AIBreakdownActionLockdown() : base(ActId, () => T.BreakdownLockdown) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate);
            WithForbiddenFlags(StatFlags.Animal);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownLockdownDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        protected override Tile FindTarget(AIAgentComp agent) {
            targetCandidates.Clear();
            var doors = agent.S.Components.AllByType<DoorComp>();
            if (doors == null) { return null; }
            foreach (var comp in doors) {
                var t = (comp as DoorComp)?.Tile;
                if (IsValidTarget(agent, t)) {
                    targetCandidates.Add(t);
                }
            }
            var target = targetCandidates.Count > 0 ? Rng.UFrom(targetCandidates) : null;
            targetCandidates.Clear();
            return target;
        }

        protected override bool IsSpreeTarget(AIAgentComp agent, Tile t) {
            if (!t.TryGetComponent<DoorComp>(out var door)) { return false; }
            if (door.IsLocked || !door.IsOperational) { return false; }
            return !LeadsSomewhereUnsafe(agent, door);
        }

        // A locked door between the ship and the outside can leave someone stranded
        // in vacuum with their oxygen running out. Those doors are never touched.
        public static bool LeadsSomewhereUnsafe(AIAgentComp agent, DoorComp door) {
            if (door.IsAirlock) { return true; }
            var p = door.Tile.PosIdx;
            return IsUnsafeSide(agent, Pos.N(p)) || IsUnsafeSide(agent, Pos.E(p))
                || IsUnsafeSide(agent, Pos.S(p)) || IsUnsafeSide(agent, Pos.W(p));
        }

        private static bool IsUnsafeSide(AIAgentComp agent, int pos) {
            var s = agent.S;
            // open space
            if (s.Sys.Areas.At(pos) == 0) { return true; }
            // walls the door sits in are not a side anyone can stand on
            if (!s.Sys.Path.IsRoamable(pos, true)) { return false; }
            return !agent.BeingData.Safety.IsActuallySafeAtWithoutEquipment(pos);
        }

        protected override void ActOn(AIAgentComp agent, Tile target, long ticks) {
            if (!target.TryGetComponent<DoorComp>(out var door)) { return; }
            // Lock(true) would raise a UI warning popup on failure, and this is not
            // the player clicking. Power was already checked in IsSpreeTarget.
            door.Lock(false);
            Announce(agent, T.BreakdownLockdownHit_V2.F(
                Texts.LinkToBeing(agent.BeingData), Texts.LinkToTile(target)));
        }
    }
}
