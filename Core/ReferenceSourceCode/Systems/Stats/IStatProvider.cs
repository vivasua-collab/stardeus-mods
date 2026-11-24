using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Systems.Stats {
    /// <summary>
    /// For components that want to display various stats to the player in
    /// Full Stats list. Only for non-being entities.
    /// </summary>
    public interface IStatProvider : IComponent {
        /// <returns>provider category / name</returns>
        string StatsCategoryName { get; }
        void AddStats(Lifetime life, WGroup block, ref int idx);
        /// <summary>
        /// Smaller number = higher up / more important
        /// </summary>
        int StatsOrder { get; }
    }
}
