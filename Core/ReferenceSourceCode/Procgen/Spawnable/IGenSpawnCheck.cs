using System.Collections.Generic;
using Game.Components;
using Game.Data;
using KL.Randomness;

namespace Game.Procgen {
    public interface IGenSpawnCheck {
        string Id { get; }
        int Quantity(GameState s, Def def, ComponentConfig config, Rng rng);
    }
}
