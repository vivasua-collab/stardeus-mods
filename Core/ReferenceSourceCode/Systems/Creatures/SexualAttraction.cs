using System;
using KL.Utils;

namespace Game.Systems.Creatures {
    [Serializable]
    public class SexualAttraction {
        public string Sex;
        public char SexChar;
        public string Orientation;
        public string AttractedTo;
        public BeingSex[] Attractions;

        public void Initialize(Species s) {
            if (Sex == null || Sex.Length != 1) {
                D.Err("Invalid SexualAttraction defined for species {0}. Sex must be 1 character, actual: '{1}'",
                    s, Sex);
                SexChar = 'N';
            }
            SexChar = Sex[0];
            if (!string.IsNullOrWhiteSpace(AttractedTo)) {
                Attractions = new BeingSex[AttractedTo.Length];
                for (int i = AttractedTo.Length - 1; i >= 0; i--) {
                    char a = AttractedTo[i];
                    var sex = s.GetSex(a);
                    if (sex == null) {
                        D.Err("Got null when initializing sexual attraction for species: {0} | {1} | {1}",
                            s, Sex, Orientation);
                        Attractions = null;
                        return;
                    } else {
                        Attractions[i] = sex;
                    }
                }
            }
        }

    }
}
