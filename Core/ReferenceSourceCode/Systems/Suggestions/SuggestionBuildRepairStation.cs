using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionBuildRepairStation : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionBuildRepairStation());
        }

        public const string SuggId = "BuildRepairStation";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CWrench;
        // Only ever added by AINeedMaintenance when a bot needs repairs and no station exists,
        // so it has to come back for a colony that lost the station it built
        public override bool IsRecurring => true;
        public override bool IsAvailable(GameState state) {
            return false;
        }

        // The same test AINeedMaintenance adds it on. Station slots exist from construction
        // until destruction, where the first RepairStationComp found may be a blueprint
        public override bool IsComplete(GameState state) {
            return state.Sys.Slots.HasFor(SlotDesignationH.BotRepair);
        }

        public override float Score(GameState state) {
            return float.MaxValue - 2000;
        }
    }
}
