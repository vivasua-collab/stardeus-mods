using System.Collections.Generic;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class NPCShipSysData {
        /// <summary>
        /// When this version is old, active NPC ship loadouts can be regenerated from
        /// class, name, tag, faction, and preserved world state.
        /// </summary>
        public const uint CurrentGenVersion = 4;
        [Key("GenVersion")] public uint GenVersion;
        [Key("LastShipId")] public uint LastShipId;
        [Key("Ships")] public List<NPCShipData> Ships;
        [Key("HyperTrails")] public List<ShipHyperTrailData> HyperTrails;
        [Key("UnlockedClasses")] public List<string> UnlockedClasses;
        [Key("ReactionCooldowns")] public Dictionary<int, long> ReactionCooldowns;
    }
}
