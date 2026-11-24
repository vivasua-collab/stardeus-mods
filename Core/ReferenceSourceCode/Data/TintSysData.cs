using System.Collections.Generic;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class TintSysData {
        [Key("Color")] public Color Color;
        [Key("Layer")] public byte Layer;
        [Key("FloorTint")] public Dictionary<int, Color> FloorTint;
        [Key("WallTint")] public Dictionary<int, Color> WallTint;
        [Key("SavedColors")] public List<Color> SavedColors;
    }
}
