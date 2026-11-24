using System;
using Game.Data;
using Game.UI;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class QuickChoiceHandler : BaseWidgetHandler<QuickChoiceWidget> {
        private QuickChoice current;
        public QuickChoiceHandler(GameState s, QuickChoiceWidget widget) : base(s, widget) {
            widget.Initialize(s, OnCollapse);
            s.UIQuery.ShowQuickChoice.SetResponder(OnShow);
            s.UIQuery.CancelQuickChoice.SetResponder(OnCancel);
        }
        private void OnCollapse() {
            UIShowing.Unset(UIShowingFlags.QuickChoice);
            Coroutines.OnNextFrame(UIHoverFix.RefreshHover);
            if (current != null) {
                current.OnClose?.Invoke();
                current.OnClose = null;
                current = null;
            }
        }


        private bool OnCancel() {
            var res = widget.Cancel();
            current = null;
            return res;
        }

        public void Update() {
            if (widget.UpdateWidget()) {
                current.OnUpdate?.Invoke();
            } else {
                if (current != null) {
                    current.OnClose?.Invoke();
                    current.OnClose = null;
                    current = null;
                }
            }
        }

        private bool OnShow(QuickChoice choice) {
            current = choice;
            return widget.Show(choice);
        }
    }
}
