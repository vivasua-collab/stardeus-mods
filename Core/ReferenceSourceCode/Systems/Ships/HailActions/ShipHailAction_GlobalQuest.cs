using System;
using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// These actions can apply to any ship, globally active quest steps will
    /// determine which ship has interactions and what they are.
    /// </summary>
    public sealed class ShipHailAction_GlobalQuest : ShipHailAction,
            IShipHailAction_P2N {
        public const string ActId = "GlobalQuest";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_GlobalQuest());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var gi = A.S.Query.GetGlobalInteractiveQuestSteps.Ask();
            foreach (var step in gi) {
                if (step.CanInteractWith(target.SO, out var icon, out var title)) {
                    var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
                    btn.Load(icon, title, () => {
                        step.InteractWithShip(target, ui);
                    });
                }
            }
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) {
                return false;
            }
            var gi = A.S.Query.GetGlobalInteractiveQuestSteps.Ask();
            for (int i = gi.Count - 1; i >= 0; i--) {
                var step = gi[i];
                if (step.CanInteractWith(target.SO, out _, out _)) {
                    return true;
                }
            }
            return false;
        }
    }
}
