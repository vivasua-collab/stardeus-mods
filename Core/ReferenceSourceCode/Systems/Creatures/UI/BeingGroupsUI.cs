using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Pool;
using UnityEngine;
using Game.UI;
using Game.Systems.UIUX;
using UnityEngine.UI;
using Game.Rendering;
using Game.Input;
using Game.Utils;
using System;
using KL.Utils;

namespace Game.Systems.Creatures {
    public sealed class BeingGroupsUI : MonoBehaviour {
        [SerializeField] private RectTransform groupsContainer;
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private WBeingAvatar avatarWidgetPrefab;
        [SerializeField] private WBeingGroup groupContainerPrefab;
        /// <summary>
        /// For friendlies
        /// </summary>
        [SerializeField] private BeingGroupUIWidget groupWidgetPrefab;
        /// <summary>
        /// For Enemies and Dead
        /// </summary>
        [SerializeField] private BeingGroupUIWidget groupWidgetPrefab2;
        [SerializeField] private WBeingAvatarGroup groupPopoutPrefab;
        private RectTransform rekt;
        private RectTransform topBarRect;
        private readonly Dictionary<BeingGroup, BeingGroupUIWidget> groupIconWidgets = new();
        private readonly Dictionary<BeingData, WBeingAvatar> spawnedAvatars = new();
        public Dictionary<BeingData, WBeingAvatar> SpawnedAvatars => spawnedAvatars;
        private readonly List<WBeingGroup> spawnedGroups = new();
        private readonly List<BeingGroupUIWidget> spawnedGroupIcons = new();
        public Dictionary<BeingGroup, BeingGroupUIWidget> SpawnedWidgets => groupIconWidgets;
        private List<BeingGroup> groups;
        private GameState S;
        private WBeingAvatarGroup groupPopout;
        private BeingGroup showingGroup;
        public BeingGroup ShowingGroup => showingGroup;
        private Lifetime lifetime;
        private bool needsRebuild;
        private bool needsFullRebuild;
        private bool isListening;

        public void CleanLists() {
            groupIconWidgets.Clear();
            spawnedAvatars.Clear();
            spawnedGroups.Clear();
        }

        private void Awake() {
            rekt = GetComponent<RectTransform>();
        }

        private void OnDestroy() {
            if (isListening) {
                isListening = false;
                The.SysSig.ResolutionChanged.RemoveListener(OnResolutionChange);
            }
        }

        private void OnResolutionChange() {
            RebuildGroups();
        }

        private void OnLoadSave(GameState state) {
            S = state;
            S.Sig.RebuildBeingGroups.AddListener(RebuildGroups, gameObject);
            S.Query.ToggleBeingGroupUI.SetResponder(ToggleGroupUI);
            S.Sig.EveryFrame.AddListener(EveryFrame, gameObject);
            S.Clock.OnTick.AddListener(OnTick, gameObject);
            S.Sig.BeingGroupChanged.AddListener(OnGroupChanged, gameObject);
            S.Sig.MultiSelect.AddListener(OnMultiSelect, gameObject);
            S.Sig.BeingsSelected.AddListener(OnBeingsSelected, gameObject);
            S.UISig.ToggleShipCombatMode.AddListener(OnShipCombatMode, gameObject);
            Ready.When(WhenH.BeingsLoaded, InitializeGroups);
        }

        private void OnShipCombatMode(bool on) {
            gameObject.SetActiveFast(!on);
            if (on && showingGroup != null) {
                ToggleGroupUI(null);
            }
        }

        private void OnBeingsSelected(List<Being> beings) {
            foreach (var av in spawnedAvatars.Values) {
                var b = av.Being;
                if (b == null) {
                    av.SetSelected(false);
                    continue;
                }
                av.SetSelected(beings.Contains(b));
            }
        }

        private void OnGroupChanged(BeingData data, int oldGroupId) {
            RebuildGroups();
            Rebuild();
        }

        private void OnTick(long ticks) {
            foreach (var avatar in spawnedAvatars.Values) {
                avatar.UpdateStatus();
            }
        }

        private void OnMultiSelect(List<Being> b, List<Obj> o, List<Tile> t) {
            OnBeingsSelected(b);
        }


        private void EveryFrame() {
            if (S == null) {
                return;
            }
            if (needsFullRebuild) {
                needsFullRebuild = false;
                DoRebuildGroups();
                return;
            }
            if (needsRebuild) {
                Rebuild();
            }
            if (showingGroup != null && UIShowing.FullScreenWidget) {
                ToggleGroupUI(showingGroup);
            }
        }

        private bool ToggleGroupUI(BeingGroup group) {
            if (showingGroup != group) {
                showingGroup = group;
            } else {
                showingGroup = null;
            }
            Rebuild();
            return showingGroup != null;
        }

        private void Rebuild() {
            if (showingGroup?.Size == 0) {
                showingGroup = null;
                DoRebuildGroups();
                return;
            }
            needsRebuild = false;
            if (showingGroup != null) {
                if (groupPopout == null) {
                    groupPopout = PrefabPool.Spawn(groupPopoutPrefab, listContainer);
                    // groupPopout.Rect.anchoredPosition = Consts.V2Zero;
                    // groupPopout.Rect.position = Consts.V3Zero;
                }
                groupPopout.Show(showingGroup);
                RepositionList();
                UIShowing.Set(UIShowingFlags.BeingGroup);
            } else {
                if (groupPopout != null) {
                    PrefabPool.Despawn(groupPopout.gameObject);
                    groupPopout = null;
                }
                UIShowing.Unset(UIShowingFlags.BeingGroup);
            }
        }

