using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Creatures;

namespace Game.Systems.UIUX {
    public sealed class WakeUpChoiceHandler : BaseWidgetHandler<WakeUpChoiceWidget> {
        public WakeUpChoiceHandler(GameState s, WakeUpChoiceWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowWakeUpChoice.SetResponder(ShowChoice);
        }

        private bool ShowChoice(List<BeingData> options, Action<BeingData> onChoose) {
            return widget.Show(S, options, onChoose);
        }
    }
}
