using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using Game.Data;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using Game.Systems.UIUX;
using UnityEngine.EventSystems;

namespace Game.UI {
    /// <summary>
    /// This is the popup text box that is used for starting scenario texts, quest texts, etc
    /// </summary>
    public sealed class UIDialogBox : WBase, IPointerClickHandler {
        [SerializeField] private TMP_Text text;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text sideText;
        [SerializeField] private WButtonText leftButton;
        [SerializeField] private WButtonText rightButton;
        [SerializeField] private Image avatar;
        [SerializeField] private Image background;
        [SerializeField] private Image countBG;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Canvas canvas;
        // If it's the only button, it will be OK, otherwise cancel / ignore
        public Button RightButton => rightButton.Button;
        public Button OKButton => leftButton.Button;

        // The text as given: the field's own text carries the tags that flow it around the avatar
        public string CurrentText => wrap.Text;

        private readonly List<DialogData> queue = new();
        public bool HasQueue => queue.Count != 0;
        // True while any dialog is on screen, about to show, queued, or scheduled with a
        // delay. UIShowing.Dialog alone misses the gaps between queued/delayed dialogs.
        public bool IsBusy => UIShowing.Dialog || currentData != null || queue.Count != 0
            || delayedShowsPending > 0;
        private int delayedShowsPending;

        private Material backgroundMat;
        private DialogData currentData;
        private int currentTextTplHash;
        private DialogTextReveal reveal;
        private UITextWrapAroundImage wrap;
        // Buttons stay locked and dimmed until the text is fully revealed
        private CanvasGroup buttonsGroup;
        private float buttonsTargetAlpha = 1f;
        private const float ButtonsLockedAlpha = 0.35f;
        private const float ButtonsFadeSpeed = 1f / 0.15f;

        public void Reset() {
            queue.Clear();
            currentData = null;
            Hide();
        }


        private void Update() {
            if (!GameState.IsCurrent(S)) {
                DestroyImmediate(gameObject);
                return;
            }
            reveal.Tick(GameTimer.unscaledTime);
            if (buttonsGroup.alpha != buttonsTargetAlpha) {
                buttonsGroup.alpha = Mathf.MoveTowards(buttonsGroup.alpha, buttonsTargetAlpha,
                    GameTimer.unscaledDeltaTime * ButtonsFadeSpeed);
            }
            if (UIShowing.Dialog) {
                if (currentData?.ShouldBeRemoved?.Invoke() == true) {
                    Hide();
                    return;
                }
            }
            if (queue.Count == 0) { return; }
            bool wasRemoved = false;
            for (int i = queue.Count - 1; i >= 0; i--) {
                DialogData item = queue[i];
                if (item.ShouldBeRemoved?.Invoke() == true) {
                    queue.RemoveAt(i);
                    wasRemoved = true;
                }
            }
            if (wasRemoved) {
                UpdateCount();
                if (queue.Count == 0) {
                    return;
                }
            }
            if (UIShowing.Dialog) {
                if (currentData == null || string.IsNullOrWhiteSpace(currentData.Text)) {
                    // D.Warn("Hiding empty dialog! {0}", currentData);
                    if (queue.Count == 0) {
                        Hide();
                        return;
                    }
                }
                if (currentData.RightButtonText == null
                        && currentData.LeftButtonText == null) {
                    ShowRightButton(T.Next);
                }
                return;
            }
            if (currentData != null) {
                // A dialog is already selected and waiting for its deferred RebuildAndShow to
                // set UIShowing.Dialog. Don't pop the next queued dialog and overwrite it.
                return;
            }
            currentData = queue[0];
            queue.RemoveAt(0);
            UpdateCount();
            ShowCurrentData();
        }

        private GameState S;

        private void Awake() {
            reveal = new DialogTextReveal(text, OnRevealComplete);
            wrap = text.GetComponent<UITextWrapAroundImage>();
            var buttonsParent = rightButton.transform.parent.gameObject;
            if (!buttonsParent.TryGetComponent(out buttonsGroup)) {
                buttonsGroup = buttonsParent.AddComponent<CanvasGroup>();
            }
        }

        private void Start() {
            // Copy to prevent saving shit
            backgroundMat = new Material(background.material);
            backgroundMat.hideFlags = HideFlags.DontSave;
            background.material = backgroundMat;
            rightButton.Load(T.Ok, OnRightClick);
            leftButton.Load(T.Next, OnLeftClick);
            Hide();
            S = The.R.State;
        }

