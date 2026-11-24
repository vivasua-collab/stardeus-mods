namespace Game.Systems.Ships {
    public enum ShipChatTopic : byte {
        Undefined,
        /// <summary>
        /// when NPC hails the player
        /// </summary>
        HailRequest,
        /// <summary>
        /// when Player hails NPC
        /// </summary>
        HailResponse,
        /// <summary>
        /// when player asks the NPC a general question (i.e offers to give a gift)
        /// </summary>
        Generic,
        /// <summary>
        /// When NPC wants to trade with player
        /// </summary>
        TradeRequest,
        /// <summary>
        /// When player wants to trade with NPC
        /// </summary>
        TradeResponse,
        /// <summary>
        /// When NPC wants to send gift to the player
        /// </summary>
        GiftRequest,
        /// <summary>
        /// NPCs reaction to a gift, after player sends the gift to the NPC
        /// </summary>
        GiftResponse,
        /// <summary>
        /// /// When NPC is asking player for fuel
        /// </summary>
        FuelRequest,
        /// <summary>
        /// When player is asking NPC for fuel
        /// </summary>
        FuelResponse,
        /// <summary>
        /// When NPC is extorting the player
        /// </summary>
        ExtortionRequest,
        /// <summary>
        /// When player is extorting an NPC
        /// </summary>
        ExtortionResponse,
        /// <summary>
        /// When NPC is surrendering to the player
        /// </summary>
        SurrenderRequest,
        /// <summary>
        /// When player is trying to surrender to an NPC
        /// </summary>
        SurrenderResponse,
        /// <summary>
        /// When NPC is about to engage in combat with the player
        /// </summary>
        CombatEngage,
        /// <summary>
        /// When an action is invoked while player's ship is too far and npc
        /// ship has no ability to fly closer.
        /// </summary>
        TooFar,
        /// <summary>
        /// When NPC ship is offering a quest to player
        /// </summary>
        QuestRequest,
        /// <summary>
        /// When player is asking for a quest
        /// </summary>
        QuestResponse,
        /// <summary>
        /// When player is delivering a quest item to NPC
        /// </summary>
        QuestDeliveryResponse,
    }
}
