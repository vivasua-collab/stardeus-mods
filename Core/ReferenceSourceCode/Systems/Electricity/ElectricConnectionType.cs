namespace Game.Systems.Electricity {
    public enum ElectricConnectionType : byte {
        Uninitialized,
        Consumer,
        TwoConnectors,
        ShipComputer,
        Producer,
        Battery,
    }
}
