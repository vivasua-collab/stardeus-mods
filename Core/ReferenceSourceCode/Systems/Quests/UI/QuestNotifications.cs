using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using System.Linq;
using Game.Input;
using Game.Systems.UIUX;
using UnityEngine.UI;

namespace Game.Systems.Quests {
    public sealed class QuestNotifications {
        private readonly GameState S;
        private readonly QuestSys sys;
        private QuestWidget widget;
        public QuestNotifications(GameState s, QuestSys sys) {
            S = s;
            this.sys = sys;
            s.UISig.ReloadQuests.AddListener(Update);
            s.Sig.QuestStateChanged.AddListener(OnQuestStateChanged);
            Ready.WhenGame(this, "LoadQuestsWidget", LoadWidget);
        }

        private void LoadWidget() {
            widget = (QuestWidget)Registries.UIWidgets.FastGet(UIWidgetIdH.Quest);
            widget.SetClickFunc(ToggleQuests);
            widget.SetNewQuestsAction(ToggleQuests);
            widget.SetNextQuestAction(TrackNextQuest);
            widget.SetNewQuests(0);
        }

        // A new offer pulls the eye to the quest widget: the "+1" badge alone went unnoticed
        // in playtests. Keyed on the state change rather than on the available count, which
        // has no earlier count to compare the very first offer of a game against.
        private void OnQuestStateChanged(QuestData data, QuestState before, QuestState after) {
            if (after != QuestState.Available || before == QuestState.Available) { return; }
            if (widget == null) { return; }
            UIFocusRing.Request(widget.Rect, waitForQuestsWindow: true);
        }

        private void ShowQuests() {
            S.UIQuery.ShowQuests.Ask(true);
        }
        private void ToggleQuests() {
            S.UIQuery.ShowQuests.Ask(!UIShowing.Quests);
        }

        // Steps rewrite their titles in a tick without a state change, so nothing redraws
        public void SyncStepTitles() {
            if (widget == null) { return; }
            widget.SyncStepTitles();
        }

        public void ShowStepCompleted(QuestStep step) {
            S.Sig.ShowFanfare.Send($"{GlyphMap.SpriteCheck} {Texts.Green(T.AdjDone)}: {step.Title}");
        }

        public void Update() {
            bool wasShowing = widget.IsShowing;
            Refresh();
            // The tracker sliding in from nothing is news whatever brought it: quests that
            // skip the offer (accepted from a Quest Hub, activated by another quest's step)
            // never reach the Available ring in OnQuestStateChanged, and slid in without one.
            // A save being restored is not news.
            if (!wasShowing && widget.IsShowing && !sys.IsRestoringQuests) {
                UIFocusRing.Request(widget.Rect, waitForQuestsWindow: true);
            }
        }

        private void Refresh() {
            bool hasAvailable = UpdateAvailableQuests();
            Quest quest = sys.TrackedQuest;
            int activeCount = sys.ActiveQuests.Count;
            widget.ShowNextQuest(activeCount > 1);
            if (quest == null) {
                if (activeCount == 0) {
                    if (!hasAvailable) {
                        widget.Hide();
                    }
                    return;
                }
                widget.ShowAlert(!hasAvailable);
                widget.SetTitle(T.QuestNoTracked);
                widget.SetTooltipFunc(QuestListTooltip);
                widget.SetClickFunc(ToggleQuests);
                widget.ShowLinkLine(Texts.Italic(Texts.LinkToUI(T.QuestClickShowActive, UIId.Quests)));
            } else {
                widget.SetClickFunc(() => S.UIQuery.ToggleQuest.Ask(quest));
                UpdateTrackedQuest(quest);
            }
            if (hasAvailable || quest != null || activeCount > 0) {
                widget.Show();
            } else {
                widget.Hide();
            }
        }

        private void UpdateTrackedQuest(Quest quest) {
            widget.SetIcon(QuestUtils.IconFor(quest, true));
            widget.ShowAlert(false);
            widget.SetTitle(quest.Data.Title);
            widget.SetTooltipFunc(QuestListTooltip);
            widget.ShowQuest(quest);
            widget.Show();
        }

        // In AllPlayerQuests order, as the Quests window and the tracker tooltip list them.
        // ActiveQuests is in activation order until a load rebuilds it in this one.
        private void TrackNextQuest() {
            var all = sys.AllPlayerQuests;
            int start = all.IndexOf(sys.TrackedQuest);
            for (int i = 1; i <= all.Count; i++) {
                var q = all[(start + i) % all.Count];
                if (q.IsActive && q != sys.TrackedQuest) {
                    S.Query.TrackQuest.Ask(q, true);
                    return;
                }
            }
        }

        private void ToggleLastAvailableQuest() {
            for (int i = sys.AllPlayerQuests.Count - 1; i >= 0; i--) {
                var q = sys.AllPlayerQuests[i];
                if (q.IsAvailable) {
                    S.UIQuery.ToggleQuest.Ask(q);
                    return;
                }
            }
            D.Err("Trying to toggle available quest when none are available!");
            ToggleQuests();
        }

        public bool UpdateAvailableQuests() {
            var availableCount = 0;
            bool hasAvailableMain = false;
            for (int i = sys.AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = sys.AllPlayerQuests[i];
                if (q.IsAvailable) {
                    if (!hasAvailableMain && q.IsMain) {
                        hasAvailableMain = true;
                    }
                    availableCount++;
                }
            }

            if (availableCount == 0) {
                widget.SetNewQuests(availableCount);
                widget.SetClickFunc(ToggleQuests);
                return false;
            }
            widget.SetClickFunc(ToggleLastAvailableQuest);
            bool wantAlert = false;
            if (sys.TrackedQuest == null) {
                widget.SetTooltipFunc(QuestListTooltip);
                if (sys.ActiveQuests.Count == 0) {
                    widget.SetNewQuests(0);
                } else {
                    widget.SetNewQuests(availableCount);
                }
                wantAlert = true;
                if (availableCount > 1) {
                    widget.SetTitle(T.QuestNewAvailablePlural_V1.F(Units.Num(availableCount)));
                } else {
                    widget.SetTitle(T.QuestNewAvailableSingular);
                }
                widget.ShowLinkLine(Texts.Italic(Texts.LinkToUI(T.QuestClickShowAvailable, UIId.Quests)));
            } else {
                widget.SetTooltipFunc(QuestListTooltip);
                widget.SetNewQuests(availableCount);
            }
            widget.Show();
            widget.ShowAlert(wantAlert);
            return true;
        }

        private readonly StringBuilder questTitles = new();

        private string QuestListTooltip() {
            questTitles.Clear();
            AppendQuestList(questTitles, T.NounAvailableQuests, available: true);
            AppendQuestList(questTitles, T.NounActiveQuests, available: false);
            return questTitles.ToString();
        }

        private void AppendQuestList(StringBuilder sb, string header, bool available) {
            bool first = true;
            foreach (var q in sys.AllPlayerQuests) {
                if (available ? !q.IsAvailable : !q.IsActive) { continue; }
                if (first) {
                    if (sb.Length > 0) { sb.Append('\n'); }
                    sb.Append(header);
                    sb.Append(':');
                    first = false;
                }
                sb.AppendFormat("\n {0} {1}",
                    Texts.Accented(GlyphMap.ArrowRight), QuestUtils.DecoratedTitle(q.Data));
                if (q.Data.GetNextDeadline(out var dl, out var step)) {
                    sb.Append(" (");
                    QuestUtils.AppendDeadline(sb, dl, G.Ticks, suffix: null);
                    sb.Append(')');
                }
            }
        }
    }
}
