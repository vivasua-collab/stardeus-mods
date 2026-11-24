using Game.CodeGen;

namespace Game.Constants {
    /// <summary>
    /// Items starting with underscore will not create social interactions
    /// </summary>
    public static class SocialContext {
        public const string _LoveConfession = nameof(_LoveConfession);
        public const string _Insult = nameof(_Insult);
        public const string Meal = nameof(Meal);
        public const string Drink = nameof(Drink);
        public const string Workout = nameof(Workout);
    }
}
