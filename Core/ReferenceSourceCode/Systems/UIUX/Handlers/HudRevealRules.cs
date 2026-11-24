using System;
using System.Collections.Generic;
using Game.Constants;

namespace Game.Systems.UIUX {
    public enum HudRevealReason {
        // Not hidden by the reveal at all: not in a wave, or the reveal is not running
        NotGated,
        Hidden,
        Milestone,
        Day,
        Used,
    }

    // Which HUD buttons and Ship Computer apps a new run shows while the tutorial chain is
    // running. Pure rules over quest bools, the day and Show Hints, so they are the same on
    // load as in play and need no save data of their own. See Docs/Architecture/Systems/HudReveal.md.
    public static class HudRevealRules {
        public sealed class Wave {
            // All of them must be set
            public readonly string[] Milestones;
            // A player who ignores the quests is never stuck with a reduced HUD
            public readonly float LatestDay;
            public readonly string[] Keys;
            public Wave(string milestone, float latestDay, params string[] keys)
                : this(new[] { milestone }, latestDay, keys) { }
            public Wave(string[] milestones, float latestDay, params string[] keys) {
                Milestones = milestones;
                LatestDay = latestDay;
                Keys = keys;
            }

            public bool IsMilestoneReached(Func<string, bool> isSet) {
                for (int i = 0; i < Milestones.Length; i++) {
                    if (!isSet(Milestones[i])) { return false; }
                }
                return true;
            }
        }

        // HUD keys are bottom bar tool ids, plus these
        public const string KeyResearch = "research";
        public const string KeyStarmap = "starmap";
        public const string KeyWallet = "wallet";
        public const string AppKeyPrefix = "app.";
        public const string TabKeyPrefix = "tab.";
        public const string UsedBoolPrefix = "HudRevealed_";

        public static string AppKey(string appId) => $"{AppKeyPrefix}{appId}";
        // Build menu tabs, by tab id: the first segment of a def's tool path
        // (ToolPlaceTiles.MenuTabOf)
        public static string TabKey(string tabId) => $"{TabKeyPrefix}{tabId}";

        // Bottom bar entity actions a new player does not need during the tutorial. Keyed by
        // action id up front, so the bar build checks them without building strings.
        private static readonly Dictionary<string, string> actionKeys = new() {
            { EjectAllMatAction.ActionId, $"action.{EjectAllMatAction.ActionId}" },
            { EjectAllObjAction.ActionId, $"action.{EjectAllObjAction.ActionId}" },
            { CopyConfigAction.ActionId, $"action.{CopyConfigAction.ActionId}" },
            { SelectSimilarVisibleAction.ActionId, $"action.{SelectSimilarVisibleAction.ActionId}" },
        };

        public static bool TryGetActionKey(string actionId, out string key) {
            return actionKeys.TryGetValue(actionId, out key);
        }

        // Never in a wave, so always shown: Build, Connect, Overlays (collapsed by default,
        // quest steps open overlays and their legends dock to it), the top right, the
        // Codex, Quests and Electricity apps plus installed minigames, and the Floors, Walls
        // and Doors build tabs, as well as any build tab a mod adds.
        //
        // A build tab comes with the bool that offers the first quest asking for one of its
        // items (that quest's AvailableWhen), not with that quest's completion: the quest
        // must never name something the menu does not show. TutorialChainTests checks this
        // for every quest it walks.
        public static readonly Wave[] Waves = {
            // ProvideChargeStation on the wrecks. Its only gate also starts the reveal, so the
            // tab waits for the offer itself; on intact ships it comes with the first quest.
            new(QuestBoolIds.TutorialQuestOffered, 1f,
                TabKey("Energy")),
            new(QuestBoolIds.HasChargeStation, 1f,
                "repair", "remove_tiles", AppKey("Log")),
            // EstablishProduction, or EstablishAutomatedProduction
            new(QuestBoolIds.HasConnectedToSurvivorArea, 1.5f,
                TabKey("Production")),
            new(QuestBoolIds.HasEstablishedProduction, 2f,
                "store", "manage_ads", AppKey("Production"),
                // EstablishResearch
                TabKey("Computing")),
            new(QuestBoolIds.HasEstablishedResearch, 3f,
                KeyResearch,
                // The Winch in ConnectToStasisArray
                TabKey("Special")),
            // MakeStasisAreaSurvivable
            new(QuestBoolIds.EnclosedStasis, 4f,
                TabKey("Atmosphere")),
            // Day 5, not later: the "Build a Storage Unit" suggestion (from day 5) points
            // at a storage in Facilities
            new(QuestBoolIds.MadeStasisSurvivable, 5f,
                AppKey("Surgeries"), AppKey("JobMatrix"), AppKey("Policies"),
                // ProvideRepairStation and ProvideBeds. The colonist branch begins here,
                // and the repair station is where combat starts to be expected.
                TabKey("Facilities"), TabKey("Furniture"), TabKey("Lights"),
                TabKey("Decorations"), TabKey("Security")),
            // EstablishFoodProduction
            new(QuestBoolIds.HasBeds, 6f,
                TabKey("Food")),
            // BuildARadar waits for both, which can come in either order
            new(new[] { QuestBoolIds.HasRepairStation, QuestBoolIds.HasFoodProduction }, 7f,
                TabKey("SpaceTravel")),
            // ProvideEntertainment
            new(QuestBoolIds.HasHygiene, 8f,
                TabKey("Entertainment")),
            new(QuestBoolIds.HasRadar, 8f,
                KeyStarmap, KeyWallet, AppKey("Expeditions"), AppKey("Wallet"),
                AppKey("ShipConfig"), AppKey("Diplomacy"), AppKey("CombatControls"),
                AppKey("Situation"), AppKey("Notes"),
                $"action.{EjectAllMatAction.ActionId}", $"action.{EjectAllObjAction.ActionId}",
                $"action.{CopyConfigAction.ActionId}",
                $"action.{SelectSimilarVisibleAction.ActionId}"),
        };

        // Wanted: Show Hints and Gradual UI Reveal are both on
        public static bool IsRunning(Func<string, bool> isSet, bool wanted) {
            return wanted
                && isSet(QuestBoolIds.HudRevealActive)
                && isSet(QuestBoolIds.ReadyForTutorialQuests)
                && !isSet(QuestBoolIds.HudRevealDone);
        }

        public static Wave WaveOf(string key) {
            for (int i = 0; i < Waves.Length; i++) {
                var keys = Waves[i].Keys;
                for (int j = 0; j < keys.Length; j++) {
                    if (keys[j] == key) { return Waves[i]; }
                }
            }
            return null;
        }

        public static HudRevealReason Evaluate(Wave wave, string usedBool,
                Func<string, bool> isSet, float days) {
            if (wave == null) { return HudRevealReason.NotGated; }
            if (wave.IsMilestoneReached(isSet)) { return HudRevealReason.Milestone; }
            if (days >= wave.LatestDay) { return HudRevealReason.Day; }
            if (usedBool != null && isSet(usedBool)) { return HudRevealReason.Used; }
            return HudRevealReason.Hidden;
        }

        // Every wave in by milestone or day: the reveal is over for good
        public static bool AreAllWavesIn(Func<string, bool> isSet, float days) {
            for (int i = 0; i < Waves.Length; i++) {
                var w = Waves[i];
                if (!w.IsMilestoneReached(isSet) && days < w.LatestDay) { return false; }
            }
            return true;
        }
    }
}
