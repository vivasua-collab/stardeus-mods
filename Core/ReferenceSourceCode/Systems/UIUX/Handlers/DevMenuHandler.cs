#if !STARDEUS_DEMO
using System.Collections;
using Game.Data;
using Game.Input;
using KL.Utils;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class DevMenuHandler : BaseWidgetHandler<DevMenuWidget> {
        public DevMenuHandler(GameState s, DevMenuWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ToggleDevMenu.SetResponder(ToggleDevMenu);
            s.UIQuery.ShowDevMenuTab.SetResponder(ShowTab);
            // Coroutines.Start(DevMenuShortcuts(s), this, "DevMenuShortcuts", false, false);
        }

        private bool ToggleDevMenu(bool show) {
            widget.Toggle(show);
            return true;
        }

        private bool ShowTab(string tabId) {
            if (!widget.gameObject.activeSelf) {
                widget.Toggle(true);
            }
            widget.ShowTab(tabId);
            return true;
        }

        private IEnumerator DevMenuShortcuts(GameState s) {
            while (GameState.IsCurrent(s)) {
                yield return null;
                var kc = Keyboard.current;
                if (kc == null || !kc.ctrlKey.isPressed) { continue; }
                if (kc.f2Key.wasPressedThisFrame) {
                    s.UIQuery.ToggleDevMenu.Ask(!widget.gameObject.activeSelf);
                }
                if (kc.f3Key.wasPressedThisFrame) {
                    s.UIQuery.ShowDevMenuTab.Ask(DevMenuTab_CameraTools.TabIdConst);
                }
                if (kc.f4Key.wasPressedThisFrame) {
                    s.UIQuery.ShowDevMenuTab.Ask(DevMenuTab_StoryEvents.TabIdConst);
                }
                if (kc.f6Key.wasPressedThisFrame) {
                    s.UIQuery.ShowDevMenuTab.Ask(DevMenuTab_Drama.TabIdConst);
                }
            }
        }
    }
}
#endif
