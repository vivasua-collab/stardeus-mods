using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Suggestions {
    public class SuggestionEnsureShipCPUVentilation : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionEnsureShipCPUVentilation());
        }
        public const string SuggId = "EnsureShipCPUVentilation";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CTemperature;
        public override bool IsAvailable(GameState state) {
            return false;
        }

        public override bool IsComplete(GameState state) {
            if (!Ready.Areas) { return false; }
            var shipCPU = state.Components.FindFirstByType<ShipComputerComp>();
            if (shipCPU == null) { return false; }
            var heatC = state.GetTemp(Pos.NeighborAt(shipCPU.Tile.PosIdx, 0, -1));
            heatC = Units.KelvinToCelsius(heatC);
            return heatC < 25;
        }

        public override float Score(GameState state) {
            throw new System.NotImplementedException("N/A, shouldn't be called");
        }
    }
}
