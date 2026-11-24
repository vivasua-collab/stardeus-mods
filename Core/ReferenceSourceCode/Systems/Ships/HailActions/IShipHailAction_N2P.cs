using Game.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// NPC to Player action
    /// </summary>
    public interface IShipHailAction_N2P {
        /// <summary>
        /// The player will have to choose from these buttons to react to
        /// NPC's hail action
        /// </summary>
        void N2P_LoadPlayerChoice(ShipHailActionUI ui, NPCShip caller, Lifetime life, RectTransform container, ref int idx);
        void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship);
        void N2P_OnPlayerChoiceTimeout(NPCShip caller);
    }
}
