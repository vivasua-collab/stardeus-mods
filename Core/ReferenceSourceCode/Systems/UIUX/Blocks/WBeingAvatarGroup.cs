using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBeingAvatarGroup : WGroup, ICustomPoolable, ICloseableWidget {
        [SerializeField] private GameObject popout;
        [SerializeField] private WToggleBeingListItem listToggleItemPrefab;
        [SerializeField] private WBeingListItem listItemPrefab;
        [SerializeField] private WToggle globalToggle;
        [SerializeField] private GameObject toggleWrapper;
        [SerializeField] private HorizontalLayoutGroup headerLayout;
        [SerializeField] private GameObject footer;
        [SerializeField] private WButtonText applyBtn;
        [SerializeField] private WButtonText panelHeader;
        [SerializeField] private WButtonText ctrlBtn1;
        [SerializeField] private WButtonText ctrlBtn2;
        [SerializeField] private RectOffset paddingWithToggles;
        [SerializeField] private RectOffset paddingWithoutToggles;
        [SerializeField] private ScrollRect scroll;
        private Lifetime lifetime;
        private bool hasToggles;
        private float pendingScrollRestore = -1f;
        private int scrollRestoreFrames;
        public bool IsShowing;
        public BeingGroup Group;

        private readonly Dictionary<BeingData, bool> toggleChanges = new();
        private readonly List<WToggleBeingListItem> spawnedToggleable = new();
        private void Awake() {
            MiscUtils.DestroyChildren(Container);
            popout.SetActiveFast(false);
            footer.SetActiveFast(false);
            panelHeader.SetOnClick(Hide);
            applyBtn.Load(T.Apply);
            applyBtn.SetOnClick(ApplyToggles);
            globalToggle.SetOnToggle(ToggleAll);
        }

        private void ToggleAll(bool yes) {
            globalToggle.ClearCheckmarkOverride();
            foreach (var b in Group.Members) {
                toggleChanges[b] = yes;
            }
            foreach (var w in spawnedToggleable) {
                w.LoadToggle(yes);
            }
            footer.SetActiveFast(true);
        }

        private void ApplyToggles() {
            bool changed = false;
            foreach (var change in toggleChanges) {
                var before = change.Key.HideAvatarButton;
                var after = !change.Value;
                if (before != after) {
                    changed = true;
                    change.Key.HideAvatarButton = after;
                }
            }
            Hide();
            if (changed) {
                A.S.Sig.RebuildBeingGroups.Send();
            }
        }

        public void Show(BeingGroup beings) {
            bool refresh = IsShowing && Group == beings;
            float savedScroll = refresh && scroll != null
                ? scroll.verticalNormalizedPosition : 1f;

            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create($"WBeingAvatarGroup_{beings.Id}");

            hasToggles = !beings.IsNotControllable;
            if (hasToggles) {
                toggleWrapper.SetActiveFast(true);
            }
            headerLayout.padding = hasToggles ? paddingWithToggles : paddingWithoutToggles;
            Group = beings;
            ctrlBtn1.Load(T.SelectAll, () => SelectAll(beings));
            // ctrlBtn2 could be used in future to add mass action for the group
            ctrlBtn2.SetActiveFast(false);
            panelHeader.SetText(beings.NameT);
            IsShowing = true;
            int idx = 0;
            bool showToggle = !beings.IsNotControllable;
            spawnedToggleable.Clear();
            foreach (var b in beings.SortedMembers()) {
                WBase wbase = null;
                if (hasToggles) {
                    var w = UISpawn.Widget(lifetime, listToggleItemPrefab, Container,
                        idx++);
                    wbase = w;
                    w.Load(b, showToggle);
                    w.SetOnToggle(v => ToggleBeing(b, v));
                    spawnedToggleable.Add(w);
                    if (toggleChanges.TryGetValue(b, out var tv)) {
                        w.LoadToggle(tv);
                    }
                } else {
                    var w = UISpawn.Widget(lifetime, listItemPrefab, Container, idx++);
                    w.Load(b);
                    wbase = w;
                }
                UIUtils.AddHover(lifetime, wbase, (h) => {
                    if (b.Being != null) {
                        EntityUtils.ShowEntityLink(null, b.Being, h);
                    }
                });
            }
            UpdateGlobalToggle();
            Rect.SetAsLastSibling();
            popout.SetActiveFast(true);
            if (scroll != null) {
                if (refresh) {
                    pendingScrollRestore = savedScroll;
                    scrollRestoreFrames = 2;
                    ApplyScrollRestore();
                } else {
                    Canvas.ForceUpdateCanvases();
                    scroll.verticalNormalizedPosition = 1f;
                }
            }
            if (refresh && toggleChanges.Count > 0) {
                footer.SetActiveFast(true);
            }
        }

        private void ApplyScrollRestore() {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(pendingScrollRestore);
        }

        private void LateUpdate() {
            if (scrollRestoreFrames > 0 && scroll != null && pendingScrollRestore >= 0f) {
                scrollRestoreFrames--;
                scroll.verticalNormalizedPosition = Mathf.Clamp01(pendingScrollRestore);
                if (scrollRestoreFrames == 0) { pendingScrollRestore = -1f; }
            }
        }

        private void SelectAll(BeingGroup beings) {
            var list = Caches.BeingList;
            list.Clear();
            foreach (var b in beings.Members) {
                if (b.Being == null) { continue; }
                list.Add(b.Being);
            }
            A.S.Query.SelectBeings.Ask(list);
            Hide();
        }

        private void ToggleBeing(BeingData b, bool v) {
            toggleChanges[b] = v;
            UpdateGlobalToggle();
            footer.SetActiveFast(true);
        }

        private void UpdateGlobalToggle() {
            bool hasSelected = false;
            bool hasDeselected = false;
            foreach (var mem in Group.Members) {
                if (!toggleChanges.TryGetValue(mem, out var selected)) {
                    selected = !mem.HideAvatarButton;
                }
                if (selected) {
                    hasSelected = true;
                } else {
                    hasDeselected = true;
                }
                if (hasSelected && hasDeselected) {
                    break;
                }
            }
            if (hasSelected && hasDeselected) {
                globalToggle.SetValue(true, notify: false);
                globalToggle.SetCheckmarkOverride(GraphicsId.UIButtonsCheckDash,
                    The.UITheme.GetColor("White", this));
            } else if (hasSelected && !hasDeselected) {
                globalToggle.ClearCheckmarkOverride();
                globalToggle.SetValue(true, notify: false);

            } else if (hasDeselected && !hasSelected) {
                globalToggle.SetValue(false, notify: false);
                globalToggle.ClearCheckmarkOverride();
            }
        }

        public void Hide() {
            if (!IsShowing) { return; }
            IsShowing = false;
            toggleChanges.Clear();
            footer.SetActiveFast(false);
            spawnedToggleable.Clear();
            pendingScrollRestore = -1f;
            scrollRestoreFrames = 0;
            A.S.Query.ToggleBeingGroupUI.Ask(Group);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public override string ToString() {
            return $"WBeingAvatarGroup[{Group}]";
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            IsShowing = false;
            Group = null;
            toggleChanges.Clear();
            spawnedToggleable.Clear();
            pendingScrollRestore = -1f;
            scrollRestoreFrames = 0;
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public bool TryCloseWidget() {
            if (IsShowing) {
                Hide();
                return true;
            }
            return false;
        }
    }
}
