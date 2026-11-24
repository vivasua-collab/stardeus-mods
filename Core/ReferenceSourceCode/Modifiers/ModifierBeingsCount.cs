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
    public sealed class ModifierBeingsCount : Modifier, IScenarioModifier {
        public static string ModifierId(string species) {
            return $"BeingsCount_{species}";
        }
        public override int SortOrder => 75;
        public override string GroupName => T.InitialCrew;
        private readonly string species;
        private readonly string id;
        public override string Id => id;

        private readonly float minVal;
        private readonly float maxVal;
        private float defaultVal = 1;
        public override string LabelT => base.LabelT.Replace(": {0}", string.Empty);
        public override string NameT => nameT ??= base.LabelT.F(Mathf.RoundToInt(curVal));
        public static bool IsConfigurable;
        public override bool CanChangeInGame => false;
        public override bool ForceShowInCloneGame => true;
        private float curVal;
        public override bool IsEasierThanDefault =>
            Mathf.RoundToInt(curVal) > Mathf.RoundToInt(defaultVal);
        [SelfInit]
        private static void Register() {
            IsConfigurable = true;
            Register(new ModifierBeingsCount(SpeciesId.Robot, 0, 5));
            Register(new ModifierBeingsCount(SpeciesId.Drone, 0, 5));
            Register(new ModifierBeingsCount(SpeciesId.Carrier, 0, 5));
            Register(new ModifierBeingsCount(SpeciesId.Human, 0, 5));
        }
        public ModifierBeingsCount(string speciesId, int min, int max) {
            minVal = min;
            maxVal = max;
            species = speciesId;
            id = ModifierId(speciesId);
        }

        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            if (!IsConfigurable) { return; }
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT);
            slider.Slider.Load(curVal, OnChange, minVal, maxVal);
            slider.Slider.SetValueTextFunc((v) => Mathf.RoundToInt(v).ToString());
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        private void OnChange(float v) {
            curVal = Mathf.RoundToInt(v);
            IsActive = Mathf.RoundToInt(curVal) != Mathf.RoundToInt(defaultVal);
            if (IsActive) {
                D.Warn("Setting modifier as active: {0} => {1} / {2}", Id, curVal, defaultVal);
            }
            nameT = null;
            Apply(A.S, IsActive, false);
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                A.S.Prefs.UnsetInt(KeyFor(species));
            }
            IsActive = false;
            var sc = data?.Scenario ?? Scenario.DefaultScenario;
            defaultVal = curVal = DefaultValueForScenario(sc);
            D.Warn("Resetting modifier: {0} => {1} / {2}", Id, curVal, defaultVal);
        }

        public static string KeyFor(string species) {
            return $"start.num.{species}";
        }
        public static int ValueFor(GameState s, string species, out int defaultVal,
                Scenario scenario = null) {
            scenario ??= s.Scenario;
            var counts = scenario.GetStartingBeingCounts();

            var sp = Species.Get(species);
            var k = KeyFor(species);
            var kh = Hashes.S(k);

            if (Arrays.IsEmpty(counts)) {
                defaultVal = 0;
                return 0;
            }
            foreach (var cnt in counts) {
                if (cnt.Item1 == sp) {
                    defaultVal = cnt.Item2;
                    return s.Prefs.GetInt(k, kh, Mathf.RoundToInt(defaultVal));
                }
            }

            defaultVal = 0;
            return 0;
        }

        public float DefaultValueForScenario(Scenario scenario) {
            var counts = scenario.GetStartingBeingCounts();
            var sp = Species.Get(species);

            if (Arrays.IsEmpty(counts)) {
                return 0;
            }
            foreach (var cnt in counts) {
                if (cnt.Item1 == sp) {
                    return cnt.Item2;
                }
            }
            return 0f;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            labelT = null;
            var val = ValueFor(s, species, out var defVal);
            defaultVal = defVal;
            if (isActive) {
                if (isLoading) {
                    curVal = val;
                } else {
                    s.Prefs.SetInt(KeyFor(species), Mathf.RoundToInt(curVal));
                }
            } else {
                curVal = defaultVal;
                if (!isLoading) {
                    s.Prefs.UnsetInt(KeyFor(species));
                }
            }
            /*
            D.Warn("Applying modifier: {0}. Val: {1}. Loading: {2}. Active: {3}",
                Id, curVal, isLoading, IsActive);
                */
        }

        public bool ShowInScenario(Scenario scenario) {
            var starting = scenario.GetStartingBeingCounts();
            if (starting == null) { return false; }
            foreach (var cnt in starting) {
                if (cnt.Item1.Id == species && cnt.Item2 > 0) {
                    return true;
                }
            }
            return false;
        }
    }
}
