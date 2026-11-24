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
using System;

namespace Game.Modifiers {
    public sealed class ModifierStoryPace : Modifier {
        public const string ModifierId = "StoryPace";
        public override int SortOrder => 66;
        public override string GroupName => T.SituationStoryevents;
        public override string Id => ModifierId;
        private const float minMul = 0.05f;
        private const float maxMul = 3.0f;
        private const float defaultMul = 1f;
        private const float step = 0.1f;
        public override string NameT => nameT ??= $"{nameKey.T(Units.Percentage(curVal, false))}";
        // FIXME Change translation for label to remove hacks
        public override string LabelT => T.ModifierStorypace;

        public static float Current;
        public override bool CanChangeInGame => true;
        public override bool SkipSandbox => false;
        private float curVal;
        // Fewer events means fewer raids and disasters, so only a slower pace is easier
        public override bool IsEasierThanDefault =>
            IsActive && !Maths.Approximately(curVal, defaultMul) && curVal < defaultMul;
        [SelfInit]
        private static void Register() {
            Current = defaultMul;
            Register(new ModifierStoryPace());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT);
            slider.Slider.Load(curVal, OnChange, minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => {
                return Units.Percentage(v, false);
            });
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }
        public override void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT);
            slider.Slider.Load(curVal,
                (i) => OnChangeInGame(i), minMul, maxMul);
            slider.Slider.SetValueTextFunc((v) => {
                return Units.Percentage(v, false);
            });
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        private void OnChange(float v) {
            curVal = Maths.ClampSnap(v, minMul, maxMul, step);
            IsActive = !Maths.Approximately(curVal, 1f);
            // clear name cache;
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }
        private void OnChangeInGame(float v) {
            OnChange(v);
            Apply(A.S, IsActive, false);
            if (BlocksAchievements) {
                A.S.IsAchievementsBlocked = true;
            }
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetFloat(Pref.StoryPace);
            }
            Current = curVal = defaultMul;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            nameT = null;
            if (isActive) {
                if (isLoading) {
                    curVal = s.Prefs.GetFloat(Pref.StoryPace, PrefH.StoryPace, defaultMul);
                } else {
                    s.Prefs.SetFloat(Pref.StoryPace, curVal);
                }
                Current = curVal;
            } else {
                // A loaded save never ran Reset, and the in-game slider starts from curVal
                Current = curVal = defaultMul;
            }
        }
    }
}
