using System.Collections.Generic;
using Game.Systems.Policies;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class PoliciesSysData {
        [Key("Global")] public EquipmentPolicy GlobalEquipment;
        // Keyed by Species.Id rather than its hash so a renamed or reordered
        // species registry can't silently repoint a policy.
        [Key("Species")] public Dictionary<string, EquipmentPolicy> EquipmentBySpecies;
    }
}
