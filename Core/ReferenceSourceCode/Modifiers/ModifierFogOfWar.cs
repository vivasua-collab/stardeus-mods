using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using KL.Clock;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierFogOfWar : Modifier {
        public const string ModifierId = "FogOfWar";
        public override int SortOrder => 72;
        public override string GroupName => T.NounExtraChallenge;
        public override string Id => ModifierId;
        public override string NameT => T.AmbColorDark;
        public override string LabelT => T.AmbColorDark;
        public override string DescT => T.FogOfWarDesc;
        // Locked for the run: BeatFogOfWar only checks IsEnabled at the ending, so toggling
        // this mid-run would let it be earned without playing blind the whole way.
        public override bool CanChangeInGame => false;
        public override bool PreventsAchievements => false;
        public static bool IsEnabled;

        [SelfInit]
        private static void Register() {
            Register(new ModifierFogOfWar());
            IsEnabled = false;
        }
        public static string SettingPath => $"{T.MenuSettings} {GlyphMap.ArrowRight} {T.MenuSettingsCurrent_Game} {GlyphMap.ArrowRight} {T.AmbColorDark}";

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetBool(Pref.FogOfWar);
            }
            IsEnabled = false;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            s.Prefs.SetBool(Pref.FogOfWar, isActive);
            IsEnabled = isActive;
            if (!isLoading) {
                // Set ambient color to trigger fog of war change
                var ambColorKey = The.Prefs.GetString(Pref.GAmbColor, PrefH.GAmbColor,
                        Consts.AmbientLightColorDefault);
                var ambDef = AmbientLightDef.Get(ambColorKey);
                The.SysSig.AmbientColorChanged.Send(ambDef);
            }
        }
    }
}
