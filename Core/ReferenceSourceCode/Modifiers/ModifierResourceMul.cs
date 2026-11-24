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
    public sealed class ModifierResourceMul : Modifier {
        public const string ModifierId = "ResourceMul";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;
        private const float minMul = 0.5f;
        private const float maxMul = 3f;
        private const float defaultMul = 1f;
        private const float step = 0.25f;
        public override string LabelT => T.ModifierResourcemul_V1.Replace("[{0:0.##x}]", string.Empty);
        public override string NameT => nameT ??= T.ModifierResourcemul_V1.F(curVal);
        public static float Current;
        public override bool CanChangeInGame => false;
        public override bool SkipSandbox => true;
        public static int Apply(int amt) {
            return Mathf.RoundToInt(amt * Current);
        }
        private float curVal;
        public override bool IsEasierThanDefault =>
            IsActive && !Maths.Approximately(curVal, defaultMul) && curVal > defaultMul;
        [SelfInit]
        private static void Register() {
            Current = defaultMul;
            Register(new ModifierResourceMul());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{curVal:0.##}x");
            slider.Slider.Load(curVal, OnChange, minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => $"{v:0.##}x");
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        public static void ForceInSetup(float val) {
            ModifierResourceMul mod = (ModifierResourceMul) ById[ModifierId];
            Current = defaultMul;
            mod.curVal = val;
            mod.IsActive = val != 1f;
            mod.labelT = null;
        }

        private void OnChange(float v) {
            curVal = Maths.ClampSnap(v, minMul, maxMul, step);
            IsActive = !Maths.Approximately(curVal, 1f);
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetFloat(Pref.ResourceMul);
            }
            Current = curVal = defaultMul;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            labelT = null;
            if (isActive) {
                if (isLoading) {
                    curVal = s.Prefs.GetFloat(Pref.ResourceMul, PrefH.ResourceMul, defaultMul);
                } else {
                    s.Prefs.SetFloat(Pref.ResourceMul, curVal);
                }
                Current = curVal;
            } else {
                Current = defaultMul;
            }
        }
    }
}
