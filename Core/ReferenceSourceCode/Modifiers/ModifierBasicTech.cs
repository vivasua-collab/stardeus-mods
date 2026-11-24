using System.Collections.Generic;
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
    public sealed class ModifierBasicTech : Modifier {
        public enum Strategy {
            Unlocked,
            Randomize,
            Locked,
        }
        public const string ModifierId = "BasicTech";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;
        // public override string NameT => T.ModifierBasictech;
        // public override string DescT => T.GameDifficultyTip;
        public override bool PreventsAchievements => false;
        public override bool SkipModifiersPanel => false;
        public override bool ForceShowInCloneGame => false;
        public override bool CanChangeInGame => false;
        public override bool SkipSandbox => true;
        private int currentOption;
        private List<string> options;
        public static Strategy Current;
        private int defaultVal;
        // The scenario decides, as with ModifierDestructionLevel: New Beginning starts Locked
        private Strategy scenarioDefault = Strategy.Unlocked;
        [SelfInit]
        private static void Register() {
            Register(new ModifierBasicTech());
        }

        public List<string> Options {
            get {
                if (options != null) { return options; }
                options = new List<string>() {
                    T.ResearchUnlocked,
                    T.Randomize,
                    T.ResearchLocked,
                };
                return options;
            }
        }

        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            LoadOptions(scenarioDefault);

            var w = UISpawn.Widget<WDropdownWithLabel>(lifetime,
                UIPrefabId.WTabPanelItem_Dropdown, parent, idx);
            w.Label.Load(DecoratedLabelT);
            w.Dropdown.Load(Options, currentOption, OnChange);
            AddTip(lifetime, w, DescT);
        }

        private void OnChange(int sel) {
            if (sel == -1) {
                sel = defaultVal;
            }
            currentOption = sel;
            IsActive = currentOption != defaultVal;
            Current = (Strategy) sel;
            A.S.Sig.ModifierChanged.Send(this);
        }

        private void LoadOptions(Strategy currentDefault) {
            if (options == null) {
                IsActive = false;
            }
            Current = currentDefault;
            defaultVal = currentOption = (int) Current;
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            scenarioDefault = data?.Scenario?.DefaultBasicTech ?? Strategy.Unlocked;
            defaultVal = currentOption = (int) scenarioDefault;
            Current = scenarioDefault;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            // application will happen when unlocking will happen
        }
    }
}
