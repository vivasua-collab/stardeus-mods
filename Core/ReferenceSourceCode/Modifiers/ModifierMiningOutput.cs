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
    public sealed class ModifierMiningOutput : Modifier {
        public const string ModifierId = "MiningOutput";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;
        private const float minMul = 0.5f;
        private const float maxMul = 10f;
        private const float defaultMul = 1f;
        private const float step = 0.1f;
        public override string NameT => nameT ??= $"{T.ModifierMiningoutput}: {curMul:0.##}x";
        public static float Current;
        public override bool CanChangeInGame => true;
        public override bool SkipSandbox => false;
        private float curMul;
        public override bool IsEasierThanDefault =>
            IsActive && !Maths.Approximately(curMul, defaultMul) && curMul > defaultMul;
        [SelfInit]
        private static void Register() {
            Current = defaultMul;
            Register(new ModifierMiningOutput());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{curMul:0.#}x");
            slider.Slider.Load(curMul, OnChange, minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => $"{v:0.#}x");
            AddTip(lifetime, slider, DescT);
            OnChange(curMul);
        }
        public override void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{curMul:0.#}x");
            slider.Slider.Load(curMul, (i) => OnChangeInGame(The.R.State, i), minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => $"{v:0.#}x");
            AddTip(lifetime, slider, DescT);
            OnChange(curMul);
        }
        private void OnChangeInGame(GameState s, float v) {
            OnChange(v);
            Apply(s, IsActive, false);
            if (BlocksAchievements) {
                s.IsAchievementsBlocked = true;
            }
            A.S.Sig.ModifierChanged.Send(this);
        }

        private void OnChange(float v) {
            curMul = Maths.ClampSnap(v, minMul, maxMul, step);
            IsActive = !Maths.Approximately(curMul, 1f);
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetFloat(Pref.MiningOutput);
            }
            Current = curMul = defaultMul;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            labelT = null;
            if (isActive) {
                if (isLoading) {
                    curMul = s.Prefs.GetFloat(Pref.MiningOutput, PrefH.MiningOutput, defaultMul);
                } else {
                    s.Prefs.SetFloat(Pref.MiningOutput, curMul);
                }
                Current = curMul;
            } else {
                // A loaded save never ran Reset, and the in-game slider starts from curMul
                Current = curMul = defaultMul;
            }
        }
    }
}
