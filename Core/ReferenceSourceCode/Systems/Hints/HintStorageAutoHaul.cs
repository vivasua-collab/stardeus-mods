using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintStorageAutoHaul : Hint {
        public const string Id = "storage_auto_haul";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintStorageAutoHaul());
        }
    }
}
