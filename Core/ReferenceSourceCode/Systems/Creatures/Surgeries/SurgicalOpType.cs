namespace Game.Systems.Creatures.Surgeries {
    public enum SurgicalOpType : byte {
        Undefined,
        Install,
        Swap,
        Remove,
        FillMissing,
        RemoveAll,
        RemoveIrreparable,
        ReplaceIrreparableAndMissing,
        MassOperation = FillMissing | RemoveAll | RemoveIrreparable | ReplaceIrreparableAndMissing,
    }
}
