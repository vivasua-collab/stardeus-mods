using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Space;
using UnityEngine;

namespace Game.Systems.Quests {
    public interface IQuestProvider {
        QuestProviderType ProviderType { get; }
        public uint ProviderId { get; }
        FactionDef Faction { get; }
        int Seed { get; }
        uint QuestId { get; set; }
        string StarmapLink { get; }
        // These are used in tests
        public Vector2 SectorLocation { get; }
        public SpaceObject ParentSector { get; }
        public SpaceObject ProviderSO { get; }
    }
}
