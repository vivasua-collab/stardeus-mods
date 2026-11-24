using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class QuickChoiceWidget : BaseWidget {
        [SerializeField] private WGroup container;
        [SerializeField] private WButtonSubmenu submenu;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private ScrollRect scroll;
        // Same top band as UIUtils.MoveIntoScreenBounds keeps clear
        private const int ScreenTopReserve = 48;
        private const float ScrollbarSpace = 16f;
        private int boundsTopOffset = ScreenTopReserve;
        private Vector2 autoCloseCenter;
        private Lifetime lifetime;
        private bool hasInput;
        private bool ctxFullScreenWidget;
        private bool ctxTutorial;
        private readonly List<WBase> optionWidgets = new();
        private QuickChoice currentChoice;
        public void Initialize(GameState s, Action onCollapse) {
            Register(UIWidgetId.QuickChoice, this);
            submenu.DisableSubmenu();
            submenu.OnCollapse = onCollapse;
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
            optionWidgets.Clear();
            currentChoice?.SpawnedWidgets?.Clear();
            currentChoice = null;
        }

        private WBase AddOption(int idx, string name, QuickChoice.OptionType type, object actionObj) {
            switch (type) {
                case QuickChoice.OptionType.Button: {
                    return AddButtonOption(idx, name, actionObj);
                }
                case QuickChoice.OptionType.IconButton: {
                    return AddIconButtonOption(idx, name, actionObj);
                }
                case QuickChoice.OptionType.Toggle: {
                    return AddToggleOption(idx, name, actionObj);
                }
                case QuickChoice.OptionType.DynamicText: {
                    return AddDynamicText(idx, name, actionObj);
                }
                case QuickChoice.OptionType.DynamicTextIcon: {
                    return AddDynamicTextIcon(idx, name, actionObj);
                }
                case QuickChoice.OptionType.Progress: {
                    return AddProgress(idx, name, actionObj);
                }
                case QuickChoice.OptionType.Input: {
                    return AddInputOption(idx, name, actionObj);
                }
                case QuickChoice.OptionType.Slider: {
                    return AddSliderOption(idx, name, actionObj);
                }
                case QuickChoice.OptionType.Label: {
                    var w = UISpawn.Widget<WText>(lifetime, UIPrefabId.WButtonPopoutLabel, container.Container, idx);
                    optionWidgets.Add(w);
                    w.Load(name);
                    return w;
                }
                default: {
                    D.Err("Unhandled option type: {0}", type);
                    return null;
                }
            }
        }
        private WBase AddDynamicTextIcon(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.DynamicTextIcon qcDynText) {
                D.Err("DynamicText quick choice actionObj is not QuickChoice.DynamicTextIcon: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WIconText>(lifetime, UIPrefabId.WButtonPopoutIconText, container.Container, idx);
            w.Load(qcDynText.Icon, name);
            if (qcDynText.Tint.HasValue) {
                w.SetTint(qcDynText.Tint.Value);
            }
            qcDynText.TextB.Bind(lifetime, t => {
                w.SetText(t);
                ResizeWidget(currentChoice);
            });
            if (qcDynText.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcDynText.Tip);
            } else if (qcDynText.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcDynText.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }
        private WBase AddDynamicText(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.DynamicText qcDynText) {
                D.Err("DynamicText quick choice actionObj is not QuickChoice.DynamicText: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WText>(lifetime, UIPrefabId.WButtonPopoutText, container.Container, idx);
            w.Load(name);
            qcDynText.TextB?.Bind(lifetime, t => {
                w.SetText(t);
                ResizeWidget(currentChoice);
            });
            if (qcDynText.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcDynText.Tip);
            } else if (qcDynText.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcDynText.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }
        private WBase AddProgress(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.Progress qcProgress) {
                D.Err("Progress quick choice actionObj is not QuickChoice.Progress: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WProgressText>(lifetime, UIPrefabId.WButtonPopoutProgress, container.Container, idx);
            w.Load(name, 0f);
            qcProgress.ProgressB.Bind(lifetime, p => {
                w.SetPercent(p, false);
            });
            qcProgress.TextB.Bind(lifetime, t => {
                w.SetText(t);
                ResizeWidget(currentChoice);
            });
            if (qcProgress.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcProgress.Tip);
            } else if (qcProgress.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcProgress.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }
        private WBase AddToggleOption(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.Toggle qcToggle) {
                D.Err("Toggle quick choice actionObj is not QuickChoice.Toggle: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WButtonPopoutToggle, container.Container, idx);
            w.Load(name, qcToggle.Value, qcToggle.OnToggle);
            if (qcToggle.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcToggle.Tip);
            } else if (qcToggle.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcToggle.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }

        private WBase AddInputOption(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.Input qcInput) {
                D.Err("Input quick choice actionObj is not QuickChoice.Input: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            hasInput = true;
            var w = UISpawn.Widget<WInputWithLabel>(lifetime, UIPrefabId.WButtonPopoutInput, container.Container, idx);
            w.Label.Load(name);
            w.Input.Load(qcInput.Placeholder, qcInput.Value, qcInput.OnChange);
            w.Input.SetOnSubmit(qcInput.OnSubmit);
            if (qcInput.AutoFocus) {
                D.Warn("Will focus {0}", name);
                Coroutines.OnNextFrame(() => {
                    w.Input.Focus();
                });
            }
            if (qcInput.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcInput.Tip);
            } else if (qcInput.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcInput.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }

        private WBase AddSliderOption(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.Slider qcSlider) {
                D.Err("Slider quick choice actionObj is not QuickChoice.Slider: {0} -> {1}", name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WButtonPopoutIconTextSlider, container.Container, idx);
            w.Item.Load(qcSlider.Icon, name, qcSlider.ValueToText(qcSlider.Value));
            if (qcSlider.IconTint.HasValue) {
                w.Item.SetTint(qcSlider.IconTint.Value);
            }
            w.Slider.Load(qcSlider.Value, v => {
                w.Item.SetText2(qcSlider.ValueToText(v));
                qcSlider.OnValueChanged?.Invoke(v);
            }, qcSlider.Min, qcSlider.Max);
            if (qcSlider.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcSlider.Tip);
            } else if (qcSlider.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcSlider.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }

        private WBase AddIconButtonOption(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.IconButton qcBtn) {
                D.Err("Input quick choice actionObj is not QuickChoice.IconButton: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WButtonIconText>(lifetime, UIPrefabId.WButtonPopoutIcon, container.Container, idx);
            w.Load(null, name, () => {
                // disabled action
                if (actionObj == null) {
                    UISounds.PlayActionDenied();
                    return;
                }
                // on click with validation
                if (qcBtn.OnClickValidate != null) {
                    if (qcBtn.OnClickValidate.Invoke()) {
                        if (qcBtn.AutoClose) {
                            Cancel();
                        }
                        return;
                    }
                    UISounds.PlayActionDenied();
                    return;
                }
                if (qcBtn.OnClick == null) {
                    UISounds.PlayActionDenied();
                    return;
                }
                // regular on click
                qcBtn.OnClick.Invoke();
                if (qcBtn.AutoClose) {
                    Cancel();
                }
            });
            w.SetSprite(qcBtn.Icon);
            if (qcBtn.Tint.HasValue) {
                w.SetTint(qcBtn.Tint.Value);
            }
            if (qcBtn.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcBtn.Tip);
            } else if (qcBtn.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcBtn.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }
        private WBase AddButtonOption(int idx, string name, object actionObj) {
            if (actionObj is not QuickChoice.Button qcBtn) {
                D.Err("Input quick choice actionObj is not QuickChoice.Button: {0} -> {1}",
                    name, actionObj);
                return null;
            }
            var w = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WButtonPopoutItem, container.Container, idx);
            w.Load(name, () => {
                // disabled action
                if (actionObj == null) {
                    UISounds.PlayActionDenied();
                    return;
                }
                // on click with validation
                if (qcBtn.OnClickValidate != null) {
                    if (qcBtn.OnClickValidate.Invoke()) {
                        if (qcBtn.AutoClose) {
                            Cancel();
                        }
                        return;
                    }
                    UISounds.PlayActionDenied();
                    return;
                }
                if (qcBtn.OnClick == null) {
                    UISounds.PlayActionDenied();
                    return;
                }
                // regular on click
                qcBtn.OnClick.Invoke();
                if (qcBtn.AutoClose) {
                    Cancel();
                }
            });
            if (qcBtn.Tip != null) {
                UIUtils.AddTip(lifetime, w, qcBtn.Tip);
            } else if (qcBtn.TipFunc != null) {
                UIUtils.AddTipFunc(lifetime, w, qcBtn.TipFunc);
            }
            optionWidgets.Add(w);
            return w;
        }

        public bool UpdateWidget() {
            if (!submenu.IsExpanded) { return false; }
            if (ctxFullScreenWidget != UIShowing.FullScreenWidget || ctxTutorial != UIShowing.Tutorial) {
                Cancel();
                return false;
            }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                return true;
            }
            #endif
            var mousePos = The.Bindings.Mouse.ScreenPositionUnclamped;
            // The visible area: Content runs past it once the list scrolls
            var corners = Caches.Corners;
            viewport.GetWorldCorners(corners);
            // corners: 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right
            float left = corners[0].x;
            float right = corners[2].x;
            float bottom = corners[0].y;
            float top = corners[2].y;
            bool inside = mousePos.x >= left && mousePos.x <= right &&
                          mousePos.y >= bottom && mousePos.y <= top;
            if (inside) {
                var center = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
                autoCloseCenter = center;
                cg.alpha = 1f;
                return true;
            }
            var distToCenter = Vectors.Distance(mousePos, autoCloseCenter);
            cg.alpha = Mathf.InverseLerp(1f, 0f, (distToCenter - 100f) / 300f);
            if (cg.alpha < 0.01f) {
                Cancel();
                return false;
            }
            return true;
        }

        public bool Show(QuickChoice choice) {
            var rebuild = UIShowing.QuickChoice;
            // An in-place refresh (same choice rebuilt after a click) keeps its scroll position
            var keepScroll = rebuild && currentChoice != null && currentChoice.Label == choice.Label;
            var scrollPos = scroll.content.anchoredPosition;
            // kill any tooltips because they may be in the way
            The.SysSig.HideTooltip.Send();
            Cleanup();
            currentChoice = choice;
            lifetime = Lifetime.Create("QuickChoice");
            var cnt = choice.Count;
            if (cnt == 0) {
                D.Err("Trying to show choice with 0 options! {0}", choice.Label);
                return false;
            }
            int idx = 0;
            D.Warn("Showing quick choice: {0}", choice);
            if (choice.Label != null) {
                var w = AddOption(idx++, choice.Label, QuickChoice.OptionType.Label, null);
                choice.SpawnedWidgets?.Add(w);
            }
            hasInput = false;
            for (int i = 0; i < cnt; i++) {
                var opt = choice.Options[i];
                var w = AddOption(idx++, opt.name, opt.type, opt.action);
                choice.SpawnedWidgets?.Add(w);
            }
            if (!rebuild) {
                // Hanging below a widget may cover the top band, like AfterShowPopupAttached
                boundsTopOffset = choice.Widget != null && !choice.AttachAbove ? 0 : ScreenTopReserve;
            }
            // Active first, so ResizeWidget can measure the laid out rows
            submenu.ShowSubmenu();
            ResizeWidget(choice);
            if (keepScroll) {
                scroll.content.anchoredPosition = scrollPos;
            } else {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1f;
            }
            // Opened by a click, so the choice comes into view even if that puts it under
            // the cursor
            if (!rebuild) {
                if (choice.Widget != null && choice.AttachAbove) {
                    choice.Widget.GetWorldCorners(Caches.Corners);
                    var widgetTopLeft = Caches.Corners[1];
                    viewport.GetWorldCorners(Caches.Corners);
                    Rect.position += widgetTopLeft - Caches.Corners[0];
                    Rect.SetAsLastSibling();
                    UIUtils.MoveIntoScreenBounds(Rect, boundsTopOffset, keepOffMouse: false);
                } else if (choice.Widget != null) {
                    D.Warn("Showing quick choice for widget: {0}", D.GOPath(choice.Widget));
                    UIUtils.AfterShowPopupAttached(Rect, choice.Widget, compensateParentOffset: true,
                        keepOffMouse: false);
                } else {
                    UIUtils.AfterShowPopup(Rect, false, 0f, compensateParentOffset: true,
                        keepOffMouse: false);
                }
                UIShowing.Set(UIShowingFlags.QuickChoice);
                ctxFullScreenWidget = UIShowing.FullScreenWidget;
                ctxTutorial = UIShowing.Tutorial;
            } else {
                // A choice opened from a row of this one (or rebuilt after a click) stays in
                // its place, and only moves as far as it needs to fit
                UIUtils.MoveIntoScreenBounds(Rect, boundsTopOffset, keepOffMouse: false);
            }
            autoCloseCenter = The.Bindings.Mouse.ScreenPosition;
            return true;
        }

        private void ResizeWidget(QuickChoice choice) {
            if (choice == null) { return; }
            var width = 60f;
            var padding = 16f;
            foreach (var opt in optionWidgets) {
                // label
                if (opt is WText wText) {
                    var textWidth = wText.Text.preferredWidth;
                    width = Mathf.Max(width, textWidth + padding);
                    // button
                } else if (opt is WButtonText wBtn) {
                    var textWidth = wBtn.Text.preferredWidth;
                    width = Mathf.Max(width, textWidth + padding);
                    // icon button
                } else if (opt is WButtonIconText wIconBtn) {
                    var textWidth = wIconBtn.Text.preferredWidth;
                    // icon is 28x28 plus 8 padding, then text
                    width = Mathf.Max(width, textWidth + padding);
                    // toggle
                } else if (opt is WToggleText wToggle) {
                    var textWidth = wToggle.Text.preferredWidth;
                    width = Mathf.Max(width, textWidth + padding);
                    // dynamic text
                } else if (opt is WText wDynText) {
                    var textWidth = wDynText.Text.preferredWidth;
                    width = Mathf.Max(width, textWidth + padding);
                    // dyn text icon
                } else if (opt is WIconText wIcnText) {
                    var textWidth = wIcnText.Text.preferredWidth;
                    width = Mathf.Max(width, textWidth + padding);
                    // progress
                } else if (opt is WProgressWithLabel wProg) {
                    var labelWidth = wProg.Label.Text.preferredWidth;
                    var progressWidth = wProg.Progress.Text.preferredWidth;
                    width = Mathf.Max(width, labelWidth + progressWidth + padding);
                    // input
                } else if (opt is WInputWithLabel wInput) {
                    var labelWidth = wInput.Label.Text.preferredWidth;
                    width = Mathf.Max(width, labelWidth + 200f + padding);
                    // slider
                } else if (opt is WSliderWithIconText2 wSlider) {
                    var labelWidth = wSlider.Item.Text.preferredWidth + wSlider.Item.Text2.preferredWidth;
                    width = Mathf.Max(width, labelWidth + 150f + padding);
                }
            }
            // Lay the rows out at this width and measure them. Taller than the screen, the
            // body scrolls instead
            var content = container.Container;
            viewport.sizeDelta = new Vector2(width, viewport.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var contentH = content.rect.height;
            var scale = Rect.lossyScale.y > 0f ? Rect.lossyScale.y : 1f;
            var maxH = (RenderUtils.ScreenHeight - boundsTopOffset) / scale;
            var scrolls = contentH > maxH + 0.5f;
            if (scrolls) {
                width += ScrollbarSpace;
            }
            scroll.vertical = scrolls;
            Rect.sizeDelta = viewport.sizeDelta = new Vector2(width, scrolls ? maxH : contentH);
        }

        public bool Cancel() {
            if (submenu.IsExpanded) {
                if (hasInput && !Ready.TextInputFocus) {
                    Ready.SetTextInputFocus(true, null, "Close QuickChoice");
                }
                submenu.HideSubmenu();
                Cleanup();
                return true;
            }
            Cleanup();
            return false;
        }
    }
}
