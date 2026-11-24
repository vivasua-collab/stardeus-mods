using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionBuildDiskModule : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionBuildDiskModule());
        }

        public const string SuggId = "BuildDiskModule";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CDisk;
        // Disk modules sit in the Computing build tab, which the tutorial shows for research
        public override string WaitsForBool => QuestBoolIds.HasEstablishedResearch;
        public override bool IsAvailable(GameState state) {
            var free = A.Grid.TotalDisk - state.Research.UsedDisk;
            return free < 6;
        }

        public override bool IsComplete(GameState state) {
            return state.Components.CountOfConstructed<DiskModuleComp>() > 0;
        }

        public override float Score(GameState state) {
            return int.MaxValue - state.Clock.Ticks;
        }
    }
}
