using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Suggestions;
using Game.Utils;
using KL.Algos;
using KL.Collections;
using KL.Utils;
using Game.UI;
using Game.Systems.UIUX;
using Game.Systems.Notify;

namespace Game.Systems {
    public sealed class SuggestionSys : GameSystem, ISaveable {
        public const string SysId = "SuggestionSys";
        public override string Id => SysId;
        private const int CurrentSuggestionLimit = 3;
        public override bool SkipInSandbox => true;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SuggestionSys());
        }

        private GameState state;
        private readonly HashSet<string> completedIds = new();
        private readonly List<Suggestion> current = new();
        private readonly List<Suggestion> ignored = new();
        private readonly List<Suggestion> completed = new();
        private readonly Dictionary<string, long> timing = new();
        // Came from an event during a fight. Not saved: the senders are in-memory
        // one-shots too, and fire again after a load.
        private readonly List<Suggestion> deferred = new();
        private bool hasExhaustedAll;
        private bool hasNotifiedAfterLoad;
        // First goal can kick after 5 minutes
        private double lastGoalAtRealTime = -60 * 3;
        // 5 minutes
        private const double minSecondsBetweenSuggestions = 60 * 3;
        public bool IsDisabled;

        public bool IsCurrent(Suggestion goal) {
            return current.Contains(goal);
        }

        // Nobody wants a hint popup mid-fight, and combat power draw would trigger
        // some of them (energy budget) for the wrong reason. A fight nobody was told about
        // yet is no reason to hold them.
        private bool IsCombatOngoing => S.Sys.Combat.IsHostileAlertUp
            || A.Player?.Ship?.IsInCombat == true;

        // A suggestion about something the running tutorial is about to teach would be a
        // second voice saying the same thing, often before the quest gets there
        private bool IsWaitingForLesson(Suggestion s) {
            var b = s.WaitsForBool;
            if (b == null) { return false; }
            if (!S.Query.GetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests)) { return false; }
            return !S.Query.GetQuestBool.Ask(b);
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.AddSuggestion.AddListener(AddSuggestion);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
        }

        private void OnSettingChanged(string key) {
            if (key == Pref.GShowHints && !A.ShowHints) {
                DismissShown();
            }
        }

        // Show Hints off: dismissed rather than ignored or completed, so they can come
        // back on their own once hints are on again
        private void DismissShown() {
            for (int i = current.Count - 1; i >= 0; i--) {
                if (!current[i].IsInternal) {
                    DismissSuggestion(current[i]);
                }
            }
        }

        private void AddSuggestion(string id) {
            if (IsDisabled || S.Vars.IsShowingCutscene) {
                return;
            }
            var canAddAfter = timing.Get(id, 0);
            if (G.Ticks < canAddAfter) { return; }
            timing[id] = G.Ticks + Consts.TicksPerWeek;
            // D.Warn("Active goal being added: {0}", id);
            var goal = Suggestion.Find(id);
            if (goal != null) {
                AddNew(goal);
            }
        }

        private void OnLoadSave(GameState state) {
            this.state = state;
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.CanAddSuggestionNow.SetResponder(CanAddSuggestionNow);
            state.Query.TryAddSuggestion.SetResponder(TryAddSuggestion);
        }

        private bool TryAddSuggestion(string id, bool checkLimits) {
            if (checkLimits && !CanAddSuggestionNow()) {
                return false;
            }
            var goal = Suggestion.Find(id);
            if (current.Contains(goal)) {
                return false;
            }
            if (ignored.Contains(goal)) {
                if (!goal.IsRecurring) {
                    return false;
                }
            }
            return AddNew(goal);
        }

        public override void OnDeactivate() {
            The.R.Query.CanAddSuggestionNow.SetResponder(() => true);
        }

        private const long suggEnqueueMod = Consts.TicksPerHour + 7;

        private void OnTick(long ticks) {
            if (IsDisabled) { return; }
            if (S.Vars.IsShowingCutscene) { return; }
            if (!hasNotifiedAfterLoad) {
                NotifyAfterLoad();
            }
            if (ticks % 37 == 0) {
                EvaluateCurrentSuggestions();
                ShowDeferred();
            } else if (!hasExhaustedAll && ticks % suggEnqueueMod == 0) {
                MaybeEnqueueNewSuggestion();
            }
        }

        private void NotifyAfterLoad() {
            hasNotifiedAfterLoad = true;
            if (!A.ShowHints) {
                DismissShown();
                return;
            }
            foreach (var goal in current) {
                CreateNewNotification(goal, true);
            }
        }

        private void CreateNewNotification(Suggestion goal, bool skipAutoAction) {
            var notif = Notification.Build(IconId.CTarget, goal.Title, 0, NotificationType.Info)
                .Group("sugg", T.OverlayTipGoals, IconId.CTarget)
                .WithTooltipFunc(() => goal.Description)
                .WithOnClick(() => {
                    OnIconClick(goal);
                });
            goal.Notify(S, notif, skipAutoAction);
        }

        private readonly HeapPriorityQueue<Suggestion> candidates = new(100);

        // Public for tests
        public void EvaluateCurrentSuggestions() {
            for (int i = current.Count - 1; i >= 0; i--) {
                Suggestion s = current[i];
                if (s.IsComplete(state)) {
                    CompleteSuggestion(s, false);
                } else if (s.ShouldBeDismissed(state)) {
                    DismissSuggestion(s);
                }
            }
        }

        // Public for tests. One at a time, so a long fight doesn't end in a pile of popups.
        // AddNew checks again whether each one still applies. Ones still waiting for their
        // tutorial lesson stay queued, AddNew would only defer them again.
        public void ShowDeferred() {
            if (deferred.Count == 0 || IsCombatOngoing) { return; }
            for (int i = 0; i < deferred.Count; i++) {
                var s = deferred[i];
                if (IsWaitingForLesson(s)) { continue; }
                deferred.RemoveAt(i);
                if (AddNew(s)) { return; }
                i--;
            }
        }

        private void DismissSuggestion(Suggestion s) {
            current.Remove(s);
            s.Dismiss();
        }

        public void IgnoreSuggestion(Suggestion s) {
            s.OnIgnore(state);
            DismissSuggestion(s);
        }

        private bool CanAddSuggestionNow() {
            if (!A.ShowHints) { return false; }
            if (IsCombatOngoing) { return false; }
            if (!The.Toolbox.IsFreeSelect) { return false; }
            if (UIShowing.AnyInGame) { return false; }
            if (UIShowing.MainMenu) { return false; }
            if (current.Count >= CurrentSuggestionLimit) { return false; }
            var t = GameTimer.realtimeSinceStartupAsDouble;
            var lastMotion = S.Vars.LastInputTime;
            if (t - lastMotion < 3f) {
                // the player wasn't idle long enough
                return false;
            }
            #if PRODUCTION_BUILD
            if (lastGoalAtRealTime + minSecondsBetweenSuggestions > t) {
                return false;
            }
            #endif
            return true;
        }

        private void MaybeEnqueueNewSuggestion() {
            if (A.InstantBuild) { return; }
            if (!CanAddSuggestionNow()) { return; }
            if (Suggestion.Count <= current.Count + completed.Count) {
                hasExhaustedAll = true;
                return;
            }
            // evaluate a possibility of adding new goals
            foreach (var sugg in Suggestion.All) {
                var s = sugg.Value;
                if (s.IsIgnored) { continue; }
                if (s.IsCurrent || s.IsCompleted) {
                    if (!s.IsRecurring) { continue; }
                }
                if (current.Contains(s)) {
                    continue;
                }
                if (IsWaitingForLesson(s)) { continue; }
                if (s.CheckDependencies(completedIds) && s.IsAvailable(state)) {
                    candidates.Enqueue(s, -s.Score(state));
                }
            }
            if (candidates.TryDequeue(out var newS)) {
                AddNew(newS);
            }
            candidates.Clear();
        }

        // Public for tests
        public void CompleteSuggestion(Suggestion s, bool silent) {
            current.Remove(s);
            completed.Add(s);
            completedIds.Add(s.Id);
            s.IsCompleted = true;
            s.OnCompleted(state);
            s.NotifyComplete(silent);
        }

        private bool AddNew(Suggestion s) {
            if (IsDisabled) { return false; }
            if (current.Contains(s)) { return false; }
            if (ignored.Contains(s)) { return false; }
            if (s.ShouldBeDismissed(state)) { return false; }
            if (!s.IsRecurring && completedIds.Contains(s.Id)) {
                return false;
            }
            if (s.IsInternal) {
                current.Add(s);
                if (s.IsComplete(state)) {
                    CompleteSuggestion(s, true);
                }
                return false;
            }
            // After the internal ones: those are gates that unlock other suggestions, and
            // must still be recorded for hints turned back on later
            if (!A.ShowHints) { return false; }
            // Deferred, not dropped: the periodic path is already held back by
            // CanAddSuggestionNow, so these came from an event that may not fire again
            if (IsCombatOngoing || IsWaitingForLesson(s)) {
                if (!deferred.Contains(s)) {
                    deferred.Add(s);
                }
                return false;
            }
            // D.Err("Adding new suggestion: {0}", s);
            lastGoalAtRealTime = GameTimer.realtimeSinceStartupAsDouble;
            s.OnAdded(state);
            s.IsCurrent = true;
            if (s.IsRecurring) {
                s.IsCompleted = false;
                completed.Remove(s);
                completedIds.Remove(s.Id);
            }
            if (s.IsComplete(state)) {
                // D.Warn("New goal already completed! {0}", goal);
                CompleteSuggestion(s, true);
                return false;
            }
            current.Add(s);
            CreateNewNotification(s, false);
            return true;
        }

        public override void Unload() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            lastGoalAtRealTime = 0;
            completedIds.Clear();
            current.Clear();
            completed.Clear();
            ignored.Clear();
            deferred.Clear();
            foreach (var g in Suggestion.All) {
                g.Value.Reset();
            }
        }

        public string GetName() {
            return Id;
        }

        // FIXME if we'll need internal current goal, adjust the count
        // and visibility fir currentGoals list
        private void OnIconClick(Suggestion s) {
            if (s.HasUIAction && s.CanIgnore) {
                var popupData = WPopup.Create(false, IconId.CComputing, s.Title, null, s.Description);
                popupData.WithAction(T.Options, () => ShowIgnoreOptions(s, popupData))
                    .WithAction2(T.Ok, null)
                    .WithKeepOpenOnButton1()
                    .WithDistToEdge(0.2f)
                    .Spawn(center: false);
            } else if (s.HasUIAction) {
                WPopup.Create(false, IconId.CComputing, s.Title, null, s.Description)
                    .WithAction2(T.Ok, null)
                    .WithAction(s.UIActionText, () => s.UIAction(S))
                    .WithDistToEdge(0.2f)
                    .Spawn(center: false);

                // var dd = The.Dialog.ShowChoice(IconId.CComputing,
                //     s.Description,
                //     The.Dialog.Hide,
                //     () => {
                //         s.UIAction(S);
                //         The.Dialog.Hide();
                //     }, T.Ok, s.UIActionText, canCloseWithEsc: true);
                // dd.ShouldBeRemoved = s.UIActionShouldBeRemoved;
            } else {
                The.Dialog.ShowSimple(IconId.CComputing,
                    s.Description);
            }
        }

        private void ShowIgnoreOptions(Suggestion s, WPopupData popupData) {
            var qc = QuickChoice.Create(s.Title);
            qc.Widget = popupData.Widget.Button1.Rect;
            qc.AddButton(s.UIActionText, () => {
                ClosePopup(popupData);
                s.UIAction(S);
            });
            qc.AddButton(T.Ignore, () => {
                ClosePopup(popupData);
                IgnoreSuggestion(s);
            });
            qc.Show();
        }

        private static void ClosePopup(WPopupData popupData) {
            var w = popupData.Widget;
            if (w != null && w.gameObject.activeSelf) {
                w.Close();
            }
        }
        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        private void SaveSuggestions(List<Suggestion> list, string name) {
            string[] ids = null;
            if (!Arrays.IsEmpty(list)) {
                ids = new string[list.Count];
                for (int i = 0; i < list.Count; i++) {
                    ids[i] = list[i].Id;
                }
            }
            Data.SetStringSet(name, ids);
        }
        private void LoadGoals(List<Suggestion> list, string name, bool isCurrent,
                bool isCompleted) {
            list.Clear();
            string[] ids = Data.GetStringSet(name, null);
            if (ids == null) { return; }
            for (int i = 0; i < ids.Length; i++) {
                string id = ids[i];
                var goal = Suggestion.Find(id);
                if (goal == null) { continue; }
                if (isCurrent) {
                    goal.IsCurrent = true;
                }
                if (isCompleted) {
                    goal.IsCompleted = true;
                    completedIds.Add(goal.Id);
                }
                // FIXME could be a bad assumption
                if (!isCurrent && !isCompleted) {
                    goal.IsIgnored = true;
                }
                list.Add(goal);
            }
        }

        public ComponentData Save() {
            SaveSuggestions(current, "CurrentGoals");
            SaveSuggestions(completed, "CompletedGoals");
            SaveSuggestions(ignored, "IgnoredGoals");
            // Saved for all suggestions, not only listed ones, because dismissed
            // suggestions are in no list but can still hold state (e.g. ignores)
            foreach (var sugg in Suggestion.All) {
                if (sugg.Value is ISaveableChild sc) {
                    sc.OnSave(Data);
                }
            }
            return Data;
        }

        public void Load(ComponentData data) {
            this.data = data;
            lastGoalAtRealTime = 0;
            LoadGoals(current, "CurrentGoals", true, false);
            LoadGoals(completed, "CompletedGoals", false, true);
            LoadGoals(ignored, "IgnoredGoals", false, false);
            foreach (var sugg in Suggestion.All) {
                if (sugg.Value is ISaveableChild sc) {
                    sc.OnLoad(data);
                }
            }
        }
    }
}
