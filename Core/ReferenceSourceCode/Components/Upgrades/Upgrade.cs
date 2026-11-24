using System.Text;
using Game.Data;

namespace Game.Components.Upgrades {
    public abstract class Upgrade {
        public abstract string UpgradeId { get; }
        public abstract string Icon { get; }
        public abstract string Title { get; }
        public abstract string MatTypeId { get; }
        public virtual bool CanRemove(out string reason) {
            reason = null;
            return true;
        }
        protected static void Add(string id, Upgrade instance) {
            UpgradeRegistry.Add(id, instance);
        }

        public abstract bool IsCompatibleWith(Entity entity);
        public virtual bool CanInstallNow(Entity entity) {
            if (entity is Tile t) {
                return t.IsConstructed;
            }
            return true;
        }

        public virtual void OnInstall(Entity entity) { }
        public virtual void OnUninstall(Entity entity) { }
        // Extra lines for the upgrade item's description (Def.ExtendedDesc)
        public virtual void AddDefInfo(StringBuilder sb) { }
    }
}
