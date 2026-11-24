namespace Game.Components {
    // A marker interface - when present on an IRanged component with
    // RadiusSameArea, the range is drawn as the edge of the part of the disc
    // that shares the area (including the tiles along the walls that cut it
    // off) instead of a plain ring. A ring clipped to a room that is smaller
    // than the radius would show nothing at all
    public interface IRangedAreaOutline : IRanged {
    }
}
