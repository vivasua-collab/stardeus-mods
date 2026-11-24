#if !PRODUCTION_BUILD
#define DEBUG_UI
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI {
    public static class UIUtils {
        public static int AdjustPriority0To5(int priority, int direction) {
            int low = 0;
            if (The.Bindings.IsEitherPressed(ActionType.CtrlModifier,
                    ActionType.ShiftModifier)) {
                direction *= 5;
                low = 1;
            }
            priority = Mathf.Clamp(priority + direction, low, 5);
            return priority;
        }
        public static int AdjustAmount(int amt, int direction, int min, int max) {
            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                direction *= 10;
            }
            if (The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                direction *= 25;
            }
            amt = Mathf.Clamp(amt + direction, min, max);
            return amt;
        }
        public static float AdjustAmount(float value, int direction, float min, float max) {
            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                direction *= 10;
            }
            if (The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                direction *= 25;
            }
            value = Mathf.Clamp(value + direction, min, max);
            return value;
        }

        public static Vector2 ScreenPos(RectTransform rt) {
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 worldCenter = (corners[0] + corners[2]) / 2;
            return worldCenter;
        }

        public static void Flatten(Transform root, Transform target) {
            if (root == target) {
                var children = new List<Transform>();
                foreach (Transform child in target) {
                    children.Add(child);
                }
                foreach (var child in children) {
                    Flatten(root, child);
                }
                return;
            }
            if (target.childCount == 0) return;

            // Process the first child
            Transform firstChild = target.GetChild(0);

            // Recursively flatten this child first if it has children
            if (firstChild.childCount > 0) {
                Flatten(root, firstChild);
            }

            // Move the child to the original parent (if it's not already)
            if (firstChild.parent != root) {
                firstChild.SetParent(root, true);
                firstChild.SetAsLastSibling(); // Maintain the original order
            }

            // Process the next child in the next function call
            Flatten(root, target);
        }

        public static void FixSliderRect(RectTransform rt) {
            if (float.IsNaN(rt.localPosition.x)) {
                rt.localPosition = Consts.V3Zero;
                // D.Err("Fixing NaN localPosition: {0}", D.GOPath(rt));
            }
            if (float.IsNaN(rt.sizeDelta.x)) {
                rt.sizeDelta = Consts.V2Zero;
                // D.Err("Fixing NaN sizeDelta: {0}", D.GOPath(rt));
            }
            if (float.IsNaN(rt.anchoredPosition.x)) {
                rt.anchoredPosition = Consts.V2Zero;
                // D.Err("Fixing NaN anchoredPosition: {0}", D.GOPath(rt));
            }
            if (rt.anchoredPosition.x != 0f) {
                // D.Warn("Fixing anchored pos: {0}", D.GOPath(rt));
                rt.anchoredPosition = Consts.V2Zero;
            }
        }

        public static bool IsChildOf(Transform parent, Transform potentialChild, int maxDepth = 10) {
            while (potentialChild != null && maxDepth-- > 0) {
                if (potentialChild.transform == parent) {
                    return true;
                }
                potentialChild = potentialChild.parent;
            }
            return false;
        }

        public static void FocusInput(TMP_InputField input) {
            D.Warn("focusing input: {0}", D.GOPath(input.transform));
            UIActivator.Deselect();
            UIActivator.Select(input.gameObject);
            if (input.text.Length > 0) {
                //filterField.ActivateInputField();
                // input.Select();
                input.selectionFocusPosition = input.text.Length;
                input.selectionAnchorPosition = 0;
            }
        }

        public static bool SetText(TMP_Text textField, string text,
                ref string lastText, bool disableOnBlank) {
            if (lastText == text) {
                if (text == null && !string.IsNullOrEmpty(textField.text)) {
                    if (disableOnBlank) {
                        SetTextFieldActive(textField, false);
                    } else {
                        textField.SetText(string.Empty);
                    }
                }
                return false;
            }
            textField.gameObject.name = $"Text_{text}";
            lastText = text;
            if (string.IsNullOrEmpty(text)) {
                if (disableOnBlank) {
                    SetTextFieldActive(textField, false);
                }
                textField.SetText(string.Empty);
            } else {
                textField.SetText(text);
                if (disableOnBlank) {
                    SetTextFieldActive(textField, true);
                }
            }
            return true;
        }

        public static void SetTextFieldActive(TMP_Text textField, bool on) {
            textField.SetActiveFast(on);
            if (textField.linkedTextComponent != null) {
                textField.linkedTextComponent.SetActiveFast(on);
            }
        }

        private static string lockText;

        public static string GetTooltipLockText() {
            if (lockText != null) {
                return lockText;
            }
            if (The.Bindings.HasBinding(ActionType.ShiftModifier)) {
                var glyph = The.Bindings.GetControlName(ActionType.ShiftModifier);
                lockText = T.PressToLockTooltip_V1.F(glyph);
                return lockText;
            } else {
                lockText = string.Empty;
            }
            return lockText;
        }

        public static void RebuildLayoutRecursive(RectTransform rect) {
            if (rect == null) return;
            if (!rect.gameObject.activeSelf) { return; }
            if (rect.childCount > 0) {
                foreach (Transform child in rect) {
                    // sometimes it can be something without rect transform inside
                    // example - hyperjump trail fx
                    if (child is RectTransform rektChild) {
                        RebuildLayoutRecursive(rektChild);
                    }
                }
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }

        // The wake-up, gift and ship class choices draw at canvas order 4, above the in-game
        // popup layer at 0. A nested canvas order is absolute, and the same popups also spawn
        // into the main menu (25000) and loading screen (29999) containers, so a fixed
        // override would bury them there. The lift only applies while such a choice is up.
        public static void LiftAbovePanelModal(Canvas canvas, int order) {
            if (canvas == null) { return; }
            canvas.overrideSorting = UIShowing.PanelModal;
            if (canvas.overrideSorting) {
                canvas.sortingOrder = order;
            }
        }

        public static void ToggleCG(CanvasGroup cg, bool on) {
            // D.Warn("Toggling CG: {0} -> {1}", on, D.GOPath(cg.transform));
            if (on) {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            } else {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
        }


        public static void AfterShowPopup(RectTransform rt, bool centerOfScreen = false,
                float distFromEdgeToMouse = 1f, bool randShift = true,
                bool compensateParentOffset = false, bool keepOffMouse = true) {

            // Shift the popup a bit randomly so multiple popups will not cover each other
            Vector2 pos;
            if (centerOfScreen || The.Bindings?.Mouse == null) {
                pos = new Vector2(Screen.width / 2, Screen.height / 2) - rt.rect.size * rt.localScale.x * new Vector2(0.5f, -0.5f);
                if (randShift) {
                    // Shift the popup a bit randomly so multiple popups will not cover each other
                    pos += Vectors.Abs(Rng.UInsideUnitCircle().normalized * 64);
                }
            } else {
                if (distFromEdgeToMouse == 0f) {
                    distFromEdgeToMouse = 1f;
                }
                pos = The.Bindings.Mouse.ScreenPosition * distFromEdgeToMouse;
            }
            RepositionOnScreenNearPoint(rt, pos, 32f, compensateParentOffset);

            rt.SetAsLastSibling();
            MoveIntoScreenBounds(rt, keepOffMouse: keepOffMouse);
        }

        public static void AfterShowPopupAttached(RectTransform rt, RectTransform attachTo,
                bool compensateParentOffset = false, bool keepOffMouse = true) {
            attachTo.GetWorldCorners(Caches.Corners);
            var bottomLeft = Caches.Corners[0];
            Vector2 pos = bottomLeft;
            RepositionOnScreenNearPoint(rt, pos, 0f, compensateParentOffset);
            rt.SetAsLastSibling();
            // topOffset: 0 — the popup is intentionally anchored to attachTo, so the
            // default top safe-area band must not shove it down and detach it from a
            // top-anchored widget (e.g. HyperspaceWidget at UI scale < 1.0).
            MoveIntoScreenBounds(rt, topOffset: 0, keepOffMouse: keepOffMouse);
        }

        // Puts the popup's bottom right corner on dockTopRight (screen space), so it sits
        // right-aligned above whatever it docks to
        public static void AfterShowPopupAbove(RectTransform rt, Vector3 dockTopRight) {
            rt.GetWorldCorners(Caches.Corners);
            var bottomRight = Caches.Corners[3];
            rt.position += dockTopRight - bottomRight;
            rt.SetAsLastSibling();
            MoveIntoScreenBounds(rt);
        }

        private static void RepositionOnScreenNearPoint(RectTransform rt, Vector2 screenPos,
                float offset, bool compensateParentOffset) {
            if (!compensateParentOffset || rt.parent is not RectTransform parent) {
                rt.RepositionOnScreenNearMouse(screenPos, 1f, keepAnchors: true, offset: offset);
                return;
            }

            var size = rt.sizeDelta * rt.localScale;
            var width = size.x;
            var height = size.y;

            float x;
            if (screenPos.x + width < RenderUtils.ScreenWidth) {
                x = screenPos.x + offset;
            } else {
                x = screenPos.x - offset - width;
            }
            if (x < 0f) {
                x = 0f;
            }

            float y;
            if (screenPos.y - height < offset) {
                y = screenPos.y + height;
            } else {
                y = screenPos.y - offset;
            }
            if (y > RenderUtils.ScreenHeight) {
                y = RenderUtils.ScreenHeight;
            }

            parent.GetWorldCorners(Caches.Corners);
            var parentTopLeft = Caches.Corners[1];
            rt.anchoredPosition = new Vector2(x - parentTopLeft.x, y - parentTopLeft.y);
        }

        public static Tooltip AddHover(Lifetime life, WBase owner, Action<bool> callback) {
            if (callback != null) {
                var tip = Tooltip.CreateFor(life, owner, owner.Rect, (string) null);
                tip.HoverFun = callback;
                return tip;
            }
            return null;
        }

        public static Tooltip AddTip(Lifetime life, WBase owner, string tip) {
            if (!string.IsNullOrEmpty(tip)) {
                return Tooltip.CreateFor(life, owner, owner.Rect, Texts.Beautify(tip));
            }
            return null;
        }
        public static Tooltip AddTipFunc(Lifetime life, WBase owner, Func<string> tip) {
            if (tip != null) {
                return Tooltip.CreateFor(life, owner, owner.Rect, tip);
            }
            return null;
        }

        /// <summary>
        /// Warning - don't use this with widgets that you spawn, only for
        /// preloaded contents.
        /// </summary>
        /// <param name="dropdown"></param>
        /// <param name="tooltips"></param>
        /// <param name="translate"></param>
        public static void AddTooltip(WDropdown dropdown, string[] tooltips,
                bool translate = false) {
            var itemTemplate = dropdown.Dropdown.itemText.transform.parent;
            if (!itemTemplate.TryGetComponent<DropdownTooltipItemHelper>(out _)) {
                itemTemplate.gameObject.AddComponent<DropdownTooltipItemHelper>();
            }
            if (!dropdown.TryGetComponent<DropdownTooltipHelper>(out var helper)) {
                helper = dropdown.gameObject.AddComponent<DropdownTooltipHelper>();
            }
            helper.ItemHoverCallback = (go, on) => {
                if (on) {
                    var idx = go.transform.GetSiblingIndex() - 1;
                    var tip = tooltips.SafeGet(idx);
                    if (string.IsNullOrEmpty(tip)) {
                        The.SysSig.HideTooltip.Send();
                    } else {
                        var tipT = translate ? tip.T() : tip;
                        The.SysSig.ShowTooltip.Send(tipT, true);
                    }
                } else {
                    The.SysSig.HideTooltip.Send();
                }
            };
            AddTooltipDynamic(dropdown.gameObject,
                () => {
                    var tip = tooltips.SafeGet(dropdown.Value);
                    if (translate) {
                        if (string.IsNullOrWhiteSpace(tip)) {
                            return null;
                        }
                        return tip.T();
                    } else {
                        return tip;
                    }
                });
        }
        public static void RemoveCodexTooltip(GameObject target) {
            if (target.TryGetComponent<UIHoverCallback>(out var hover)) {
                hover.ClearCallback();
                TMP_Text_Clickable.OnHoverChanged(target);
            }
        }
        public static void AddCodexTooltip(Lifetime lifetime, GameObject target, WBase caller,
                Def def, bool optional = true, float showDelay = 0f, bool isChartMode = false,
                bool anchorTop = true) {
            if (!target.TryGetComponent<UIHoverCallback>(out var hover)) {
                hover = target.AddComponent<UIHoverCallback>();
            }
            if (isChartMode) {
                optional = false;
            }
            var codexTooltip = CodexTooltip.CreateFor(caller, def, optional, showDelay, anchorTop);
            codexTooltip.IsChartMode = isChartMode;
            hover.SetCallback((on) => {
                if (on) {
                    A.S.UIQuery.ShowCodexTooltipFor.Ask(lifetime, codexTooltip);
                } else {
                    A.S.UIQuery.HideCodexTooltip.Ask();
                }
            }, null, clearOnDeactivate: true);
            TMP_Text_Clickable.OnHoverChanged(target);
        }

        public static void AddTooltipDynamic(GameObject target, Func<string> tooltipFunc) {
            if (!target.TryGetComponent<UIHoverCallback>(out var hover)) {
                hover = target.AddComponent<UIHoverCallback>();
            }
            hover.SetCallback((on) => {
                if (on) {
                    var tip = tooltipFunc();
                    if (string.IsNullOrEmpty(tip)) {
                        The.SysSig.HideTooltip.Send();
                    } else {
                        The.SysSig.ShowTooltip.Send(tip, true);
                    }
                } else {
                    The.SysSig.HideTooltip.Send();
                }
            }, null);
            TMP_Text_Clickable.OnHoverChanged(target);
        }

        public static void LoadSearch(WInput wSearch, Action<string> onUpdate,
                Action<string> onSubmit = null, string placeholder = null, string value = null) {
            placeholder ??= T.SearchPlaceholder;
            placeholder = Texts.TitleWithShortcutGlyph(placeholder, ActionType.QuickSearch);

            wSearch.Load(placeholder, value, onUpdate);
            wSearch.SetOnSubmit(onSubmit);
        }

        public static T GetWidget<T>(string id) where T : BaseWidget {
            var w = Registries.UIWidgets.Get(id);
            if (w == null) {
                D.Err("can't get widget by id: {0}", id);
            }
            if (w is T wt) {
                return wt;
            }
            D.Throw("Widget by id {0} is not of expected type {1}",
                id, typeof(T));
            return null;
        }

        public static bool HasOverlap(Rect rect, RectTransform rt) {
            Vector3[] corners = Caches.Corners;
            rt.GetWorldCorners(corners);

            Rect rtRect = new Rect(corners[0].x, corners[0].y,
                corners[2].x - corners[0].x,
                corners[2].y - corners[0].y);
            // D.Warn("Rect: {0} | {1}", rect, rtRect);

            return rect.Overlaps(rtRect);
        }
        public static string GetPlaceholderText(TMP_InputField input) {
            if (input?.placeholder == null) { return string.Empty; }
            var text = input.placeholder.GetComponentInChildren<TMP_Text>()?.text;
            if (!string.IsNullOrWhiteSpace(text)) {
                return text.Replace("...", "");
            } else {
                return string.Empty;
            }
        }

        public static void CloseFullScreenUI() {
            if (UIShowing.Starmap) {
                A.S.UIQuery.ShowStarmap.Ask(false);
            }
            if (UIShowing.Research) {
                A.S.UIQuery.ShowResearch.Ask(false);
            }

        }

        public static int NumActiveChildren(RectTransform container, int max = 0) {
            int count = 0;
            for (int i = container.childCount - 1; i >= 0; i--) {
                if (container.GetChild(i).gameObject.activeSelf) {
                    count++;
                    if (count == max) { break; }
                }
            }
            return count;
        }

        // keepOffMouse: a hover tooltip moved under the cursor takes the hover from what it
        // describes and flickers, so it stays put instead. Popups opened by a click must come
        // into view even if that puts them under the cursor.
        public static void MoveIntoScreenBounds(RectTransform rect, int topOffset = 48,
                bool keepOffMouse = true) {
            Vector3[] corners = Caches.Corners;
            rect.GetWorldCorners(corners);
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            for (int i = 0; i < 4; i++) {
                var corner = corners[i];
                if (corner.x < minX) { minX = corner.x; }
                if (corner.y < minY) { minY = corner.y; }
                if (corner.x > maxX) { maxX = corner.x; }
                if (corner.y > maxY) { maxY = corner.y; }
            }
            var screenRect = new Rect(0, 0, RenderUtils.ScreenWidth, RenderUtils.ScreenHeight - topOffset);
            // Per edge, not per corner: two corners share each edge, and summing them pushed
            // popups twice as far as they stuck out. A rect bigger than the screen keeps its
            // top left in view, where titles and first options are.
            Vector3 offset = Vector3.zero;
            if (minX < screenRect.xMin || maxX - minX > screenRect.width) {
                offset.x = screenRect.xMin - minX;
            } else if (maxX > screenRect.xMax) {
                offset.x = screenRect.xMax - maxX;
            }
            if (maxY > screenRect.yMax || maxY - minY > screenRect.height) {
                offset.y = screenRect.yMax - maxY;
            } else if (minY < screenRect.yMin) {
                offset.y = screenRect.yMin - minY;
            }
            if (offset == Vector3.zero) { return; }
            D.Warn("Moving rect into screen bounds by offset: {0}", offset);
            if (keepOffMouse) {
                var mousePos = MouseUtils.ScreenPos;
                if (mousePos.x >= minX + offset.x && mousePos.x <= maxX + offset.x
                        && mousePos.y >= minY + offset.y && mousePos.y <= maxY + offset.y) {
                    return;
                }
            }
            rect.position += offset;
        }

        public static void RebuildScroll(ScrollRect scroll, bool scrollToTop) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            if (scrollToTop) {
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        // How far the content is scrolled down, in pixels from the top. Pair with
        // RebuildScrollAt to keep the player looking at the same rows across a rebuild
        // that respawns the content: the normalized position alone would drift,
        // because it is relative to a content height the rebuild is about to change.
        public static float GetScrollOffset(ScrollRect scroll) {
            var max = scroll.content.rect.height - scroll.viewport.rect.height;
            if (max <= 0f) {
                return 0f;
            }
            return (1f - scroll.verticalNormalizedPosition) * max;
        }

        public static void RebuildScrollAt(ScrollRect scroll, float offsetFromTop) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            var max = scroll.content.rect.height - scroll.viewport.rect.height;
            scroll.verticalNormalizedPosition = max <= 0f
                ? 1f
                : 1f - Mathf.Clamp01(offsetFromTop / max);
        }

        public static void RefreshSelectedEntityUI() {
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            A.S.UIQuery.RebuildEntityActions.Ask();
        }

        public static bool IsAllTextSelected(TMP_InputField input) {
            if (!input.isFocused)
                return false;

            int length = input.text.Length;

            int min = Mathf.Min(input.selectionAnchorPosition, input.selectionFocusPosition);
            int max = Mathf.Max(input.selectionAnchorPosition, input.selectionFocusPosition);

            return min == 0 && max == length && length > 0;
        }

    }
}
