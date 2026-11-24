#if !PRODUCTION_BUILD
#define SKIP_TUTORIALS_IN_TESTS
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class TutorialHandler : BaseWidgetHandler<TutorialWidget> {
        private readonly List<PendingTutorialPopup> popupQueue = new();
        private readonly HashSet<string> seenConcepts = new();
        /// <summary>
        /// Runtime reference to whichever popup is currently displayed as a toast.
        /// Null when no toast is on screen. Never serialized.
        /// </summary>
        private PendingTutorialPopup shownPopup;
        private float quietUntil;
        #if SKIP_TUTORIALS_IN_TESTS
        private static float QuietSeconds => The.TestsAreRunning ? 1f : 5f;
        #else
        private const float QuietSeconds = 5f;
        #endif
        // How long an unchanged quest window still counts as being read. The quiet period
        // follows it, so popups resume counting down ~30s after the content last changed.
        private const float QuestReadSeconds = 25f;
        private float questsReadUntil;

        public TutorialHandler(GameState s, TutorialWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ShowTutorial.SetResponder(ShowTutorial);
            s.UIQuery.ShowTutorialPopup.SetResponder(EnqueueTutorialPopup);
            s.UIQuery.CloseTutorial.SetResponder(CloseTutorial);
            s.UIQuery.IsTutorialPopupPending.SetResponder(IsPopupPending);
            s.Sig.QuestStateChanged.AddListener(OnQuestStateChanged);
            s.UISig.QuestsContentChanged.AddListener(OnQuestsContentChanged);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            // Kept alive by its signal subscription, which lives as long as this game state
            _ = new ScanningTutorialTrigger(s);
            _ = new DraftingTutorialTrigger(s);
        }

        public void Cleanup() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
        }

        private void OnSettingChanged(string key) {
            if (key == Pref.GShowHints && !A.ShowHints) {
                DropAll();
            }
        }

        // Show Hints off drops popups rather than holding them: QuestActivator holds the
        // tutorial quests while the Operations Manual popup is pending, so a held queue would
        // never offer the first quest. Dropped popups are not marked seen, so they can come
        // back once hints are on again
        private void DropAll() {
            if (shownPopup != null) {
                widget.Popup.Close();
                shownPopup = null;
            }
            popupQueue.Clear();
        }

        // A page the player marked as learned (e.g. in the Codex) while its popup waited is
        // dropped by the queue only when the popup's turn comes, which a dialog can postpone
        // indefinitely, so it counts as no longer pending right away
        private bool IsPopupPending(int conceptIdH) {
            for (int i = 0; i < popupQueue.Count; i++) {
                var p = popupQueue[i];
                if (p.ConceptIdH != conceptIdH) { continue; }
                var page = CodexConceptPage.FindById(p.ConceptId);
                return page != null && !page.IsLearned;
            }
            return false;
        }

        private bool CloseTutorial() {
            if (UIShowing.Tutorial) {
                widget.Close();
                return true;
            }
            return false;
        }

        private bool EnqueueTutorialPopup(string codexPageId, float delaySeconds) {
            if (!A.ShowHints) { return false; }
            var page = CodexConceptPage.FindById(codexPageId);
            if (page == null) {
                D.Err("Couldn't find a codex page for tutorial popup: {0}", codexPageId);
                return false;
            }
            if (page.IsLearned) {
                D.Warn("Skipping tutorial popup for learned codex concept: {0}", codexPageId);
                return false;
            }
            if (seenConcepts.Contains(codexPageId)) {
                D.Warn("Skipping tutorial popup for already seen codex concept: {0}", codexPageId);
                return false;
            }
            foreach (var pending in popupQueue) {
                if (pending.ConceptId == codexPageId) {
                    D.Warn("Tutorial popup already queued for codex concept: {0}", codexPageId);
                    return false;
                }
            }
            popupQueue.Add(new PendingTutorialPopup {
                ConceptId = codexPageId,
                Remaining = delaySeconds,
                ConceptIdH = Hashes.S(codexPageId),
            });
            return true;
        }

        private void ProcessPopupQueue() {
            if (popupQueue.Count == 0) {
                return;
            }

            // Step 1: if a popup is currently on screen, wait for the toast to be dismissed.
            if (shownPopup != null) {
                if (!UIShowing.TutorialPopup) {
                    popupQueue.Remove(shownPopup);
                    shownPopup = null;
                }
                return;
            }

            // Step 2: global gate — don't tick while menus/codex/tutorial/dialogs are open.
            if (UIShowing.MainMenu || UIShowing.Codex || UIShowing.Tutorial || UIShowing.TutorialPopup) {
                return;
            }
            // The quest window and dialogs are where the player reads: a popup counting down
            // while a new quest is read arrived "right after the quest was accepted" in
            // playtests. Hold, and give the player a quiet moment after they close.
            // The quest window doesn't block play, and a stuck player may leave it open while
            // trying things, so it only holds while its content is fresh.
            // A player writing a bug report is busy too, and a tutorial over the form buries it.
            bool isReadingQuests = UIShowing.Quests && GameTimer.unscaledTime < questsReadUntil;
            if (isReadingQuests || UIShowing.Feedback
                    || (The.Dialog != null && The.Dialog.IsBusy)) {
                quietUntil = GameTimer.unscaledTime + QuietSeconds;
                return;
            }
            if (GameTimer.unscaledTime < quietUntil) {
                return;
            }

            if (S.Vars.IsTemplateMode) {
                return;
            }

            // Step 3: resolve the tracked quest's def once (zero alloc — field read via responder).
            var trackedDef = S.Query.GetTrackedQuest.Ask()?.Data?.Def;

            // Step 4: walk the queue and tick the first eligible popup.
            // Unbound popups are always eligible (legacy behaviour preserved).
            // Bound popups only count down while the tracked quest is one of their bound quests.
            for (int i = 0; i < popupQueue.Count; i++) {
                var p = popupQueue[i];
                bool eligible;
                if (!QuestDef.IsBoundTutorial(p.ConceptIdH)) {
                    eligible = true;
                } else {
                    eligible = trackedDef?.BoundTutorialIdHSet != null
                               && trackedDef.BoundTutorialIdHSet.Contains(p.ConceptIdH);
                }
                if (!eligible) { continue; }

                p.Remaining -= GameTimer.unscaledDeltaTime;
                if (p.Remaining > 0f) { return; }

                // Timer elapsed — re-resolve and re-check in case the page was learned while waiting.
                var page = CodexConceptPage.FindById(p.ConceptId);
                if (page == null || page.IsLearned || seenConcepts.Contains(p.ConceptId)) {
                    popupQueue.RemoveAt(i);
                    return;
                }
                shownPopup = p;
                p.Shown = true;
                seenConcepts.Add(p.ConceptId);
                widget.Popup.ShowForCodexPage(page);
                return;
            }
        }

        private void OnQuestsContentChanged() {
            questsReadUntil = GameTimer.unscaledTime + QuestReadSeconds;
        }

        /// <summary>
        /// Cancels pending bound tutorial popups when a quest transitions from Active to any
        /// terminal state (Completed, Failed, Rejected, Deleted). Preserves a popup if another
        /// currently-active quest also binds it.
        /// </summary>
        private void OnQuestStateChanged(QuestData data, QuestState before, QuestState after) {
            if (before != QuestState.Active || (after & QuestState.Ended) == 0) { return; }
            var def = data?.Def;
            if (def?.BoundTutorialIdHSet == null) { return; }
            for (int i = popupQueue.Count - 1; i >= 0; i--) {
                var p = popupQueue[i];
                if (p.Shown) { continue; }
                if (!def.BoundTutorialIdHSet.Contains(p.ConceptIdH)) { continue; }
                if (StillWantedByActiveQuest(p.ConceptIdH, data.Quest)) { continue; }
                popupQueue.RemoveAt(i);
            }
        }

        /// <summary>
        /// Returns true when at least one other active quest (besides the abandoned one) binds
        /// the given tutorial id hash — in which case the popup should not be cancelled.
        /// </summary>
        private bool StillWantedByActiveQuest(int tutorialIdH, Quest abandonedQuest) {
            var active = S.Query.GetActiveQuests.Ask();
            if (active == null) { return false; }
            for (int i = 0; i < active.Count; i++) {
                var q = active[i];
                if (q == abandonedQuest) { continue; }
                var d = q.Data?.Def;
                if (d?.BoundTutorialIdHSet != null && d.BoundTutorialIdHSet.Contains(tutorialIdH)) {
                    return true;
                }
            }
            return false;
        }

        private bool ShowTutorial(string codexPageId) {
            if (UIShowing.Tutorial) {
                D.Err("Already showing another tutorial, can't do it now! Showing: {0}. Wants to show: {1}",
                    widget.CurrentPage?.Id, codexPageId);
                return false;
            }
            var page = CodexConceptPage.FindById(codexPageId);
            if (page == null) {
                D.Err("Couldn't find a codex page for tutorial: {0}", codexPageId);
                return false;
            }
            #if SKIP_TUTORIALS_IN_TESTS
            if (The.TestsAreRunning) {
                return true;
            }
            #endif
            widget.ShowCodexPage(page);

            return true;
        }

        public void Update() {
            ProcessPopupQueue();
        }

        public void OnSave(SystemsDataSpecial sd) {
            if (popupQueue.Count == 0 && seenConcepts.Count == 0) {
                return;
            }
            var data = new TutorialSysData {
                PendingPopups = new(),
                SeenConcepts = new(seenConcepts),
            };
            foreach (var pending in popupQueue) {
                if (pending.Shown) {
                    // already seen and recorded in SeenConcepts; don't re-queue on load
                    continue;
                }
                data.PendingPopups.Add(new PendingTutorialPopup {
                    ConceptId = pending.ConceptId,
                    Remaining = pending.Remaining,
                });
            }
            sd.Tutorial = data;
        }

        public void OnLoad(TutorialSysData data) {
            popupQueue.Clear();
            shownPopup = null;
            // Saved while hints were on: the settings signal never fires for that
            if (A.ShowHints && data?.PendingPopups != null) {
                foreach (var pending in data.PendingPopups) {
                    popupQueue.Add(new PendingTutorialPopup {
                        ConceptId = pending.ConceptId,
                        Remaining = pending.Remaining,
                        ConceptIdH = Hashes.S(pending.ConceptId),
                    });
                }
            }
            seenConcepts.Clear();
            if (data?.SeenConcepts != null) {
                foreach (var id in data.SeenConcepts) {
                    seenConcepts.Add(id);
                }
            }
        }
    }
}
