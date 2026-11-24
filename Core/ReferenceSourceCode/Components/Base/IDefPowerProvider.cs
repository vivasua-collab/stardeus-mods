using Game.Data;

namespace Game.Components {
    // Reports the electricity a component draws or produces, read from its def config
    // alone, so the codex can show it before the device is built
    public interface IDefPowerProvider {
        void AddDefPower(Def def, ComponentConfig config, ref DefPower power);
    }
}
