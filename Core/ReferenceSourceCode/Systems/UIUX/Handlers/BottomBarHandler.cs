using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class BottomBarHandler : BaseWidgetHandler<BottomBarWidget> {
        private readonly Dictionary<string, OverlayInfo> allOverlays = new();
        private readonly List<OverlayInfo> visualOverlays = new();
        private readonly EntityActionsHintHandler actionsHint;
        public BottomBarHandler(GameState s, BottomBarWidget widget) : base(s, widget) {
            widget.Initialize();
            actionsHint = new EntityActionsHintHandler(s, widget);

            LoadTools();
            widget.LoadSpecialButtons(s);

            Ready.WhenGameLate(this, "LoadOverlays", LoadOverlays);

            s.Sig.ShowOverlay.AddListener(OnShowOverlay);
            s.Sig.HideOverlay.AddListener(OnHideOverlay);
            s.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            s.Sig.ToggleOverlayById.AddListener(OnToggleOverlayById);
            s.Sig.HideTargetOverlay.AddListener(OnHideTargetOverlay);
            s.UIQuery.ShowEntityActions.SetResponder(ShowEntityActions);
            s.Sig.EveryFrame.AddListener(EveryFrame);
            s.UISig.ToggleShipCombatMode.AddListener(OnToggleShipCombat);
        }

        private void OnToggleShipCombat(bool on) {
            widget.SetActiveFast(!on);
            if (!on && UIShowing.Overlay) {
                widget.ShowOverlaysSubmenu();
            }
        }

        private bool ShowEntityActions(List<EntityActionData> actions, bool animate) {
            return widget.LoadActions(S, actions, animate);
        }

        private void OnToggleOverlayById(string id) {
            if (allOverlays.TryGetValue(id, out var ov)) {
                S.Sig.ToggleOverlay.Send(ov, !ov.IsShowing);
            }
        }

        private void OnHideOverlay() {
            foreach (var ov in allOverlays.Values) {
                if (ov.IsShowing) {
                    S.Sig.ToggleOverlay.Send(ov, false);
                }
            }
            UIShowing.Unset(UIShowingFlags.Overlay);
            widget.HideOverlaysSubmenu();
        }
        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (on) {
                UIShowing.Set(UIShowingFlags.Overlay);
                UIShowing.OverlayType = info.Id;
            } else {
                UIShowing.Unset(UIShowingFlags.Overlay);
                UIShowing.OverlayType = null;
            }
        }
        private void OnHideTargetOverlay(string id) {
            if (id == null) {
                D.Err("Trying to hide null overlay");
                return;
            }
            if (allOverlays.TryGetValue(id, out var ov)) {
                if (ov.IsShowing) {
                    S.Sig.ToggleOverlay.Send(ov, false);
                    UIShowing.Unset(UIShowingFlags.Overlay);
                }
            }
        }

        private void OnShowOverlay(string id) {
            if (allOverlays.TryGetValue(id, out var ov)) {
                S.Sig.ToggleOverlay.Send(ov, true);
                widget.ShowOverlaysSubmenu();
            }
        }

        private void LoadTools() {
            foreach (var tool in S.Toolbox.AvailableTools
                    .Where(t => !t.SkipMenu && !t.IsDevTool && t.ParentId == null)
                    .OrderBy(t => ToolMenuOrder(t))
                    .ThenBy(t => t.NameT)) {
                widget.AddToolButtonFor(S, tool);
            }
        }

        private int ToolMenuOrder(ITool tool) {
            if (Tunable.TryInt($"ToolOrder_{tool.Id}", out var order)) {
                return order;
            }
            return 1000;
        }

        private void LoadOverlays() {
            List<OverlayInfo> ovs = new();
            foreach (var sys in S.Sys.Active) {
                if (sys is IOverlayProvider op) {
                    foreach (var ov in op.Overlays) {
                        if (ov.Type == OverlayType.Visual) {
                            ovs.Add(ov);
                        }
                        allOverlays.Add(ov.Id, ov);
                    }
                }
            }
            visualOverlays.AddRange(ovs.OrderBy(o => o.Priority));
            widget.CreateOverlays(S, visualOverlays.ToArray());
        }

        private void ToggleWidget(int i) {
            var ov = Arrays.SafeGet(visualOverlays, i);
            if (ov != null) {
                // Keyboard toggle bypasses the button, so play its click sound here
                UISounds.PlayClick(UISounds.ClickType.High, true);
                S.Sig.ToggleOverlay.Send(ov, !ov.IsShowing);
            }
            if (UIShowing.Overlay) {
                widget.ShowOverlaysSubmenu();
            } else {
                widget.HideOverlaysSubmenu();
            }
        }

        private void EveryFrame() {
            // Ahead of the checks below: a swap that started before ship combat hid
            // the bar still has to land its new contents
            widget.TickSwap(GameTimer.unscaledDeltaTime);
            actionsHint.Update();
            if (!Ready.Input || !Ready.Game) { return; }
            if (UIShowing.ShipCombatMode) { return; }
            var k = Keyboard.current;
            if (k == null) { return; }
            if (k.shiftKey.isPressed || k.ctrlKey.isPressed) {
                return;
            }
            if (k.f1Key.wasPressedThisFrame) {
                ToggleWidget(0);
            } else if (k.f2Key.wasPressedThisFrame) {
                ToggleWidget(1);
            } else if (k.f3Key.wasPressedThisFrame) {
                ToggleWidget(2);
            } else if (k.f4Key.wasPressedThisFrame) {
                ToggleWidget(3);
            } else if (k.f5Key.wasPressedThisFrame) {
                ToggleWidget(4);
            } else if (k.f6Key.wasPressedThisFrame) {
                ToggleWidget(5);
            } else if (k.f7Key.wasPressedThisFrame) {
                // F7 is shortcut to record gifs with ScreenToGif
                #if !UNITY_EDITOR
                ToggleWidget(6);
                #endif
            } else if (k.f8Key.wasPressedThisFrame) {
                ToggleWidget(7);
            } else if (k.f9Key.wasPressedThisFrame) {
                ToggleWidget(8);
            #if PRODUCTION_BUILD
            } else if (k.f10Key.wasPressedThisFrame) {
                ToggleWidget(9);
            #endif
            }
        }
    }
}
