using Game.Data.Factions;

namespace Game.Components {
    public interface IWithFaction : IComponent {
        public FactionDef Faction { get; set; }
    }
}
