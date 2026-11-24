#if !PRODUCTION_BUILD
// #define DEBUG_TIPS
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class TooltipHandler {
        private const float MaxScreenHeightRatio = 0.9f;
        private readonly List<Tooltip> activeTooltips = new();
        private readonly MouseTooltip mouseTooltip;
        private Tooltip currentTip;
        public TooltipHandler(GameState s, MouseTooltip mouseTooltip) {
            this.mouseTooltip = mouseTooltip;
            s.UIQuery.AddTooltipData.SetResponder(OnAddTooltipData);
            // For debugging purposes
            s.UIQuery.GetActiveTooltips.SetResponder(() => activeTooltips);
            The.SysSig.ResolutionChanged.AddListener(UpdateTooltipMaxHeight);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            UpdateTooltipMaxHeight();
        }

        private void OnSettingChanged(string pref) {
            if (pref == Pref.GUIScale || pref == Pref.GUIAutoScale) {
                UpdateTooltipMaxHeight();
            }
        }

        private void UpdateTooltipMaxHeight() {
            int screenHeight = RenderUtils.ScreenHeight;
            if (screenHeight == 0) {
                screenHeight = Screen.height;
            }
            float uiScale = Video.GetCurrentUIScale(out _);
            int maxHeight = Mathf.FloorToInt(screenHeight * MaxScreenHeightRatio / uiScale);
            mouseTooltip.SetMaxHeight(maxHeight);
        }

        private bool OnAddTooltipData(Lifetime life, Tooltip data, bool add) {
            #if DEBUG_TIPS
            D.Warn("Adding tooltip: {0} => {1}", add, data);
            #endif
            if (add) {
                if (data == null) {
                    D.Err("Cannot add null tooltip data!");
                    return false;
                }
                life.Add(data);
                var idx = activeTooltips.IndexOf(data);
                if (idx >= 0) {
                    var activeTip = activeTooltips[idx];
                    if (!activeTip.IsDisposed && activeTip.Life?.IsDisposed == false) {
                        D.Err("Tooltip already added (IndexOf)! {0}", data);
                        return false;
                    }
                }
                // showing def tooltip will require a lifetime, so we'll pass it
                if (data.Def != null) {
                    data.Life = life;
                }
                activeTooltips.Add(data);
                #if DEBUG_TIPS
                D.Warn("Active tips: {0}", activeTooltips.Count);
                #endif
                return true;
            } else {
                if (data == null) {
                    return RemoveTemporalTooltips();
                } else {
                    if (!activeTooltips.Remove(data)) {
                        D.Err("Tooltip not added! {0}", data);
                        return false;
                    }
                    #if DEBUG_TIPS
                    D.Warn("Active tips: {0}", activeTooltips.Count);
                    #endif
                    return true;
                }
            }
        }

        private bool RemoveTemporalTooltips() {
            for (int i = activeTooltips.Count - 1; i >= 0; i--) {
                var t = activeTooltips[i];
                if ((t.Flags & TooltipFlags.Temporal) == TooltipFlags.Temporal) {
                    t.Flags |= TooltipFlags.Destroyed;
                    #if DEBUG_TIPS
                    D.Warn("Removing temporal tooltip: {0}", t);
                    #endif
                    activeTooltips.RemoveAt(i);
                }
            }
            #if DEBUG_TIPS
            D.Warn("Active tips: {0}", activeTooltips.Count);
            #endif
            return true;
        }

        public void Update() {
            Tooltip wantedTip = null;
            int activeTips = 0;
            for (int i = activeTooltips.Count - 1; i >= 0; i--) {
                var tip = activeTooltips[i];
                if ((tip.Flags & TooltipFlags.Destroyed) == TooltipFlags.Destroyed) {
                    // D.Warn("Removing destroyed tooltip: {0}", tip);
                    activeTooltips.RemoveAt(i);
                    continue;
                }
                activeTips++;
            }
            if (activeTips > 0) {
                var first = MouseUtils.FirstUnderCursor;
                wantedTip = GetWantedTip(first);
            }
            if (wantedTip != currentTip) {
                ShowTip(wantedTip);
            }
        }

        private Tooltip GetWantedTip(GameObject go) {
            if (go == null) { return null; }
            Tooltip tip = null;
            int minDepth = int.MaxValue;
            var tr = go.transform;

            for (int i = activeTooltips.Count - 1; i >= 0; i--) {
                var t = activeTooltips[i];
                var target = t.Target;
                // direct hit!
                if (target.gameObject == go) {
                    return t;
                }
                if (MiscUtils.IsParentOf(target, tr, out var depth, 6)) {
                    if (minDepth > depth) {
                        minDepth = depth;
                        tip = t;
                    }
                }
            }
            return tip;
        }

        private void ShowTip(Tooltip wantedTip) {
            #if DEBUG_TIPS
            D.Warn("Will show tooltip: [{2}] {0}=>{1}", currentTip, wantedTip, GameTimer.frameCount);
            #endif
            if (wantedTip == null) {
                if (currentTip != null) {
                    if (currentTip.Def != null) {
                        A.S.UIQuery.HideCodexTooltip.Ask();
                    } else {
                        The.SysSig.HideTooltip.Send();
                    }
                }
            } else {
                if (currentTip?.Def != null) {
                    A.S.UIQuery.HideCodexTooltip.Ask();
                }
                if (wantedTip.Def != null) {
                    var codexTooltip = CodexTooltip.CreateFor(wantedTip.Owner, wantedTip.Def);
                    A.S.UIQuery.ShowCodexTooltipFor.Ask(wantedTip.Life, codexTooltip);
                } else if (wantedTip.Flags.HasFlag(TooltipFlags.Function)) {
                    The.SysSig.ShowDynamicTooltip.Send(wantedTip.TooltipFun, true);
                } else if (!string.IsNullOrEmpty(wantedTip.Tip)) {
                    The.SysSig.ShowTooltip.Send(wantedTip.Tip, true);
                } else {
                    if (wantedTip.HoverFun == null) {
                        D.Err("Tooltip has no content to show, and no hover function! {0}",
                            wantedTip);
                    }
                }
            }
            // handle hover callbacks, if any
            currentTip?.HoverFun?.Invoke(false);
            wantedTip?.HoverFun?.Invoke(true);

            currentTip = wantedTip;
        }

        public void Cleanup() {
            The.SysSig.ResolutionChanged.RemoveListener(UpdateTooltipMaxHeight);
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
        }
    }
}
