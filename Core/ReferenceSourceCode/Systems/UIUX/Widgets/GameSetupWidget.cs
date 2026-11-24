using System;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Scenarios;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class GameSetupWidget : BaseWidget {
        [SerializeField] private CanvasGroup mainMenuCG;
        [SerializeField] private GameSetupCrewWidget setupCrewWidget;
        [SerializeField] private GameSetupSpaceWidget setupSpaceWidget;
        [SerializeField] private WText txtHeader;
        [SerializeField] private WButtonText btnBack;
        [SerializeField] private WButtonText btnNext;
        [SerializeField] private WButtonText btnAchievementsDisabled;
        [SerializeField] private WToggleText toggleGradualUIReveal;
        [SerializeField] private WText txtFooterMessage;
        private GameSetupData data;
        // For tests
        public ShipPreview ShipPreview => setupCrewWidget.ShipPreview;
        // For tests
        public GameSetupData Data => data;
        private Lifetime lifetime;

        public void Initialize() {
            Register(UIWidgetId.GameSetup, this);
            setupCrewWidget.Initialize(txtFooterMessage);
            setupSpaceWidget.Initialize(txtFooterMessage);
            btnBack.Load($"{GlyphMap.TriangleLeft} {T.MenuBack}", OnBack);
            btnAchievementsDisabled.Load(T.AchievementsDisabled, OnAchievementsDisabled);
            btnAchievementsDisabled.SetActiveFast(false);
            toggleGradualUIReveal.SetActiveFast(false);
            Toggle(false);
        }
        private void OnAchievementsDisabled() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            foreach (var m in Modifier.All) {
                if (m.BlocksAchievements) {
                    sb.AppendFormat("{0} {1}\n", GlyphMap.SpriteWarning, m.NameT);
                }
            }
            var pData = WPopup.Create(true, IconId.CWarning, T.AchievementsWillBeDisabled, T.ModifiersAchievementsDisabledTip,
                    sb.ToString());

            if (!data.Scenario.IsSandbox) {
                pData.WithAction(T.Ok, null);
                pData.WithAction2(T.Revert, setupCrewWidget.RestoreAchievements);
            }

            pData.Spawn(UIPoolRoot.SystemPopupContainer);
        }


        private void EveryFrame() {
            switch (data.SetupPhase) {
                case GameSetupData.Phase.Crew: {
                    setupCrewWidget.EveryFrame();
                    break;
                }
                case GameSetupData.Phase.Space: {
                    setupSpaceWidget.EveryFrame();
                    break;
                }
            }
        }

        private void OnBack() {
            switch (data.SetupPhase) {
                case GameSetupData.Phase.Crew: {
                    A.S.UIQuery.ShowGameSetup.Ask(null, false);
                    break;
                }
                case GameSetupData.Phase.Space: {
                    btnNext.SetText(T.Confirm);
                    UIFocusRing.Cancel(btnNext.Rect);
                    toggleGradualUIReveal.SetActiveFast(false);
                    setupSpaceWidget.Close();
                    setupCrewWidget.Show(lifetime, data);
                    break;
                }
            }
        }

        public void BeginSetup(GameSetupData data) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("GameSetup");
            if (data.Scenario == Scenario.DefaultScenario) {
                txtHeader.Load(T.GameCustomStart);
            } else {
                txtHeader.Load($"{T.GameCustomScenario} {GlyphMap.ArrowRight} {data.Scenario.NameT}");
            }
            A.S.Clock.SetSpeed(ClockSpeed.Stopped);
            A.S.Clock.ForceStop(KL.Clock.ForceStopReason.MenuPanel);
            Modifier.ResetAll(data);
            this.data = data;
            UIUtils.ToggleCG(mainMenuCG, false);
            setupCrewWidget.Show(lifetime, data);
            UIShowing.Set(UIShowingFlags.FullScreenWidget);
            A.S.Sig.ToggleRendering.Send(false);
            btnAchievementsDisabled.SetActiveFast(false);
            toggleGradualUIReveal.SetActiveFast(false);
            btnNext.Load(T.Confirm, OnNext);
            A.S.Sig.EveryFrame.AddListener(EveryFrame, gameObject);
            A.S.Sig.ModifierChanged.AddListener(OnModifierChanged, gameObject);
            UIShowing.Set(UIShowingFlags.GameSetup);
            Toggle(true);
        }

        private void OnModifierChanged(Modifier mod) {
            setupCrewWidget.OnModifierChanged(mod);
            if (mod?.PreventsAchievements == false) { return; }
            bool noAchievements = false;
            foreach (var m in Modifier.All) {
                if (m.BlocksAchievements) {
                    D.Warn("No achievements due to: {0}", m.Id);
                    noAchievements = true;
                    break;
                }
            }
            btnAchievementsDisabled.SetActiveFast(noAchievements);
            if (noAchievements) {
                UIUtils.AddTip(lifetime, btnAchievementsDisabled, T.AchievementsWillBeDisabled);
            }
        }

        private void OnNext() {
            if (setupSpaceWidget.gameObject.activeSelf) {
                if (setupSpaceWidget.TryNext()) {
                    setupSpaceWidget.Close();
                    data.Save();
                    The.SysSig.ShowLoadingScreen.Send("New game", true);
                    var cmd = new CmdNewGame(data);
                    ExitSetup();
                    Coroutines.Start(cmd.ExecuteAsync(),
                        "GameSetup", "NewGame", false, true);
                }
            } else if (setupCrewWidget.TryNext(ProceedFromCrew)) {
                ShowSpaceSetup();
            }
        }

        // From the missing jobs popup. System popups are not modal, so the setup may have
        // been cancelled or moved on while it was open.
        private void ProceedFromCrew() {
            if (data == null || data.SetupPhase != GameSetupData.Phase.Crew) { return; }
            ShowSpaceSetup();
        }

        private void ShowSpaceSetup() {
            btnNext.SetText(T.VerbEmbark);
            ShowGradualUIRevealToggle();
            setupCrewWidget.Close();
            setupSpaceWidget.Show(lifetime, data);
            // The same button said Confirm a moment ago: this one starts the game
            if (A.ShowHints) {
                UIFocusRing.RequestInMenu(btnNext.Rect);
            }
        }

        // Returning players met this setting only after the reveal had started. It is the
        // global one, so unticking it here holds for every later run too.
        private void ShowGradualUIRevealToggle() {
            // Sandbox has no tutorial chain, and with Show Hints off the whole HUD is shown
            // either way, so a ticked box would mislead
            var show = !data.Scenario.IsSandbox && A.ShowHints;
            toggleGradualUIReveal.SetActiveFast(show);
            if (!show) { return; }
            toggleGradualUIReveal.Load(T.GradualUiReveal, A.GradualUIReveal,
                OnGradualUIRevealToggled);
        }

        private void OnGradualUIRevealToggled(bool on) {
            The.Prefs.SetBool(Pref.GGradualUIReveal, on);
            The.SaveGlobalSettings();
        }

        public void CancelSetup() {
            data.Reset();
            ExitSetup();
        }

        public void ExitSetup() {
            // A ring cut short here would play on Confirm the next time setup opens
            UIFocusRing.Cancel(btnNext.Rect);
            Toggle(false);
            setupCrewWidget.Close();
            setupSpaceWidget.Close();
            Cleanup();
            // Ship previews prime the static grid for the new map size, while the game
            // behind the menu keeps updating on it
            A.S.Map?.PrimePos();
            data = null;
            UIUtils.ToggleCG(mainMenuCG, true);
            UIShowing.Unset(UIShowingFlags.FullScreenWidget);
            UIShowing.Unset(UIShowingFlags.GameSetup);
            A.S.Sig.EveryFrame.RemoveListener(EveryFrame);
            A.S.Sig.ModifierChanged.RemoveListener(OnModifierChanged);
            A.S.Sig.ToggleRendering.Send(true);
        }

        private void Cleanup() {
            setupCrewWidget.Cleanup();
            setupSpaceWidget.Cleanup();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private void OnDestroy() {
            Cleanup();
        }
    }
}
