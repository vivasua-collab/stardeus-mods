using Game.Data;
using Game.Procgen;
using Game.Systems.Ships;
using KL.Randomness;

namespace Game.Systems.Story {
    public interface IProducesDerelictShip {
        ShipFactory CreateShipFactory(GameState s, Rng rng, NPCShip forShip);
        void AfterGenerate(GameState s, ShipFactory factory, Rng rng);
    }
}
