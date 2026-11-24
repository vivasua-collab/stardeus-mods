using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionOperateDevice : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionOperateDevice());
        }
        public const string ActId = "OperateDevice";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionOperateDevice() : base(ActId, () => T.ActOperatedevice) {
            Preconditions = AIState.With(AIVarsH.IsOperatorPresent, false)
                .And(AIVarsH.IsNear, true);
            Outcomes = AIState.With(AIVarsH.IsOperatorPresent, true);
            WithRequiredAbilities(AbilityIdH.Manipulate, AbilityIdH.Intelligence);
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Long1NotSaved = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.Bool1 = false;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.Motion.ClearPosIdxOverride();
            if (goal.Target is not OperatableDeviceComp operatable) {
                return;
            }

            if (operatable == null) { return; }
            operatable.SetOperator(null);
            agent.Motion.TeleportTo(Pos.ToVector(operatable.Tile.Transform.WorkSpot));

            if (agent.BeingData.Species.IsBiological) {
                if (!IsChairAvailable(agent)) {
                    if (Rng.UChance(0.5f)) {
                        if (agent.BeingData.Mind.Health != null) {
                            agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                                GraphicsId.ObjectsFurnitureChairWood_D, ticks,
                                Consts.TicksPerHour, T.NoChairInWorkSpot, -5));
                        }
                    }
                } else {
                    if (Rng.UChance(0.25f)) {
                        if (agent.BeingData.Mind.Health != null) {
                            agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                                GraphicsId.ObjectsFurnitureChairWood_D, ticks,
                                Consts.TicksPerHour, T.ComfortableWorkSpot, 3));
                        }
                    }
                }
            }

            agent.BB.Long1NotSaved = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.Bool1 = false;
        }

        private bool IsChairAvailable(AIAgentComp agent) {
            return agent.BB.Bool1;
        }

        private void SetChairAvailable(AIAgentComp agent) {
            agent.BB.Bool1 = true;
        }

        private bool IsPositioned(AIAgentComp agent) {
            return agent.BB.Bool1NotSaved;
        }

        private void SetPositioned(AIAgentComp agent) {
            agent.BB.Bool1NotSaved = true;
        }

        private long DeficitStartTick(AIAgentComp agent) {
            return agent.BB.Long1NotSaved;
        }
        private void SetDeficitStartTick(AIAgentComp agent, long tick) {
            agent.BB.Long1NotSaved = tick;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target is not OperatableDeviceComp operatable) {
                return Error("Operatable goal is set on device that has no OperatableDeviceComp: {0}", goal);
            }
            var worker = agent.Being;

            if (!operatable.CanBeOperated) {
                goal.WithWorld(AIVarsH.IsOperatorPresent, true);
                return Success;
            }

            if (operatable.Operator == null) {
                operatable.SetOperator(worker);
                operatable.HideInfoIcon();
            }

            var deficitStartTick = DeficitStartTick(agent);
            // Handle deficit cancellation
            if (operatable.Tile.ENode?.HasDeficit == true) {
                if (deficitStartTick == 0) {
                    SetDeficitStartTick(agent, G.Ticks);
                } else {
                    if (G.Ticks - deficitStartTick > Consts.TicksPer30Minutes) {
                        var failmsg = T.OperateFailDeficit_V3.F(
                            operatable.Tile.NameOrLabelCodex,
                            Pos.String(operatable.Tile.Transform.WorkSpotOrPos),
                                Texts.Red(Units.Electricity(
                                    operatable.Tile.ENode.Node.Deficit)));
                        S.Sys.Log.AddLine(worker, failmsg);
                        return Failed(failmsg);
                    }
                }
            } else {
                if (deficitStartTick != 0) {
                    SetDeficitStartTick(agent, 0);
                }
            }

            var tr = operatable.Tile.Transform;
            if (!IsPositioned(agent)) {
                SetPositioned(agent);
                worker.Motion.SetPosIdxOverride(tr.WorkSpot);
                worker.Motion.TeleportTo(operatable.OperatorPosition);
                worker.Graphics.SetFacing(operatable.OperatorFacing);
                var obj = EntityUtils.ObjAt(tr.WorkSpot);
                if (obj != null && obj.TryGetComponent<SurfaceComp>(out var surf)) {
                    if (surf.SlotDesignation == SlotDesignationH.Chair) {
                        SetChairAvailable(agent);
                    }
                }
            } else {
                worker.Graphics.SetFacing(operatable.OperatorFacing);
                Animate(operatable, worker);
                if (agent.Jobs.TryJob(goal.JobIdH, out var jobSlot)) {
                    // a kept operator waiting for work learns nothing
                    var skill = operatable.IsOperatorWorking
                        ? jobSlot.UseSkill(operatable.JobLevelUpSpeed * SkillXp.DeviceWork)
                        : jobSlot.SkillEfficiency();
                    var eff = skill * jobSlot.WorkSpeedMul();
                    if (eff > 0f) {
                        operatable.OperatorEfficiency = eff;
                    }
                }
            }
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.CanIdle) {
                return Rng.UValue;
            }
            return 1000f;
        }

        private void Animate(OperatableDeviceComp operatable, Being worker) {
            switch (operatable.AnimationType) {
                case "Punch": {
                    worker.Graphics.Punch(
                        Facing.ToOffset(operatable.OperatorFacing, 1f),
                        0.1f, 1f, 1f);
                    break;
                }
                case "Strafe": {
                    worker.Graphics.Strafe(
                        Facing.ToOffset(operatable.OperatorFacing, 1f),
                        0.1f, 1f, 1f, 1f, 1f);
                    break;
                }
            }
        }
    }
}
