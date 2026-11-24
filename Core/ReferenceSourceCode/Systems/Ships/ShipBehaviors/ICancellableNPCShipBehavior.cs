using Game.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public interface ICancellableNPCShipBehavior {

        /// <summary>
        /// If action is cancellable, we can implement a button here
        /// </summary>
        void LoadCancelBlock(ShipHailActionUI ui, NPCShip ship, Lifetime life, RectTransform container, ref int idx);

    }
}
