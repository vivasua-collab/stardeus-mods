using System.Collections.Generic;
using Game.Components;
using MessagePack;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceSysData {
        [Key("Map")] public SpaceMapData Map;
        // state of the current sector's effects that implement ISaveable, by effect id
        [Key("Effects")] public Dictionary<string, ComponentData> Effects;
    }
}
