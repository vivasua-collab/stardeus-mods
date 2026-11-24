namespace Game.Components {
    // A marker interface - when this is present on a IRanged component, it will
    // show other ranges of same type components within the range
    // when building a new tile
    // useful for getting the coverage preview
    public interface IRangedShowOthers : IRanged, IComponent {
        // if this is not 0, it will only compare this id instead of component type
        int RangeGroupId { get; }
        // 0 will do (range + otherRange)^2 + 2
        float RangeOthersMaxSqrDist { get; }
    }
}