        private void InitializeGroups() {
            DoRebuildGroups();
            if (!isListening) {
                isListening = true;
                The.SysSig.ResolutionChanged.AddListener(OnResolutionChange, gameObject);
            }
        }

        private void RebuildGroups() {
            needsFullRebuild = true;
        }

        [ContextMenu("Rebuild")]
        private void DoRebuildGroups() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("BeingGroupsUI");

            CleanLists();
            groups ??= S.Query.GetBeingGroups.Ask();
            int nonEmpty = 0;
            var groupSizes = Caches.IntList;
            groupSizes.Clear();
            spawnedGroupIcons.Clear();
            var groupLifetimes = new List<Lifetime>();
            var groupSpacerFlags = new List<bool>();
            var friendlyCount = S.Situation.Friendlies;
            for (int i = 0; i < groups.Count; i++) {
                BeingGroup g = groups[i];
                var cnt = g.LoadMembers();
                if (cnt <= 0) { continue; }
                var compactGroup = g.IsNotControllable;
                g.Dispose();
                g.Lifetime = lifetime.CreateChild($"BeingGroup_{g.Id}",
                    Math.Max(8, cnt * 2 + 8));

                var groupContainer = UISpawn.Widget(g.Lifetime, groupContainerPrefab, rekt, i);
                spawnedGroups.Add(groupContainer);
                groupLifetimes.Add(g.Lifetime);

                var sorted = g.SortedMembers();
                int memIdx = 0;
                int totalMem = 0;
                bool hadHiddenAvatars = false;
                if (!compactGroup) {
                    foreach (var member in sorted) {
                        totalMem++;
                        if (!member.HideAvatarButton) {
                            var avatarWidget = UISpawn.Widget(g.Lifetime,
                                avatarWidgetPrefab, groupContainer.Rect, memIdx++);
                            avatarWidget.Load(g.Lifetime, S, member, true);
                            spawnedAvatars[member] = avatarWidget;
                        } else {
                            hadHiddenAvatars = true;
                        }
                    }
                }
                // A non-empty group must always keep one clickable widget: HideAvatarButton
                // is persisted, and the popout behind this button is the only place it can
                // be undone.
                var needsGroupButton = compactGroup || hadHiddenAvatars || friendlyCount > 10;
                if (needsGroupButton) {
                    memIdx++;
                    var gPrefab = compactGroup ? groupWidgetPrefab2 : groupWidgetPrefab;
                    var w = UISpawn.Widget(g.Lifetime, gPrefab, groupContainer.Rect, 0);
                    w.Load(g, nonEmpty);
                    groupIconWidgets.Add(g, w);
                    spawnedGroupIcons.Add(w);
                }
                // Spacers reserve column 0 under the group button, so they follow the
                // button, not the friendly count
                groupSpacerFlags.Add(!compactGroup && needsGroupButton);
                groupSizes.Add(memIdx);
                nonEmpty++;
            }
            topBarRect ??= Registries.UIWidgets.FastGet(UIWidgetIdH.TopBar)
                .GetComponent<RectTransform>();
            var actualTopBarRectWidth = topBarRect.rect.width;
            var groupCols = GridLayoutOptimizer.Optimize(
                actualTopBarRectWidth - 300f - 64f,
                new Vector2(64, 64), 5, 20, groupSizes, groupSpacerFlags.ToArray());
            for (int i = 0; i < groupCols.Length; i++) {
                var cols = groupCols[i];
                spawnedGroups[i].SetColumns(cols);
                if (!groupSpacerFlags[i] || cols <= 1) { continue; }
                int n = groupSizes[i];
                int rows = GridLayoutOptimizer.RowsWithSpacers(n, cols);
                // Insert one spacer at the start of each row beyond the first, in forward row
                // order so each SetSiblingIndex call lands at the correct absolute position.
                for (int row = 1; row < rows; row++) {
                    UISpawn.Widget<WSpacer>(groupLifetimes[i], UIPrefabId.WSpacer,
                        spawnedGroups[i].Rect, row * cols);
                }
            }

            Rebuild();
            LayoutRebuilder.ForceRebuildLayoutImmediate(groupsContainer);
            RepositionList();
        }

        private void RepositionList() {
            if (showingGroup == null) {
                return;
            }
            var rekt = showingGroup.UI.Rect;
            rekt.GetWorldCorners(Caches.Corners);
            var scale = groupPopout.Rect.localScale.x;

            var offset = showingGroup.IsNotControllable ? 0f : -32f * scale;
            // Video.GetCurrentUIScale(out _);

            if (MouseUtils.ScreenPos.x > RenderUtils.ScreenWidth / 2f) {
                var xy = Caches.Corners[3];
                var width = groupPopout.Rect.rect.width;
                groupPopout.Rect.position = xy - new Vector3(width * scale, offset, 0f);
            } else {
                var xy = Caches.Corners[0];
                groupPopout.Rect.position = xy - new Vector3(0f, offset, 0f);
            }
        }

        private void OnEnable() {
            S = The.R?.State;
            The.SysSig.AfterLoadSaveState.AddListener(OnLoadSave, gameObject);
        }


        private void OnDisable() {
            S = null;
            The.SysSig?.AfterLoadSaveState.RemoveListener(OnLoadSave);
            groups = null;
        }
    }
}