        private void Enqueue(DialogData data) {
            D.Warn("Enqueuing Dialog: {0}", data);
            if (data.Text == null) {
                D.Err("Trying to enqueue null text in data: {0}", data);
                return;
            }
            if (data.ShouldBeRemoved?.Invoke() == true) {
                return;
            }
            if (currentData != null && currentData.IsSkippable) {
                // we can replace the dialog
                Hide();
            }
            if (queue.Count > 99) {
                D.Err("Enqueued too many dialogues! {0}", queue.Count);
            }
            if (data.TextTemplateHash == 0) {
                data.TextTemplateHash = data.Text.GetHashCode();
            }
            if (data.TextTemplateHash == currentTextTplHash) {
                // Already showing, will skip, most likely tutorial
                wrap.SetText(data.Text);
                reveal.Complete(false, GameTimer.unscaledTime);
                UpdateCount();
                Flash();
                WarnAboutDuplicateDialog(data);
                return;
            }
            if (queue.Count != 0) {
                var last = queue[0];
                if (last.TextTemplateHash == data.TextTemplateHash) {
                    // Already showing, will skip, most likely tutorial
                    WarnAboutDuplicateDialog(data);
                    return;
                }
            }
            queue.Add(data);
            UpdateCount();
        }

        private void UpdateCount() {
            if (queue.Count == 0) {
                countBG.gameObject.SetActive(false);
                return;
            }

            countText.SetText(Units.Num(queue.Count));
            countBG.SetActiveFast(true);
        }

        [Conditional("UNITY_EDITOR")]
        private void WarnAboutDuplicateDialog(DialogData data) {
            D.ErrSoft("Trying to enqueue duplicate dialog data! {0}", data);
        }

        private void ShowCurrentData() {
            if (currentData == null) {
                D.Err("Not showing null data in dialog");
                Hide();
                return;
            }
            if (currentData.Being != null) {
                currentData.Being.Avatar.Load(avatar);
            } else {
                avatar.sprite = Images.Sprite(currentData.Icon);
            }
            wrap.SetText(Texts.Beautify(Texts.RemoveDoubleLineBreaks(currentData.Text)));
            reveal.Prepare();
            SetButtonsLocked(true);
            currentTextTplHash = currentData.TextTemplateHash;
            if (currentData.SideText != null) {
                sideText.SetText(currentData.SideText);
                sideText.SetActiveFast(true);
            } else {
                sideText.SetActiveFast(false);
            }
            if (currentData.RightButtonText != null) {
                rightButton.SetText(currentData.RightButtonText);
                rightButton.SetActiveFast(true);
            } else {
                rightButton.SetActiveFast(false);
            }
            if (currentData.LeftButtonText != null) {
                leftButton.SetText(currentData.LeftButtonText);
                leftButton.SetActiveFast(true);
            } else {
                leftButton.SetActiveFast(false);
            }
            if (currentData.OnShow != null) {
                currentData.OnShow();
            }
            Show();
        }
        public DialogData ShowSimple(string avatar, string text) {
            return ShowSimple(avatar, text, T.Ok);
        }

