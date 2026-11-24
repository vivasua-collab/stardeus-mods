using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintManagePriority : Hint {
        public const string Id = "haul_manage_priority";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintManagePriority());
        }
    }
}
