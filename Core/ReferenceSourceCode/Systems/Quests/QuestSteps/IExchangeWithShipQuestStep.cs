using System;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;

namespace Game.Systems.Quests {
    public interface IExchangeWithShipQuestStep {
        bool AddQuestPackageOptions(NPCShip ship, GiftChoiceUI ui, GiftDirection direction);
        bool CanExchangeWithShip(NPCShip ship, out string icon, out string title);
    }
}
