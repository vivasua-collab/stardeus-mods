using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintConnectTool : Hint {
        public const string Id = "el_connect_tool";
        protected override string id => Id;
        protected override bool HasCooldown => false;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintConnectTool());
        }
    }
}
