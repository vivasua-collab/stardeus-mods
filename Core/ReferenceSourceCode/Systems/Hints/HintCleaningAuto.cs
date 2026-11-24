using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintCleaningAuto : Hint {
        public const string Id = "cleaning_auto";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintCleaningAuto());
        }
    }
}
