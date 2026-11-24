using System;

namespace Game.Systems.Mental {
    public enum FunActivity : byte {
        None = 0,
        // subject: device Def IdH
        UseDevice = 1,
        // subject: device Def IdH
        Workout = 2,
        // subject: resolved by FunSubjects.ForAdmirable (Def IdH or subject group hash)
        Admire = 3,
        // subject: always 0, every window and glass floor is the same view
        AdmireView = 4,
        // subject: pet Species IdH
        Pet = 5,
        // subject: ridden Species IdH
        RideBot = 6,
    }

    // What a being did for fun, and with what. Repetition is remembered per source, so
    // petting a cat and petting a dog are two sources, while ten cats are one.
    public readonly struct FunSource : IEquatable<FunSource> {
        public readonly FunActivity Activity;
        public readonly int SubjectIdH;

        public static readonly FunSource None = default;
        public static readonly FunSource View = new(FunActivity.AdmireView, 0);

        public FunSource(FunActivity activity, int subjectIdH) {
            Activity = activity;
            SubjectIdH = subjectIdH;
        }

        public bool IsValid => Activity != FunActivity.None;

        public bool Equals(FunSource other) {
            return Activity == other.Activity && SubjectIdH == other.SubjectIdH;
        }

        public override bool Equals(object obj) {
            return obj is FunSource other && Equals(other);
        }

        public override int GetHashCode() {
            return (SubjectIdH * 397) ^ (int) Activity;
        }

        public static bool operator ==(FunSource a, FunSource b) => a.Equals(b);
        public static bool operator !=(FunSource a, FunSource b) => !a.Equals(b);

        public override string ToString() {
            return $"FunSource[{Activity}|{SubjectIdH}]";
        }
    }
}
