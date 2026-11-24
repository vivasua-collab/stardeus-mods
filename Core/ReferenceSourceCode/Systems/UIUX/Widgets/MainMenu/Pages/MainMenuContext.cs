using System;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using KL.UweenExt;
using UnityEngine;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class MainMenuContext {
        public BaseMainMenuPage CurrentPage;
        public BaseMainMenuPage RootPage;
        public WGroup MenuContainer;
        public WGroup MenuPopupContainer;
        public WPanel SubMenuPanel;
        public LoadGameWidget LoadGameWidget;
        public ModInfoWidget ModInfoWidget;
        public RectTransform MouseTooltipParent;
        public MouseTooltip MouseTooltip;
        public WGroup SideButtonsContainer;
        public SettingsWidget SettingsWidget;
        public WallOfTextWidget WallOfTextWidget;
        public Lifetime Lifetime;
        public Lifetime PanelLifetime;
        public WTabButton ActiveTab;
        public MainMenuWidget Widget;
        private RectTransform menuRect => MenuContainer.Rect;

        public void ToggleTab(WTabButton tab) {
            LoadGameWidget.Close();
            ModInfoWidget.Close();
            if (ActiveTab == tab) {
                tab.SetSelected(false);
                ActiveTab = null;
                return;
            }
            if (ActiveTab != null) {
                ActiveTab.SetSelected(false);
            }
            ActiveTab = tab;
            tab.SetSelected(true);
        }

        public void AddSubHeader(Lifetime life, int idx, string title) {
            var btn = UISpawn.Widget<WText>(life, UIPrefabId.WMainMenuSubHeader, menuRect, idx);
            btn.Load(title);
        }
        public WButtonText AddButton(Lifetime life, int idx, string title, Action onClick) {
            var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WMainMenuButton, menuRect, idx);
            btn.Load(title, () => ClickButton(onClick));
            return btn;
        }

        public WButtonText AddButton2(Lifetime life, int idx, string title, Action onClick) {
            var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WMainMenuButton2, menuRect, idx);
            btn.Load(title, () => ClickButton(onClick));
            return btn;
        }

        public WButtonText AddButton3(Lifetime life, int idx, string title, Action onClick) {
            var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WMainMenuButton3, menuRect, idx);
            btn.Load(title, () => ClickButton(onClick));
            return btn;
        }

        // A demo build's locked entry: struck through, saying why on hover and click
        public void AddNotInDemo(Lifetime life, int idx, string title) {
            var btn = AddButton2(life, idx, Texts.Grey(Texts.Strike(title)),
                () => WPopup.SystemWarning(T.OperationForbidden, T.NotInDemo));
            UIUtils.AddTip(life, btn, T.NotInDemo);
        }

        public void ClickButton(Action onClick) {
            if (ActiveTab != null) {
                ToggleTab(ActiveTab);
            }
            onClick();
        }

        public WTabButton AddTab(Lifetime life, int idx, string title, Action<bool> onToggle) {
            var tab = UISpawn.Widget<WTabButton>(life, UIPrefabId.WMainMenuTab, menuRect, idx);
            tab.Load(title, () => ToggleTab(tab));
            tab.SetOnToggle(onToggle);
            return tab;
        }

        public WTabButton AddTab2(Lifetime life, int idx, string title, Action<bool> onToggle) {
            var tab = UISpawn.Widget<WTabButton>(life, UIPrefabId.WMainMenuTab2, menuRect, idx);
            tab.Load(title, () => ToggleTab(tab));
            tab.SetOnToggle(onToggle);
            return tab;
        }

        public void AddSpacer(Lifetime life, int idx, int height) {
            var spacer = UISpawn.Widget<WSpacer>(life, UIPrefabId.WSpacer, menuRect, idx);
            spacer.SetHeight(height);
        }
        public void CloseSubmenu() {
            if (ActiveTab != null) {
                ToggleTab(ActiveTab);
            } else {
                D.Err("Submenu is open without an active tab!");
                SubMenuPanel.Toggle(false);
            }
        }

        public void Initialize() {
            Lifetime = Lifetime.Create("MainMenu_Init");
            MenuContainer.DestroyChildren();
            LoadGameWidget.Close();
            WallOfTextWidget.Toggle(false);
            SubMenuPanel.Initialize();
            SubMenuPanel.Load(CloseSubmenu);
            ModInfoWidget.Close();
            SettingsWidget.OnClose = OnCloseSettings;
            SubMenuPanel.Toggle(false);
            MiscUtils.ReparentUI(MouseTooltip.Rect, MenuPopupContainer.Rect);
        }

        private void OnCloseSettings() {
            if (ActiveTab?.IsOn == true) {
                ToggleTab(ActiveTab);
            }
        }

        public void CloseMainMenu() {
            TweenCGA.Add(Widget.gameObject, 0.5f, 0f).Then(DoHide);
        }
        private void DoHide() {
            Widget.DoHide();
            MouseTooltip.Hide();
            MiscUtils.ReparentUI(MouseTooltip.Rect, MouseTooltipParent);
            if (ActiveTab != null) {
                ToggleTab(ActiveTab);
            }
        }

        public bool CloseTab() {
            if (ActiveTab != null) {
                ToggleTab(ActiveTab);
                return true;
            }
            return false;
        }

        public void CreateLifetime() {
            if (Lifetime != null) {
                Lifetime.Dispose();
            }
            Lifetime = Lifetime.Create("MainMenu");
        }

        public void ShowShareSave() {
#if !DISABLESTEAMWORKS
            ShareSaveWidget.Show();
#endif
        }

        public void Dispose() {
            Lifetime?.Dispose();
            Lifetime = null;
        }
    }
}
