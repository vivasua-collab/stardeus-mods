#if !PRODUCTION_BUILD
// #define DEBUG_UI
#endif
using System;
using System.Runtime.CompilerServices;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.UI {
    public static class UIShowing {
        public static UIShowingFlags Flags;

        [ExecuteOnReload]
        public static void Reset() {
            #if DEBUG_UI
            D.Warn("UI: UIShowing: {0}=>{1}", Flags, UIShowingFlags.None);
            #endif
            Flags = UIShowingFlags.None;
            OverlayType = null;
            MinigameConsumesMovement = false;
        }

        public static void ResetInGame() {
            Flags &= ~UIShowingFlags.AnyInGame;
            Unset(UIShowingFlags.ShipCombatMini);
            OverlayType = null;
            MinigameConsumesMovement = false;
        }

        public static bool AnyInGame => (Flags & UIShowingFlags.AnyInGame) != UIShowingFlags.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AnyOf(UIShowingFlags flags) {
            return (Flags & flags) != UIShowingFlags.None;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(UIShowingFlags flags) {
            #if DEBUG_UI
                if (flags != UIShowingFlags.MouseTooltip && !Flags.HasFlag(flags)) {
                    D.Warn("UI: UIShowing: {0}+{1}", Flags, flags);
                }
            #endif
            Flags |= flags;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(UIShowingFlags flags, bool on) {
            if (on) {
                Set(flags);
            } else {
                Unset(flags);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unset(UIShowingFlags flags) {
            #if DEBUG_UI
            if (flags != UIShowingFlags.MouseTooltip && Flags.HasFlag(flags)) {
                D.Warn("UI: UIShowing: {0}-{1}", Flags, flags);
            }
            #endif
            Flags &= ~flags;
        }

        public static bool BuildMenu => (Flags & UIShowingFlags.BuildMenu) == UIShowingFlags.BuildMenu;
        public static bool SubPanel => (Flags & UIShowingFlags.SubPanel) == UIShowingFlags.SubPanel;
        // public static bool DetailPanel => (Flags & UIShowingFlags.DetailPanel) == UIShowingFlags.DetailPanel;
        public static bool EntityPanel => (Flags & UIShowingFlags.EntityPanel) == UIShowingFlags.EntityPanel;
        public static bool Dialog => (Flags & UIShowingFlags.Dialog) == UIShowingFlags.Dialog;
        public static bool MouseTooltip => (Flags & UIShowingFlags.MouseTooltip) == UIShowingFlags.MouseTooltip;
        public static bool FullScreenWidget => (Flags & UIShowingFlags.FullScreenWidget) == UIShowingFlags.FullScreenWidget;
        public static bool ShipConfig => (Flags & UIShowingFlags.ShipConfig) == UIShowingFlags.ShipConfig;
        public static bool Starmap => (Flags & UIShowingFlags.Starmap) == UIShowingFlags.Starmap;
        public static bool Research => (Flags & UIShowingFlags.Research) == UIShowingFlags.Research;
        public static bool Overlay => (Flags & UIShowingFlags.Overlay) == UIShowingFlags.Overlay;
        public static bool GameSetup => (Flags & UIShowingFlags.GameSetup) == UIShowingFlags.GameSetup;
        public static bool MainMenu => (Flags & UIShowingFlags.MainMenu) == UIShowingFlags.MainMenu;
        public static bool ScreenBlock => (Flags & UIShowingFlags.ScreenBlock) == UIShowingFlags.ScreenBlock;
        public static bool Tutorial => (Flags & UIShowingFlags.Tutorial) == UIShowingFlags.Tutorial;
        public static bool QuickSearch => (Flags & UIShowingFlags.QuickSearch) == UIShowingFlags.QuickSearch;
        public static bool Quests => (Flags & UIShowingFlags.Quests) == UIShowingFlags.Quests;
        public static bool QuickChoice => (Flags & UIShowingFlags.QuickChoice) == UIShowingFlags.QuickChoice;
        public static bool JobMatrix => (Flags & UIShowingFlags.JobMatrix) == UIShowingFlags.JobMatrix;
        public static bool ImageBlowout => (Flags & UIShowingFlags.ImageBlowout) == UIShowingFlags.ImageBlowout;
        public static bool ScrollableWidget => (Flags & UIShowingFlags.ScrollableWidget) == UIShowingFlags.ScrollableWidget;
        public static bool BeingGroup => (Flags & UIShowingFlags.BeingGroup) == UIShowingFlags.BeingGroup;
        public static bool LoadingScreen => (Flags & UIShowingFlags.LoadingScreen) == UIShowingFlags.LoadingScreen;
        public static bool PanelModal => (Flags & UIShowingFlags.PanelModal) == UIShowingFlags.PanelModal;
        public static bool Terraforming => (Flags & UIShowingFlags.Terraforming) == UIShowingFlags.Terraforming;
        public static bool Wallet => (Flags & UIShowingFlags.Wallet) == UIShowingFlags.Wallet;
        public static bool Electricity => (Flags & UIShowingFlags.Electricity) == UIShowingFlags.Electricity;
        public static bool Expeditions => (Flags & UIShowingFlags.Expeditions) == UIShowingFlags.Expeditions;
        public static bool Codex => (Flags & UIShowingFlags.Codex) == UIShowingFlags.Codex;
        public static bool Notes => (Flags & UIShowingFlags.Notes) == UIShowingFlags.Notes;
        public static bool Situation => (Flags & UIShowingFlags.Situation) == UIShowingFlags.Situation;
        public static bool EventLog => (Flags & UIShowingFlags.EventLog) == UIShowingFlags.EventLog;
        public static bool Twitch => (Flags & UIShowingFlags.Twitch) == UIShowingFlags.Twitch;
        public static bool Surgeries => (Flags & UIShowingFlags.Surgeries) == UIShowingFlags.Surgeries;
        public static bool Policies => (Flags & UIShowingFlags.Policies) == UIShowingFlags.Policies;
        public static bool ShipCombatMini => (Flags & UIShowingFlags.ShipCombatMini) == UIShowingFlags.ShipCombatMini;
        public static bool ShipCombatMode => (Flags & UIShowingFlags.ShipCombatMode) == UIShowingFlags.ShipCombatMode;
        public static bool Production => (Flags & UIShowingFlags.Production) == UIShowingFlags.Production;
        public static bool TutorialPopup => (Flags & UIShowingFlags.TutorialPopup) == UIShowingFlags.TutorialPopup;
        public static bool Diplomacy => (Flags & UIShowingFlags.Diplomacy) == UIShowingFlags.Diplomacy;
        public static bool Minigame => (Flags & UIShowingFlags.Minigame) == UIShowingFlags.Minigame;
        public static bool CombatControls => (Flags & UIShowingFlags.CombatControls) == UIShowingFlags.CombatControls;
        public static bool Feedback => (Flags & UIShowingFlags.Feedback) == UIShowingFlags.Feedback;

        public static string OverlayType;

        // True while a focused, alive minigame consumes the movement keys
        // (WASD). Written by MinigameSys each frame; read by CameraControls so
        // the camera does not pan while the player is steering the minigame.
        // Plain static like OverlayType, not a flag bit.
        public static bool MinigameConsumesMovement;
    }
}
