using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Stats;
using Game.Systems.Notify;

namespace Game.Systems.AI {
    public sealed class AIActionProvideTempConnection : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionProvideTempConnection());
        }
        public const string ActId = "ProvideTempConnection";
        private AIActionProvideTempConnection() : base(ActId, () => T.ActProvidetempgrid) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true)
                .And(AIVarsH.TempConnectionProvided, false);

            Outcomes = AIState.With(AIVarsH.TempConnectionProvided, true);
            WithRequiredAbilities(AbilityIdH.SkilledLabor);
            WithRequiredFlags(StatFlags.Robotic);
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            if (goal.Target is ElectricNodeComp c) {
                c.ToggleWorkerLink(false);
            }
            agent.BB.ClearNotification();
            agent.BB.ClearFX();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target is ElectricNodeComp c) {
                if (!c.Tile.IsActive) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (!EntityUtils.IsNearObj(agent.PosIdx, c.Tile)) {
                    D.Err("Executing AIActionProvideTempConnection while worker is not near! {0} ({1})",
                        goal, goal.WorldState);
                    goal.WithWorld(AIVarsH.IsNear, false);
                    return NeedsReplan(UnreachableBy(agent));
                }
                return DoWork(S, agent, goal, c);
            }
            return Error("Wrong target in provide temp conn goal! {0}", goal.Target);
        }

        private AIActionResult DoWork(GameState S, AIAgentComp agent, AIGoal g, ElectricNodeComp c) {
            if (c.Grid.NumShipCPUs > 0 || agent.TicksInCurrentAction > Consts.TicksPer24Hours) {
                g.WithWorld(AIVarsH.TempConnectionProvided, true);
                return Success;
            }
            if (agent.BB.Bool1NotSaved) {
                return InProgress;
            }
            agent.BB.Bool1NotSaved = true;
            var notif = Notification.Build(IconId.CConnected, T.TempConnectEstablished, 0, NotificationType.Info)
                .Group("tmp_conn")
                .Sticky();
            agent.BB.ShowNotification(notif);
            c.ToggleWorkerLink(true);
            FaceTarget(c, agent);

            if (agent.BB.FX == null) {
                agent.BB.FX = ParticlesVFX.CreateWithBudget(S,
                    "RepairFX", agent.PosExact, BBKeyH.SwarmPlayingVFX,
                        Consts.SwarmMaxVFX).WithDynamicOwnership(agent.Being,
                            () => agent.PosExact);
                var obj = c.Tile;
                var punchDir = obj.Position - agent.PosExact;
                agent.Being.Graphics.Punch(punchDir + Rng.UInsideUnitCircle() * 0.05f,
                    0.1f, Rng.URange(0.7f, 1.3f), Rng.URange(0.7f, 1.3f));
                if (agent.BB.FX.TryGetFX(out var fx)) {
                    fx.SetVector3(ShaderVars.Target, punchDir);
                    var size = obj.Size.magnitude * 0.25f;
                    fx.SetFloat(ShaderVars.Size, size);
                    fx.SetFloat(ShaderVars.EmitRate, 100f * size);
                }
                agent.BB.FX.SetClock(S.Clock);
            }


            if (agent.BB.NextSoundTime < GameTimer.realtimeSinceStartupAsDouble) {
                agent.BB.NextSoundTime = GameTimer.realtimeSinceStartupAsDouble
                    + 5f / S.Clock.FXTimeScale;
                Sounds.PlayRepairs(agent.PosExact);
            }
            return InProgress;
        }
    }
}
