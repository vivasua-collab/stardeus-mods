using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.Trade {
    [MessagePackObject]
    public sealed class TradeSysData {
        [Key("LastMerchantId")] public uint LastTraderId;
        [Key("Traders")] public Dictionary<uint, Trader> Traders;
        [Key("MatAvailability")] public Dictionary<int, MatAvailability> MatAvailability;
        [Key("OnlineOrders")] public List<OnlineOrder> OnlineOrders;
        [Key("OnlineOrdersCompleted")] public List<OnlineOrder> OnlineOrdersCompleted;
    }
}
