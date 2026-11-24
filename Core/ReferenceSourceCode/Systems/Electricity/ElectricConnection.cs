using MessagePack;
using UnityEngine;

namespace Game.Systems.Electricity {
    [MessagePackObject]
    public struct ElectricConnection {
        [IgnoreMember] public bool IsInitialized => Type != ElectricConnectionType.Uninitialized;
        [IgnoreMember] public bool IsValid => NodeIdxs.Item1 >= 0 && NodeIdxs.Item2 >= 0;
        [Key("Idx")] public int Idx;
        [Key("GridId")] public int GridId;
        [Key("HasCPU")] public bool HasCPU;
        [Key("Pos1")] public Vector2 Pos1;
        [Key("Pos2")] public Vector2 Pos2;
        [Key("Conn")] public (int, int) Connection; // Ordered tuple with two node entity ids
        [Key("Type")] public ElectricConnectionType Type;
        ///<summary>WARNING, Connection tuple order does not match NodeIdxs tuple order! Item1 in one could be Item2 in another!</summary>
        [Key("NodeIdxs")] public (int, int) NodeIdxs;

        public override string ToString() {
            return $"ElectricConnection[{Idx}/Grid:{GridId}/Con:{Connection}]";
        }

        public static ElectricConnectionType TypeBetween(ElectricNode a, ElectricNode b) {
            if (a.IsShipComputer || b.IsShipComputer) {
                return ElectricConnectionType.ShipComputer;
            }
            if (a.IsConnector && b.IsConnector) {
                return ElectricConnectionType.TwoConnectors;
            }
            if (a.IsBattery || b.IsBattery) {
                return ElectricConnectionType.Battery;
            }
            if (a.IsProducer || b.IsProducer) {
                return ElectricConnectionType.Producer;
            }
            return ElectricConnectionType.Consumer;
        }
    }
}
