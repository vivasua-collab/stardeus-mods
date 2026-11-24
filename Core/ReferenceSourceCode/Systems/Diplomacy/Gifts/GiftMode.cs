namespace Game.Systems.Diplomacy {
    public enum GiftMode : byte {
        Undefined,
        Gift,
        Extortion,
        /// <summary>
        /// Used when an enemy ship is accepting your surrender request
        /// </summary>
        SurrenderRansom,
        Fuel,
        Quest,
        /// <summary>
        /// Player is handing sendable guests (ColonyRole.Passenger) of the target's own
        /// faction back home for payment and attitude - see ShipHailAction_SendBeings.
        /// Player-to-NPC only.
        /// </summary>
        SendBeings,
    }
}
