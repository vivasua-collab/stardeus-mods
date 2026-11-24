namespace Game.Systems.Items {
    /// <summary>
    /// Which pile of provenance an item's ownership history puts it in. Numeric
    /// order doubles as display order - New, then Second-hand, then Dead man's.
    /// </summary>
    /// <see cref="ItemOwnership"/>
    public enum ItemOriginBucket : byte {
        /// <summary>
        /// The item carries no ownership history at all - a weapon, a body part,
        /// or any modded slot that did not opt into InventorySlotType.Worn. Never
        /// split or discounted; see ItemOwnership.TracksOwnership.
        /// </summary>
        Untracked = 0,
        New = 1,
        SecondHand = 2,
        DeadMans = 3,
    }
}
