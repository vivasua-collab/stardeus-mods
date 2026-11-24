using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ElectricityContext {
        public enum GridDisplayMode {
            Auto,
            All,
            ConnectorsOnly,
            Teleporters,
        }

        public string SelectedTab;
        public int SelectedGridIdx = 0;
        public GridDisplayMode ViewMode = GridDisplayMode.Auto;
        public Lifetime MainLifetime;
        public readonly BLong TickB = new(G.Ticks);

        public void ShowTab(string name) {
            if (SelectedTab == name) { return; }
            SelectedTab = name;
            A.S.UIQuery.RebuildElectricity.Ask();
        }
    }
}
