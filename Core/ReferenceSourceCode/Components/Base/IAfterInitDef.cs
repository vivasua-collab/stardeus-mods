using Game.Data;

namespace Game.Components {
    public interface IAfterInitDef {
        void AfterInitDef(Def def, ComponentConfig config);
    }
}
