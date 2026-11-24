using Game.AI;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // Shared state machine for mental breakdowns where the being goes from device
    // to device and does the same thing to each: pick a target, walk to it, fiddle
    // with it for a while, act, move on to the next one.
    //
    // This is different from AIBreakdownActionSabotageDevice, which hits a single
    // device and delays the effect until the saboteur has left. A spree is out in
    // the open - the effect is immediate and the perpetrator is named every time,
    // so the player knows whom to stop and what to undo.
    //
    // Concrete subclasses supply the target search, the target rules and the
    // effect. Only concrete subclasses carry [SelfInit].
    public abstract class AIBreakdownActionDeviceSpree : AIMentalBreakdownAction {
        // How many devices the being goes through before calming down
        protected virtual int MinTargets => 3;
        protected virtual int MaxTargets => 5;
        // How long the being spends at each device before the effect happens
        protected virtual int ActTicks => Consts.TicksPerMinute * 2;
        // Hard cap, so a spree with hard to reach targets still ends
        protected virtual int MaxDurationTicks => Consts.TicksPer4Hours;
        protected virtual int MinDurationTicks => Consts.TicksPerHour;

        protected AIBreakdownActionDeviceSpree(string actId, System.Func<string> nameT)
                : base(actId, nameT) { }

        // --- Variation hooks ---

        // Picks the next device. Must only return tiles that pass IsSpreeTarget.
        protected abstract Tile FindTarget(AIAgentComp agent);

        // Rules specific to this spree. IsUsable has already passed when this is called.
        protected abstract bool IsSpreeTarget(AIAgentComp agent, Tile t);

        // The effect. Called once per device, when the being is done fiddling with it.
        protected abstract void ActOn(AIAgentComp agent, Tile target, long ticks);

        // Called every tick while the being is busy with the device (animations)
        protected virtual void WhileActing(AIAgentComp agent, Tile target, long ticks) { }

        // --- Shared rules ---

        // Rules every spree shares: the device must exist, be finished, not be
        // already visited or proven unreachable, and standing next to it must
        // not kill the being.
        protected bool IsValidTarget(AIAgentComp agent, Tile t) {
            if (t == null || !t.IsActive || !t.IsConstructed || t.IsImmutable) {
                return false;
            }
            if (agent.BB.IsBlacklisted(t.Id)) { return false; }
            if (!agent.BeingData.Safety.IsActuallySafeAt(t.PosIdx)) { return false; }
            return IsSpreeTarget(agent, t);
        }

        // Selection-time check: only assign the breakdown when there is at least
        // one device to go after right now.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return FindTarget(agent) != null;
        }

        // --- Shared state machine ---

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (ticks >= agent.BB.Long1) {
                return EndBreakdown(agent, goal);
            }
            // Byte1: devices done so far, Int1: how many this spree is after
            if (agent.BB.Byte1 >= agent.BB.Int1) {
                // A spree with a single device in reach would be over in a minute.
                // The being stays agitated for a while either way.
                var startedAt = agent.BB.Long1 - MaxDurationTicks;
                if (ticks - startedAt < MinDurationTicks) {
                    Wander(S, agent);
                    return InProgress;
                }
                return EndBreakdown(agent, goal);
            }

            var target = agent.BB.TargetTile;
            // Target lost or changed state while we were on the way
            if (target != null && !IsValidTarget(agent, target)) {
                DropTarget(agent, target);
                target = null;
            }
            if (target == null) {
                target = FindTarget(agent);
                if (target == null) {
                    // Nothing left to go after. Marking the spree as done (instead of
                    // checking again next tick) keeps the target search, which scans
                    // every device on the map, out of the per-tick path.
                    agent.BB.Byte1 = (byte) agent.BB.Int1;
                    return InProgress;
                }
                agent.BB.ClearMoveTarget();
                agent.BB.TargetTile = target;
            }

            if (!EntityUtils.IsNearObj(agent.PosIdx, target)) {
                agent.BB.Bool1NotSaved = false;
                if (!TryGoToOnce(agent, target)) {
                    // Can't reach this one - look for another
                    DropTarget(agent, target);
                }
                return InProgress;
            }

            FacePosition(agent, target.Position);
            // Bool1NotSaved: is busy with the device, Long1NotSaved: since when.
            // Neither is saved, so after a load the being simply starts over.
            if (!agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = true;
                agent.BB.Long1NotSaved = ticks;
            }
            if (ticks - agent.BB.Long1NotSaved < ActTicks) {
                WhileActing(agent, target, ticks);
                return InProgress;
            }

            ActOn(agent, target, ticks);
            DropTarget(agent, target);
            agent.BB.Byte1++;
            return InProgress;
        }

        private static void Wander(GameState S, AIAgentComp agent) {
            agent.BB.ClearMoveTarget();
            if (Rng.UChance(0.5f)) { return; }
            if (!AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                if (pos != Pos.Invalid) {
                    agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK);
                }
            }
        }

        private static void DropTarget(AIAgentComp agent, Tile target) {
            agent.BB.AddToBlacklist(target.Id);
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Bool1NotSaved = false;
        }

        // The ship log is the only lasting record of who did what, and the fanfare
        // is what makes the player look. A dialog per device would be too much.
        protected static void Announce(AIAgentComp agent, string text) {
            var line = Texts.Yellow(text);
            agent.S.Sig.ShowFanfare.Send(line);
            agent.S.Sys.Log.AddLine(agent.Being, line);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = Rng.URange(MinTargets, MaxTargets + 1);
            agent.BB.Long1 = ticks + MaxDurationTicks;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Blacklist?.Clear();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = 0;
            agent.BB.Long1 = 0;
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
