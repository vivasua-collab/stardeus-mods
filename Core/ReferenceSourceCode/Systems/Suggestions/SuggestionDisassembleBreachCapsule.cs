using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionDisassembleBreachCapsule : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionDisassembleBreachCapsule());
        }

        public const string SuggId = "DisassembleBreachCapsule";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.WMaterials;
        public override bool IsRecurring => true;
        public override string UIActionText => T.Show;
        public override Action<GameState> UIAction => FocusOnCapsule;
        private BreachCapsuleComp FirstCapsule(GameState state) {
            if (state.Sys.Combat.IsHostileCombatInProgress) {
                return null;
            }
            return state.Components.FindFirstMatching<BreachCapsuleComp>(
                c => c.IsOpened && c.Tile.Goal == null);
        }
        public void FocusOnCapsule(GameState s) {
            var cap = FirstCapsule(s);
            if (cap != null) {
                EntityUtils.CameraFocusOn(cap.Entity);
            }
        }
        public override bool IsAvailable(GameState state) {
            return FirstCapsule(state) != null;
        }

        public override bool IsComplete(GameState state) {
            if (state.Sys.Combat.IsHostileCombatInProgress) {
                return false;
            }
            if (state.Components.CountByType<BreachCapsuleComp>() == 0) {
                return false;
            }
            return true;
        }

        public override float Score(GameState state) {
            return 10f;
        }
    }
}
