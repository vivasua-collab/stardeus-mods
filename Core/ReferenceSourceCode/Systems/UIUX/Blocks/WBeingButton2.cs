using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WBeingButton2 : WBase, ICustomPoolable {
        public WBeingListItem Item;
        public WButtonIcon IconButton1;
        public WButtonIcon IconButton2;
        public WText TextSide;
        public UITextCollisionResolver UICollisionResolver;

        public void PoolActivate() {
            Item.PoolActivate();
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            Item.PoolDeactivate();
        }
    }
}
