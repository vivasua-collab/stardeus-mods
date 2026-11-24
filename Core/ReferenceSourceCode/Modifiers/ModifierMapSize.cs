using Game.Constants;
using Game.Data;
using KL.Grid;
using UnityEngine;
using Game.Systems.UIUX;
using KL.Utils;
using Game.Utils;

namespace Game.Modifiers {
    public sealed class ModifierMapSize : Modifier {
        public const string ModifierId = "MapSize";
        public override int SortOrder => 10;
        public override string GroupName => T.MenuGameSettings;
        public override string Id => ModifierId;
        #if !PRODUCTION_BUILD
        private const int minVal = 64;
        private const int maxVal = 448;
        #else
        private const int minVal = 192;
        private const int maxVal = 448;
        #endif
        public const int Default = 320;
        private const int step = 64;//Consts.MapChunkSize * 2;
        public override string NameT => nameT ??= $"{T.MapSize}: {curVal}x{curVal}";
        public override string LabelT => T.MapSize;
        public static int Current;
        public override bool CanChangeInGame => false;
        public override bool PreventsAchievements => false;
        private int curVal;
        [SelfInit]
        private static void Register() {
            Current = Default;
            Register(new ModifierMapSize());
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, parent, idx);
            slider.Label.Load(DecoratedLabelT, $"{Current}x{Current}");
            slider.Slider.Load(curVal, OnChange, minVal, maxVal);
            slider.Slider.SetValueTextFunc((v) => $"{curVal}x{curVal}");
            AddTip(lifetime, slider, DescT);
            OnChange(curVal);
        }

        private void OnChange(float v) {
            Current = curVal = (int) Maths.ClampSnap(v, minVal, maxVal, step);
            IsActive = curVal != Default;
            nameT = null;
            A.S.Sig.ModifierChanged.Send(this);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            Current = curVal = Default;
        }

        public override void Apply(GameState s, bool IsActive, bool isLoading) {
            labelT = null;
            if (isLoading) {
                Current = curVal = Pos.GridW;
            }
        }
    }
}
