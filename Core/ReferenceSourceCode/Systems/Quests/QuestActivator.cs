using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public class QuestActivator {
        private readonly GameState S;
        private QuestSys sys;
        private int nextIdx = -1;
        private readonly List<QuestDef> autoAddQuests = new();
        // Until the player has been offered a tutorial quest, Update keeps the offers going
        // while paused. After that, offers follow play, which needs the clock running anyway.
        private bool hasOfferedTutorial;

        public QuestActivator(GameState s, QuestSys sys) {
            S = s;
            this.sys = sys;
        }

        public void LoadQuests() {
            autoAddQuests.Clear();
            nextIdx = -1;
            foreach (var def in QuestDef.All) {
                if (!def.Flags.HasFlag(QuestFlags.AutoAdd)) { continue; }
                if (sys.Seen.ContainsKey(def)) {
                    // we've seen this quest
                    continue;
                }
                autoAddQuests.Add(def);
            }
            // A quest that ended on creation (its objective was already met) is not listed,
            // so it does not count as offered: the player never saw it
            hasOfferedTutorial = false;
            foreach (var q in sys.AllPlayerQuests) {
                if (q.Data.Def.IsTutorial) {
                    hasOfferedTutorial = true;
                    break;
                }
            }
            // Saves from before TutorialQuestOffered existed: Seen also remembers offers
            // whose quest was removed from the list since
            if (hasOfferedTutorial || HasSeenTutorial()) {
                sys.Bools.Set(QuestBoolIds.TutorialQuestOffered);
            }
        }

        private bool HasSeenTutorial() {
            foreach (var def in sys.Seen.Keys) {
                if (def.IsTutorial) { return true; }
            }
            return false;
        }

        public void RemoveFromAutoAdd(QuestDef def) {
            autoAddQuests.Remove(def);
        }

        private static readonly int OpeningCodexPageIdH = Hashes.S(OpeningCodexPage.PageId);
        // An offer the moment the Operations Manual closed landed on a player still looking
        // at it, so the hold outlasts the reading by a few seconds
        private const float SettleSeconds = 3f;
        // Neither the hold sample nor the paused pass needs frame precision
        private const int UpdateEveryFrames = 30;
        private float holdUntil;

        // A tutorial quest offered while the player reads the Codex, or while the Operations
        // Manual popup is still waiting to be read, lands unseen: playtesters got the first
        // quest and that popup at the same moment and missed the quest. Nothing here is saved
        // on purpose, so a save/load in between can never stall the chain: the pending popup
        // is saved, a page the player already learned is never queued, and the settle time
        // only outlasts the reading by seconds.
        private bool IsHoldingTutorialOffers() {
            if (UIShowing.Codex || UIShowing.Tutorial
                    || S.UIQuery.IsTutorialPopupPending.Ask(OpeningCodexPageIdH)) {
                holdUntil = GameTimer.unscaledTime + SettleSeconds;
                return true;
            }
            return GameTimer.unscaledTime < holdUntil;
        }

        // Every tick or frame, whatever the clock does
        public void Update() {
            if (hasOfferedTutorial) { return; }
            if (GameTimer.frameCount % UpdateEveryFrames != 0) { return; }
            if (autoAddQuests.Count == 0) { return; }
            // Sampled here too: the tutorial window stops the clock, and the settle time has
            // to start when the reading ends, not when the rotation next reaches the quest
            IsHoldingTutorialOffers();
            if (!IsPausedByPlayer()) { return; }
            // A paused clock never ticks, so a new player who paused to think after the intro
            // never got the first quest. One def per call would take the rotation ~20s to
            // reach it at this rate, so a paused pass covers the whole pool.
            for (int i = autoAddQuests.Count; i > 0; i--) {
                Tick();
            }
        }

        // Only a pause the player chose: a force stop means a menu, a save, a cutscene or a
        // choice is in front of the game, and ticks never offered anything behind those either
        private bool IsPausedByPlayer() {
            return S.Clock.Speed == ClockSpeed.Stopped && !S.Clock.IsForceStopped
                && !UIShowing.MainMenu;
        }

        public void Tick() {
            var avCnt = autoAddQuests.Count;
            if (avCnt == 0) { return; }
            if (nextIdx < 0 || nextIdx >= avCnt) {
                nextIdx = 0;
            }
            var def = autoAddQuests[nextIdx++];
            // Checked before AvailableWhen on purpose: a removed quest must never reach the
            // ForScenarios branch below, which would set its SetOnComplete bools and advance
            // the chain past a quest the player is meant to do a different version of.
            if (QuestBools.ContainsAny(sys.Bools, def.RemoveIf)) {
                D.Warn("Removing quest from the pool, RemoveIf matched: {0}", def);
                RemoveFromAutoAdd(def);
                return;
            }
            if (!QuestBools.ContainsAll(sys.Bools, def.AvailableWhen)) {
                return;
            }
            var diff = S.Difficulty.Level;
            if (!S.Situation.Matches(def.Require, diff)) {
                return;
            }
            // Before the ForScenarios branch for the same reason RemoveIf is: a quest whose
            // world-state gate is not met yet must not be skipped-and-advanced either
            if (!QuestSys.PassesValidators(S, def)) {
                return;
            }

            if (def.ForScenarios != null && !Arrays.Contains(def.ForScenarios, S.Scenario.Id)) {
                D.Warn("Skipping quest for scenario: {0} -> {1}", S.Scenario.Id, def);
                sys.Bools.SetAll(def.SetOnComplete);
                RemoveFromAutoAdd(def);
                return;
            }

            // After the ForScenarios branch: a skipped quest advances the chain whatever the UI
            // is doing, only a real offer waits for the player to be looking
            if (def.IsTutorial && IsHoldingTutorialOffers()) {
                return;
            }

            D.Warn("Will auto-add quest: {0}", def);
            // if we're still here, we want that quest to activate
            var data = QuestData.CreateFor(S, def, S.Rng.Fork());
            var q = S.Query.AddQuest.Ask(data, false);
            if (q != null && def.IsTutorial) {
                hasOfferedTutorial = true;
                S.Query.SetQuestBool.Ask(QuestBoolIds.TutorialQuestOffered, true);
            }
        }
    }
}
