using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Text;
using System;

namespace Game.Systems.Motion {
    public sealed class MotionComp : BaseComponent<MotionComp>, IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MotionComp());
        }

        private MotionControl control;

        public override void OnLateReady(bool wasLoaded) {
            control = S.Query.RegisterMovable.Ask(this);
            control.EntityId = EntityId;
            Being.Motion = control;
            control.Being = Being;
            control.Graphics = Being.Graphics;
        }

        public override void OnRemove() {
            // D.Warn("MotionComp on remove: {0}", Being);
            if (Being.HasData && Being.IsAlive) {
                control.Remove();
            } else {
                control?.ReleaseClaimedPos();
                control?.ReleaseDestination();
            }
            S.Query.UnregisterMovable.Ask(control);
            control = null;
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            control.UpdatePosIdx(pos, dx, dy);
        }

        #if !PRODUCTION_BUILD
        public string GetMotionDebugInfo() {
            var sb = new StringBuilder();
            control.DumpNavFailMemory(sb);
            sb.AppendFormat("Permissions: {0}\n", control.PermissionsInfo);
            sb.AppendLine(D.DumpInstance(control.Position));
            sb.AppendFormat("Flags: {0}\n", control.Flags);
            if (control.ImmobilizeReasons != null) {
                sb.Append("ImmobilizedReasons: ");
                foreach (var r in control.ImmobilizeReasons) {
                    sb.Append(ImmobilizeReasonIdH.ResolveName(r));
                    sb.Append(',');
                }
                sb.AppendLine();
            }
            sb.AppendLine(D.DumpInstance(control.Progress));
            sb.Append("Velocity: ");
            sb.AppendLine(control.Velocity.ToString());
            sb.Append("Velocity%: ");
            sb.AppendLine(Units.Percentage(control.VelocityPercent));
            sb.AppendLine("Nav Failures:");
            sb.AppendLine(EntityUtils.DumpPositions(control.NavFailureMemory?.Keys));
            return sb.ToString();
        }
        #endif

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.EntityDeselected) {
                control?.HidePathViz();
            }
            if (message == MsgIdH.CarryStarted) {
                control?.ReleaseClaimedPos();
                control?.ReleaseDestination();
            }
            if (message == MsgIdH.BodyConditionChanged || message == MsgIdH.BeingFinishedLoading) {
                control?.UpdatePermissions();
            }
        }

        public void CreateRescueGoal() {
            if (HasAnyGoal || Being.IsDead || !Being.Data.IsFriendly) {
                return;
            }
            var goal = CreateGoal(JobTypeIdH.Rescue, IconId.CPropulsion,
                    T.RescueGroundedInSpace, this)
                .WithWorldToDesired(AIVarsH.IsStrandedInSpace, true, false)
                .WithPriority(4)
                .WithRequiredAbilities(AbilityIdH.Carry)
                .WriteInitialState();
            goal.WithInterrupt(2, 0);
            // D.Warn("Creating rescue goal for {0}", Being);
            entity.TrySetGoal(goal);
        }
    }
}
