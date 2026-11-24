using System;
using Game.Constants;
using KL.Utils;

namespace Game.Systems.Creatures {
    [Serializable]
    public class SexualOrientation {
        public string Type;
        public float Chance;
        public BeingSex[] Attractions(Species species, BeingSex sex) {
            if (species == null) {
                D.Err("Trying to get sexual orientation attractions for null species! {0}",
                    Type);
                return null;
            }
            return species.GetAttraction(Type, sex);
        }

        public static string ResolveName(BeingSex sex, BeingSex[] attractions, out bool isStraight) {
            if (sex == null) {
                isStraight = true;
                return null;
            }
            isStraight = false;
            if (Arrays.IsEmpty(attractions)) { return T.SexOrientationAsexual; }
            if (attractions.Length > 1) { return T.SexOrientationBisexual; }
            if (attractions[0] == sex) {
                if (sex.TypeChar == 'F') {
                    return T.SexOrientationLesbian;
                }
                return T.SexOrientationGay;
            } else {
                isStraight = true;
                return T.SexOrientationStraight;
            }
        }
    }
}
