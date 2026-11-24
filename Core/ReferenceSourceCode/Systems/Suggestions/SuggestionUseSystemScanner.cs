using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionScanAdvanced : Suggestion {
        [SelfInit]
        private static void AutoRegister() {
            Register(new SuggestionScanAdvanced());
        }

        public const string SuggId = "ScanAdvanced";
        public override string Id => SuggId;
        public override string WaitsForBool => QuestBoolIds.HasRadar;
        public override string IconId => Constants.IconId.CCommunications;
        public override bool IsRecurring => false;
        public override string UIActionText => T.OpenStarmap;
        public override Func<bool> UIActionShouldBeRemoved => () => UIShowing.Starmap;
        public override Action<GameState> UIAction => OpenStarmap;

        private void OpenStarmap(GameState s) {
            s.UIQuery.ShowStarmap.Ask(true);
        }

        public override bool IsAvailable(GameState state) {
            return false;
        }

        public override bool IsComplete(GameState state) {
            var v = state.Sys.Scan.Mode.Value;
            return v != ScanMode.None && v != ScanMode.Mat;
        }

        public override float Score(GameState state) {
            return 0;
        }
    }
}
