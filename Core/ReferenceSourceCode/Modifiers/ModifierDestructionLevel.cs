using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Modifiers {
    /// <summary>
    /// Controls how much of the ship is destroyed when generating the
    /// Wrecked Empty Ship scenario. Defaults to the chosen Difficulty's
    /// DefaultDestructionLevel, unless the player overrides the slider
    /// in custom game setup.
    /// </summary>
    public sealed class ModifierDestructionLevel : Modifier, IScenarioModifier {
        public const string ModifierId = "DestructionLevel";
        public override int SortOrder => 11;
        public override string GroupName => T.MenuGameSettings;
        public override string Id => ModifierId;
        private const float minVal = 0.15f;
        private const float maxVal = 0.75f;
        private const float step = 0.05f;
        public override string NameT => nameT ??=
            $"{nameKey.T()}: {Mathf.RoundToInt(curVal * 100)}%";
        public override bool CanChangeInGame => false;
        public override bool PreventsAchievements => false;
        public override bool SkipSandbox => true;
        /// <summary>Effective destruction level, [0.15 .. 0.75].</summary>
        public static float Current;
        /// <summary>Destruction multiplier, [0.2 .. 1.0]. 1.0 == today's default.</summary>
        public static float Intensity => Current / 0.75f;
        /// <summary>Fraction of loot to place into storage instead of scattering.</summary>
        public static float StorageRatio => Mathf.InverseLerp(0.75f, 0.15f, Current);
        /// <summary>Current difficulty's DefaultDestructionLevel. The slider widget
        /// binds to this so it updates live when Difficulty changes during setup,
        /// without needing the modifiers panel to rebuild.</summary>
        public static readonly BFloat DefaultBindable = new BFloat(0.75f);
        private float defaultVal = 0.75f;
        private float curVal;
        [SelfInit]
        private static void Register() {
            Current = 0.75f;
            Register(new ModifierDestructionLevel());
        }

        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{Mathf.RoundToInt(curVal * 100)}%");
            slider.Slider.Load(curVal, OnChange, minVal, maxVal);
            slider.Slider.SetValueTextFunc((v) => $"{Mathf.RoundToInt(v * 100)}%");
            AddTip(lifetime, slider, DescT);
            DefaultBindable.Bind(lifetime, v => {
                if (IsActive) { return; } // player already overrode the slider — leave it
                curVal = v;
                Current = v;
                nameT = null;
                slider.Slider.SetValue(v, false); // moves the thumb, no onChange loop
                slider.Label.SetText2($"{Mathf.RoundToInt(v * 100)}%");
            }, notifyImmediately: false);
        }

        private void OnChange(float v) {
            Current = curVal = Maths.ClampSnap(v, minVal, maxVal, step);
            IsActive = !Maths.Approximately(curVal, defaultVal);
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        // Pins the destruction level from code, for tests that start a game without the
        // setup UI. Apply() falls back to the difficulty default for a modifier the player
        // never touched, so a forced value only survives if it reads as an override.
        public static void ForceInSetup(float val) {
            ModifierDestructionLevel mod = (ModifierDestructionLevel) ById[ModifierId];
            Current = mod.curVal = Maths.ClampSnap(val, minVal, maxVal, step);
            mod.IsActive = !Maths.Approximately(mod.curVal, mod.defaultVal);
            mod.nameT = null;
        }

        // The scenario decides: New Beginning starts gentler than the difficulty's default
        private static float DefaultFor(Scenario scenario, Difficulty difficulty) {
            return scenario?.DefaultDestructionLevel(difficulty)
                ?? difficulty?.DefaultDestructionLevel ?? 0.75f;
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            defaultVal = DefaultFor(data?.Scenario, data?.Difficulty);
            Current = curVal = defaultVal;
            DefaultBindable.Set(defaultVal);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetFloat(key);
            }
        }

        /// <summary>
        /// Called when the Difficulty dropdown changes during custom game setup,
        /// so any on-screen slider tracks the new difficulty's default live
        /// (via DefaultBindable) unless the player already overrode it.
        /// </summary>
        public static void RefreshForDifficultyChange(GameSetupData data) {
            if (ById.TryGetValue(ModifierId, out var mod) && mod is ModifierDestructionLevel m) {
                m.defaultVal = DefaultFor(data.Scenario, data.Difficulty);
                DefaultBindable.Set(m.defaultVal);
            }
        }

        private const string key = "destruction_level";
        private static readonly int keyHash = Hashes.S(key);

        public float DefaultValueForScenario(Scenario scenario) {
            return 0.75f;
        }

        public bool ShowInScenario(Scenario scenario) {
            return scenario.UsesDestructionLevel;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            nameT = null;
            var diffDefault = DefaultFor(s.Scenario, s.Difficulty);
            defaultVal = diffDefault;
            DefaultBindable.Set(diffDefault);
            if (isActive) {
                if (isLoading) {
                    curVal = s.Prefs.GetFloat(key, keyHash, diffDefault);
                } else {
                    s.Prefs.SetFloat(key, curVal);
                }
                Current = curVal;
            } else {
                Current = curVal = diffDefault;
                if (!isLoading) {
                    s.Prefs.UnsetFloat(key);
                }
            }
        }
    }
}
