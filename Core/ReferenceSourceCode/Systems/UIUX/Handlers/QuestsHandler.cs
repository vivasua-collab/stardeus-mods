using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class QuestsHandler : BaseWidgetHandler<QuestsWidget> {
        private readonly QuestContext ctx;
        public QuestsHandler(GameState s, QuestsWidget widget) : base(s, widget) {
            ctx = new QuestContext();
            widget.Initialize(s, ctx);
            s.UIQuery.ShowQuests.SetResponder(ShowQuests);
            s.UIQuery.ShowQuest.SetResponder(ShowQuest);
            s.UIQuery.ShowNewQuest.SetResponder(ShowNewQuest);
            s.UIQuery.ToggleQuest.SetResponder(ToggleQuest);
            s.UIQuery.ShowQuestHubQuests.SetResponder(ShowQuestHubQuests);
            s.UISig.ReloadQuests.AddListener(ReloadQuests);
            s.Sig.QuestStepStateChanged.AddListener(OnQuestStepStateChanged);
        }

        private bool ToggleQuest(Quest q) {
            if (UIShowing.Quests && ctx.SelectedQuest == q) {
                ShowQuests(false);
                return false;
            }
            ShowQuest(q);
            return true;
        }

        private bool ShowQuest(Quest q) {
            widget.ShowQuest(q);
            return true;
        }
        private bool ShowNewQuest(Quest q) {
            var allQ = S.Query.GetAllPlayerQuests.Ask();
            if (allQ.Contains(q)) {
                D.Err("Trying to show new quest when it's already added! {0}", q);
                return ShowQuest(q);
            }
            S.Query.UpdateQuestSeen.Ask(q.Data);
            var lifetime = Lifetime.Create("NewQuest");
            var widget = UISpawn.Widget<WPanel>(lifetime, UIPrefabId.WListChoicePopup_Wide, UIPoolRoot.PopupContainer, 0);
            widget.Rect.SetAsLastSibling();
            widget.Load(T.QuestNewLabel, () => {
                lifetime.Dispose();
                // Nothing shows a closed colonist offer again, and a waiting one keeps the
                // colonist from asking for anything else
                if (q.Data.Def.Flags.HasFlag(QuestFlags.Signal)
                        || q.Data.Provider is ColonistQuestProvider) {
                    q.Data.SetState(QuestState.Rejected);
                }
            });
            QuestDetailsUI.LoadBlock(lifetime, widget.Content, q);

            int footIdx = 0;
            var acceptBtn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton_Confirm,
                widget.Footer.Container, footIdx++);
            acceptBtn.Load(T.VerbAccept, () => {
                S.Query.AcceptQuest.Ask(q);
                lifetime.Dispose();
                // The popup already showed everything the Quests window would, so point at
                // the tracker instead of opening the window
                QuestTrackerHandoff.FocusTracker(q);
                if (q.Data.Provider is NPCShip ship) {
                    if (ship.IsInHyperspace) {
                        ship.ExitHyperspace();
                    }
                    if (ship.WantsHyperspace) {
                        ship.SetWantsHyperspace(false);
                    }
                    NPCShipBehavior_Region_Idle.AssignTo(ship);
                }
            });
            widget.ShowFooter(footIdx > 0);
            UIUtils.AfterShowPopup(widget.Rect, true);
            return true;
        }

        private bool ShowQuests(bool show) {
            if (UIShowing.Research) {
                S.UIQuery.ShowResearch.Ask(false);
            }
            widget.Toggle(show);
            return true;
        }

        private void ReloadQuests() {
            if (UIShowing.Quests) {
                widget.Load();
            }
        }

        private void OnQuestStepStateChanged(QuestStep step,
                QuestStepState from, QuestStepState to) {
            if (UIShowing.Quests && ctx.SelectedQuest == step.Data.Quest) {
                widget.Load();
            }
        }

        private bool ShowQuestHubQuests(List<Quest> quests) {
            widget.ShowQuestHub(quests);
            return true;
        }
    }
}
