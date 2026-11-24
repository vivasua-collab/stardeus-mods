using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Quests;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class QuestWidget : BaseWidget {
        [SerializeField] private Button button;
        [SerializeField] private Image image;
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private WGroup content;
        [SerializeField] private Image alertIcon;
        [SerializeField] private NewEventIcon alert;
        [SerializeField] private GameObject newQuestBadge;
        [SerializeField] private TMP_Text newQuestText;
        [SerializeField] private Button newQuestsButton;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private WButton btnQuestNext;
        private Action onClick;
        private Lifetime contentLife;
        private Lifetime rowsLife;
        private readonly List<QuestStepLine> stepLines = new();
        private Lifetime nextTipLife;
        private string lastTitle = ".";
        private string lastIcon = ".";
        private Func<string> tooltipFunc;
        private bool isShowing;
        private Action onClickNewQuests;
        private Tween tween;
        private float neutralY;
        private bool isForceHidden;
        public bool IsShowing => isShowing;
        public bool IsVisibleToPlayer => isShowing && !isForceHidden;

        public void SetForceHidden(bool forceHidden) {
            isForceHidden = forceHidden;
            UIUtils.ToggleCG(cg, !forceHidden);
        }

        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            newQuestsButton.onClick.RemoveAllListeners();
            newQuestsButton.onClick.AddListener(OnClickNewQuests);
            newQuestBadge.SetActiveFast(false);
            btnQuestNext.SetActiveFast(false);
            MiscUtils.DestroyChildrenExcept(content.Container, button.transform);
            OnResChanged();
            ShowAlert(false);
        }

        private void OnClickNewQuests() {
            onClickNewQuests.Invoke();
        }

        public void SetNewQuests(int count) {
            bool hasNew = count > 0f;
            if (hasNew) {
                newQuestText.SetText($"+{count} {T.AdjectiveNew}");
                ShowAlert(false);
            }
            image.gameObject.SetActiveFast(!hasNew);
            newQuestBadge.gameObject.SetActiveFast(hasNew);
        }

        public void SetNewQuestsAction(Action act) {
            onClickNewQuests = act;
        }

        public void SetNextQuestAction(Action act) {
            btnQuestNext.SetOnClick(act);
            nextTipLife?.Dispose();
            nextTipLife = Lifetime.Create("QuestWidgetNextTip");
            UIUtils.AddTipFunc(nextTipLife, btnQuestNext, () => T.Next);
        }

        public void ShowNextQuest(bool show) {
            btnQuestNext.SetActiveFast(show);
        }

        private void OnClick() {
            onClick.Invoke();
        }

        public void ShowAlert(bool yes) {
            image.gameObject.SetActiveFast(!yes);
            alert.gameObject.SetActiveFast(yes);
            D.Log("Showing alert: {0}", yes);
            if (yes) {
                if (!alert.IsPlaying) {
                    alert.Play(0);
                }
            } else {
                alert.Stop();
            }
        }

        public void Initialize() {
            Register(UIWidgetId.Quest, this);
        }

        public void SetClickFunc(Action act) {
            onClick = act;
        }
        public void SetTooltipFunc(Func<string> func) {
            tooltipFunc = func;
        }

        public void Show() {
            // D.Warn("Showing quest widget (showing already: {0})", isShowing);
            if (isShowing) {
                Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, neutralY);
                return;
            }
            if (tween != null) {
                Destroy(tween);
                tween = null;
                SnapTo(false);
            }
            isShowing = true;
            SetActive(true);
            tween = TweenRectY.Add(gameObject, 0.25f, neutralY).Then(() => {
                tween = null;
                SnapTo(true);
            });
        }

        public void Hide() {
            // D.Warn("Hiding quest widget (showing already: {0})", isShowing);
            if (!isShowing) { return; }
            isShowing = false;
            if (tween != null) {
                Destroy(tween);
                tween = null;
            }
            tween = TweenRectY.Add(gameObject, 0.25f, -Rect.rect.height - neutralY).Then(Deactivate);
        }

        public void SnapTo(bool visible) {
            RebuildLayout();
            if (visible) {
                Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, neutralY);
            } else {
                Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, -Rect.rect.height - neutralY);
            }
        }

        private void Deactivate() {
            D.Warn("Deactivating quest widget (showing already: {0})", isShowing);
            isShowing = false;
            SetActive(false);
            if (tween != null) {
                tween.Skip();
                Destroy(tween);
                tween = null;
            }
            Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, -Rect.rect.height);
        }

        public void SetIcon(string icon) {
            if (lastIcon != icon) {
                lastIcon = icon;
                image.sprite = Images.Sprite(icon);
            }
        }
        public void SetTitle(string text) {
            if (text != lastTitle) {
                lastTitle = text;
                headerText.SetText(text);
            }
        }

        private Lifetime BeginContent() {
            contentLife ??= Lifetime.Create("QuestWidgetContent");
            rowsLife?.Dispose();
            rowsLife = contentLife.CreateChild("QuestWidgetRows");
            stepLines.Clear();
            return rowsLife;
        }

        // Called on every tick, after the quests ticked
        public void SyncStepTitles() {
            if (!isShowing) { return; }
            bool changed = false;
            for (int i = 0; i < stepLines.Count; i++) {
                var line = stepLines[i];
                if (line.Sync()) {
                    changed = true;
                }
                stepLines[i] = line;
            }
            if (changed) {
                RebuildLayout();
            }
        }

        private static string StepTooltip(QuestStep step) {
            var sb = Caches.StringBuilder4;
            sb.Clear();
            if (step.Desc != null) { sb.Append(Texts.Beautify(step.Desc)); }
            if (step.Deadline != 0) {
                if (sb.Length > 0) { sb.Append('\n'); }
                QuestUtils.AppendDeadline(sb, step.Deadline, G.Ticks, suffix: null);
            }
            return sb.ToString();
        }

        private void SpawnTextLine(Lifetime life, string text, ref int idx) {
            var wgt = UISpawn.Widget<WText>(life, UIPrefabId.WQuestText, content.Container, idx++);
            wgt.Load(text);
            UIUtils.AddTipFunc(life, wgt, tooltipFunc);
        }

        /// <summary>
        /// Renders the tracked quest as individual hoverable step lines (mirrors
        /// QuestDetailsUI), plus an "Active Quests: N" footer when applicable.
        /// </summary>
        public void ShowQuest(Quest quest) {
            var life = BeginContent();
            int idx = 0;
            if (!quest.IsActive) {
                if (quest.Data.State == QuestState.Completed) {
                    SpawnTextLine(life, Texts.Green(T.QuestEndCompleted), ref idx);
                } else if (quest.Data.State == QuestState.Rejected
                        || quest.Data.State == QuestState.Failed) {
                    SpawnTextLine(life, Texts.Green(T.QuestEndFailed), ref idx);
                }
                var endedLink = A.S.Query.GetActiveQuests.Ask().Count > 0
                    ? T.QuestClickShowActive : T.QuestClickShowAvailable;
                SpawnTextLine(life, Texts.Italic(Texts.LinkToUI(endedLink, UIId.Quests)), ref idx);
                RebuildLayout();
                return;
            }
            bool hadActiveStep = false;
            foreach (var step in quest.Steps) {
                if (!QuestUtils.IsStepVisible(step)) { continue; }
                var stepW = UISpawn.Widget<WIconText>(life, UIPrefabId.WQuestTaskLine,
                    content.Container, idx++);
                bool isHeadline = !hadActiveStep && step.IsActive && !step.IsOptional;
                if (isHeadline) {
                    hadActiveStep = true;
                }
                stepLines.Add(new QuestStepLine(step, stepW, isHeadline));
                if (step.IsActive && (step.Desc != null || step.Deadline != 0)) {
                    var s = step;
                    UIUtils.AddTipFunc(life, stepW, () => StepTooltip(s));
                }
            }
            var activeCount = A.S.Query.GetActiveQuests.Ask().Count;
            if (activeCount > 1) {
                var footer = UISpawn.Widget<WText>(life, UIPrefabId.WQuestFooter,
                    content.Container, idx++);
                footer.Load(Texts.Grey(Texts.Italic(
                    Texts.LinkToUI($"{T.NounActiveQuests}: {activeCount}", UIId.Quests))));
                UIUtils.AddTipFunc(life, footer, tooltipFunc);
            }
            RebuildLayout();
        }

        /// <summary>
        /// Renders a single link line (no tracked quest, but available/active quests exist).
        /// </summary>
        public void ShowLinkLine(string text) {
            var life = BeginContent();
            int idx = 0;
            SpawnTextLine(life, text, ref idx);
            RebuildLayout();
        }

        public void OnResChanged() {
            float sw = RenderUtils.ScreenWidth;
            var scale = Video.GetCurrentUIScale(out _);
            // D.Warn("Scale: {0}, sw: {1}, Sw / scale = {2}", scale, sw, sw / scale);
            if (sw <= 1200 || sw / scale <= 1900) {
                neutralY = 120f * scale;
            } else {
                neutralY = 0f;
            }
            if (isShowing) {
                Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, neutralY);
            }
        }
    }
}
