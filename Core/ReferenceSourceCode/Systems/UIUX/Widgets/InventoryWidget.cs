using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class InventoryWidget : BaseWidget {
        [SerializeField] private WIconText[] topItems;
        [SerializeField] private Button buttonExpand;
        [SerializeField] private Button buttonExpandThin;
        [SerializeField] private WButtonText buttonToggleInvPanel;
        [SerializeField] private GameObject arrowExpandThin;
        [SerializeField] private WGroup containerMats;
        [SerializeField] private GameObject containerButtons;
        [SerializeField] private RectTransform containerDrawer;
        [SerializeField] private UIDrawerSlide drawer;
        [SerializeField] public GridLayoutGroup layoutItems;
        [SerializeField] public int itemHeight = 32;
        [SerializeField] public int itemPaddingLR = 16;
        [SerializeField] public int itemPaddingTop = 8;
        [SerializeField] public int itemPaddingBottom = 0;
        private RectTransform rekt;
        private Dictionary<MatType, FavMatBlock> favMats;
        private Dictionary<MatType, int> favMatsWithThresholds;
        private Lifetime lifetime;
        [NonSerialized] public bool IsExpanded;
        private bool isItemWide;
        private Action onExpand;
        private void Awake() {
            buttonExpand.onClick.RemoveAllListeners();
            buttonExpand.onClick.AddListener(OnExpand);

            buttonExpandThin.onClick.RemoveAllListeners();
            buttonExpandThin.onClick.AddListener(OnExpand);

            drawer.SetDistance(DrawerDistance);
        }
        public void Initialize(Dictionary<MatType, FavMatBlock> favMats, Dictionary<MatType, int> favMatsWithThresholds) {
            this.favMats = favMats;
            this.favMatsWithThresholds = favMatsWithThresholds;
            Register(UIWidgetId.Inventory, this);
        }

        public void SetTopItem(int idx, string icon, string text, Func<string> tooltipFun) {
            if (topItems.Length <= idx) {
                D.Err("Trying to set top item out of bounds! {0} | {1} | {2}",
                    idx, icon, text);
                return;
            }
            var w = WidgetWidth();
            var item = topItems[idx];
            var anchY = item.Rect.anchoredPosition.y;
            item.Rect.anchoredPosition = new Vector2(itemPaddingLR + idx * (w / 3), anchY);
            item.Load(icon, text);
            UIUtils.AddTooltipDynamic(item.gameObject, tooltipFun);
        }

        public void SetTopItemText(int idx, string text) {
            topItems[idx].SetText(text);
        }

        public override void Toggle() {
            SetExpanded(!IsExpanded, true);
        }

        public void EnableContainers(bool on) {
            SetExpanded(on, false);
        }

        // The drawer slides out from under the top row, and back in before its rows go
        private void SetExpanded(bool on, bool animate) {
            IsExpanded = on;
            TweenRZ.Add(arrowExpandThin, 0.25f, on ? 180f : 0f);
            if (on) {
                LoadWidgets();
                Rect.SetAsLastSibling();
                SetContainersActive(true);
                if (animate) {
                    // The first frame of the slide needs the opened height
                    LayoutRebuilder.ForceRebuildLayoutImmediate(containerDrawer);
                    drawer.Show();
                } else {
                    drawer.Snap(true);
                }
            } else if (animate) {
                drawer.Hide(Collapse);
            } else {
                drawer.Snap(false);
                Collapse();
            }
        }

        private void Collapse() {
            UnloadWidgets();
            SetContainersActive(false);
        }

        private void SetContainersActive(bool on) {
            containerMats.gameObject.SetActive(on);
            containerButtons.gameObject.SetActive(on);
        }

        // What opening adds below the top row; the divider and the arrow line show either way
        private float DrawerDistance() {
            return containerMats.Rect.rect.height
                + ((RectTransform) containerButtons.transform).rect.height;
        }

        private void LoadWidgets() {
            int idx = 0;

            UnloadWidgets();
            lifetime = Lifetime.Create("InventoryWidget");

            var list = Caches.MatTypeList;
            list.Clear();
            foreach (var fav in favMats) {
                list.Add(fav.Key);
            }
            list.Sort(FavMatTypeComparer.Instance);

            for (int i = 0; i < list.Count; i++) {
                MatType mat = list[i];
                if (favMats.TryGetValue(mat, out var block)) {
                    block.SpawnWidget(lifetime, isItemWide, containerMats, idx++);
                    if (favMatsWithThresholds.TryGetValue(mat, out var threshold)) {
                        block.UpdateVisibility(threshold);
                    }
                }
            }
        }

        private void UnloadWidgets() {
            if (lifetime == null) { return; }
            foreach (var fav in favMats) {
                fav.Value.RestoreVisibilityBeforeDespawn();
            }
            lifetime.Dispose();
            lifetime = null;
        }

        public void ToggleContainers() {
            SetExpanded(!IsExpanded, true);
        }

        private void OnExpand() {
            if (onExpand == null) {
                D.Err("OnExpand not bound: {0}", this);
            } else {
                onExpand.Invoke();
            }
        }

        public void RigExpandButton(Action act) {
            onExpand = act;
        }

        public void RigInventoryButton(Action act) {
            buttonToggleInvPanel.Load(Texts.TitleWithShortcutGlyph(T.OverlayTipResources, Input.ActionType.OpenInventory), act);
        }

        public void SetColumns(int cols) {
            isItemWide = cols == 1;
            float w = WidgetWidth();
            var pad = itemPaddingLR;
            var itemSize = (w - pad * 2) / cols;
            layoutItems.padding = new RectOffset(itemPaddingLR, itemPaddingLR,
                itemPaddingTop, itemPaddingBottom);
            layoutItems.cellSize = new Vector2(itemSize, itemHeight);
        }

        private float WidgetWidth() {
            rekt ??= GetComponent<RectTransform>();
            var w = rekt.sizeDelta.x;
            return w;
        }


        public void RebuildItems() {
            if (IsExpanded) {
                UnloadWidgets();
                LoadWidgets();
            }
        }
    }
}
