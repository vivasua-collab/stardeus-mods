using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.UIUX;

namespace Game.Components {
    public interface ISlots : IComponent {
        int SlotDesignation { get; }
        Tile Tile { get; }
        List<Slot> Slots { get; }
        string UITitle { get; }
        bool HideUI { get; }
        BCallback StateChangedB { get; }
        // Multislot == true means that 1x2 table will have 2 slots instead of 1
        Slot GetSlot(int idx, int num = -1);
        Slot GetSlotByNumber(int num);
        bool IsMultislot { get; }
        bool IsAvailable { get; }
        bool IsMakeshift { get; }
        bool HasFreeSlot { get; }
        bool RequiresSafeArea { get; }
        bool HideBeing { get; }
        Facing.Type SpotRotation { get; }
        // Invoked every tick
        bool Use(BeingData being, Slot slot);
        void SlotChanged(Slot slot);
        bool IsCompatibleWith(BeingData being);
    }
}
