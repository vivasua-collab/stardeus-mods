using Game.CodeGen;
using Game.Constants;
using UnityEngine;

namespace Game.Systems.BodyParts {
    // How much a wound hurts and hinders. A destroyed part's wound never heals, so instead its
    // pain settles over time (progress kept in the wound's HealAmt, unused otherwise at that
    // severity). It used to hurt at full strength, and slow the being down, until surgery.
    public static class WoundPain {
        // A destroyed part this small (ear, eye, hand, foot) starts one pain step lower
        private const float SmallPartSize = 0.15f;

        // Pain of a destroyed part per quarter of settling, worst first. Once fully settled it
        // stops counting as an injury at all (Wound.IsSettled).
        private static readonly Severity[] settlingSteps = {
            Severity.Irreparable, Severity.Severe, Severity.Moderate, Severity.Mild
        };

        // The severity the wound hurts and hinders like
        public static Severity EffectiveSeverity(in Wound w, BodyPartSlot slot) {
            if (w.Severity < Severity.Irreparable) { return w.Severity; }
            if (w.IsSettled) { return Severity.Irrelevant; }
            var step = (int) (Mathf.Clamp01(w.HealAmt) * 4f);
            if (slot.Size <= SmallPartSize) {
                step++;
            }
            return settlingSteps[Mathf.Min(step, settlingSteps.Length - 1)];
        }

        // Advances a destroyed part's settling; true when its pain steps down
        public static bool Settle(ref Wound w, float healRate) {
            if (w.Severity < Severity.Irreparable || w.HealAmt >= 1f) { return false; }
            var stepBefore = (int) (w.HealAmt * 4f);
            w.HealAmt = Mathf.Min(1f, w.HealAmt + healRate * Tunables.DestroyedPartSettleMul);
            return (int) (w.HealAmt * 4f) != stepBefore;
        }

        // Parts a being walks with. Only their wounds slow it down: a hurt ear or arm doesn't.
        public static bool IsLocomotion(BodyPartSlot slot) {
            var type = slot.Type.IdH;
            if (type == BodyPartIdH.Body || type == BodyPartIdH.Hip) { return true; }
            var sources = slot.InstalledDef?.StatSources;
            if (sources == null) { return false; }
            for (int i = sources.Length - 1; i >= 0; i--) {
                if (sources[i].StatId == StatIdH.MoveSpeed) { return true; }
            }
            return false;
        }

        // A destroyed part that no longer hurts: no longer an injury that needs rest, though the
        // part stays destroyed until surgery replaces it
        public static bool HasUnsettledWound(BodyPartState state) {
            var wounds = state.Wounds;
            if (wounds == null) { return false; }
            for (int i = wounds.Count - 1; i >= 0; i--) {
                if (!wounds[i].IsSettled) { return true; }
            }
            return false;
        }
    }
}
