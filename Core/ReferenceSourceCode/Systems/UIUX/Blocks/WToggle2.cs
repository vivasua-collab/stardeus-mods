using KL.Pool;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WToggle2 : WBase, ICustomPoolable {
        public WToggleText Toggle1;
        public WToggleText Toggle2;

        public void PoolActivate() {
            Toggle1.SetInteractable(true);
            Toggle2.SetInteractable(true);
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            Toggle1.SetValue(false, notify: false);
            Toggle2.SetValue(false, notify: false);
        }
    }
}
