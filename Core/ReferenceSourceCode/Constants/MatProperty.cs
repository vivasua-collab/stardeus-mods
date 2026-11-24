using KL.Utils;

namespace Game.Constants {
    public static class MatProperty {
        public const string Solid = "Solid";
        public const string Liquid = "Liquid";
        public const string Nuclear = "Nuclear";
        public const string NuclearFuel = "NuclearFuel";
        public const string Nutrient = "Nutrient";
        public const string Gas = "Gas";
        public static readonly string[] Any = null;
        public static readonly string[] Nothing = new string[0];

        public static string[] Common(string[] one, string[] two) {
            if (one == null) { return two; }
            if (two == null) { return one; }
            if (!Arrays.EqualContents(one, two)) { return Nothing; }
            if (one != two) { return Nothing; }
            return one;
        }
    }
}
