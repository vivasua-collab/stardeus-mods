using Game.Constants;
using Game.Data;
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
    public sealed class ModifierShipSize : Modifier {
        public const string ModifierId = "ShipSize";
        public override int SortOrder => 10;
        public override string GroupName => T.MenuGameSettings;
        public override string Id => ModifierId;
        private const float minVal = 1.0f;
        private const float maxVal = 3.0f;
        private const float defaultVal = 1f;
        private const float step = 0.1f;
        public override string NameT => nameT ??= $"{T.SituationShipsize}: {Current:0.##}x";
        public override string LabelT => T.SituationShipsize;
        public static float Current;
        public static bool IsConfigurable;
        public override bool CanChangeInGame => false;
        public override bool PreventsAchievements => false;
        public override bool SkipSandbox => true;
        private float curVal;
        [SelfInit]
        private static void Register() {
            Current = defaultVal;
            IsConfigurable = true;
            Register(new ModifierShipSize());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            if (!IsConfigurable) { return; }
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{curVal:0.##}x");
            slider.Slider.Load(curVal, OnChange, minVal, maxVal);
            slider.Slider.SetValueTextFunc((v) => $"{curVal:0.##}x");
            AddTip(lifetime, slider, DescT);
        }

        private void OnChange(float v) {
            Current = curVal = Maths.ClampSnap(v, minVal, maxVal, step);
            IsActive = curVal != defaultVal;
            A.S.Sig.ModifierChanged.Send(this);
            // clear name cache;
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            Current = curVal = defaultVal;
        }

        public override void Apply(GameState s, bool IsActive, bool isLoading) {
            nameT = null;
            if (IsActive) {
                if (isLoading) {
                    Current = curVal = s.Prefs.GetFloat(Pref.StartShipSize,
                        PrefH.StartShipSize, defaultVal);
                } else {
                    s.Prefs.SetFloat(Pref.StartShipSize, curVal);
                }
            }
        }
    }
}
