using System;
using Game.Components;

namespace Game.Data {
    public interface ICopyableComp : IComponent {
        bool IsCopyable { get; }
        // Translated
        string CopyConfigText { get; }
        bool CopyConfigTo(ICopyableComp target);
        bool CanCopyTo(ICopyableComp comp);
    }
}
