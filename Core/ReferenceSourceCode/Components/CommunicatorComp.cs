using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Mods;

namespace Game.Components {
    public sealed class CommunicatorComp : BaseComponent<CommunicatorComp>,
            IDefPowerProvider, IOperatorWorkSource {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CommunicatorComp());
        }
        private const float DefaultEnergyCost = 5f;
        private float energyCost;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * Communicator";
        }

        private ElectricNodeComp elNode;
        private OperatableDeviceComp operatable;
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                    elnode.AddAfterTick(this, AfterTickGrid);
                    AfterTickGrid();
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (!elNode.IsConnected || !elNode.IsSwitchedOn) {
                    // check if there are any other communicators, and if not,
                    // kill the ping
                    var allComms = S.Components.AllByType<CommunicatorComp>();
                    bool hasActiveComms = false;
                    if (allComms != null) {
                        foreach (CommunicatorComp comm in allComms) {
                            if (comm == this) { continue; }
                            if (comm.elNode == null) { continue; }
                            if (!comm.elNode.IsConnected) { continue; }
                            if (!comm.elNode.IsSwitchedOn) { continue; }
                            hasActiveComms = true;
                            break;
                        }
                    }
                    if (!hasActiveComms) {
                        S.BB.Remove(BBKeyH.CommunicatorPing, 0L);
                        S.BB.Remove(BBKeyH.CommunicatorOperatorPing, 0L);
                        D.Warn("Removing communicator ping");
                    }
                }
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out operatable)) {
                ModRegistry.WarnAboutBrokenMod(entity.Definition,
                    "Operatable device is required for Communicator comp");
            }
            if (wasLoaded && Tile.ENode.IsSwitchedOn && Tile.ENode.IsConnected) {
                S.BB.Set(BBKeyH.CommunicatorPing, G.Ticks);
            }
        }

        private void AfterTickGrid() {
            if (elNode.IsConsuming) {
                S.BB.Set(BBKeyH.CommunicatorPing, G.Ticks);
                CheckWantsOperator();
                if (operatable.Operator != null) {
                    S.BB.Set(BBKeyH.CommunicatorOperatorPing, G.Ticks);
                }
            }
        }

        public void CheckWantsOperator() {
            if (S == null || operatable == null || elNode == null
                    || !elNode.IsConsuming) {
                return;
            }
            if (IsCallOngoing) {
                operatable.HailOperator(true);
            } else {
                operatable.DismissOperator(true);
            }
        }

        // a hail waiting for an answer or a conversation with another ship
        private bool IsCallOngoing => G.Ticks < S.BB.Get(BBKeyH.CommunicatorNeedsOperator, 0L);
        public bool IsOperatorWorking => elNode != null && elNode.IsConsuming && IsCallOngoing;
    }
}
