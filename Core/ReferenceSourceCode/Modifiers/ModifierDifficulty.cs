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
using Game.Platform;
using Game.Systems.UIUX;
using System;

namespace Game.Modifiers {
    public sealed class ModifierDifficulty : Modifier {
        private string defaultDiff => Difficulty.DefaultId;
        public const string ModifierId = "Difficulty";
        public override int SortOrder => 69;
        public override string GroupName => T.GameDifficulty;
        public override bool SkipDescriptionTooltip => true;
        public override string Id => ModifierId;
        public override string NameT => current == null ? T.GameDifficulty
            : $"{T.GameDifficulty} [{current.Name}]";
        public override string DescT => T.GameDifficultyTip;
        public override bool PreventsAchievements => false;
        public override bool SkipModifiersPanel => true;
        private int currentDifficulty;
        private static List<string> difficulties;
        private Difficulty current;
        private int defaultVal;
        [SelfInit]
        private static void Register() {
            Register(new ModifierDifficulty());
        }

        public static List<string> Difficulties {
            get {
                if (difficulties != null) { return difficulties; }
                difficulties = Difficulty.AllSorted().ConvertAll(d => d.Name);
                if (difficulties.Count == 0) {
                    difficulties = null;
                }
                return difficulties;
            }
        }

        public static Difficulty ForIdx(int idx) {
            var difficulties = Difficulty.AllSorted();
            return difficulties[idx];
        }

        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            LoadDifficulties(defaultDiff);
            var w = UISpawn.Widget<WDropdownWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Dropdown, parent, idx);
            w.Label.Load(T.GameDifficulty);
            w.Dropdown.Load(Difficulties, currentDifficulty, OnChange);
            AddTip(lifetime, w, DescT);
            OnChange(currentDifficulty);
        }
        public override void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            LoadDifficulties(The.R.State.Difficulty.Id);
            var w = UISpawn.Widget<WDropdownWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Dropdown, parent, idx);
            w.Label.Load(T.GameDifficulty);
            w.Dropdown.Load(Difficulties, currentDifficulty, (i) => OnChangeInGame(The.R.State, i));
            AddTip(lifetime, w, DescT);
            OnChange(currentDifficulty);
        }

        public void Set(Difficulty diff) {
            current = diff;
        }

        private void OnChangeInGame(GameState s, int sel) {
            OnChange(sel);
            s.Difficulty = current;
            PlatformUtils.SuggestRestart();
            A.S.Sig.ModifierChanged.Send(this);
        }

        private void OnChange(int sel) {
            if (sel == -1) {
                sel = defaultVal;
            }
            currentDifficulty = sel;
            IsActive = currentDifficulty != defaultVal;
            //D.Log("Trying to find diff: {0} in {1}", currentDifficulty, D.Dump(Difficulties));
            if (sel < 0 || sel >= Difficulties.Count) {
                D.ErrSoft("Difficulty list on change trying to select {0}, while options are: {1}",
                    sel, D.Dump(Difficulties));
                current = Difficulty.Find(defaultDiff);
            } else {
                current = Difficulty.FindByName(
                    Difficulties[currentDifficulty])
                        ?? Difficulty.Find(defaultDiff);
            }
            A.S.Sig.ModifierChanged.Send(this);
        }

        private void LoadDifficulties(string currentDefault) {
            if (difficulties == null) {
                IsActive = false;
            }
            current = Difficulty.Find(currentDefault);
            defaultVal = currentDifficulty = Difficulties.IndexOf(current.Name);
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            currentDifficulty = defaultVal;
            current = null;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            if (isLoading) {
                IsActive = s.Difficulty.Id != defaultDiff;
                current = s.Difficulty;
                return;
            }
            if (current == null) {
                // D.Err("Failed applying difficulty modifier!");
                current = Difficulty.Find(defaultDiff);
            }
            s.Difficulty = current;
        }

        public static void SetDifficulty(Difficulty difficulty) {
            if (ById.TryGetValue(ModifierId, out var mod)) {
                if (mod is ModifierDifficulty diffMod) {
                    diffMod.Set(difficulty);
                }
            }
        }
    }
}
