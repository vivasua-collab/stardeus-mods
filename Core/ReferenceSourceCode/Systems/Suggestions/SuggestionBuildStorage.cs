using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionBuildStorage : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionBuildStorage());
        }

        public const string SuggId = "BuildStorage";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.WMaterials;
        // Not before day 5: starting ships come with storages that have plenty of room, and
        // in the first minutes this was a popup about a problem the player did not have
        public override bool IsAvailable(GameState state) {
            return state.Ticks > Consts.TicksPer5Days
                && state.Research.Get(DefIdsH.ResearchConstructionStorageMini)
                .InternalUnlocked;
        }

        public override bool IsComplete(GameState state) {
            var c = state.Components.CountMatching<MatHaulTargetComp>(
                (c) => {
                    return c.IsConstructed &&
                        c.Tile.Definition.Id == DefIds.ObjectsDevicesStorageMini;
                }, 1);
            return c > 0;
        }

        public override float Score(GameState state) {
            return -500;
        }
    }
}
