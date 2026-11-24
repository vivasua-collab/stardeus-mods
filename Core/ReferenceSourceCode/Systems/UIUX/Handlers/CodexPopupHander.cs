using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class CodexPopupHandler {
        private readonly Dictionary<Def, WTooltipCodex> spawnedCodexTips = new();
        private WTooltipCodex attachedCodexTip;
        private CodexTooltip wantsCodexTooltip;
        private float codexTipDelay;
        private WTooltipCodex prefab;
        private readonly GameState S;
        public CodexPopupHandler(GameState s) {
            S = s;
            s.UIQuery.ShowCodexTooltipFor.SetResponder(ShowCodexTooltipFor);
            s.UIQuery.HideCodexTooltip.SetResponder(HideCodexTooltip);
            s.UIQuery.RegisterCodexTooltipFor.SetResponder(RegCodexTipFor);
            s.UIQuery.TryCloseAllCodexPopups.SetResponder(TryCloseAll);
            s.Sig.EveryFrame.AddListener(EveryFrame);
            prefab = ResourceLibrary.FindPrefab<WTooltipCodex>(UIPrefabId.WTooltipCodex);
        }

        private bool TryCloseAll() {
            if (UIShowing.Research) { return false; }
            if (spawnedCodexTips.Count == 0) {
                return false;
            }
            var arr = spawnedCodexTips.Values.ToArray();
            for (int i = arr.Length - 1; i >= 0; i--) {
                arr[i].Close();
            }
            if (spawnedCodexTips.Count > 0) {
                D.Err("Not all codex tips were removed! Remaining: {0}",
                    string.Join(", ", spawnedCodexTips.Keys));
                spawnedCodexTips.Clear();
            }
            attachedCodexTip = null;
            wantsCodexTooltip = null;

            return true;
        }

        private bool RegCodexTipFor(Def def, WTooltipCodex widget, bool added) {
            if (added) {
                if (spawnedCodexTips.TryGetValue(def, out var existing)) {
                    if (existing == widget) {
                        return true; // already registered, no-op
                    }
                    D.Warn("Closing existing widget! {0} ({1})", def, widget.name);
                    if (existing == attachedCodexTip) {
                        attachedCodexTip = null;
                    }
                    existing.Close();
                }
                spawnedCodexTips[def] = widget;
            } else {
                if (spawnedCodexTips.TryGetValue(def, out var existing) && existing == widget) {
                    spawnedCodexTips.Remove(def);
                } else {
                    D.Err("Couldn't remove widget for {0}", def);
                }
            }
            return true;
        }

        private bool HideCodexTooltip() {
            codexTipDelay = 0f;
            wantsCodexTooltip = null;
            return true;
        }

        private bool DoHideCodexTooltip() {
            if (attachedCodexTip != null) {
                if (!attachedCodexTip.IsLocked) {
                    attachedCodexTip.Close();
                }
                attachedCodexTip = null;
                return true;
            }
            return false;
        }

        private bool ShowCodexTooltipFor(Lifetime lifetime, CodexTooltip codexTooltip) {
            if (codexTooltip == null) {
                D.Err("Trying to show null codex tooltip request");
                return false;
            }
            var invoker = codexTooltip.Invoker;
            var def = codexTooltip.Def;
            if (invoker == null || def == null) {
                D.Err("Trying to show codex tooltip with invalid data: {0}, {1}",
                    invoker, def);
                return false;
            }
            if (invoker.IsDisposed) {
                D.Err("Trying to show codex tooltip for disposed invoker: {0}, {1}",
                    def, D.GOPath(invoker.Rect));
                return false;
            }
            if (lifetime?.IsDisposed == true) {
                D.Err("Trying to show codex tooltip for disposed lifetime: {0}, {1}",
                    def, D.GOPath(invoker.Rect));
                return false;
            }
            codexTooltip.Life = lifetime;
            wantsCodexTooltip = codexTooltip;
            codexTipDelay = codexTooltip.ShowDelay > 0f
                ? codexTooltip.ShowDelay
                : Tunables.UICodexPopupDelaySeconds;
            return true;
        }

        public void EveryFrame() {
            bool isShiftPressed = false;
            if (attachedCodexTip != null) {
                isShiftPressed = The.Bindings.IsPressed(Input.ActionType.ShiftModifier);
                if (isShiftPressed) {
                    var currentTip = attachedCodexTip;
                    attachedCodexTip.SetLocked((tip) => {
                        if (currentTip == tip) {
                            attachedCodexTip = null;
                        }
                    });
                }
            }
            if (wantsCodexTooltip?.IsDisposed == true) {
                wantsCodexTooltip = null;
            }
            if (wantsCodexTooltip != null) {
                if (attachedCodexTip != null) {
                    if (attachedCodexTip.Def == wantsCodexTooltip.Def) {
                        if (attachedCodexTip.IsChartMode != wantsCodexTooltip.IsChartMode) {
                            if (!attachedCodexTip.IsLocked) {
                                attachedCodexTip.SetChartMode(wantsCodexTooltip.IsChartMode);
                            }
                        }
                        return;
                    } else {
                        // we want a tip for a different item!
                        DoHideCodexTooltip();
                    }
                }
                codexTipDelay -= GameTimer.unscaledDeltaTime;
                if (codexTipDelay > 0f) {
                    return;
                }
                DoShowCodexTooltipFor(wantsCodexTooltip);
            } else {
                if (isShiftPressed) {
                    return;
                }
                DoHideCodexTooltip();
            }
        }

        private void DoShowCodexTooltipFor(CodexTooltip codexTooltip) {
            var def = codexTooltip.Def;
            var invoker = codexTooltip.Invoker;

            if (codexTooltip.Optional &&
                    S.Prefs.GetBool(Pref.DisableCodexPopups, PrefH.DisableCodexPopups, false)) {
                return;
            }

            if (!spawnedCodexTips.TryGetValue(def, out var w)) {
                D.Warn("Spawning codex tooltip for {0}", def);
                w = PrefabPool.Spawn(prefab, UIPoolRoot.PopupContainer, preInstantiate: true);
                w.Load(S, def, false);
            } else if (w.IsLocked) {
                return; // a locked popup already covers this def, skip the attached tooltip
            }
            w.SetChartMode(codexTooltip.IsChartMode);
            attachedCodexTip = w;
            var uiScale = Video.GetCurrentUIScale(out _);

            RepositionPopup(invoker, w, uiScale, codexTooltip.AnchorTop);

            attachedCodexTip.Rect.SetAsLastSibling();
        }

        private static void RepositionPopup(WBase invoker, WTooltipCodex w, float uiScale, bool anchorTop) {
            var rt = w.Rect;
            const float padding = 10f;
            var margin = 200f * uiScale;

            var ir = invoker.Rect;
            var pos = ir.GetScreenCenter();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                UIPoolRoot.Container, pos,
                null, out var locPoint);
            int corner = rt.GetOnScreenRepositionCorner(locPoint, pos, rightOffset: margin);
            // D.Warn("Corner: {0}", corner);

            bool? ft = null; // force top
            bool? fl = null; // force left
            switch (corner) {
                case 0: ft = false; fl = true;  pos = anchorTop ? ir.GetScreenBottomRight() : ir.GetScreenTopRight();    break;
                case 1: ft = true;  fl = true;  pos = anchorTop ? ir.GetScreenTopRight()    : ir.GetScreenBottomRight(); break;
                case 2: ft = true;  fl = false; pos = anchorTop ? ir.GetScreenTopLeft()     : ir.GetScreenBottomLeft();  break;
                case 3: ft = false; fl = false; pos = anchorTop ? ir.GetScreenBottomLeft()  : ir.GetScreenTopLeft();     break;
            }
            pos += new Vector2(fl == true ? padding : -padding,
                anchorTop ? 0f : (ft == true ? -padding : padding)) * uiScale;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                UIPoolRoot.Container, pos,
                null, out locPoint);

            rt.RepositionToBeOnScreen(locPoint, pos, forceLeft: fl, forceTop: ft);
        }
    }
}
