using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.UI;

namespace Game.Systems.AI {
    public sealed class AIActionIdleDock : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionIdleDock(IdleActId, AIVarsH.IsIdle, () => T.ActBotdock));
            RegisterAction(new AIActionIdleDock(ManualActId, AIVarsH.IsDocked, () => T.ActBotdock));
            RegisterAction(new AIActionIdleDock(RechargeActId, AIVarsH.IsRecharged, () => $"{T.ActRecharge} ({T.Dock})"));
        }
        public const string RechargeActId = "RechargeAtDock";
        public const string IdleActId = "IdleDock";
        public static int IdleActIdH = Hashes.S(IdleActId);
        public static int RechargeActIdH = Hashes.S(RechargeActId);
        public const string ManualActId = "ManualDock";
        private readonly int aiVar;
        private AIActionIdleDock(string actId, int aiVar, Func<string> title) : base(actId, () => title()) {
            this.aiVar = aiVar;
            Preconditions = AIState.With(aiVar, false);
            Outcomes = AIState.With(aiVar, true);
            WithRequiredFlags(StatFlags.Robotic);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.ShowThoughtBubble(IconId.CDock, T.Docking);
            agent.BB.ClearMoveTarget();
            if (goal.Type == AIGoalType.Idle) {
                goal.WithInterrupt(0, 0);
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var dock = S.Query.FindBotDock.Ask(agent.BeingData);
            if (dock == null || !dock.CanDock(agent.Dockable)) {
                if (dock != null) {
                    // possibly unpowered / disconnected, or has deficit - let's fall back to charge station for a while
                    agent.Motion.StoreNavFailureAt(dock.Tile.PosIdx);
                }
                return Failed(T.AdRejDockNotAvailable);
            }

            if (TryGoToOnce(agent, dock.Tile.PosIdx, true)) {
                agent.Dockable.SetDockingNow(true);
                return InProgress;
            } else {
                // we are either arrived or not there at all
                if (agent.PosIdx != dock.Tile.PosIdx) {
                    return FailedNavToTarget(agent);
                }
            }

            // if we went to the dock for a recharge, we will wait until we have
            // full charge
            if (aiVar == AIVarsH.IsRecharged && dock.IsDocked(agent.Dockable)) {
                var stats = agent.BeingData.StatsBeing;
                if (stats.BatteryCharge.Value >= stats.BatteryCapacity.Value) {
                    goal.WithWorld(aiVar, true);
                    return Success;
                } else {
                    return InProgress;
                }
            }

            if (dock.Dock(agent.Dockable)) {
                if (aiVar == AIVarsH.IsRecharged) {
                    return InProgress;
                }
                goal.WithWorld(aiVar, true);
                return Success;
            } else if (agent.PosIdx != dock.Tile.PosIdx) {
                return FailedNavToTarget(agent, dock.Tile.PosIdx);
            } else {
                return Failed(T.AdRejDockNotAvailable);
            }
        }

        private void AddCooldown(AIAgentComp agent, long ticks) {
            var bb = agent.BB;
            if (aiVar == AIVarsH.IsRecharged) {
                var stats = agent.BeingData.StatsBeing;
                if (stats.BatteryCharge.Value >= stats.BatteryCapacity.Value * 0.99f) {
                    bb.SetCooldown(IdH, ticks,
                        Consts.TicksPer2Hours, Consts.TicksPer3Hours);
                } else {
                    // recharge ended early (interrupted / failed) — allow a
                    // prompt retry instead of locking the bot out for hours
                    bb.SetCooldown(IdH, ticks,
                        Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                }
            } else {
                bb.SetCooldown(IdH, ticks,
                    Consts.TicksPer30Minutes, Consts.TicksPerHour);
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (aiVar != AIVarsH.IsDocked) {
                AddCooldown(agent, ticks);
            }
            agent.Dockable.SetDockingNow(false);
            agent.BB.ClearMoveTarget();
            agent.HideThoughtBubble();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // for non-idle action
            if (aiVar == AIVarsH.IsDocked) {
                return 1f;
            }
            if (agent.BeingData.Species.IsBiological) { return 0f; }
            var d = agent.BeingData.Dockable;
            if (d.NoAutoDock) { return 0f; }
            if (!agent.BeingData.EffectiveFaction.IsPlayer) {
                return 0f;
            }
            if (d.DockId != 0) {
                if (aiVar == AIVarsH.IsRecharged) {
                    var dockComp = agent.S.Components.Find<BotDockComp>(d.DockId);
                    if (dockComp != null
                            && agent.Motion.HadNavFailureAt(dockComp.Tile.PosIdx)) {
                        // Assigned dock was recently proven unreachable, so let a
                        // charge station plan win until nav-failure memory expires.
                        return 0f;
                    }
                    // *5 is less than recharge at a charge station, which does * 10
                    return (agent.Being.Data.StatsBeing.BatteryCharge.Value + 0.001f) * 5f;
                }
                if (!agent.BB.IsCoolingDown(IdH, ticks)) {
                    // Dock is assigned, we'll haul ass there
                    return 0.001f;
                }
                return 0f;
            } else if (d.NoAutoAssign || aiVar == AIVarsH.IsRecharged)  {
                // no dock and we don't want to auto assign, or it's a recharging task
                return 0f;
            }
            return CachedRandomCostWithCoolDown(agent.BB, ticks, Consts.TicksPer4Hours,
                0.001f, 0.01f);
        }
    }
}
