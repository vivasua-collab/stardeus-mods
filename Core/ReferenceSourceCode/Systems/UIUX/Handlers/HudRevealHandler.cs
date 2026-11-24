using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // Applies HudRevealRules to the bottom bar, the top bar wallet, the Ship Computer apps
    // list and the build menu tabs. It only toggles buttons the widgets already built:
    // input, hotkeys, tools and saves are untouched, so a hidden feature still opens by its
    // hotkey (and using it reveals it). See Docs/Architecture/Systems/HudReveal.md.
    public sealed class HudRevealHandler {
        private readonly GameState S;
        private readonly BottomBarWidget bottomBar;
        private readonly TopBarWidget topBar;
        private readonly TopRightWidget topRight;
        private readonly Func<string, bool> isSet;

        private sealed class Entry {
            public string Key;
            public string UsedBool;
            public HudRevealRules.Wave Wave;
            public bool IsApp;
            // Build menu tab id, or null
            public string TabId;
            // What the previous pass decided, to ring and report only real reveals
            public bool WasShown;
        }
        private readonly List<Entry> entries = new();
        private readonly Dictionary<string, Entry> byKey = new();

        // The bool lookups are cheap, but nothing here needs frame precision
        private const int UpdateEveryFrames = 30;
        private bool isRunning;
        // False until the first pass after a load: what a save already shows is no news
        private bool hasBaseline;

        public HudRevealHandler(GameState s, BottomBarWidget bottomBar, TopBarWidget topBar,
                TopRightWidget topRight) {
            S = s;
            this.bottomBar = bottomBar;
            this.topBar = topBar;
            this.topRight = topRight;
            isSet = IsBoolSet;
            foreach (var wave in HudRevealRules.Waves) {
                foreach (var key in wave.Keys) {
                    var e = new Entry {
                        Key = key,
                        UsedBool = $"{HudRevealRules.UsedBoolPrefix}{key}",
                        Wave = wave,
                        IsApp = key.StartsWith(HudRevealRules.AppKeyPrefix,
                            StringComparison.Ordinal),
                        TabId = key.StartsWith(HudRevealRules.TabKeyPrefix,
                                StringComparison.Ordinal)
                            ? key.Substring(HudRevealRules.TabKeyPrefix.Length)
                            : null,
                    };
                    entries.Add(e);
                    byKey.Add(key, e);
                }
            }
            s.UIQuery.IsHudRevealed.SetResponder(IsRevealed);
            s.UIQuery.TakeNewAppsHint.SetResponder(TakeNewAppsHint);
            s.UIQuery.TakeNewBuildTabHint.SetResponder(TakeNewBuildTabHint);
            s.UIQuery.RevealHudKey.SetResponder(RevealKey);
            s.Sig.NewGameInitialized.AddListener(OnNewGame);
            s.Sig.AfterLoadState.AddListener(OnAfterLoad);
            // The intro sets ReadyForTutorialQuests through this, just before it shows the
            // HUD: applying at once keeps the full bar from flashing up for a moment
            s.Sig.QuestBoolsChanged.AddListener(OnQuestBoolsChanged);
            s.Sig.ToolChanged.AddListener(OnToolChanged);
        }

        private bool IsBoolSet(string flag) {
            return S.Query.GetQuestBool.Ask(flag);
        }

        private float Days => G.Ticks / (float) Consts.TicksPerDay;

        // Only runs started with the reveal hide anything; sandbox has no tutorial chain
        private void OnNewGame() {
            if (S.Scenario == null || S.Scenario.IsSandbox) { return; }
            S.Query.SetQuestBool.Ask(QuestBoolIds.HudRevealActive, true);
        }

        private void OnAfterLoad(GameState s) {
            hasBaseline = false;
            // The bottom bar rebuilds its buttons on load, all of them shown
            Ready.WhenGameLate(this, "HudReveal", () => Refresh());
        }

        private void OnQuestBoolsChanged(string flag, bool on) {
            if (!Ready.Game) { return; }
            Refresh();
        }

        // Runtime only: after a load the player has had the chance to look
        private bool hasNewApps;
        private bool TakeNewAppsHint() {
            var had = hasNewApps;
            hasNewApps = false;
            return had;
        }

        // Tabs revealed since the build menu last showed them. Runtime only, like the apps.
        private readonly HashSet<string> newTabs = new();
        private bool TakeNewBuildTabHint(string tabId) {
            return newTabs.Remove(tabId);
        }

        private bool RevealKey(string key) {
            if (isRunning) { MarkUsed(key); }
            return true;
        }

        public bool IsRevealed(string key) {
            if (!isRunning) { return true; }
            if (!byKey.TryGetValue(key, out var e)) { return true; }
            return e.WasShown;
        }

        // A hidden tool used by its hotkey (or opened for the player by the game) is one
        // the player already knows
        private void OnToolChanged(ITool tool) {
            if (!isRunning || tool == null) { return; }
            // Building something reveals its tab (search, Quick Search, Build a Copy...)
            if (tool is ToolPlaceTiles && tool.CurrentParam != null) {
                var def = The.Defs.Get(tool.CurrentParam);
                if (def != null) {
                    MarkUsed(HudRevealRules.TabKey(ToolPlaceTiles.MenuTabOf(def)));
                }
            }
            var key = tool.ParentId ?? tool.Id;
            MarkUsed(key);
        }

        private void MarkUsed(string key) {
            if (!byKey.TryGetValue(key, out var e) || e.WasShown) { return; }
            S.Query.SetQuestBool.Ask(e.UsedBool, true);
            Refresh();
        }

        public void Update() {
            TryRebuildLayout();
            if (GameTimer.frameCount % UpdateEveryFrames != 0) { return; }
            if (!Ready.Game) { return; }
            if (isRunning) {
                if (UIShowing.Research) { MarkUsed(HudRevealRules.KeyResearch); }
                if (UIShowing.Starmap) { MarkUsed(HudRevealRules.KeyStarmap); }
                if (UIShowing.Wallet) {
                    MarkUsed(HudRevealRules.KeyWallet);
                    MarkUsed(HudRevealRules.AppKey("Wallet"));
                }
                // The drafting tutorial's moment: combat reactions and defenses matter now
                if (S.Sys.Combat.IsHostileAlertUp) {
                    MarkUsed(HudRevealRules.AppKey("CombatControls"));
                    MarkUsed(HudRevealRules.TabKey("Security"));
                }
            }
            Refresh();
        }

        private void Refresh() {
            // Setting a quest bool below sends QuestBoolsChanged, which lands back here
            if (isRefreshing) { return; }
            isRefreshing = true;
            try {
                RefreshNow();
            } finally {
                isRefreshing = false;
            }
        }
        private bool isRefreshing;

        private void RefreshNow() {
            var days = Days;
            var wasRunning = isRunning;
            var wanted = IsWanted;
            var running = HudRevealRules.IsRunning(isSet, wanted);
            if (running) {
                if (HudRevealRules.AreAllWavesIn(isSet, days)) {
                    Finish("allWavesIn");
                    running = false;
                }
            } else if (!wanted && IsActive()) {
                // Show Hints or Gradual UI Reveal off: the player asked not to be taught, for
                // the rest of the run
                Finish(A.ShowHints ? "settingOff" : "hintsOff");
            }
            isRunning = running;
            // No rings with hints off, none for what a save already showed, and none after
            // Skip Tutorial, which reveals everything at once
            var announce = hasBaseline && A.ShowHints
                && !isSet(QuestBoolIds.SkippedTutorial);
            var ringedHud = false;
            var revealedApp = false;
            // The build menu builds its tabs from IsRevealed, and a load starts from scratch
            var tabsChanged = !hasBaseline;
            for (int i = 0; i < entries.Count; i++) {
                var e = entries[i];
                var reason = running
                    ? HudRevealRules.Evaluate(e.Wave, e.UsedBool, isSet, days)
                    : HudRevealReason.NotGated;
                var shown = reason != HudRevealReason.Hidden;
                if (hasBaseline && shown && !e.WasShown && running) {
                    PlayStats.TrackHudRevealed(e.Key, ReasonText(reason));
                }
                var revealedNow = announce && shown && !e.WasShown;
                if (e.TabId != null) {
                    tabsChanged |= (!wasRunning || e.WasShown) != (!running || shown);
                    if (revealedNow) { newTabs.Add(e.TabId); }
                }
                e.WasShown = shown;
                if (e.IsApp) {
                    revealedApp |= revealedNow;
                    continue;
                }
                var target = TargetFor(e.Key);
                if (target == null) { continue; }
                var go = target.gameObject;
                if (go.activeSelf != shown) {
                    go.SetActive(shown);
                    layoutDirty = true;
                }
                if (revealedNow && !ringedHud) {
                    ringedHud = true;
                    UIFocusRing.Request(target);
                }
            }
            if (revealedApp && topRight != null) {
                UIFocusRing.Request(topRight.AppsButtonRect);
                // The new apps sit behind the button: the Apps tab rings once more when the
                // player opens the Ship Computer
                hasNewApps = true;
            }
            hasBaseline = true;
            TryRebuildLayout();
            if (tabsChanged) {
                S.Sig.RebuildBuildMenu.Send();
            }
        }

        // The bars nest layout groups that only settle when rebuilt from the top (the bottom
        // bar's own swap does the same), so a hidden button left its gap until something else
        // rebuilt the bar. A bar that is inactive right now is rebuilt once it is back.
        private bool layoutDirty;
        private void TryRebuildLayout() {
            if (!layoutDirty) { return; }
            if (bottomBar == null || !bottomBar.gameObject.activeInHierarchy) { return; }
            if (topBar == null || !topBar.gameObject.activeInHierarchy) { return; }
            LayoutRebuilder.ForceRebuildLayoutImmediate(bottomBar.Rect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(topBar.Rect);
            layoutDirty = false;
        }

        private static bool IsWanted => A.ShowHints && A.GradualUIReveal;

        private bool IsActive() {
            return isSet(QuestBoolIds.HudRevealActive)
                && isSet(QuestBoolIds.ReadyForTutorialQuests)
                && !isSet(QuestBoolIds.HudRevealDone);
        }

        private void Finish(string reason) {
            if (isSet(QuestBoolIds.HudRevealDone)) { return; }
            for (int i = 0; i < entries.Count; i++) {
                if (!entries[i].WasShown && hasBaseline) {
                    PlayStats.TrackHudRevealed(entries[i].Key, reason);
                }
            }
            S.Query.SetQuestBool.Ask(QuestBoolIds.HudRevealDone, true);
        }

        private RectTransform TargetFor(string key) {
            if (key == HudRevealRules.KeyWallet) {
                return topBar != null ? topBar.WalletRect : null;
            }
            return bottomBar != null && bottomBar.TryGetRevealTarget(key, out var rect)
                ? rect : null;
        }

        private static string ReasonText(HudRevealReason reason) {
            switch (reason) {
                case HudRevealReason.Milestone: return "milestone";
                case HudRevealReason.Day: return "day";
                case HudRevealReason.Used: return "used";
                default: return "other";
            }
        }
    }
}
