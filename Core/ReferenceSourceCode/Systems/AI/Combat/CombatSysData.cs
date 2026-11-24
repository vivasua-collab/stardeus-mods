using Game.Systems.Combat;
using MessagePack;

namespace Game.Systems.AI.Combat {
    [MessagePackObject]
    public class CombatSysData {
        [Key("Flags")] public CombatFlags Flags;
        [Key("Armies")] public ArmyData[] Armies;
        [Key("LastHackConversion")] public long LastHackConversionTick;
        // CombatAlerts: the stage (HostileAlert, 0 for saves from before it) and the last contact
        [Key("Alert")] public byte Alert;
        [Key("LastContact")] public long LastContactTick;
    }
}
