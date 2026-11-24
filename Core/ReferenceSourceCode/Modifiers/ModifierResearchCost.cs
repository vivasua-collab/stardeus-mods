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
    public sealed class ModifierResearchCost : Modifier {
        public const string ModifierId = "ResearchCost";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;
        private const float minMul = 0.1f;
        private const float maxMul = 3f;
        private const float defaultMul = 1f;
        private const float step = 0.1f;
        public override string NameT => nameT ??= $"{T.ModifierResearchcost}: {curVal.ToString("0.#")}x";
        public static float Current;
        public override bool CanChangeInGame => false;
        public override bool SkipSandbox => true;
        public static int Apply(int amt) {
            // 0.1 steps are not exact in float, so 1.3 * 10 lands just above 13
            return Mathf.CeilToInt(amt * Current - 0.001f);
        }

        private float curVal;
        public override bool IsEasierThanDefault =>
            IsActive && !Maths.Approximately(curVal, defaultMul) && curVal < defaultMul;
        [SelfInit]
        private static void Register() {
            Current = defaultMul;
            Register(new ModifierResearchCost());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT);
            slider.Slider.Load(curVal, OnChange, minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => $"{v:0.#}x");
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        public static void ForceInSetup(float val) {
            ModifierResearchCost mod = (ModifierResearchCost) ById[ModifierId];
            Current = defaultMul;
            mod.curVal = val;
            mod.IsActive = val != 1f;
            mod.nameT = null;
        }

        private void OnChange(float v) {
            curVal = Maths.ClampSnap(v, minMul, maxMul, step);
            IsActive = !Maths.Approximately(curVal, 1f);
            // clear name cache;
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetFloat(Pref.ResearchCost);
            }
            Current = curVal = defaultMul;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            nameT = null;
            if (isActive) {
                if (isLoading) {
                    curVal = s.Prefs.GetFloat(Pref.ResearchCost, PrefH.ResearchCost, defaultMul);
                } else {
                    s.Prefs.SetFloat(Pref.ResearchCost, curVal);
                }
                Current = curVal;
            } else {
                Current = defaultMul;
            }
        }
    }
}
