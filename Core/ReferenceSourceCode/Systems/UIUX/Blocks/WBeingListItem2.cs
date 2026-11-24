using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WBeingListItem2 : WBase, ICustomPoolable {
        public WBeingListItem Item;
        public WGroup StatusIcons;
        public GameObject SelectedArrow;
        public GameObject ActiveBar;
        public Lifetime StatusIconsLife;

        public void PoolActivate() {
            Item.PoolActivate();
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            if (StatusIconsLife != null) {
                StatusIconsLife.Dispose();
                StatusIconsLife = null;
            }
            gameObject.SetActiveFast(false);
            Item.PoolDeactivate();
        }
    }
}
