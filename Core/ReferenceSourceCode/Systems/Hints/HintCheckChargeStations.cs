using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintCheckChargeStations : Hint {
        public const string Id = "el_check_charge_stations";
        protected override string id => Id;
        protected override bool IsRecurring => true;
        protected override float cooldownTimeSeconds => 180;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintCheckChargeStations());
        }
    }
}
