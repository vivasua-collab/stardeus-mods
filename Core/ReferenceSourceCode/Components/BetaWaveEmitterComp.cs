using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Utils;
using Game.CodeGen;

namespace Game.Components {
    /// <summary>
    /// Broadcasts the beta wave carrier that <see cref="BetaWavesSys"/> turns into
    /// ship wide biological obedience. The emitter itself holds no state - it only
    /// reports whether it is currently powered. Rogue AI tech.
    /// </summary>
    public sealed class BetaWaveEmitterComp : BaseComponent<BetaWaveEmitterComp>,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BetaWaveEmitterComp());
        }

        private const float DefaultEnergyCost = 400f;
        private float energyCost;
        private ElectricNodeComp elNode;
        private BetaWavesSys sys;
        private bool isRegistered;
        private bool isEmitting;

        /// <summary>
        /// A constructed, powered, undamaged emitter. Read every tick by the system.
        /// </summary>
        public bool IsEmitting => isEmitting;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed) {
                RegisterEmitter(true);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp node) {
                    elNode = node;
                    elNode.AddAfterTick(this, AfterTickGrid);
                    elNode.SetConsumerWantedInput(energyCost);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                RegisterEmitter(true);
            } else if (message == MsgIdH.ElectricNodeSwitchedOff
                    || message == MsgIdH.ElectricNodeConnectionChanged) {
                // A node that leaves its grid stops running AfterTickGrid, so the
                // broadcast has to be dropped here rather than waiting for a tick
                // that will never come.
                if (elNode?.IsConsuming != true) {
                    SetEmitting(false);
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                SetEmitting(false);
                RegisterEmitter(false);
            }
        }

        public override void OnRemove() {
            SetEmitting(false);
            RegisterEmitter(false);
        }

        private void RegisterEmitter(bool add) {
            // The early return keeps OnRemove from looking the system up on an emitter
            // that never registered - by then the system may already be unloaded.
            if (isRegistered == add) { return; }
            isRegistered = add;
            sys ??= S.Sys.Get<BetaWavesSys>(BetaWavesSys.SysId);
            sys?.RegisterEmitter(this, add);
        }

        private void AfterTickGrid() {
            SetEmitting(isRegistered && elNode.IsConsuming
                && !Tile.IsPendingMove && !Tile.Constructable.HasGoal);
        }

        private void SetEmitting(bool emitting) {
            if (isEmitting == emitting) { return; }
            isEmitting = emitting;
            if (emitting) {
                ShowInfoIcon(IconId.CBrainWaves, T.BetaWavesBroadcasting);
            } else {
                HideInfoIcon();
            }
        }

        public override string ToString() {
            return $" * BetaWaveEmitter [Emitting: {isEmitting}]";
        }
    }
}
