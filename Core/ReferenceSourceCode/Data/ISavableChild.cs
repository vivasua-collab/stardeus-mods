using Game.Components;

namespace Game.Data {
    public interface ISaveableChild {
        void OnSave(ComponentData data);
        void OnLoad(ComponentData data);
    }
}
