using System;
using KL.Utils;

namespace Game.Systems.Mental {
    [Serializable]
    public struct StartingJob {
        public string Job;
        public JobType JobType;
        /// <summary>
        /// How much of a starting level anyone gets regardless of any other factors
        /// </summary>
        public Curve Curve;
        /// <summary>
        /// How much age can influence a skill level
        /// </summary>
        public Curve AgeCurve;
    }
}
