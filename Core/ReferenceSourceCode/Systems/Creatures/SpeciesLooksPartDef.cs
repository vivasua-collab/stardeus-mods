using System;
using UnityEngine;

namespace Game.Systems.Creatures {
    [Serializable]
    public class SpeciesLooksPartDef {
        public string PartType;
        private LookPartTypeDef type;
        public LookPartTypeDef Type => type ??= LookPartTypeDef.Get(PartType);
        public float Scale;
        public Vector2 Offset;
        public string PositionFilter;
        public bool RenderSorted;

        public bool MobilizerAffected;
        /// <summary>
        /// Alternative pos filter for MobilizerVisualComp
        /// </summary>
        public string MobilizerAltPosFilter;
        public string MobilizerLooksDefault;
        public string MobilizerLooksAlt;
        /// <summary>
        /// This one can be left as null, in that case Default looks would be used instead of empty
        /// </summary>
        public string MobilizerLooksEmpty;
        /// <summary>
        /// Mostly for moving shadows up and down. This value is ADDED to the Offset.y when alternative looks are visible
        /// </summary>
        public float MobilizerAltOffsetY;
        public float MobilizerEmptyOffsetY;
        /// <summary>
        /// Hides the part entirely if alternative mobilizer is installed (for hiding shadows of flying bots)
        /// </summary>
        public bool MobilizerAltHidden;
        /// <summary>
        /// For hiding treaded robot's shadow when no alternative mobilizer is installed
        /// </summary>
        public bool MobilizerDefaultHidden;
    }
}
