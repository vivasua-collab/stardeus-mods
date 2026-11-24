using Game.CodeGen;

namespace Game.Constants {
    /// <summary>
    /// Stable keys of permanent mood effects. A permanent effect lasts as long as its cause,
    /// and its source sets or removes it by this key. The translated Reason text can't be the
    /// key: it is saved with the effect, so it changes with the language, and some sources
    /// change it with the value (pain severity, obey chip CPU bonus).
    /// </summary>
    [GenerateHashes]
    public static class MoodSourceId {
        public const string ObeyChip = nameof(ObeyChip);
        public const string MoodRegulator = nameof(MoodRegulator);
        public const string Naked = nameof(Naked);
        public const string DeadMansGear = nameof(DeadMansGear);
        public const string SecondHandGear = nameof(SecondHandGear);
        public const string TatteredGear = nameof(TatteredGear);
        public const string SleepDeprivation = nameof(SleepDeprivation);
        public const string Pain = nameof(Pain);
        public const string Depression = nameof(Depression);
        public const string Hacked = nameof(Hacked);
        public const string Inspired = nameof(Inspired);
        public const string FoodPoisoning = nameof(FoodPoisoning);
        public const string SpaceDiarrhea = nameof(SpaceDiarrhea);
        public const string Starving = nameof(Starving);
        public const string PsychicStorm = nameof(PsychicStorm);
        public const string CometShower = nameof(CometShower);
        // Optimist / Pessimist
        public const string Outlook = nameof(Outlook);
    }
}
