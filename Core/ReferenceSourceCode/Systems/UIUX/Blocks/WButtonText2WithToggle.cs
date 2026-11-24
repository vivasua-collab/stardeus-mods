using KL.Pool;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WButtonText2WithToggle : WBase, ICustomPoolable {
        public WButtonText2 Button;
        public WToggle Toggle;

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            Toggle.SetValue(false, false);
        }
    }
}
