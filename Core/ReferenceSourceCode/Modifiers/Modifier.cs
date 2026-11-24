using System;
using System.Collections.Generic;
using System.Text;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using Game.Systems.UIUX;

namespace Game.Modifiers {
    public abstract class Modifier {
        public static string Asterisk => A.IsSaveTemplate ? string.Empty : Texts.Accented("*");
        public abstract int SortOrder { get; }
        public abstract string GroupName { get; }
        [ExecuteOnReload]
        public static void CleanupStatics() {
            All.Clear();
            Modified.Clear();
            ById.Clear();
            activeNames?.Clear();
            activeNames = null;
        }
        private static List<string> activeNames;
        public static readonly List<Modifier> All = new();
        public static readonly Dictionary<string, Modifier> ById = new();
        public static readonly Dictionary<string, Modifier> Modified = new();

        public static void ResetAll(GameSetupData data) {
            Modified.Clear();
            activeNames = null;
            foreach (var m in All) {
                m.Reset(data);
            }
            A.S.Sig.ModifierChanged.Send(null);
        }
        protected Scenario Scenario => The.R.State.Scenario;

        public virtual bool SkipSandbox => false;
        public virtual bool ForceShowInCloneGame => false;
        public virtual bool SkipModifiersPanel => false;
        public virtual bool PreventsAchievements => true;

        public bool IsModified => IsActive != IsActiveByDefault;
        // A two-way modifier costs achievements only when its setting makes the run easier
        public virtual bool IsEasierThanDefault => IsModified;
        public bool BlocksAchievements => PreventsAchievements && IsEasierThanDefault;
        public virtual bool SkipDescriptionTooltip => false;
        public virtual bool CanChangeInGame => true;

        // This gets called before mod loader!
        public virtual void Reset(GameSetupData data) {
            IsActive = IsActiveByDefault;
        }

        public virtual void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            WToggleText toggle = null;
            toggle = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WTabPanelItem_Toggle, parent, idx);
            toggle.Load(DecoratedLabelT, IsActive, (v) => {
                ToggleControlInNewGame(v, toggle);
            });
            AddTip(lifetime, toggle, DescT);
        }
        public virtual void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var toggle = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WTabPanelItem_Toggle, parent, idx);
            LoadInGameToggle(lifetime, toggle);
        }

        /// Id of the modifier that shares a row with this one. Only the left modifier of a
        /// pair declares it, and both have to be in the same group.
        public virtual string PairedWithId => null;

        public void CreateInGameControlsPaired(Lifetime lifetime, int idx, RectTransform parent,
                Modifier other) {
            var pair = UISpawn.Widget<WToggle2>(lifetime, UIPrefabId.WTabPanelItem_Toggle2, parent, idx);
            LoadInGameToggle(lifetime, pair.Toggle1);
            other.LoadInGameToggle(lifetime, pair.Toggle2);
        }

        private void LoadInGameToggle(Lifetime lifetime, WToggleText toggle) {
            toggle.Load(DecoratedLabelT, IsActive, (v) => {
                ToggleControlInGame(v, toggle);
            });
            AddTip(lifetime, toggle, DescT);
        }

        private void ToggleControlInNewGame(bool v, WToggleText toggle) {
            #region d
            #if !STARDEUS_DEMO
            // if (PreventsAchievements && v != IsActiveByDefault && !A.IsSaveTemplate) {
            //     WPopup.Create(true, IconId.CWarning, T.Warning,
            //         T.ModifierAchievementBlockWarning)
            //         .WithAction(T.Cancel, () => {
            //             toggle.SetValue(IsActive, false);
            //         })
            //         .WithAction2(T.Confirm, () => {
            //             IsActive = v;
            //         }).Spawn(UIPoolRoot.SystemPopupContainer);
            // } else {
                IsActive = v;
            // }
            A.S.Sig.ModifierChanged.Send(this);
            #endif
            #endregion
        }
        private void ToggleControlInGame(bool v, WToggleText toggle) {
            #region d
            #if !STARDEUS_DEMO
            // Going back to the default never costs achievements
            if (PreventsAchievements && v != IsActiveByDefault && !A.S.IsAchievementsBlocked) {
                WPopup.Create(true, IconId.CWarning, T.Warning,
                    T.ModifierAchievementBlockWarning)
                    .WithAction2(T.Cancel, () => {
                        toggle.SetValue(IsActive, false);
                    })
                    .WithAction(T.Confirm, () => {
                        ApplyInGame(v);
                    }).Spawn(UIPoolRoot.SystemPopupContainer);
            } else {
                ApplyInGame(v);
            }
            #endif
            #endregion
        }

        // A modifier only does its work in Apply, which runs when a game is started or
        // loaded. A change made while playing has to apply itself, or it sits in IsActive
        // doing nothing until the save is reloaded.
        private void ApplyInGame(bool v) {
            IsActive = v;
            var s = The.R?.State;
            if (GameState.IsCurrent(s)) {
                Apply(s, v, false);
            }
            if (BlocksAchievements) {
                A.S.IsAchievementsBlocked = true;
            }
            A.S.Sig.ModifierChanged.Send(this);
        }

        public abstract string Id { get; }
        protected string nameT;
        protected string labelT;
        private string descT;
        protected string nameKey => $"modifier.{Id}";
        protected string descKey => $"{nameKey}.desc";
        /// <summary>
        /// Label + Value
        /// </summary>
        public virtual string NameT => nameT ??= nameKey.T();
        public virtual string LabelT => labelT ??= nameKey.T();
        public virtual string DescT => descT ??= descKey.T();
        public string DecoratedLabelT {
            get {
                if (PreventsAchievements && A.S?.Scenario?.IsSandbox != true) {
                    return $"{Asterisk}{LabelT}";
                }
                return LabelT;
            }
        }
        public bool IsActive;
        public virtual bool IsActiveByDefault => false;
        protected static void Register(Modifier m) {
            if (ById.ContainsKey(m.Id)) {
                D.Err("Trying to add duplicate modifier: {0}. Already added: {1}. Trying to add {2}",
                    m.Id, ById[m.Id].GetType().Name, m.GetType().Name);
                return;
            }
            ById.Add(m.Id, m);
            All.Add(m);
        }
        public virtual void Apply(GameState s, bool IsActive, bool isLoading) {
            // Override to implement
        }


        public static string ActiveNames(string separator) {
            if (activeNames == null) {
                activeNames = new();
            } else {
                activeNames.Clear();
            }
            foreach (var m in Modified) {
                activeNames.Add(m.Value.NameT);
            }
            return string.Join(separator, activeNames);
        }
        protected void AddTip(Lifetime life, WBase owner, string tip) {
            if (!string.IsNullOrEmpty(tip)) {
                Tooltip.CreateFor(life, owner, owner.Rect, Texts.Beautify(tip));
            }
        }
    }
}
