using Game.Data;
using Game.Systems.Policies;

namespace Game.Systems.UIUX {
    // A tab in the right-hand pane of the Policies widget. Register with [SelfInit]
    // in a static Register() method, exactly like ShipComputerApp.
    public abstract class BasePolicyTab {
        public abstract string Id { get; }
        public abstract string NameT { get; }
        public abstract string Icon { get; }
        // Lower comes first in the tab strip.
        public virtual int Order => 0;

        public virtual bool SupportsScope(PolicyScope scope) {
            return true;
        }

        public abstract void Build(PolicyTabContext ctx);

        public static void Register(BasePolicyTab tab) {
            Registries.PolicyTabs.Add(tab.Id, tab);
        }
    }
}
