using Game.Data;

namespace Game.Systems.Ships {
    /// <summary>
    /// NPC to NPC action
    /// </summary>
    public interface IShipHailAction_N2N {
        bool N2P_IsCompatible(NPCShip caller, NPCShip target);
        /// <returns>UDB for player to invoke this action on an NPC</returns>
        void N2N_OnHail(NPCShip caller, NPCShip target);
    }
}
