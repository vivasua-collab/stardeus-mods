using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures.Surgeries {
    public class SurgeryListView : IContextPopupWithWidth {

        private readonly GameState S;
        private BeingData being;
        private bool isShowing;

        public SurgeryListView(GameState s) {
            S = s;
        }

        public bool SetBeing(BeingData being) {
            if (being != this.being) {
                this.being = being;
                return true;
            }
            return false;
        }

        public void Toggle() {
            S.UIQuery.ShowContextPopup.Ask(this, !isShowing);
        }

        public void Show() {
            S.UIQuery.ShowContextPopup.Ask(this, true);
        }
        public void Hide() {
            S.UIQuery.ShowContextPopup.Ask(this, false);
        }

        public int UniquePopupHash => JobTypeIdH.Medical;
        public bool ShowInScreenCenter => being == null;

        public int PopupWidth => 600;

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            if (being != null) {
                return ShowBeingSurgeries(lifetime, wContent, wFooter);
            } else {
                return ShowAllSurgeries(lifetime, wContent, wFooter);
            }

        }

        private const int PageSize = 25;

        private string ShowAllSurgeries(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var surgeries = S.Query.GetAllSurgeries.Ask();
            var ordered = surgeries.OrderBy(s => s.TargetBeing.Species.Name)
                .ThenBy(s => s.TargetBeing.Mind.Name.ShortName).ToList();

            int bIdx = 0;
            SpawnSurgeryPage(lifetime, wContent, ordered, 0, ref bIdx);

            var cancel = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, wFooter.Container, 0);
            cancel.Load(T.CancelProcedures, () => {
                var ops = S.Query.GetAllSurgeries.Ask();
                Caches.BeingDataList.Clear();
                foreach (var op in ops) {
                    if (op.TargetBeing == null) { continue; }
                    Caches.BeingDataList.AddUniqueInefficient(op.TargetBeing);
                }
                foreach (var b in Caches.BeingDataList) {
                    S.Query.ClearSurgicalOpsFor.Ask(b);
                }
                Hide();
            });

            wFooter.SetActiveFast(true);
            return T.NounSurgery;
        }

        private void SpawnSurgeryPage(Lifetime lifetime, WGroup wContent,
                System.Collections.Generic.List<SurgicalOperation> ordered, int startBeingIdx, ref int bIdx) {
            int beingCount = 0;
            BeingData lastBeing = null;
            WIndentWrapper indent = null;
            int opIdx = 0;

            // find the start operation index for startBeingIdx distinct beings
            int distinctSeen = 0;
            int opStart = 0;
            for (int i = 0; i < ordered.Count; i++) {
                if (ordered[i].TargetBeing != (i > 0 ? ordered[i - 1].TargetBeing : null)) {
                    if (distinctSeen == startBeingIdx) { opStart = i; break; }
                    distinctSeen++;
                }
            }

            for (int i = opStart; i < ordered.Count; i++) {
                var op = ordered[i];
                if (lastBeing != op.TargetBeing) {
                    if (beingCount == PageSize) {
                        int remaining = 0;
                        BeingData prev = null;
                        for (int j = i; j < ordered.Count; j++) {
                            if (ordered[j].TargetBeing != prev) { remaining++; prev = ordered[j].TargetBeing; }
                        }
                        var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WContent2ButtonSimple,
                            wContent.Container, bIdx++);
                        int nextStart = startBeingIdx + PageSize;
                        btn.Load(Texts.Italic($"+{remaining}"), () => {
                            btn.gameObject.SetActiveFast(false);
                            int appendIdx = btn.transform.GetSiblingIndex();
                            SpawnSurgeryPage(lifetime, wContent, ordered, nextStart, ref appendIdx);
                        });
                        return;
                    }
                    var b = op.TargetBeing;
                    lastBeing = b;
                    var block = UISpawn.Block(lifetime, wContent, bIdx++);
                    int hIdx = 0;
                    var header = UISpawn.Widget<WBeingListItem>(lifetime, UIPrefabId.WBeingListItem,
                        block.Container, hIdx++);
                    header.Load(b, () => S.UIQuery.ShowSurgeriesFor.Ask(b));
                    indent = UISpawn.Widget<WIndentWrapper>(lifetime, UIPrefabId.WIndentWrapper, block.Container, hIdx++);
                    indent.SetIndent(40);
                    opIdx = 0;
                    beingCount++;
                }
                AddSurgeryEntry(lifetime, op, indent.Container, ref opIdx);
            }
        }

        private string ShowBeingSurgeries(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var numSurgeries = S.Query.GetSurgicalOperationsFor.Ask(
                being.Body, Caches.SurgicalOps);
            int idx = 0;
            for (int i = 0; i < numSurgeries; i++) {
                var op = Caches.SurgicalOps[i];
                AddSurgeryEntry(lifetime, op, wContent.Container, ref idx);
            }
            if (numSurgeries == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime, UIPrefabId.WContentText, wContent.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else if (numSurgeries > 1) {
                var empty = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WContent2ButtonSimple, wContent.Container, idx++);
                empty.Load(Texts.Italic(T.ClearAll), () => {
                    S.Query.ClearSurgicalOpsFor.Ask(being);
                    S.UIQuery.ShowContextPopup.Ask(this, false);
                });
            }
            var cancel = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, wFooter.Container, 0);
            cancel.Load(T.CancelProcedures, () => {
                S.Query.ClearSurgicalOpsFor.Ask(being);
                Hide();
            });
            wFooter.SetActiveFast(true);
            return $"{T.NounSurgery} {GlyphMap.ArrowRight} {being.Mind.Name.ShortName}";
        }

        private void AddSurgeryEntry(Lifetime life, SurgicalOperation operation, RectTransform container, ref int idx) {
            var op = operation;
            var row = UISpawn.Widget<WContentTextWithX>(life, UIPrefabId.WContent2TextX, container, idx++);
            op.State.Bind(life, (st) => {
                op.GetOpTexts(out var opName, out var name, out var side, out var tip);
                var n = name;
                if (op.TargetSlot != null) {
                    n = $"{op.TargetSlot.NameT}: {n}";
                }
                if (side != null) {
                    n = $"{n} {Texts.GreyParentheses(side)}";
                }

                row.Text.Load(op.Icon, n);
            });
            row.XButton.Load(() => {
                if (!S.Query.RegisterSurgicalOperation.Ask(op, false)) {
                    if (op.FailReason != null) {
                        S.Sig.ShowErrorMessage.Send(op.FailReason);
                    } else {
                        UISounds.PlayActionDenied();
                    }
                } else {
                    UISounds.PlayDigitalConfirm();
                    row.Dispose();
                }
            });
            UIUtils.AddTipFunc(life, row, () => {
                op.GetOpTexts(out var opName, out var name, out var side, out var tip);
                return tip;
            });
        }

        public void OnPopupClosed() {
            isShowing = false;
        }

        public void OnPopupOpened() {
            isShowing = true;
        }
    }
}
