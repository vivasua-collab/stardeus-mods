using System;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    /// textMain = header
    /// text2 = header subtext
    /// text3 = text
    public partial class WPopup : WText3, ICustomPoolable, ICloseableWidget {

        [ExecuteOnReload]
        private static void Cleanup() {
            Spawned.Clear();
        }

        [SerializeField] private Image icon;
        [SerializeField] private WButtonText button1;
        [SerializeField] private WButtonText button2;
        [SerializeField] private GameObject textContainer;
        [SerializeField] private GameObject buttonPadding;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UICountdownWidget countdown;
        [SerializeField] private GameObject countdownContainer;
        [SerializeField] private GameObject inputContainer;
        [SerializeField] private WInput input;
        [SerializeField] private TMP_Text inputHeader;
        public WButtonText Button1 => button1;
        public WButtonText Button2 => button2;
        public WPopupData Data => data;

        public static readonly List<WPopup> Spawned = new();
        private WPopupData data;
        private Tween closeTween;
        private long lastTick;
        public static WPopupData Create(bool isSystem, string icon, string title, string subtitle, string text = null) {
            var data = new WPopupData(isSystem, icon, title, subtitle, text);
            return data;
        }
        /// <summary>
        /// Don't spawn this directly, spawn using WPopupData.Spawn! That will
        /// handle non-main thread popups!
        /// </summary>
        /// <param name="data"></param>
        /// <param name="container"></param>
        /// <returns></returns>
        public static void Spawn(WPopupData data) {
            var prefab = ResourceLibrary.FindPrefab<WPopup>(UIPrefabId.WPopup);
            var w = PrefabPool.Spawn(prefab, data.Container
                    ?? UIPoolRoot.PopupContainerWithFallback, preInstantiate: true);
            w.Load(data);
            // Above the feedback form (6), so its warnings stay visible over a choice
            UIUtils.LiftAbovePanelModal(w.GetComponent<Canvas>(), 7);
            data.Widget = w;
            data.OnSpawn?.Invoke(w);
        }
        private void Load(WPopupData data) {
            this.data = data;
            #if UNITY_EDITOR
            name = $"WPopup_{Texts.StripTags(data.Title)}";
            #endif

            // A.S.UIQuery.RegisterCodexTooltipFor.Ask(def, this, true);
            if (data.Icon != null && Images.TrySprite(data.Icon, out var sprite)) {
                icon.sprite = sprite;
            } else {
                icon.sprite = LinkedResources.Instance.WarningSprite;
            }

            SetText(data.Title);
            SetText2(Texts.Beautify(Texts.RemoveDoubleLineBreaks(data.SubTitle)),
                disableOnBlank: false);
            if (data.Text == null) {
                textContainer.SetActiveFast(false);
            } else {
                SetText3(Texts.Beautify(data.Text));
                textContainer.SetActiveFast(true);
            }

            if (data.TimeoutTicks > 0) {
                countdown.Setup(G.Ticks, data.TimeoutTicks, OnTimeout, data.OnTickAct);
                countdownContainer.SetActiveFast(true);
            } else {
                countdownContainer.SetActiveFast(false);
                countdown.Hide();
            }

            button1.Load(data.Button1Text, Click1);

            if (data.Button2Text != null) {
                button2.Load(data.Button2Text, Click2);
                button2.SetActiveFast(true);
                buttonPadding.SetActiveFast(false);
            } else {
                button2.SetActiveFast(false);
                buttonPadding.SetActiveFast(true);
            }

            if (data.InputOnSubmit != null) {
                input.Load(data.InputPlaceholder, null);
                input.SetOnSubmit(OnInputSubmit);
                input.SetValue(data.InputValue, false);
                inputHeader.SetText(data.InputLabel);
                inputContainer.SetActiveFast(true);
            } else {
                inputContainer.SetActiveFast(false);
            }

            UIUtils.RebuildLayoutRecursive(Rect);
            UIUtils.AfterShowPopup(Rect, data.CenterOfScreen, data.DistToEdge);
            Coroutines.OnFrame(3, AfterRebuildLayouts);
        }

        private void OnInputSubmit(string text) {
            var err = data.InputOnSubmit.Invoke(text);
            if (err == null) {
                Close();
            } else {
                inputHeader.SetText(Texts.Red(err));
                UISounds.PlayActionDenied();
            }
        }

        private void Click1() {
            if (data.ValidateButton1 != null) {
                if (!data.ValidateButton1()) {
                    UISounds.PlayActionDenied();
                    return;
                }
            }
            var act = data.Button1Act;
            if (act != null) {
                act.Invoke();
                if (data == null) {
                    D.Err("Popup action should not dispose of the popup! {0}", act.Target);
                    return;
                }
            }
            if (data.KeepOpenOnButton1) {
                return;
            }
            if (data.InputOnSubmit == null) {
                Close();
            } else {
                OnInputSubmit(input.Text);
            }
        }
        private void Click2() {
            if (data.ValidateButton2 != null) {
                if (!data.ValidateButton2()) {
                    UISounds.PlayActionDenied();
                    return;
                }
            }
            data.Button2Act?.Invoke();
            Close();
        }

        private void OnTimeout() {
            data.OnTimeoutAct?.Invoke();
            data.OnTimeoutAct = null;
            Close();
        }

        public void UpdateTimeout(long startTick, long endTick) {
            countdown.StartTick = startTick;
            countdown.EndTick = endTick;
        }

        public void SetCountdownSilent(bool silent) {
            countdown.IsSilent = silent;
        }

        private bool lastInteractable;

        private void Update() {
            // Disable buttons mid save - important to avoid data corruption
            var interactableNow = Ready.Save;
            if (lastInteractable != interactableNow) {
                lastInteractable = interactableNow;
                button1.SetInteractable(interactableNow);
                button2.SetInteractable(interactableNow);
            }

            if (G.Ticks == lastTick) { return; }
            lastTick = G.Ticks;
            if (countdown.Tick(lastTick)) {
                Close();
                return;
            }
            if (data.TimeoutTicks > 0 && UIShowing.MainMenu) {
                A.S.Clock.SetSpeed(ClockSpeed.Stopped);
                return;
            }
        }

        private void OnDestroy() {
            Spawned.Remove(this);
        }

        private void AfterRebuildLayouts() {
            if (MiscUtils.IsDestroyed(this)) { return; }
            if (data == null) { return;}
            UIUtils.RebuildLayoutRecursive(Rect);
            if (data.IsSystem || G.HasEnteredGame) {
                UISounds.PlayPopup();
            }
            ShowCG();
            if (input != null && input.gameObject.activeInHierarchy) {
                input.Focus();
            }
            UIUtils.MoveIntoScreenBounds(Rect);
        }

        public void Close() {
            closeTween = TweenCGA.Add(gameObject, Tunables.UITooltipFadeInSeconds, 0f)
                .Then(CloseInstant);
        }

        public void CloseInstant() {
            countdownContainer.SetActiveFast(false);
            countdown.Hide();
            PrefabPool.Despawn(gameObject);
        }

        public override string ToString() {
            return $"WPopup[{lastText}]";
        }

        public void PoolDeactivate() {
            Spawned.Remove(this);
            data?.OnDespawn();
            // Pooled instance gets reused - an old WPopupData handle must not close the next popup
            if (data != null && data.Widget == this) {
                data.Widget = null;
            }
            data = null;
            lastTick = 0;
            HideCG();
            gameObject.SetActiveFast(false);
            var rt = Rect;
            // reset pooled anchors
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1f);
        }

        public void TrySubmitInput() {
            OnInputSubmit(input.Text);
        }

        public void PoolActivate() {
            Spawned.Add(this);
            if (closeTween != null) {
                Destroy(closeTween);
                closeTween = null;
            }
            HideCG();
            gameObject.SetActiveFast(true);
        }

        private void ShowCG() {
            UIUtils.ToggleCG(canvasGroup, true);
        }
        private void HideCG() {
            UIUtils.ToggleCG(canvasGroup, false);
        }

        public static void SystemWarning(string icon, string title, string subtitle, string txt) {
            UISounds.PlayActionDenied();
            Create(true, icon, title, subtitle, txt)
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }
        public static void SystemWarning(string icon, string title, string msg) {
            SystemWarning(icon, title, msg, null);
        }
        public static void SystemWarning(string title, string msg) {
            SystemWarning(IconId.CWarning, title, msg, null);
        }

        public static void Warning(string title, string subtitle) {
            Warning(title, subtitle, null);
        }
        public static void Warning(string title, string subtitle, string text) {
            UISounds.PlayActionDenied();
            Create(false, IconId.CWarning, title, subtitle, text)
                .Spawn();
        }

        public bool TryCloseWidget() {
            if (data.Button1Act != null || data.Button2Act != null) {
                return false;
            }
            CloseInstant();
            return true;
        }
    }
}
