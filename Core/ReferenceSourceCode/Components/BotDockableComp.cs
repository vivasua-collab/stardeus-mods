using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using KL.Utils;
using Game.UI;

namespace Game.Components {
    public sealed class BotDockableComp : BaseComponent<BotDockableComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BotDockableComp());
        }

        public uint BeingDataId => Being.Data.Id;
        public bool IsDockingNow;
        public bool HasDockingGoal => Being.AIAgent.Goal?.DesiredState.Try(AIVarsH.IsDocked, out _) == true;
        private BotDockComp assignedDock;
        public BotDockComp AssignedDock {
            get {
                var dockId = Being.Data.Dockable.DockId;
                if (dockId == 0) { return null; }
                if (assignedDock != null && assignedDock.EntityId != dockId) {
                    assignedDock = null;
                }
                if (assignedDock == null) {
                    assignedDock = S.Components.Find<BotDockComp>(dockId);
                    if (assignedDock == null) {
                        D.Err("Assigned dock is not available by id {0} for {1}", dockId, Being);
                        S.Query.AssignBotDock.Ask(Being.Data, null);
                        Being.Data.Dockable.DockId = 0;
                        assignedDock = null;
                    }
                }
                return assignedDock;
            }
            // set {
            //     if (value == null || !value.Tile.IsActive) {
            //         if (Being.Data.Dockable.DockId != 0) {
            //             D.Warn("Unassigning dock for {0}", this);
            //         }
            //         assignedDock = null;
            //         Being.Data.Dockable.DockId = 0;
            //     } else {
            //         D.Warn("Assigning dock for {0}", this);
            //         Being.Data.Dockable.DockId = value.EntityId;
            //         assignedDock = value;
            //     }
            //     UpdateUIBlock(true);
            // }
        }
        public bool IsDocked => AssignedDock?.IsDocked(this) == true;

        public override string ToString() {
            return $" * BotDockable[{Being.Data.Dockable}]";
        }

        public override void OnLateReady(bool wasLoaded) {
        }

        public void UpdateUIBlock(bool wasUpdated) {
            S.Sig.DockableStateChanged.Send(BeingDataId);
        }

        public void TryOrderDocking() {
            if (IsDockingNow || HasDockingGoal) {
                UISounds.PlayDigitalConfirm();
                return;
            }
            var g = AIGoal.CreatePersonal(JobTypeIdH.Task, IconId.CDock,
                    T.ActBotdock, Being.AIAgent, G.Ticks)
                .WithWorldToDesired(AIVarsH.IsDocked, false, true)
                .WithoutRetry()
                .ForceAssigned();
            // forced docking should clear the cooldown
            Being.AIAgent.BB.Cooldowns.Remove(Hashes.S(AIActionIdleDock.ManualActId));
            if (!S.Query.ForceGoal.Ask(g, true)) {
                UISounds.PlayActionDenied();
            } else {
                UISounds.PlayDigitalConfirm();
            }
            UpdateUIBlock(true);
        }

        public void SetDockingNow(bool v) {
            // every notification reloads an open dock tab, so skip the ones that change nothing
            if (IsDockingNow == v) { return; }
            IsDockingNow = v;
            UpdateUIBlock(true);
            AssignedDock?.UpdateUIBlock(true);
        }
    }
}