        public DialogData ShowSimple(string avatar, string text,
                string okButtonText, string sideText = null,
                Action onShow = null, int textTplHash = 0,
                bool isSkippable = false) {
            var dd = new DialogData() {
                Icon = avatar,
                Text = text,
                SideText = sideText,
                IsSkippable = isSkippable,
                CanCloseWithEsc = true,
                TextTemplateHash = textTplHash,
                RightButtonAct = Hide,
                RightButtonText = okButtonText,
                OnShow = onShow,
            };
            Enqueue(dd);
            return dd;
        }
        public void HideAndShowWithActionDelayed(float delay, string avatar,
                string text, Action ok, string okButtonText,
                string sideText = null, int textTplHash = 0) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning || G.ClockSpeed >= ClockSpeed.Unlimited) {
            #else
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
            #endif
                Hide();
                ShowWithAction(avatar, text, ok, okButtonText,
                    sideText: sideText, textTplHash: textTplHash);
            } else {
                Coroutines.Start(
                    HideAndShowWithActionAfterDelay(delay, avatar, text, sideText, textTplHash,
                        ok, okButtonText), this, "DelayedDialog", false, false);
            }
        }

        private IEnumerator HideAndShowWithActionAfterDelay(float delay,
                string avatar, string text, string sideText, int textTplHash,
                Action ok, string okButtonText) {
            Hide();
            delayedShowsPending++;
            var showTime = Time.realtimeSinceStartupAsDouble + delay;
            var rt = The.R;
            while (Time.realtimeSinceStartupAsDouble < showTime) {
                yield return null;
                if (!rt.IsCurrent) {
                    delayedShowsPending--;
                    yield break;
                }
            }
            delayedShowsPending--;
            ShowWithAction(avatar, text, ok, okButtonText,
                sideText: sideText, textTplHash: textTplHash);
        }

        public DialogData ShowWithoutAction(string avatar, string text, string sideText = null,
                Action onShow = null, int textTplHash = 0, bool isSkippable = false) {
            var dd = new DialogData() {
                Icon = avatar,
                Text = text,
                SideText = sideText,
                CanCloseWithEsc = true,
                IsSkippable = isSkippable,
                TextTemplateHash = textTplHash,
                OnShow = onShow,
            };
            Enqueue(dd);
            return dd;
        }
        public DialogData ShowWithAction(string avatar, string text, Action ok, string sideText = null) {
            return ShowWithAction(avatar, text, ok, T.Ok, sideText);
        }

        public DialogData ShowWithAction(string avatar, string text, Action ok,
                string okButtonText, string sideText = null,  Action onShow = null, int textTplHash = 0,
                bool isSkippable = false) {
            var dd = new DialogData() {
                Icon = avatar,
                Text = text,
                SideText = sideText,
                IsSkippable = isSkippable,
                CanCloseWithEsc = true,
                TextTemplateHash = textTplHash,
                RightButtonText = okButtonText,
                RightButtonAct = ok,
                OnShow = onShow,
            };
            Enqueue(dd);
            return dd;
        }
        ///<summary>RightKey is usually OK, left is some action or cancel</summary>
        public DialogData ShowChoice(string avatar, string text,
                Action left, Action right, string leftButtonText, string rightButtonText,
                bool canCloseWithEsc, Action onShow = null, string sideText = null,
                int textTplHash = 0, bool isSkippable = false) {
            var dd = new DialogData() {
                Icon = avatar,
                Text = text,
                SideText = sideText,
                IsSkippable = isSkippable,
                TextTemplateHash = textTplHash,
                CanCloseWithEsc = canCloseWithEsc,
                RightButtonAct = right,
                LeftButtonAct = left,
                RightButtonText = rightButtonText,
                LeftButtonText = leftButtonText,
                OnShow = onShow,
            };
            Enqueue(dd);
            return dd;
        }

        private void Show() {
            Coroutines.OnNextFrame(RebuildAndShow);
        }

        private void RebuildAndShow() {
            if (currentData == null || MiscUtils.IsDestroyed(this)) {
                // hide was called before we could show
                return;
            }
            UIShowing.Set(UIShowingFlags.Dialog);
            // The dialog shares one parent with every panel and popup, where sibling
            // index is the z-order. Panels raise themselves when opened, so without
            // this the dialog stays buried under whatever the player opened earlier.
            Rect.SetAsLastSibling();
            Flash();
            RebuildLayout();
            if (IsInstantReveal()) {
                reveal.Complete(false, GameTimer.unscaledTime);
            } else {
                reveal.Begin(GameTimer.unscaledTime);
            }
            UIUtils.ToggleCG(canvasGroup, true);
            if (currentData.OverrideSortOrder != 0) {
                canvas.overrideSorting = true;
                canvas.sortingOrder = currentData.OverrideSortOrder;
                // When we're doing this, we're 100% showing screen block.
                if (UIShowing.ScreenBlock) {
                    // the dialog is in the blocked canvas
                    The.ScreenBlock.ShowBlockedCanvas();
                }
            } else {
                canvas.overrideSorting = false;
                canvas.sortingOrder = 0;
            }
        }

        public void Flash() {
            if (!G.HasEnteredGame) { return; }
            // UISounds.PlayShowDialog();
            UISounds.Play("UI/DialogBlip", 0.5f);
            backgroundMat.SetFloat(ShaderVars._FlashStartTime, GameTimer.unscaledTime);
        }

        public void ClearQueue() {
            queue.Clear();
            UpdateCount();
        }

        private static bool IsInstantReveal() {
            #if !PRODUCTION_BUILD
            return The.TestsAreRunning || G.ClockSpeed >= ClockSpeed.Unlimited;
            #else
            return G.ClockSpeed >= ClockSpeed.Unlimited;
            #endif
        }

        private void SetButtonsLocked(bool locked) {
            buttonsGroup.interactable = !locked;
            buttonsGroup.blocksRaycasts = !locked;
            buttonsTargetAlpha = locked ? ButtonsLockedAlpha : 1f;
            if (locked) {
                buttonsGroup.alpha = ButtonsLockedAlpha;
            }
        }

        private void OnRevealComplete() {
            SetButtonsLocked(false);
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (!UIShowing.Dialog || !reveal.IsRevealing) { return; }
            reveal.Complete(true, GameTimer.unscaledTime);
        }

        public void Hide() {
            reveal.Complete(false, GameTimer.unscaledTime);
            SetButtonsLocked(false);
            buttonsGroup.alpha = 1f;
            currentData = null;
            wrap.SetText(null);
            currentTextTplHash = 0;
            UIShowing.Unset(UIShowingFlags.Dialog);
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0;
        }

        private void OnRightClick() {
            currentData?.RightButtonAct?.Invoke();
            Hide();
        }
        private void OnLeftClick() {
            currentData?.LeftButtonAct?.Invoke();
            Hide();
        }

        private void OnEnable() {
            The.Dialog = this;
        }

        private void OnDisable() {
            The.Dialog = null;
        }

        public void ShowRightButton(string text) {
            // UISounds.PlayHoverSound();
            rightButton.SetText(text);
            rightButton.SetActiveFast(true);
        }

        public bool TryCloseWithEsc() {
            if (!UIShowing.Dialog) { return false; }
            if (reveal.IsRevealing) {
                // Esc skips the typing first, pressing it again closes
                reveal.Complete(true, GameTimer.unscaledTime);
                return true;
            }
            if (currentData?.CanCloseWithEsc != true) {
                return false;
            }
            rightButton.Click();
            return true;
        }
    }
}
