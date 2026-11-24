namespace Game.Input {
    // Ordered by priority: on a shared control the higher layer wins and the
    // lower one does not fire. Always is outside of that order - it is never
    // shadowed and never shadows, which is why it must not share controls with
    // any other layer (camera, click, modifiers, time controls).
    public enum InputLayer : byte {
        Always = 0,
        World = 1,
        Selection = 2,
        Tool = 3,
        ShipCombat = 4,
    }
}
