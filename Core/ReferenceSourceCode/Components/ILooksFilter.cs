using Game.Systems.Creatures;

namespace Game.Components {
    public interface ILooksFilter {
        /// <returns>true if avatar must be rebuilt later</returns>
        bool FilterLooks(SpeciesLooksDef looksDef, BeingLooks looks);
    }
}
