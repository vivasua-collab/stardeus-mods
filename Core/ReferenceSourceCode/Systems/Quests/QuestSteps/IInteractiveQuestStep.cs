using System;
using Game.Data.Space;
using Game.Systems.Ships;

namespace Game.Systems.Quests {
    public interface IInteractiveQuestStep {
        public QuestStep Step { get; }
        bool CanInteractWith(SpaceObject so, out string icon, out string title);
        bool InteractWithShip(NPCShip target, ShipHailActionUI ui);
    }
}
