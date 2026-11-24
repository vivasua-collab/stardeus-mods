using Game.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Player to NPC action
    /// </summary>
    public interface IShipHailAction_P2N {
        bool P2N_IsCompatible(NPCShip target);
        void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx);
    }
}
