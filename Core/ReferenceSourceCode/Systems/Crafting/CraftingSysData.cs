using System.Collections.Generic;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Crafting {
    [MessagePackObject]
    public class CraftingSysData {
        [Key("Orders")] public List<CraftingOrderData> Orders;
    }
}
