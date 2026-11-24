using System;
using Game.Constants;
using Game.Procgen.Names;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEditor;

namespace Game.Systems.Creatures {
    [Serializable]
    public class BeingSex {
        public string Type;
        public char TypeChar;
        public float Chance;
        public float ChanceBald;
        /// <summary>
        /// [M] - masculine (sex: male)
        /// [F] - feminine (sex: female)
        /// [N] - neutral (sex: any)
        /// </summary>
        public string HairTypes;
        /// <summary>
        /// [M] - masculine (sex: male)
        /// [F] - feminine (sex: female)
        /// [N] - neutral (sex: any)
        /// </summary>
        public string HeadTypes;
        /// <summary>
        /// [M] - masculine (sex: male)
        /// [F] - feminine (sex: female)
        /// [T] - thin (sex: any)
        /// [L] - large (obese) (sex: any)
        /// </summary>
        public string BodyTypes;
        /// <summary>
        /// Each index will have a weight for random chance of the BodyTypes string character index
        /// null value = equal chances for each letter
        /// </summary>
        public float[] BodyTypeChances;
        private string icon;
        public string Icon => icon ??= string.Intern($"Icons/Color/Sex_{Type}");
        private string nameT;
        public string NameT {
            get {
                if (nameT == null) {
                    switch (TypeChar) {
                        case 'M': nameT = T.Male; break;
                        case 'F': nameT = T.Female; break;
                        default: nameT = $"sex.{TypeChar}".T(); break;
                    }
                }
                return nameT;
            }
        }
        // For path overrides
        public NamePartDef[] NameParts;

        public override string ToString() {
            return $"Gender: {Type} [{string.Join(",", BodyTypes)}]";
        }

        public override bool Equals(object obj) {
            return obj is BeingSex g && g.TypeChar == TypeChar;
        }

        public void Initialize(Species species) {
            if (Type == null || Type.Length != 1) {
                D.Err("Invalid sex defined for species: {0}. Sex Type must be exactly one character, but is: '{1}'", species, Type);
                TypeChar = 'N';
                return;
            }
            TypeChar = Type[0];
        }
        public static char[] Serialize(BeingSex[] sexes) {
            if (Arrays.IsEmpty(sexes)) { return null; }
            var res = new char[sexes.Length];
            for (int i = sexes.Length - 1; i >= 0; i--) {
                BeingSex sex = sexes[i];
                res[i] = sex.TypeChar;
            }
            return res;
        }


        public static BeingSex[] Deserialize(char[] sexes, Species species) {
            if (Arrays.IsEmpty(sexes)) { return null; }
            var res = new BeingSex[sexes.Length];
            for (int i = sexes.Length - 1; i >= 0; i--) {
                char sex = sexes[i];
                var beingSex = species.GetSex(sex);
                if (beingSex == null) {
                    D.Err("Trying to deserialize BeingSex {0} for species {1} - does not exist!",
                        sex, species);
                    return null;
                }
                res[i] = beingSex;
            }
            return res;
        }

        public override int GetHashCode() {
            var hash = 12371;
            if (Type != null) {
                hash = Hashes.Quick(hash, Type.GetHashCode());
            }
            return hash;
        }
    }
}
