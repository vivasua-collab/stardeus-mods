using System;
using KL.Utils;

namespace Game.Systems.Story {
    [Serializable]
    public sealed class StoryScheduleModifier {
        public string Schedule;
        public float PointAllowanceMul;
        public float CooldownMul;
        public static readonly StoryScheduleModifier Default = new() {
            PointAllowanceMul = 1f,
            CooldownMul = 1f,
        };

        public void Validate(string diffId) {
            if (PointAllowanceMul == 0f) {
                D.Err("PointAllowanceMul was 0f in {0} for schedule: {1}",
                    diffId, Schedule);
                PointAllowanceMul = 1f;
            }
            if (CooldownMul == 0f) {
                D.Err("CooldownMul was 0f in {0} for schedule: {1}",
                    diffId, Schedule);
                CooldownMul = 1f;
            }
        }
    }
}
