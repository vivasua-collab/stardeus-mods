using Game.Components;

namespace Game.Data {
    public interface ISaveable {
        ComponentData Save();
        void Load(ComponentData data);
    }
}
