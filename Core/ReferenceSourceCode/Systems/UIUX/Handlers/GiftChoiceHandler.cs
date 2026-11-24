using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Diplomacy;

namespace Game.Systems.UIUX {
    public sealed class GiftChoiceHandler : BaseWidgetHandler<GiftChoiceWidget> {
        public GiftChoiceHandler(GameState s, GiftChoiceWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowGiftChoice.SetResponder(ShowChoice);
        }

        private bool ShowChoice(List<GiftPackage> options, Action<GiftPackage> onChoose) {
            return widget.Show(S, options, onChoose);
        }
    }
}
