using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // Shared state machine for the "sabotage a device" mental-breakdown family.
    // The being finds the nearest eligible device, walks to it, lingers (arming
    // the sabotage), then walks away. The sabotage effect fires in OnDeactivate
    // the moment the breakdown ends, so it happens after the saboteur has left.
    //
    // Concrete subclasses supply the target predicate, optional arm step, optional
    // extra guard, and the effect. Only concrete subclasses carry [SelfInit].
    public abstract class AIBreakdownActionSabotageDevice : AIMentalBreakdownAction {
        // How far (in region depth) the search looks for a target. Subclasses may
        // override for devices that are rare or placed in predictable locations.
        protected virtual int SearchDepth => 30;

        // agent.BB.Byte1 phase values
        private const byte PhaseSeek = 0;
        private const byte PhaseLeave = 2;

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownsabotageDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        protected AIBreakdownActionSabotageDevice(string actId)
                : base(actId, () => T.ActBreakdownsabotage) {
            // Sabotaging a device means walking up to it and manipulating it by hand -
            // animals and beings without manipulator parts can't do this.
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate);
            WithForbiddenFlags(StatFlags.Animal);
        }

        // --- Variation hooks ---

        // Returns true if this tile is a valid sabotage target for this breakdown.
        protected abstract bool IsSabotageTarget(Tile t);

        // Called once when the being arrives and arms the device (default: no-op).
        protected virtual void OnArm(AIAgentComp agent, Tile target) { }

        // Additional guard in OnDeactivate. The tile is guaranteed to be active
        // and constructed when this is checked. Default: always allow.
        protected virtual bool CanSabotage(Tile target) => true;

        // The actual sabotage effect, called when armed and CanSabotage passes.
        protected abstract void PerformSabotage(AIAgentComp agent, Tile target);

        // --- Shared state machine ---

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            var target = agent.BB.TargetTile;

            // Find a target if we don't have one yet
            if (target == null) {
                target = FindTarget(S, agent);
                if (target == null) {
                    // Nothing to sabotage around here - end harmlessly
                    return EndBreakdown(agent, goal);
                }
                agent.BB.ClearMoveTarget();
                agent.BB.TargetTile = target;
                return InProgress;
            }

            // Target lost (deconstructed / destroyed) - end without effect
            if (!target.IsActive || !target.IsConstructed) {
                agent.BB.TargetTile = null;
                agent.BB.Bool1 = false;
                return EndBreakdown(agent, goal);
            }

            // Walking away from the target before the breakdown ends
            if (agent.BB.Byte1 == PhaseLeave) {
                if (EntityUtils.IsNearObj(agent.PosIdx, target)
                        && ticks < agent.BB.Long1NotSaved) {
                    MoveAway(S, agent);
                    return InProgress;
                }
                return EndBreakdown(agent, goal);
            }

            // Approach the target
            if (!EntityUtils.IsNearObj(agent.PosIdx, target)) {
                if (!TryGoToOnce(agent, target)) {
                    // Can't reach this one, look for another. The search does not know doors
                    // the being may not pass, so it would find this one again and again: a
                    // raider stood in a sabotage breakdown for days (STA-1753)
                    if (agent.BB.MoveProgress?.HasFailed != false) {
                        agent.BB.AddToBlacklist(target.Id);
                    }
                    agent.BB.ClearMoveTarget();
                    agent.BB.TargetTile = null;
                }
                return InProgress;
            }

            // Arrived: arm the sabotage once and start lingering
            if (!agent.BB.Bool1) {
                agent.BB.Bool1 = true;
                FacePosition(agent, target.Position);
                OnArm(agent, target);
                agent.BB.Long1NotSaved = ticks + Rng.URange(
                    Consts.TicksPer10Minutes, Consts.TicksPer20Minutes);
                return InProgress;
            }

            // Linger near the target for a bit
            if (ticks < agent.BB.Long1NotSaved) {
                FacePosition(agent, target.Position);
                return InProgress;
            }

            // Done lingering: start walking away (effect fires when breakdown ends)
            agent.BB.Byte1 = PhaseLeave;
            // Hard cap so we don't loop forever if the being gets stuck
            agent.BB.Long1NotSaved = ticks + Consts.TicksPer10Minutes;
            MoveAway(S, agent);
            return InProgress;
        }

        private Tile FindTarget(GameState S, AIAgentComp agent) {
            return S.Query.FindTileNearby.Ask(
                agent.PosIdx, agent.Motion.IsGrounded, SearchDepth,
                WorldLayer.Objects, (t) => {
                    if (!t.IsConstructed) { return false; }
                    if (agent.BB.IsBlacklisted(t.Id)) { return false; }
                    // Don't send the being somewhere it could actually die reaching it
                    // (e.g. a reactor only accessible through open space/vacuum).
                    if (!agent.BeingData.Safety.IsActuallySafeAt(t.PosIdx)) { return false; }
                    return IsSabotageTarget(t);
                });
        }

        // Selection-time check: only assign this breakdown if there's a target the
        // being can actually reach safely right now.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return FindTarget(agent.S, agent) != null;
        }

        private void MoveAway(GameState S, AIAgentComp agent) {
            agent.BB.ClearMoveTarget();
            if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(2, 5), true)) {
                return;
            }
            var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
            if (pos != Pos.Invalid) {
                agent.Motion.TryGoTo(pos,
                    PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK);
            }
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Bool1 = false;
            agent.BB.Byte1 = PhaseSeek;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // The breakdown is ending. If the sabotage was armed and the target
            // still exists and passes the extra guard, perform the effect.
            if (agent.BB.Bool1) {
                var target = agent.BB.TargetTile;
                if (target != null && target.IsActive && target.IsConstructed
                        && CanSabotage(target)) {
                    PerformSabotage(agent, target);
                    The.Dialog.ShowSimple(target.Definition.Preview,
                        T.DeviceWasSabotaged_V1.F(Texts.LinkToTile(target)));
                }
            }
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Bool1 = false;
            agent.BB.Byte1 = PhaseSeek;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Blacklist?.Clear();
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
