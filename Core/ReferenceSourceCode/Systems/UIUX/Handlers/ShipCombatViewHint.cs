using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Rings the arrow on the ship combat mini widget while combat needs the player's hands:
    // players missed that the arrow opens the tactical view, and unmanned or Manual-mode
    // battle stations never fired. Repeats on real time, because combat starts with a danger
    // pause that would stop a game-time timer. Once the player has opened the tactical view,
    // they know where it is: no ring for the rest of the session. Not saved, so a loaded game
    // starts over.
    public sealed class ShipCombatViewHint {
        private const double RepeatSeconds = 30;
        private readonly RectTransform target;
        private bool wasOpened;
        private bool wasInCombat;
        private double nextRingAt;

        public ShipCombatViewHint(RectTransform target) {
            this.target = target;
        }

        public void MarkOpened() {
            wasOpened = true;
            UIFocusRing.Cancel(target);
        }

        public void Update() {
            if (wasOpened || target == null || !Ready.Game || UIShowing.ShipCombatMode) {
                return;
            }
            var now = GameTimer.realtimeSinceStartupAsDouble;
            var inCombat = A.Player?.Ship?.IsInCombat == true;
            if (inCombat && !wasInCombat) {
                nextRingAt = now;
            }
            wasInCombat = inCombat;
            if (!inCombat || now < nextRingAt || !UIShowing.ShipCombatMini) { return; }
            // Show Hints is the opt-out
            if (!A.ShowHints || !NeedsManualControl() || !UIFocusRing.CanShowNow(target)) {
                return;
            }
            UIFocusRing.Request(target);
            nextRingAt = now + RepeatSeconds;
        }

        // A station fires on its own only when manned in Automatic mode, the rule of
        // ShipCombatAI.TickBattleStations: keep this in step with it
        private static bool NeedsManualControl() {
            var stations = A.Player.Ship.CombatState.BattleStations;
            for (int i = stations.Count - 1; i >= 0; i--) {
                var bs = stations[i];
                if (bs == null || !bs.IsConstructed) { continue; }
                if (bs.Tile.ENode?.IsPowerable != true) { continue; }
                if (bs.AssignedWeapons.Value <= 0) { continue; }
                if (!bs.IsOperatorPresent || bs.EffectiveManualFireMode) {
                    return true;
                }
            }
            return false;
        }
    }
}
