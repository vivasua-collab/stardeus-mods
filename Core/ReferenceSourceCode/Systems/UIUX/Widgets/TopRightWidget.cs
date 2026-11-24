using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TopRightWidget : BaseWidget {
        [SerializeField] private WButtonIcon btnMainMenu;
        [SerializeField] private WButtonIcon btnQuickSearch;
        [SerializeField] private WButtonIcon btnApps;
        [SerializeField] private CanvasGroup cg;
        // Found at runtime: it sits inside the nested SpeedButtons prefab
        public RectTransform PauseButtonRect { get; private set; }
        public RectTransform AppsButtonRect => btnApps.Rect;

        public void Initialize(GameState s) {
            Register(UIWidgetId.TopRight, this);
            FindPauseButton();
            btnMainMenu.Load(IconId.WCog, () => s.UIQuery.ShowMainMenu.Ask(true));
            btnQuickSearch.Load(IconId.CMagnifier, () => {
                s.UIQuery.ShowQuickSearch.Ask(!UIShowing.QuickSearch);
            });
            var searchTip = btnQuickSearch.gameObject.AddComponent<UIPermanentTip>();
            searchTip.SetTip(Texts.TitleWithShortcutGlyph(
                T.OverlayTipSearch, Input.ActionType.QuickSearch));
            btnApps.Load(IconId.CComputing, () => {
                ShipComputerComp.ShowApps(s);
            });
            var appsTip = btnApps.gameObject.AddComponent<UIPermanentTip>();
            appsTip.SetTip(Texts.TitleWithShortcutGlyph(T.ObjectsDevicesShipcomputer, Input.ActionType.ShortcutApps));
        }

        private void FindPauseButton() {
            var buttons = GetComponentsInChildren<SetGameSpeedButton>(true);
            for (int i = 0; i < buttons.Length; i++) {
                if (buttons[i].Speed == ClockSpeed.Stopped) {
                    PauseButtonRect = (RectTransform) buttons[i].transform;
                    return;
                }
            }
            D.Err("No pause button found in {0}", this);
        }

        public bool CanRingPauseButton() {
            return UIFocusRing.CanShowNow(PauseButtonRect);
        }

        public void RingPauseButton() {
            UIFocusRing.Request(PauseButtonRect);
        }

        public void ShowInFullScreenWidget(bool yes) {
            if (yes) {
                UIUtils.ToggleCG(cg, true);
            }
        }
    }
}
