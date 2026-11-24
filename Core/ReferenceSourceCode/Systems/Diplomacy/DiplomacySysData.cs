using System.Collections.Generic;
using Game.Data.Factions;
using MessagePack;

namespace Game.Systems.Diplomacy {
    [MessagePackObject]
    public class DiplomacySysData {
        [Key("Attitudes")] public Dictionary<string, Dictionary<string, float>> Attitudes;
        /// <summary>
        /// Symmetric faction relations, stored in both directions. Values are
        /// FactionRelation cast to int.
        /// </summary>
        [Key("Relations")] public Dictionary<string, Dictionary<string, int>> Relations;
        /// <summary>
        /// Faction id => tick until which the player may not propose an alliance again.
        /// </summary>
        [Key("AllianceCooldowns")] public Dictionary<string, long> AllianceCooldowns;
        /// <summary>
        /// Incremented on every universe regeneration. Feeds the era bias hash so a
        /// regenerated universe gets a fresh diplomatic schedule - nothing else in
        /// GameState reliably changes when the universe is regenerated.
        /// </summary>
        [Key("UniverseGen")] public int UniverseGen;
    }
}
