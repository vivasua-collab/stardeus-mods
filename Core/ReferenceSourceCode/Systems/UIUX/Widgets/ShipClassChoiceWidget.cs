using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ShipClassChoiceWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WGroup container;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WText header;
        [SerializeField] private WText footerText;
        [SerializeField] private WButtonText cancelButton;
        public bool IsShowing => gameObject.activeSelf;
        private Action<ShipClassDef> onChoose;
        private Lifetime lifetime;
        private GameState lastS;
        private List<ShipClassDef> lastOptions;
        private bool canCancel;

        public void Initialize() {
            Register(UIWidgetId.ShipClassChoice, this);
            container.DestroyChildren();
            Toggle(false);
        }

        public bool Show(GameState s, List<ShipClassDef> options, Action<ShipClassDef> onChoose,
                bool canCancel = true) {
            if (IsShowing) { return true; }
            if (!s.UIQuery.ShowPanelModalFor.Ask(this, true)) {
                // another modal is showing!
                return false;
            }
            s.Clock.ForceStop(ForceStopReason.ShipModification);
            this.onChoose = onChoose;
            this.canCancel = canCancel;
            lastS = s;
            lastOptions = options;
            Rebuild(s, options);
            return true;
        }

        private void Rebuild(GameState s, List<ShipClassDef> options) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("ShipClassChoiceWidget");
            Toggle(true);
            s.UIQuery.ToggleGameUI.Ask(false, false);
            scroll.verticalNormalizedPosition = 1f;

            header.Load(T.ChooseShipClass);
            LoadCancelButton();
            footerText.SetText(null);

            var unlocked = s.Query.GetUnlockedShipClasses.Ask();
            var prefab = ResourceLibrary.FindPrefab<WShipClassOption>(
                UIPrefabId.ShipClassOptionWidget);
            int idx = 0;
            var currentClass = A.Player.Ship.Class;
            WShipClassOption currentWidget = null;
            foreach (var opt in options) {
                var w = UISpawn.Widget(lifetime, prefab, container.Container, idx++);
                bool isUnlocked = unlocked.Contains(opt);
                w.Load(lifetime, s, opt, isUnlocked, OnChoose, Refresh);
                if (opt == currentClass) {
                    currentWidget = w;
                }
            }
            if (currentWidget != null) {
                ScrollTo(currentWidget);
                // hack to actually scroll there on first open
                Coroutines.OnNextFrame(() => {
                    ScrollTo(currentWidget);
                });
            }
        }

        private void ScrollTo(WShipClassOption currentWidget) {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(container.Container);
            var scrollableWidth = container.Container.rect.width - scroll.viewport.rect.width;
            if (scrollableWidth > 0f) {
                var targetLeft = currentWidget.Rect.anchoredPosition.x - 24;
                scroll.horizontalNormalizedPosition = Mathf.Clamp01(targetLeft / scrollableWidth);
            }
        }

        private void Refresh() {
            if (!IsShowing || lastS == null || lastOptions == null) {
                return;
            }
            Rebuild(lastS, lastOptions);
        }

        private void LoadCancelButton() {
            cancelButton.gameObject.SetActive(canCancel);
            if (canCancel) {
                cancelButton.Load(T.Cancel, () => OnChoose(null));
            }
        }

        public bool TryCloseWidget() {
            if (!IsShowing || !canCancel) { return false; }
            OnChoose(null);
            return true;
        }

        private void OnChoose(ShipClassDef data) {
            A.S.Clock.ForceResume(ForceStopReason.ShipModification);
            onChoose.Invoke(data);
            Toggle(false);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            lastS = null;
            lastOptions = null;
            A.S.UIQuery.ToggleGameUI.Ask(true, false);
            A.S.UIQuery.ShowPanelModalFor.Ask(this, false);
        }
    }
}
