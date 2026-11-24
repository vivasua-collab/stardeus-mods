namespace Game.Constants {
    // If new placement constraints will be added, you should refactor
    // CmdPlaceTile.CanPlacePartial, because it would break!
    public enum PlacementConstraint {
        Anywhere,
        OnFloor,
        InSpace,
        InSpaceAttached,
        InSpaceAttachedChained,
        NearShipComputer,
    }
}
