using System.Collections.Generic;
using Game.Data;
using Game.Systems.Ships;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipCombatSysData {
        [Key("CombatStartTick")] public long CombatStartTick;
        [Key("PlayerInitiatedCombat")] public bool PlayerInitiatedCombat;
        [Key("Allies")] public List<uint> Allies;
        [Key("Enemies")] public List<uint> Enemies;
        [Key("NPCShipBattles")] public List<NPCShipBattleData> NPCShipBattles;
        // Armed sector nukes: SpaceObject id -> absolute detonation tick. Null when none.
        [Key("RiggedSOs")] public Dictionary<int, long> RiggedSOs;

        public static ShipCombatSysData Serialize(ShipCombatSys sys) {
            return new ShipCombatSysData {
                CombatStartTick = sys.CombatStartTick,
                PlayerInitiatedCombat = sys.PlayerInitiatedCombat,
                Allies = sys.Allies.ConvertAll(a => a.Id),
                Enemies = sys.Enemies.ConvertAll(e => e.Id),
                NPCShipBattles = sys.NPCShipBattles.ConvertAll(NPCShipBattleData.Serialize),
            };
        }
    }
}
