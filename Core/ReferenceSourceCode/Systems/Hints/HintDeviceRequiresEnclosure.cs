using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintDeviceRequiresEnclosure : Hint {
        public const string Id = "dev_req_enclosure";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDeviceRequiresEnclosure());
        }
    }
}
