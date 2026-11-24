using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI {
    // "Remove an upgrade" breakdown. The being walks to a random device that has an
    // upgrade installed, rips the upgrade out, and carries the module around for the
    // rest of the breakdown. When the breakdown ends, the module is dropped on the
    // floor (in OnDeactivate), from where it can be hauled and reinstalled as usual.
    public sealed class AIBreakdownActionRemoveUpgrade : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionRemoveUpgrade());
        }
        public const string ActId = "BreakdownRemoveUpgrade";

        protected override int AiVar => AIVarsH.MentalRemovedUpgrade;

        // agent.BB.Byte1 phase values
        private const byte PhaseSeek = 0;
        private const byte PhaseCarry = 1;

        // Scratch list for target picking. Only used during selection / retargeting,
        // never in a hot per-tick path.
        private static readonly List<Tile> targetCandidates = new();

        private AIBreakdownActionRemoveUpgrade()
                : base(ActId, () => T.ActBreakdownremoveupgrade) {
            // Tearing an upgrade out of a device means manipulating it by hand -
            // animals and beings without manipulator parts can't do this.
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate, AbilityIdH.Carry);
            WithForbiddenFlags(StatFlags.Animal);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.Byte1 == PhaseCarry) {
                return CarryAround(S, agent, goal, ticks);
            }
            return SeekDevice(S, agent, goal, ticks);
        }

        // Find an upgraded device, walk to it, rip the upgrade out and start carrying it.
        private AIActionResult SeekDevice(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var target = agent.BB.TargetTile;

            // Target lost (deconstructed / upgrade already gone) - pick another one.
            if (target != null && (!target.IsActive || !target.IsConstructed
                    || !HasRemovableUpgrade(target))) {
                agent.BB.AddToBlacklist(target.Id);
                agent.BB.ClearMoveTarget();
                agent.BB.TargetTile = null;
                target = null;
            }

            if (target == null) {
                target = FindTarget(agent);
                if (target == null) {
                    // Nothing left to strip - end harmlessly
                    return EndBreakdown(agent, goal);
                }
                agent.BB.ClearMoveTarget();
                agent.BB.TargetTile = target;
            }

            // Approach the device
            if (!EntityUtils.IsNearObj(agent.PosIdx, target)) {
                if (!TryGoToOnce(agent, target)) {
                    // Can't reach this one - blacklist it and look for another
                    agent.BB.AddToBlacklist(target.Id);
                    agent.BB.ClearMoveTarget();
                    agent.BB.TargetTile = null;
                }
                return InProgress;
            }

            // Arrived: rip the first removable upgrade out and start carrying it
            FacePosition(agent, target.Position);
            var upgrade = RemoveUpgrade(agent, target);
            agent.BB.TargetTile = null;
            if (upgrade == null) {
                // This device is spent - try another one (or end) on the next tick
                agent.BB.AddToBlacklist(target.Id);
                return InProgress;
            }
            agent.BB.Byte1 = PhaseCarry;
            agent.BB.Long1 = ticks + Rng.URange(Consts.TicksPerHour,
                Consts.TicksPerHour * 2);
            The.Dialog.ShowSimple(IconId.CUpgrade, T.UpgradeWasTornOut_V3.F(
                Texts.LinkToBeing(agent.BeingData), upgrade.Title, Texts.LinkToTile(target)));
            return InProgress;
        }

        // Wander around hugging the stolen module until the breakdown ends
        private AIActionResult CarryAround(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }

            agent.BB.ClearMoveTarget();
            if (!AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                if (pos != Pos.Invalid) {
                    agent.Motion.TryGoTo(pos,
                        PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK);
                }
            }
            return InProgress;
        }

        // Random upgraded device the being can safely go to. Blacklisted (proven
        // unreachable or already stripped) devices are skipped.
        private static Tile FindTarget(AIAgentComp agent) {
            targetCandidates.Clear();
            var upgraded = agent.S.Sys.Upg.Upgraded;
            for (int i = 0; i < upgraded.Count; i++) {
                var t = upgraded[i].Tile;
                if (t == null || !t.IsActive || !t.IsConstructed || t.IsImmutable) {
                    continue;
                }
                if (agent.BB.IsBlacklisted(t.Id)) { continue; }
                // Don't send the being somewhere it could actually die reaching it.
                if (!agent.BeingData.Safety.IsActuallySafeAt(t.PosIdx)) { continue; }
                if (!HasRemovableUpgrade(t)) { continue; }
                targetCandidates.Add(t);
            }
            return targetCandidates.Count > 0 ? Rng.UFrom(targetCandidates) : null;
        }

        // Selection-time check: only assign this breakdown if there's an upgraded
        // device the being can actually go to right now.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return FindTarget(agent) != null;
        }

        // Public so tests can assert how many devices the breakdown could target.
        public static bool HasRemovableUpgrade(Tile t) {
            if (!t.TryGetComponent<UpgradesComp>(out var upgrades)
                    || upgrades.Upgrades == null) {
                return false;
            }
            for (int i = 0; i < upgrades.Upgrades.Length; i++) {
                var id = upgrades.Upgrades[i];
                if (id == null) { continue; }
                var upgrade = UpgradeRegistry.Get(id);
                if (upgrade != null && upgrade.CanRemove(out _)) {
                    return true;
                }
            }
            return false;
        }

        // Eject the first removable upgrade and start carrying the spawned module.
        // Returns the upgrade (for the notification), or null when nothing was taken.
        private static Upgrade RemoveUpgrade(AIAgentComp agent, Tile target) {
            if (!target.TryGetComponent<UpgradesComp>(out var upgrades)
                    || upgrades.Upgrades == null) {
                return null;
            }
            for (int i = 0; i < upgrades.Upgrades.Length; i++) {
                var id = upgrades.Upgrades[i];
                if (id == null) { continue; }
                var upgrade = UpgradeRegistry.Get(id);
                if (upgrade == null || !upgrade.CanRemove(out _)) { continue; }
                // Eject onto the being's own tile: it picks the module up right away,
                // and the device's port tile can be out of its reach.
                var pile = upgrades.EjectSlot(i, upgrade, agent.PosIdx);
                if (pile == null || pile.Obj == null) { return null; }
                pile.RemoveFromInventory();
                if (!agent.Being.Graphics.Carry(pile.Obj, false, false)) {
                    D.Err("Failed carrying the ripped-out upgrade {0} for {1}",
                        pile, agent.BeingData);
                    // Put it back on the floor properly - an orphan pile outside the
                    // inventory would never be hauled or reinstalled again.
                    pile.AddToInventory();
                    return null;
                }
                return upgrade;
            }
            return null;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // Never stack the module on top of something already being carried.
            agent.Being.Graphics.DropIfCarrying();
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Byte1 = PhaseSeek;
            agent.BB.Long1 = 0;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // The breakdown is over: drop the module on the floor where the being
            // stands (this must happen before base.OnDeactivate ends the breakdown).
            if (agent.Being.Graphics.Carrying is Obj obj) {
                obj.Pile?.AddToInventory();
                agent.Being.Graphics.Drop(obj);
            }
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Byte1 = PhaseSeek;
            agent.BB.Long1 = 0;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownremoveupgradeDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
    }
}
