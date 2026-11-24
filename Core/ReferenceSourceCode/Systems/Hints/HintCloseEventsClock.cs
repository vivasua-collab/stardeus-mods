using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintCloseEventsClock : Hint {
        public const string Id = "close_events_clock";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintCloseEventsClock());
        }
    }
}
