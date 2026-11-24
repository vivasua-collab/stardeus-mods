using System;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class HyperspaceHandler : BaseWidgetHandler<HyperspaceWidget> {
        private readonly HyperjumpQueue q;
        private readonly HyperjumpProgress progress;
        private string lastOverlayIcon;
        private bool overlayStateApplied;

        public HyperspaceHandler(GameState s, HyperspaceWidget widget) : base(s, widget) {
            widget.Initialize(OnClickWidget, GetStateTip);
            q = S.Query.GetHyperjumpQueue.Ask();
            if (q == null) {
                D.Throw("HyperspaceHandler couldn't get HyperjumpQueue! Initialization sequence bug?");
            }
            progress = q.Progress;
            s.Clock.OnTick.AddListener(OnTick);
            s.Sig.HyperspaceStateChanged.AddListener(OnHSChanged);
            s.Sig.HyperjumpQueueChanged.AddListener(OnQueueChanged);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReload);
            OnQueueChanged(q, true);
        }

        private void OnQueueChanged(HyperjumpQueue q, bool needsRedraw) {
            widget.SetActiveFast(q.HasTarget);
            UpdateOverlay();
        }

        private void UpdateOverlay() {
            if (!q.HasTarget) { return; }
            string icon = null;
            Color color = default;
            if (q.IsPaused) {
                icon = IconId.WPause;
                color = The.UITheme.GetColor("Accent", this);
            } else {
                if (!A.IsInHyperspace) {
                    if (q.IsRecharging) {
                        icon = IconId.WRetry;
                        color = The.UITheme.GetColor("Blue", this);
                    } else if (!S.Sys.ShipNav.HasEngineForLayer(q.QueueLayer, out _)) {
                        icon = IconId.CWarning;
                        color = Consts.ColorWhite;
                    } else {
                        icon = IconId.CTime;
                        color = Consts.ColorWhite;
                    }
                // } else {
                //     if (S.Situation.GridDeficit > 0f) {
                //         icon = IconId.CNoEnergy;
                //         color = Consts.ColorWhite;
                //     }
                }
            }
            if (overlayStateApplied && icon == lastOverlayIcon) {
                return;
            }
            overlayStateApplied = true;
            lastOverlayIcon = icon;
            if (icon != null) {
                widget.ShowOverlay(icon, color);
            } else {
                widget.HideOverlay();
            }
        }

        private readonly StringBuilder tipSB = new();

        private string GetStateTip() {
            tipSB.Clear();
            tipSB.Append(T.NounHyperjump);
            tipSB.Append(": ");
            if (q.IsPaused) {
                tipSB.Append(T.AdjPaused);
                if (q.PausedUntil > 0 && q.PausedUntil > G.Ticks) {
                    tipSB.Append(" ");
                    tipSB.Append(Texts.GreyParentheses(Units.TicksToTime(q.PausedUntil - G.Ticks)));
                }
            } else if (A.IsInHyperspace) {
                tipSB.Append(T.GoalStateInprogress);
                if (S.Situation.GridDeficit > 0f) {
                    tipSB.Append(" (");
                    tipSB.Append(Texts.Red(T.ElectricityDeficit));
                    tipSB.Append(")");
                }
            } else if (q.IsRecharging) {
                q.GetChargeState(out var ready, out var wanted);
                tipSB.Append(T.ActRecharge);
                tipSB.Append(" ");
                tipSB.Append(Texts.GreyParentheses(Units.XOutOfY(ready, wanted)));
            } else if (S.Sys.ShipNav.CanEnterHyperspaceNow(false, out var reason, q.QueueLayer)) {
                tipSB.Append(T.WaitingForOperator);
            } else {
                tipSB.Append(Texts.Accented(T.Waiting));
                tipSB.Append("\n");
                tipSB.Append(reason);
            }

            var next = q.GetNextJumpTarget();
            if (next != null) {
                tipSB.Append(GlyphMap.BRHR);
                tipSB.Append(next == q.WarpTarget ? T.VerbWarp : T.NextStop);
                tipSB.Append(": ");
                tipSB.Append(next.SO.ParentPath());

                if (q.Queue.Count > 1) {
                    var wp = q.Queue.First.Next;
                    while (wp != null) {
                        tipSB.Append("\n");
                        tipSB.Append(GlyphMap.ArrowRight);
                        tipSB.Append(" ");
                        tipSB.Append(wp.Value.SO.ParentPath());
                        wp = wp.Next;
                    }
                    tipSB.Append(GlyphMap.BRHR);
                    tipSB.Append(T.VisitWaypoints);
                    tipSB.Append(": ");
                    tipSB.Append(q.VisitWaypoints ? T.Yes : T.No);
                }
            }

            return tipSB.ToString();
        }

        private void OnClickWidget() {


            var qc = QuickChoice.Create(null);
            var tog = new QuickChoice.Toggle() {
                OnToggle = (v) => {
                    q.VisitWaypoints.Set(v);
                },
                Value = q.VisitWaypoints,
                Tip = T.VisitWaypointsTip,
            };
            qc.AddToggle(T.VisitWaypoints, tog);

            if (!A.IsInHyperspace) {
                if (q.IsPaused) {
                    qc.AddButton(T.HyperjumpResume, () => {
                        q.SetPaused(false);
                        UpdateOverlay();
                    });
                } else {
                    qc.AddButton(T.HyperjumpPause, () => {
                        q.SetPaused(true);
                        UpdateOverlay();
                    });
                }
                qc.AddButton($"{GlyphMap.SpriteCross} {T.HyperjumpCancel}", () => {
                    if (q.Progress.IsMoving) {
                        UISounds.PlayActionDenied();
                        return;
                    }
                    q.CancelAll();
                });
            }
            qc.Widget = widget.Rect;
            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void OnHSChanged(HyperspaceState state) {
            widget.SetProgress(progress.Progress);
            widget.SetActiveFast(q.HasTarget);
            if (state.IsInHyperspace) {
                UpdateOverlay();
                var dens = 7f;
                var speed = 0.4f;
                if (progress.IsMovingInUniverseLayer) {
                    dens *= 2f;
                    speed *= 2f;
                }
                widget.SetStripes(dens, speed);
            } else {
                widget.SetStripes(0f, 1f);
                UpdateOverlay();
            }
        }

        public void OnTick(long ticks) {
            if (progress.IsMoving) {
                widget.SetProgress(progress.Progress);
                UpdateOverlay();
            }
        }

        private void OnThemeReload() {
            widget.SetLooks();
        }

        public void Cleanup() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReload);
        }
    }
}
