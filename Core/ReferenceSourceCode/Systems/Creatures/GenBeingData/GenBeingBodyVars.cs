using System;

namespace Game.Systems.Creatures {
    [Serializable]
    public struct GenBeingBodyVars {
        public float ChanceModded;
        // TODO add scars
        public float ChanceScarred;
        /// <summary>
        /// if above 0, for biologicals, some parts will be robotic, for robots,
        /// some parts will be biological
        /// </summary>
        public float ChanceBionic;
        /// <summary>
        /// if above 0, some body parts may be replaced with non-standard items
        /// example: medium heart -> large heart for human
        /// or tracks -> mini jet for robot
        /// </summary>
        public float ChanceNonStandard;
        public float ChanceHighQualityParts;
        public float ChanceLowQualityParts;
        /// <summary>
        /// if above 0, some body parts may be replaced with wrong species items.
        /// Only for biologicals.
        /// Example: human with cat eyes
        /// </summary>
        public float ChanceFreak;
        /// <summary>
        /// if above 0, some there will be a chance to generate mind that belongs
        /// to another species
        /// </summary>
        public float ChanceCustomMind;
        /// <summary>
        /// chance to have something in the implant slot
        /// </summary>
        public float ChanceImplant;
    }
}
