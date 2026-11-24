using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionConnectStasisArray : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionConnectStasisArray());
        }
        public const string SuggId = "ConnectStasis";
        public override string Id => SuggId;
        public override string WaitsForBool => QuestBoolIds.ConnectedToStasis;
        public override string IconId => GraphicsId.ObjectsDevicesStasisArrayMini;
        public override bool IsRecurring => true;
        public override bool IsAvailable(GameState state) {
            return state.Ticks > Consts.TicksPer3Days &&
                state.Components.CountMatching<StasisArrayComp>(
                    s => !s.Tile.ENode.IsConnected, 1) > 0;
        }

        public override bool IsComplete(GameState state) {
            return state.Components.CountMatching<StasisArrayComp>(
                    s => !s.Tile.ENode.IsConnected, 1) == 0;
        }

        public override float Score(GameState state) {
            return int.MaxValue;
        }
    }
}
