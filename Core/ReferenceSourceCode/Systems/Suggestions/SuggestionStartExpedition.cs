using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionStartExpedition : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionStartExpedition());
        }

        public const string SuggId = "StartExpedition";
        public override string Id => SuggId;
        public override string WaitsForBool => QuestBoolIds.HadExpedition;
        public override string IconId => Constants.IconId.CEnterSpaceObject;
        public override bool IsRecurring => false;
        public override string UIActionText => T.Expeditions;
        public override Func<bool> UIActionShouldBeRemoved => () => UIShowing.Overlay
            && UIShowing.OverlayType == OverlayId.Expedition;
        public override Action<GameState> UIAction => OpenExpeditions;

        private void OpenExpeditions(GameState s) {
            s.Sig.ShowOverlay.Send(OverlayId.Expedition);
        }

        public override bool IsAvailable(GameState state) {
            return false;
        }

        public override bool IsComplete(GameState state) {
            var s = state?.Situation;
            if (s == null) { return false; }
            return s.ExpeditionsCompleted > 0 || s.ExpeditionsOngoing > 0;
        }

        public override float Score(GameState state) {
            return 0;
        }
    }
}
