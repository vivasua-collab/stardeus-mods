using System;
using System.Collections.Generic;
using System.IO;
using Game.Audio;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Utils;
using KL.UweenExt;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class MainMenuWidget : BaseWidget {
        private CanvasGroup cg;
        [SerializeField] private CanvasGroup[] hideWhenShowing;
        [SerializeField] private WGroup menuContainer;
        [SerializeField] private WGroup menuPopupContainer;
        [SerializeField] private WPanel subMenuPanel;
        public RectTransform PopupContainer => menuPopupContainer.Rect;
        [SerializeField] private LoadGameWidget loadGameWidget;
        [SerializeField] private ModInfoWidget modInfoWidget;
        [SerializeField] private RectTransform mouseTooltipParent;
        [SerializeField] private MouseTooltip mouseTooltip;
        public MouseTooltip MouseTooltip => mouseTooltip;
        [SerializeField] private WGroup sideButtonsContainer;
        [SerializeField] private SettingsWidget settingsWidget;
        [SerializeField] private WallOfTextWidget wallOfTextWidget;
        private MainMenuContext ctx;
        // A Canvas that overrides sorting is where Unity stops checking parent CanvasGroups
        // for blocksRaycasts, so the in-game widgets that have one stayed clickable while
        // hidden, invisible over the menu. Their raycasters go off with the hidden groups.
        private readonly List<GraphicRaycaster> suppressedRaycasters = new();
        public int SuppressedRaycasterCount => suppressedRaycasters.Count;
        // Widgets spawned or enabled under the hidden groups while the menu is open come with
        // live raycasters, so the scan repeats while it is open, not only when it opens
        private const float RaycasterRescanSeconds = 0.25f;
        private readonly List<GraphicRaycaster> raycasterScan = new();
        private bool isHudHidden;
        private float nextRaycasterScanAt;
        private void Awake() {
            cg = GetComponent<CanvasGroup>();
            LoadSideButtons();
            BuildMainMenu();
            HideBlockedCanvas();
            D.Log("Awaking main menu");
        }

        private void BuildMainMenu() {
            ctx = new MainMenuContext() {
                CurrentPage = null,
                RootPage = null,
                MenuContainer = menuContainer,
                MenuPopupContainer = menuPopupContainer,
                SubMenuPanel = subMenuPanel,
                LoadGameWidget = loadGameWidget,
                ModInfoWidget = modInfoWidget,
                MouseTooltipParent = mouseTooltipParent,
                MouseTooltip = mouseTooltip,
                SideButtonsContainer = sideButtonsContainer,
                SettingsWidget = settingsWidget,
                WallOfTextWidget = wallOfTextWidget,
                Widget = this,
            };
            ctx.Initialize();
            ctx.RootPage = new MainMenuPageMain(null, ctx);
        }

        private void AddSideButton(string text, Sprite sprite, Action onClick) {
            var b = UISpawn.WidgetPermanent<WButtonIconText>(
                UIPrefabId.WMainMenuSideButton, sideButtonsContainer.Rect);
            b.Load(null, text, onClick);
            b.SetSprite(sprite);
        }

        private void LoadSideButtons() {
            sideButtonsContainer.DestroyChildren();


            #if STARDEUS_DEMO
                AddSideButton("Get on Steam",
                    LinkedResources.Instance.LogoSteam, () => {
                        Files.OpenOSUrl("https://store.steampowered.com/app/1380910/Stardeus/?utm_source=Demo");
                    });
            #else
            if (The.Platform.IsExtrasAvailable()) {
                AddSideButton("Extras",
                    Images.Sprite(IconId.WSpecial), () => {
                        var rootPath = Directory.GetParent(Application.dataPath);
                        var extrasPath = System.IO.Path.Join(rootPath.FullName, "Extras");
                        if (Directory.Exists(extrasPath)) {
                            Files.OpenOSFolder(extrasPath);
                        } else {
                            WPopup.SystemWarning(IconId.CWarning,
                                "Extras directory is not available",
                                "Check if the The Journey DLC is downloaded.");
                        }
                    });
            }
            #endif

            AddSideButton(T.MenuSettingsLanguage,
                LinkedResources.Instance.IconLanguagesA, ShowLanguageSettings);
            AddSideButton(T.TwitchIntegration,
                LinkedResources.Instance.LogoTwitch, ShowTwitchSettings);
            AddSideButton("@StardeusGame",
                LinkedResources.Instance.LogoX, () => Files.OpenOSUrl("https://x.com/StardeusGame"));
            AddSideButton(T.BtnJoinDiscord,
                LinkedResources.Instance.LogoDiscord, () => Files.OpenOSUrl("https://discord.com/invite/hNBDxWsTgC"));
            AddSideButton(T.FeedbackF1,
                Images.Sprite(IconId.WBug), FeedbackButton.ShowPanel);
        }
        public void ShowAudioSettings() {
            ShowSettingsPage(SettingsPageAudio.PageId);
        }

        public void ShowSettingsPage(string id) {
            DrillUpToRoot();
            ctx.SettingsWidget.ShowPage(id);
        }

        public void ShowChangelog() {
            DrillUpToRoot();
            ctx.CloseTab();
            var about = new MainMenuPageAbout(ctx.RootPage, ctx);
            about.Show();
            about.OpenChangelog();
        }

        private void DrillUpToRoot() {
            while (ctx.CurrentPage != ctx.RootPage) {
                ctx.CurrentPage.DrillUp();
            }
        }


        private void ShowLanguageSettings() {
            ShowSettingsPage(SettingsPageLanguage.PageId);
        }

        private void ShowTwitchSettings() {
            ShowSettingsPage(SettingsPageTwitch.PageId);
        }

        public void Initialize() {
            Register(UIWidgetId.MainMenu, this);
            // The page loads here and not in Awake: its tooltips register with this game
            // state's tooltip handler, which exists only from now on (UIUXSys)
            if (ctx?.Lifetime != null) {
                (ctx.CurrentPage ?? ctx.RootPage).Show();
            }

            if (VersionInfo.HasMajorUpdate) {
                WPopup.Create(true, IconId.CUpgrade,
                    Texts.Accented(T.MajorUpdateWarning),
                    Consts.Version,
                    T.MajorUpdateOldBranch_V2.F(
                        Consts.LoadSaveUpdateVersion,
                        Consts.LoadSaveOldBranch))
                    .WithAction(T.Ok, null)
                    .WithAction2(T.Changelog, () => A.S.UIQuery.ShowChangelog.Ask())
                    .WithDistToEdge(0.25f)
                    .Spawn(PopupContainer);
            } else if (VersionInfo.HasUpdated) {
                MainMenuChangelogPanel.ParseChangelog(true);
            }
        }

        private void HideBlockedCanvas() {
            foreach (var group in hideWhenShowing) {
                UIUtils.ToggleCG(group, false);
            }
            isHudHidden = true;
            SuppressHudRaycasters();
        }

        private void SuppressHudRaycasters() {
            nextRaycasterScanAt = GameTimer.unscaledTime + RaycasterRescanSeconds;
            foreach (var group in hideWhenShowing) {
                raycasterScan.Clear();
                group.GetComponentsInChildren(true, raycasterScan);
                foreach (var raycaster in raycasterScan) {
                    // The mouse tooltip moves into the menu while it is open and has to stay
                    // clickable (links in a locked tooltip)
                    if (raycaster.transform.IsChildOf(mouseTooltip.transform)) { continue; }
                    if (raycaster.enabled) {
                        raycaster.enabled = false;
                        suppressedRaycasters.Add(raycaster);
                    }
                }
            }
            raycasterScan.Clear();
        }

        private void Update() {
            if (!isHudHidden) { return; }
            ReleaseRaycastersThatLeftHud();
            if (GameTimer.unscaledTime >= nextRaycasterScanAt) {
                SuppressHudRaycasters();
            }
        }

        // Pooled widgets keep their parent when despawned, so a HUD popup or bug report form
        // parked under the hidden groups gets its raycaster switched off, and the pool then
        // hands it to the menu's own popups: drawn, but every click fell through (STA-1622)
        private void ReleaseRaycastersThatLeftHud() {
            for (int i = suppressedRaycasters.Count - 1; i >= 0; i--) {
                var raycaster = suppressedRaycasters[i];
                if (raycaster == null) {
                    suppressedRaycasters.RemoveAt(i);
                    continue;
                }
                if (IsUnderHiddenGroup(raycaster.transform)) { continue; }
                raycaster.enabled = true;
                suppressedRaycasters.RemoveAt(i);
            }
        }

        private bool IsUnderHiddenGroup(Transform t) {
            foreach (var group in hideWhenShowing) {
                if (t.IsChildOf(group.transform)) { return true; }
            }
            return false;
        }

        private void RestoreBlockedCanvas() {
            isHudHidden = false;
            foreach (var raycaster in suppressedRaycasters) {
                if (raycaster != null) {
                    raycaster.enabled = true;
                }
            }
            suppressedRaycasters.Clear();
            foreach (var group in hideWhenShowing) {
                UIUtils.ToggleCG(group, true);
            }
        }

        public void Show() {
            ctx.CreateLifetime();
            D.Warn("Showing main menu");
            UIShowing.Set(UIShowingFlags.MainMenu, true);
            HideBlockedCanvas();
            mouseTooltip.Hide();
            MiscUtils.ReparentUI(mouseTooltip.Rect, menuPopupContainer.Rect);
            gameObject.SetActiveFast(true);
            ctx.RootPage.Show();
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                DoShow();
                return;
            }
            #endif
            TweenCGA.Add(gameObject, 0.25f, 1f).Then(DoShow);
        }

        private void DoShow() {
            UIUtils.ToggleCG(cg, true);
            if (!G.IsDemoMode) {
                A.S.Clock.ForceStop(ForceStopReason.MenuPanel);
            }
        }

        public void Hide() {
            if (G.IsDemoMode) { return; }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                DoHide();
                return;
            }
            #endif
            TweenCGA.Add(gameObject, 0.25f, 0f).Then(DoHide);
        }

        public void DrillUp() {
            if (!ctx.CurrentPage.DrillUp()) {
                Hide();
            }
        }

        public void FullyClose(bool instant) {
            int attempts = 10;
            while (ctx.CurrentPage.DrillUp() && attempts-- > 0) {
                // closing
            }
            if (instant) {
                DoHide();
            } else {
                Hide();
            }
        }

        public void DoHide() {
            if (G.IsDemoMode) { return; }
            G.HasEnteredGame = true;
            MiscUtils.ReparentUI(mouseTooltip.Rect, mouseTooltipParent);
            gameObject.SetActiveFast(false);
            UIUtils.ToggleCG(cg, false);
            RestoreBlockedCanvas();
            ctx.Dispose();
            // No state when the menu hides while a new game is being set up (the hide tween
            // outlives the backdrop game); the new game's clock starts on its own
            if (!G.IsDemoMode && A.S != null) {
                A.S.Clock.ForceResume(ForceStopReason.MenuPanel);
            }
        }

        private void OnEnable() {
            UIShowing.Set(UIShowingFlags.MainMenu);
        }

        private void OnDisable() {
            UIShowing.Unset(UIShowingFlags.MainMenu);
        }
    }
}
