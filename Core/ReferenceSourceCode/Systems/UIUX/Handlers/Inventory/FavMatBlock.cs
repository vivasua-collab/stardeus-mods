using System;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class FavMatBlock : IDisposable {
        private readonly MatType mat;

        private WButtonIconText2 widgetWide;
        private WButtonIconText widget;
        private bool isWide;
        private int lastCount;
        private string lastCountTxt;
        private bool isShowing => widget?.IsDisposed == false || widgetWide?.IsDisposed == false;

        public FavMatBlock(MatType mat) {
            this.mat = mat;
            lastCount = 0;
            lastCountTxt = Texts.Red("0");
        }

        public void SpawnWidget(Lifetime life, bool wide, WGroup container, int idx) {
            isWide = wide;
            if (wide) {
                widgetWide = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WFavMatWide, container.Container, idx);
                widgetWide.Load(mat.IconId, mat.NameT, lastCountTxt, () => ShowToggleFavQuickChoice(mat, widgetWide.Rect));
                widgetWide.SetTint(mat.IconTint);
                UIUtils.AddCodexTooltip(life, widgetWide.gameObject, widgetWide, mat.Def, optional: false, isChartMode: true);
                // Tooltip.CreateFor(life, widgetWide, widgetWide.Rect, mat.Def);
            } else {
                widget = UISpawn.Widget<WButtonIconText>(life, UIPrefabId.WFavMat, container.Container, idx);
                widget.Load(mat.IconId, lastCountTxt, () => ShowToggleFavQuickChoice(mat, widget.Rect));
                widget.SetTint(mat.IconTint);
                UIUtils.AddCodexTooltip(life, widget.gameObject, container, mat.Def, optional: false, isChartMode: true);
            }
        }

        public static void ShowToggleFavQuickChoice(MatType mat, RectTransform rect) {
            var S = A.S;
            var fav = S.Query.IsFavMat.Ask(mat);
            var qc = QuickChoice.Create(mat.NameT);
            qc.Widget = rect;
            qc.AddToggle(T.ShowInQuickDrawer, new QuickChoice.Toggle() {
                OnToggle = (val) => {
                    S.Sig.SetFavMat.Send(mat, val, 0);
                    // rebuild quick choice
                    ShowToggleFavQuickChoice(mat, rect);
                },
                Value = fav.isFav,
            });
            if (fav.isFav) {
                qc.AddToggle(T.ShowOnlyWhenLess, new QuickChoice.Toggle() {
                    OnToggle = (val) => {
                        if (val) {
                            S.Sig.SetFavMat.Send(mat, true, 100);
                        } else {
                            S.Sig.SetFavMat.Send(mat, true, 0);
                        }
                        // rebuild quick choice
                        ShowToggleFavQuickChoice(mat, rect);
                    },
                    Value = S.Query.IsFavMat.Ask(mat).countThreshold > 0,
                });
                if (fav.countThreshold > 0) {
                    qc.AddInput(T.NounThreshold, new QuickChoice.Input() {
                        OnChange = (val) => ToggleFavWithThresh(mat, val, false),
                        OnSubmit = (val) => ToggleFavWithThresh(mat, val, true),
                        Placeholder = T.NounThreshold,
                        AutoFocus = true,
                        Tip = T.MatThresholdQuickDrawerTip,
                        Value = Units.Num(S.Query.IsFavMat.Ask(mat).countThreshold),
                    });
                }
            }

            qc.AddButton(T.FindItem, () => {
                MatSearchResult.FocusNext(mat);
            }, autoClose: false);
            qc.AddButton(T.Close, () => { });
            qc.Show();
        }

        private static void ToggleFavWithThresh(MatType mat, string val, bool isSubmit) {
            if (int.TryParse(val, out var threshold)) {
                var S = A.S;
                S.Sig.SetFavMat.Send(mat, true, threshold);
                if (isSubmit) {
                    S.UIQuery.CancelQuickChoice.Ask();
                }
            }
        }

        public void UpdateVisibility(int threshold) {
            if (widget == null && widgetWide == null) { return; }
            var visible = threshold <= 0 || lastCount <= threshold;
            if (isWide) {
                widgetWide.SetActiveFast(visible);
            } else {
                widget.SetActiveFast(visible);
            }
        }

        public void SetCount(int count) {
            if (count == lastCount) { return; }
            // D.Warn("Updating fav mat count: {0} -> {1} -> {2}",
            //     mat.Id, lastCount, count);
            lastCount = count;
            var text = Units.NumColoredAbbr(count);
            if (lastCountTxt == text) { return; }
            lastCountTxt = text;
            if (!isShowing) { return; }
            if (isWide) {
                widgetWide.SetText2(text);
            } else {
                widget.SetText(text);
            }
        }

        // The pool refuses to despawn an inactive GameObject, and a fav hidden by its count
        // threshold is exactly that, so make it visible again before it goes back to the pool.
        public void RestoreVisibilityBeforeDespawn() {
            if (widget != null && !widget.IsDisposed) {
                widget.SetActiveFast(true);
            }
            if (widgetWide != null && !widgetWide.IsDisposed) {
                widgetWide.SetActiveFast(true);
            }
        }

        public void Dispose() {
            RestoreVisibilityBeforeDespawn();
            if (widget != null) {
                if (!widget.IsDisposed) {
                    widget.Dispose();
                }
                widget = null;
            }
            if (widgetWide != null) {
                if (!widgetWide.IsDisposed) {
                    widgetWide.Dispose();
                }
                widgetWide = null;
            }
        }
    }
}
