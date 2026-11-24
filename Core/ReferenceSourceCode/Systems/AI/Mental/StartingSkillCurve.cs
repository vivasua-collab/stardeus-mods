using System;

namespace Game.Systems.Mental {
    [Serializable]
    public struct StartingSkillCurve {
        public string[] Jobs;
        public float[] LevelCurve;
    }
}
