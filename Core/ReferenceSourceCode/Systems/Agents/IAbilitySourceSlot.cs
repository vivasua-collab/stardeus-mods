namespace Game.Systems.AI {
    public interface IAbilitySourceSlot {
        string SlotName { get; }
        string SlotType { get; }
        string SlotIcon { get; }
        bool IsUsableWithParent { get; }
        IAbilitySourceItem AbilitySourceItem { get; }
    }
}
