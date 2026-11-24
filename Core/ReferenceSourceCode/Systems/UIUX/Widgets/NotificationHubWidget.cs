using System;
using Game.Constants;
using Game.Rendering;
using Game.Systems.Notify;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.UI;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class NotificationHubWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text text;
        [SerializeField] private TMP_Text noAlertsText;
        [SerializeField] private Image buttonIcon;
        [SerializeField] private WImage alertIcon;
        [SerializeField] private GameObject buttonIconGO;
        [SerializeField] private Button button;
        [SerializeField] private WGroup eventsContainer;
        [SerializeField] private GameObject containerGO;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private UIDrawerSlide drawer;
        [SerializeField] private UIButtonWithHoverCursor buttonHover;
        [SerializeField] private UIHoverCallback notificationListHover;
        [SerializeField] private float widthMinimized;
        [SerializeField] private float widthNormal;
        [SerializeField] private Image alertOverlay;
        [SerializeField] private WButton detachedPanelHeader;
        [SerializeField] private WButtonIcon detachButton;
        private Vector2 attachedPanelPosition;
        private bool isDetached;

        private Material alertOverlayMat;
        private Lifetime lifetime;
        private bool isShowing;
        public bool IsShowing => isShowing;
        private int lastCount = -1;
        private string lastMsg;
        private string lastIcon = ".";
        private Tween punchCountTween;
        private Tween punchIconTween;
        private bool isSmall;
        private NotificationHubNeglect neglect = new();

        public void SetCount(int count, string msg) {
            if (lastCount == count) { return; }
            lastCount = count;
            lastMsg = msg;
            if (count == 0) {
                countText.SetActiveFast(false);
                text.SetActiveFast(false);
                alertIcon.Load(IconId.CCheck);
                noAlertsText.SetActiveFast(!isSmall);
                noAlertsText.SetText(BalanceTwoLines(noAlertsText, msg));
            } else {
                noAlertsText.SetActiveFast(false);
                if (count < 100) {
                    countText.SetText(Units.Num(count));
                } else {
                    countText.SetText("∞");
                }
                countText.SetActiveFast(true);
                text.SetActiveFast(true);
                text.SetText(BalanceTwoLines(text, msg));
            }
        }

        // TMP wraps greedily and can strand a lone last word on line two.
        // Break where both lines come out closest in width instead; if no
        // single break fits (no spaces, or 3+ lines needed), TMP wraps as usual.
        private static string BalanceTwoLines(TMP_Text label, string msg) {
            // Measuring needs an awakened label; the only hidden case is the
            // no-alerts text on small screens, where nothing is shown anyway
            if (!label.isActiveAndEnabled) { return msg; }
            // Preferred width includes the label margins, like rect width
            var maxWidth = label.rectTransform.rect.width;
            if (label.GetPreferredValues(msg).x <= maxWidth) { return msg; }
            var bestIdx = -1;
            var bestWidth = float.MaxValue;
            for (var i = msg.IndexOf(' '); i > 0; i = msg.IndexOf(' ', i + 1)) {
                var first = label.GetPreferredValues(msg.Substring(0, i)).x;
                var second = label.GetPreferredValues(msg.Substring(i + 1)).x;
                var widest = Mathf.Max(first, second);
                if (widest <= maxWidth && widest < bestWidth) {
                    bestWidth = widest;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) { return msg; }
            return $"{msg.Substring(0, bestIdx)}\n{msg.Substring(bestIdx + 1)}";
        }

        private Func<WGroup, Lifetime> onShow;
        private Func<string> summaryTip;

        public void Initialize(Func<WGroup, Lifetime> onShow, Func<string> summaryTip) {
            this.onShow = onShow;
            this.summaryTip = summaryTip;
            neglect = new NotificationHubNeglect();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Toggle);
            drawer.SetDistance(ListHeight);
            drawer.Snap(false);
            DisableCanvas();
            isDetached = false;
            buttonHover.OnHover = OnHoverButton;
            detachButton.SetActiveFast(true);
            detachButton.Load(GraphicsId.UIMiscArrowDiagonal, DetachPanel);
            attachedPanelPosition = containerGO.transform.localPosition;
            detachedPanelHeader.SetActiveFast(false);
            detachedPanelHeader.Load(ReattachDetachedList);
            notificationListHover.SetCallback(OnHoverList, null);
            Register(UIWidgetId.NotificationHub, this);
            UpdateSize();
            alertOverlayMat = alertOverlay.material = new Material(alertOverlay.material);
            SetNeedAttention(false);
        }

        private void DetachPanel() {
            // A detached list floats on its own, there is no edge to slide from
            drawer.Snap(true);
            detachedPanelHeader.SetActiveFast(true);
            detachButton.SetActiveFast(false);
            containerGO.transform.localPosition = attachedPanelPosition + new Vector2(200, -8);
            isDetached = true;
        }

        private void ReattachDetachedList() {
            detachButton.SetActiveFast(true);
            detachedPanelHeader.SetActiveFast(false);
            containerGO.transform.localPosition = attachedPanelPosition;
            isDetached = false;
        }

        public void UpdateSize() {
            var rt = Rect;
            isSmall = RenderUtils.ScreenWidth < 1600;
            var targetWidth = isSmall ? widthMinimized : widthNormal;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            if (lastMsg != null) {
                var label = lastCount == 0 ? noAlertsText : text;
                label.SetText(BalanceTwoLines(label, lastMsg));
            }
        }

        private void OnHoverList(bool yes) {
            if (!Tunables.NotificationHub_ForceStopClock) { return; }
            // don't do this with no-pause modifier
            if (A.S.Clock.MinSpeed > ClockSpeed.Stopped) { return; }
            if (yes) {
                if (G.IsClickDrag) { return; }
                A.S.Clock.ForceStop(ForceStopReason.HoveringNotifications);
            } else if (A.S.Clock.IsForceStopped) {
                A.S.Clock.ForceResume(ForceStopReason.HoveringNotifications);
            }
        }

        private void OnHoverButton(bool hover) {
            if (hover) {
                The.SysSig.ShowDynamicTooltip.Send(summaryTip, true);
            } else {
                The.SysSig.HideTooltip.Send();
            }
        }

        public override void Toggle() {
            isShowing = !isShowing;
            TweenRZ.Add(buttonIconGO, 0.25f, isShowing ? 180f : 0f);
            if (isShowing) {
                ShowContainer();
                The.SysSig.HideTooltip.Send();
            } else {
                HideContainer();
            }
        }

        private void ShowContainer() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = onShow(eventsContainer);
            containerGO.SetActive(true);
            cg.blocksRaycasts = true;
            UIUtils.AddTip(lifetime, detachButton, T.VerbDetach);
            TweenCGA.Add(containerGO, 0.1f, 1f).Then(EnableCanvas);
            if (isDetached) {
                drawer.Snap(true);
            } else {
                drawer.Show();
            }
            Rect.SetAsLastSibling();
            SetNeedAttention(false);
            neglect.MarkSeen();
        }

        // The list keeps its content until it is out of sight, so it does not leave empty. It
        // takes no clicks or hover meanwhile: hovering it would stop the clock again.
        private void HideContainer() {
            A.S.Clock.ForceResume(ForceStopReason.HoveringNotifications);
            cg.blocksRaycasts = false;
            if (isDetached) {
                TweenCGA.Add(containerGO, 0.1f, 0f).Then(DisableCanvas);
            } else {
                drawer.Hide(DisableCanvas);
            }
        }

        private float ListHeight() {
            return ((RectTransform) containerGO.transform).rect.height;
        }

        private void EnableCanvas() {
            cg.alpha = 1f;
        }
        private void DisableCanvas() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            containerGO.SetActive(false);
            cg.alpha = 0f;
            A.S.Clock.ForceResume(ForceStopReason.HoveringNotifications);
        }

        public bool TryCloseWidget() {
            if (!isShowing || isDetached) { return false; }
            Toggle();
            return true;
        }

        public void Rebuild() {
            if (!isShowing) { return; }
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = onShow(eventsContainer);
            RebuildLayout();
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }

        public void PunchCount() {
            if (punchCountTween?.IsComplete != false) {
                punchCountTween = MiscUtils.PunchScale(countText.gameObject, 1.25f, 0.15f);
            }
        }

        public void SetAlertIcon(string icon, bool punch) {
            if (lastIcon == icon) { return; }
            lastIcon = icon;
            alertIcon.Load(icon);
            if (punch && punchIconTween?.IsComplete != false) {
                punchIconTween = MiscUtils.PunchScale(alertIcon.gameObject, 1.25f, 0.2f);
            }
        }

        public void SetNeedAttention(bool yes) {
            if (IsShowing) { yes = false; }
            alertOverlayMat.SetFloat(ShaderVars._Amount, yes ? 1f : 0f);
        }

        // Military alert colors from the theme, so each theme can fit them to its palette.
        // More urgent pulses also spread further: the size tells levels apart at a glance and
        // for players who cannot tell the colors apart.
        // `pulseOnly` is for alerts whose own UI is already in front of the player.
        public void RingAlert(NotificationType type, bool pulseOnly = false) {
            if (IsShowing) { return; }
            string colorId;
            float spread;
            switch (type) {
                // Action Required pulses only when pulseOnly, otherwise it gets the focus ring
                case NotificationType.Immediate: colorId = "AlertGreen"; spread = 3f; break;
                case NotificationType.Critical: colorId = "AlertRed"; spread = 4f; break;
                case NotificationType.Alert: colorId = "AlertAmber"; spread = 3f; break;
                case NotificationType.Warning: colorId = "AlertYellow"; spread = 2f; break;
                case NotificationType.Info: colorId = "AlertWhite"; spread = 1f; break;
                default: return;
            }
            var color = The.UITheme.GetColor(colorId, "NotificationHubWidget.RingAlert");
            int priority = NotificationType.Info - type;
            // Info only pulses. It is never worth a ring from across the screen, and so it
            // does not count toward an ignored hub either. The same goes for an alert whose
            // own UI the player is already looking at (an open trade panel, a hail popup):
            // a ring sweeping across that panel is noise. Under a focus ring they stay quiet
            // rather than spread out of the hub while that ring shrinks onto it.
            if (type == NotificationType.Info || pulseOnly) {
                if (!UIFocusRing.IsActiveOn(Rect)) {
                    UIPulseRing.Request(Rect, color, priority, spread);
                }
                return;
            }
            // Action Required blocks progress until the player answers it, and it is rare,
            // so it always gets the ring that points at the hub from across the screen. It
            // spends the neglect cooldown like an escalated alert, so the next escalated
            // alert does not sweep the screen again right after it.
            if (type == NotificationType.Immediate) {
                neglect.MarkFocused(GameTimer.realtimeSinceStartupAsDouble);
                UIFocusRing.Request(Rect, color, priority);
                return;
            }
            // A player who stopped opening the hub gets the ring that points at it from
            // across the screen. While that ring is on its way, a pulse spreading out of the
            // hub would fight it, so later alerts only raise its color.
            if (neglect.ShouldFocus(GameTimer.realtimeSinceStartupAsDouble)
                    || UIFocusRing.IsActiveOn(Rect)) {
                UIFocusRing.Request(Rect, color, priority);
                return;
            }
            UIPulseRing.Request(Rect, color, priority, spread);
        }

        public void DebugAgeNeglect(double seconds) {
            neglect.DebugAge(seconds);
        }
    }
}
