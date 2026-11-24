using System;
using KL.Collections;

namespace Game.Data {
    public sealed class StateTempVars {
        /// <summary>
        /// Set to true when a save file from different major version is being loaded
        /// </summary>
        public bool IsLoadingLegacySave;
        public long LastAreaChangeTick;
        public bool IsTemplateMode;
        public double LastInputTime;
        public bool IsHoveringEntity;
        public int ShipCombatEnemyCount;
        public bool IsShowingCutscene;
        /// <summary>
        /// GameTimer.time at the moment of entry into hyperspace
        /// </summary>
        public float HyperspaceEnterGameTime;
    }
}
