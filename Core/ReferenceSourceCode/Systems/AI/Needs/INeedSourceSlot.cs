namespace Game.Systems.AI {
    public interface INeedSourceSlot {
        INeedSourceItem NeedSourceItem { get; }
        string SlotName { get; }
        string SlotType { get; }
    }
}
