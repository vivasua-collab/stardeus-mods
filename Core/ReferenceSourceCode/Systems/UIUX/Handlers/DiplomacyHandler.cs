using Game.Data;
using Game.Data.Factions;
using Game.UI;

namespace Game.Systems.UIUX {
    public sealed class DiplomacyHandler : BaseWidgetHandler<DiplomacyWidget> {
        public DiplomacyHandler(GameState s, DiplomacyWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ToggleDiplomacy.SetResponder(ToggleDiplomacy);
            // Subscribed once for the whole session - relation changes are rare, and the
            // widget rebuilds from scratch when it opens, so nothing is missed while it
            // is closed.
            s.Sig.FactionRelationChanged.AddListener(OnRelationChanged);
        }

        private bool ToggleDiplomacy(bool show) {
            widget.Toggle(show);
            return true;
        }

        private void OnRelationChanged(FactionDef a, FactionDef b) {
            if (!UIShowing.Diplomacy) { return; }
            widget.OnRelationChanged();
        }
    }
}
