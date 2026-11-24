using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Systems.UIUX;

namespace Game.Modifiers {
    public sealed class ModifierPetsCount : Modifier, IScenarioModifier {
        public override string Id => "PetsCount";
        public override int SortOrder => 76;
        public override string GroupName => T.InitialCrew;
        private readonly float minVal;
        private readonly float maxVal;
        private float defaultVal = 1;
        public override string LabelT => T.ModifierPetscount_V1.Replace(": {0}", string.Empty);
        public override string NameT => nameT ??= T.ModifierPetscount_V1.F(Mathf.RoundToInt(curVal));
        public static bool IsConfigurable;
        public override bool CanChangeInGame => false;
        public override bool PreventsAchievements => false;
        private float curVal;
        [SelfInit]
        private static void Register() {
            IsConfigurable = true;
            Register(new ModifierPetsCount(0, 5));
        }
        public ModifierPetsCount(int min, int max) {
            minVal = min;
            maxVal = max;
        }

        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            if (!IsConfigurable) { return; }
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, Mathf.RoundToInt(curVal).ToString());
            slider.Slider.Load(curVal, OnChange, minVal, maxVal);
            slider.Slider.SetValueTextFunc((v) => Mathf.RoundToInt(v).ToString());
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        private void OnChange(float v) {
            curVal = Mathf.RoundToInt(v);
            IsActive = Mathf.RoundToInt(curVal) != Mathf.RoundToInt(defaultVal);
            nameT = null;
            Apply(The.R.State, IsActive, false);
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetInt(key);
            }
            defaultVal = curVal = DefaultValueForScenario(data?.Scenario ?? Scenario.DefaultScenario);
        }
        private const string key = "pet_count";
        private static readonly int keyHash = Hashes.S(key);

        public static int ValueFor(GameState s, out int defaultVal,
                Scenario scenario = null) {
            scenario ??= s?.Scenario;
            var count = scenario?.StartingPetCount ?? 0;
            defaultVal = count;
            return s.Prefs.GetInt(key, keyHash, Mathf.RoundToInt(defaultVal));
        }

        public float DefaultValueForScenario(Scenario scenario) {
            return scenario.StartingPetCount;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            labelT = null;
            var val = ValueFor(s, out var defVal);
            defaultVal = defVal;
            if (isActive) {
                if (isLoading) {
                    curVal = val;
                } else {
                    s.Prefs.SetInt(key, Mathf.RoundToInt(curVal));
                }
            } else {
                curVal = defaultVal;
                if (!isLoading) {
                    s.Prefs.UnsetInt(key);
                }
            }
        }

        public bool ShowInScenario(Scenario scenario) {
            return scenario.StartingPetCount > 0;
        }
    }
}
