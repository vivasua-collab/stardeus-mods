using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SplitStackAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new SplitStackAction());
        }

        public const string ActionId = "SplitStack";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.VerbSplit;

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanSplit(obj, out var pile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, pile), Priority.Normal, -9);
            return true;
        }

        // Only available for single selection
        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, UnstoredMatComp pile) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CTraffic, T.VerbSplit, () => ShowSplitQuickChoice(pile, widget));
            return widget;
        }

        private static void ShowSplitQuickChoice(UnstoredMatComp pile, WBase anchor) {
            if (!pile.Entity.IsActive) { return; }

            string inputStr = "";
            int stackSize = pile.StackSize;
            var qc = QuickChoice.Create($"{T.VerbSplit}: {pile.Type.NameT}");
            qc.Widget = anchor.Rect;

            if (stackSize >= 1) {
                qc.AddButton($"1", () => ExecuteSplit(pile, 1));
            }
            if (stackSize >= 10) {
                qc.AddButton($"10", () => ExecuteSplit(pile, 10));
            }
            int quarter = stackSize / 4;
            if (quarter > 1) {
                qc.AddButton($"25% - {quarter}x", () => ExecuteSplit(pile, quarter));
            }
            int half = stackSize / 2;
            if (half > 1) {
                qc.AddButton($"50% - {half}x", () => ExecuteSplit(pile, half));
            }

            qc.AddInput(T.EnterAmount, new QuickChoice.Input {
                AutoFocus = true,
                Value = "",
                OnChange = v => inputStr = v,
                OnSubmit = v => {
                    if (int.TryParse(v, out int n) && n > 0) {
                        if (ExecuteSplit(pile, n)) {
                            A.S.UIQuery.CancelQuickChoice.Ask();
                        }
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
            });
            qc.AddButton(T.Confirm, new QuickChoice.Button {
                OnClickValidate = () => {
                    if (int.TryParse(inputStr, out int n) && n > 0) {
                        return ExecuteSplit(pile, n);
                    }
                    UISounds.PlayActionDenied();
                    return false;
                },
                AutoClose = true,
            });

            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private static bool ExecuteSplit(UnstoredMatComp pile, int amount) {
            if (!pile.Entity.IsActive) {
                UISounds.PlayActionDenied();
                return false;
            }
            int stackSize = pile.StackSize;
            if (amount >= stackSize || amount < 1) {
                UISounds.PlayActionDenied();
                return false;
            }
            var took = pile.Take(pile.Type, null, amount, null);
            if (took > 0) {
                var obj = EntityUtils.SpawnRawMaterial(
                    pile.Type, amount, pile.Entity.PosIdx, 0.5f, true, false);
                if (obj != null) {
                    A.S.Sig.SelectEntity.Send(obj.Entity);
                    EntityUtils.CameraFocusOn(obj.Entity);
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
                return true;
            }
            UISounds.PlayActionDenied();
            return false;
        }

        private static bool CanSplit(Obj obj, out UnstoredMatComp pile) {
            pile = null;
            if (!obj.IsActive || !obj.TryGetComponent(out pile)) {
                return false;
            }
            return !pile.TreatAsObj && pile.StackSize > 1;
        }
    }
}
