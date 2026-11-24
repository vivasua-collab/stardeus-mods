using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Tools {
    public sealed class FreeSelectRightClickUI {
        public FreeSelectRightClickUI(GameState s, QuickChoice choice, FreeSelectContext ctx) {
            S = s;
            this.choice = choice;
            this.ctx = ctx;
        }

        private readonly FreeSelectContext ctx;
        public readonly GameState S;
        private readonly List<Being> currentSelection = new();
        private readonly QuickChoice choice;
        private bool isForBeings;

        private void ShowForBeings() {
            if (ctx.SelectedBeings.Count <= 0) {
                D.Err("Nothing is selected, trying to show context actions!");
                return;
            }
            string selectedTitle;
            if (ctx.SelectedBeings.Count == 1) {
                var se = ctx.SelectedBeings[0];
                selectedTitle = se.Data.Name;
            } else {
                selectedTitle = T.ObjectSelected_V1.F(Units.Num(ctx.SelectedBeings.Count));
            }
            if (ctx.SelectedCount > ctx.SelectedBeings.Count) {
                ctx.DeselectNonBeings();
            }
            currentSelection.Clear();
            currentSelection.AddRange(ctx.SelectedBeings);

            choice.WithLabel(selectedTitle);
        }

        public void OnUIUpdate() {
            if (!isForBeings) { return; }
            bool close = false;
            if (ctx.SelectedBeings.Count == 0) {
                close = true;
            } else if (ctx.SelectedBeings.Count != currentSelection.Count) {
                close = true;
            } else if (ctx.SelectedCount != currentSelection.Count) {
                close = true;
            } else {
                for (int i = currentSelection.Count - 1; i >= 0; i--) {
                    if (currentSelection[i] != ctx.SelectedBeings[i]) {
                        close = true;
                        break;
                    }
                }
            }
            if (close) {
                if (choice.Count > 0) {
                    choice.Clear();
                    S.UIQuery.CancelQuickChoice.Ask();
                }
            }
        }

        public void Show(bool forBeings) {
            choice.OnUpdate = OnUIUpdate;
            isForBeings = forBeings;
            if (!forBeings) {
                currentSelection.Clear();
            } else {
                ShowForBeings();
            }
            S.UIQuery.ShowQuickChoice.Ask(choice);
        }
    }
}
