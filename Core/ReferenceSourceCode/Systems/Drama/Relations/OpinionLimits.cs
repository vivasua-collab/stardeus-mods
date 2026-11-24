using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Drama {
    /// <summary>
    /// Ceilings for how far a relationship can develop, derived from the
    /// innate SocialAffinity / SexualAttraction of an opinion. Without these,
    /// a near-neutral pair would drift all the way to nemesis or profound
    /// friendship given enough interactions.
    /// </summary>
    public static class OpinionLimits {
        // Where BeingOpinion promotes a flag on its own. Together with the ceilings they say
        // which bonds can happen naturally, which ManifestBonds relies on.
        public const float CrushAboveLove = 0.25f;
        public const float FriendFromFriendship = 0.75f;
        public const float NemesisAtHate = 1f;

        [ClearOnReload] private static Curve maxFriendshipCurve;
        [ClearOnReload] private static Curve maxHateCurve;
        [ClearOnReload] private static Curve maxLoveCurve;

        public static float MaxFriendship(float socialAffinity) {
            maxFriendshipCurve ??= CurveDef.FastGet(
                CurveIdH.Drama_SocialAffinity_MaxFriendship);
            return maxFriendshipCurve.Evaluate(socialAffinity);
        }

        public static float MaxHate(float socialAffinity) {
            maxHateCurve ??= CurveDef.FastGet(
                CurveIdH.Drama_SocialAffinity_MaxHate);
            return maxHateCurve.Evaluate(socialAffinity);
        }

        public static float MaxLove(float sexualAttraction) {
            maxLoveCurve ??= CurveDef.FastGet(
                CurveIdH.Drama_SexualAttraction_MaxLove);
            return maxLoveCurve.Evaluate(sexualAttraction);
        }

        /// <summary>
        /// Stops growth at the ceiling, but never pulls an already higher value
        /// down: relationships from older saves or from a retuned curve stay
        /// where they are and can only go down through negative interactions.
        /// </summary>
        public static float CapGrowth(float current, float grown, float max) {
            if (grown <= max) { return grown; }
            return Mathf.Max(current, max);
        }
    }
}
