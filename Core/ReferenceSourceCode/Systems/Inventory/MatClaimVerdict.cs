namespace Game.Systems.Inventory {
    public enum MatClaimVerdict {
        // Enough in stock to cover the whole request without touching anyone's claim
        Available,
        // Not enough for the whole request, but this requester holds the claim, so it
        // takes whatever exists now and the rest as it is produced
        Trickle,
        // Stock exists, but a higher priority requester is waiting to accumulate it
        Reserved,
        // Nothing to take
        Lacking,
    }
}
