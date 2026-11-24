using KL.Pool;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WContentButton2Text2 : WBase, ICustomPoolable {
        public WButtonIconText2 Item;
        public WButtonText Button;

        public void PoolActivate() {
            // some other UI code deactivates this button
            Button.SetActiveFast(true);
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
        }
    }
}
