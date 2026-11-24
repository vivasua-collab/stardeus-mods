using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintAdCancel : Hint {
        public const string Id = "ad_cancel";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintAdCancel());
        }
    }
}
