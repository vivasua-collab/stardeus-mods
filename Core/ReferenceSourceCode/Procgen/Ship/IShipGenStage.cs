using System.Collections;
using Game.Data;
using KL.Grid;
using KL.Randomness;

namespace Game.Procgen {
    public interface IShipGenStage {
        bool SkipRNG { get; }
        bool StartOver { get; }
        int Attempts { get; set; }
        void Initialize(ShipSpec spec, GameState s);
        bool IsAsync { get; }
        void Apply();
        string Message { get; }
        IEnumerator ApplyAsync();
        ShipSpec Result { get; }
        public void SetRng(Rng rng);
        void Reset();

        Rng StageRng { get; }
        bool NeedsRepeating { get; }
        bool StartOverIfNeedsRepeating { get; }
        ///<summary>We will need to trigger area recalculation before this</summary>
        bool RequiresAreas { get; }
        bool ProducesIslands { get; }
        string LogPrefix { get; }
        bool ForceDebug { get; }
    }
}
