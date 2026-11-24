using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems {
    /// <summary>
    /// Keeps OperatableDeviceComp.AllowedMindIds free of operators that no longer exist
    /// or are dead, so a device never gets stuck restricted to someone who can't work it.
    /// </summary>
    public sealed class DeviceOperatorSys : GameSystem {
        public const string SysId = "DeviceOperatorSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new DeviceOperatorSys());
        }

        private readonly List<OperatableDeviceComp> devices = new();

        protected override void OnInitialize() {
            S.Sig.BeingDied.AddListener(OnBeingDied);
            S.Sig.AfterLoadState.AddListener(OnAfterLoadState);
        }

        public override void Unload() {
            devices.Clear();
        }

        private void OnBeingDied(BeingData being) {
            if (being?.Mind == null) { return; }
            RemoveMindFromAllDevices(being.Mind.Id);
        }

        private void OnAfterLoadState(GameState state) {
            // Repairs saves that already carry a dead/gone operator in their whitelist.
            Ready.When(WhenH.BeingsLoaded, PruneDeadOperators);
        }

        private void RemoveMindFromAllDevices(uint mindId) {
            devices.Clear();
            S.Components.FindAll(devices);
            for (int i = 0; i < devices.Count; i++) {
                devices[i].TryRemoveAllowedMindId(mindId);
            }
            devices.Clear();
        }

        private void PruneDeadOperators() {
            devices.Clear();
            S.Components.FindAll(devices);
            for (int i = 0; i < devices.Count; i++) {
                PruneDevice(devices[i]);
            }
            devices.Clear();
        }

        private void PruneDevice(OperatableDeviceComp op) {
            var ids = op.AllowedMindIdsActual;
            if (Arrays.IsEmpty(ids)) { return; }
            // Iterate a snapshot: TryRemoveAllowedMindId swaps op's live array, but the
            // values already read out of this one stay valid.
            for (int i = ids.Length - 1; i >= 0; i--) {
                var mindId = ids[i];
                if (!IsValidOperator(mindId)) {
                    op.TryRemoveAllowedMindId(mindId);
                }
            }
        }

        private bool IsValidOperator(uint mindId) {
            var mind = S.Query.FindMind.Ask(mindId);
            return mind?.Owner != null && !mind.Owner.HasStateFlag(BeingState.Dead);
        }
    }
}
