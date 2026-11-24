// Only the Editor ignores it, so player saves can be debugged there. Dev player builds,
// where tests and playtests run, have to behave like the shipped game.
#if UNITY_EDITOR
#define IGNORE_PERMADEATH
#endif
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    /// <summary>
    /// If we ever want to add an achievement for permadeath, this should either be
    /// hidden in game, or should warn that this achievement will not be possible if
    /// this ever gets disabled for the current session.
    /// </summary>
    public sealed class ModifierNoReload : Modifier {
        public const string ModifierId = "NoReload";
        public override int SortOrder => 72;
        public override string GroupName => T.NounExtraChallenge;
        public override string Id => ModifierId;
        //public override string NameT => IsActive ? base.NameT : T.SaveLoadAnytime;
        public override bool PreventsAchievements => false;
        public override bool SkipModifiersPanel => true;
        public static ModifierNoReload Instance;

        [SelfInit]
        private static void Register() {
            Register(Instance ??= new ModifierNoReload());
        }
        public override bool IsActiveByDefault => false;

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            #if !IGNORE_PERMADEATH
            s.IsPermadeath = isActive;
            #endif
        }
    }
}
