using System;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class EntityPanelHandler : BaseWidgetHandler<EntityPanelWidget> {
        private float nextGoalCheck;
        public EntityPanelHandler(GameState s, EntityPanelWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowBeingPanel.SetResponder(ShowBeingPanel);
            s.UIQuery.ShowObjPanel.SetResponder(ShowObjPanel);
            s.UIQuery.ShowTilePanel.SetResponder(ShowTilePanel);
            s.UIQuery.HideEntityPanel.SetResponder(HidePanel);
            s.UIQuery.GetEntitySubPanel.SetResponder(widget.GetSubPanel);
            s.UIQuery.CloseEntitySubPanel.SetResponder(widget.CloseSubPanel);
            s.UIQuery.ShowSelectedEntityTab.SetResponder(widget.ShowSelectedTab);
            s.UIQuery.ResetSelectedEntityTab.SetResponder(widget.ResetSelectedTab);
            s.UIQuery.ReloadEntityTab.SetResponder(widget.ReloadTab);
            s.UIQuery.ReloadCurrentEntity.SetResponder(widget.ReloadPanel);
            s.UIQuery.ReloadEntityIfSelected.SetResponder(ReloadEntityIfSelected);

            s.Sig.BeingNameChanged.AddListener(OnBeingRenamed);
        }

        // Runs after the selection handler, so a selection change this frame has already
        // loaded the new entity and the old one is not reloaded for nothing
        public void Update() {
            if (Time.unscaledTime < nextGoalCheck || !widget.CheckOutdatedGoal()) { return; }
            nextGoalCheck = Time.unscaledTime + EntitySelectionHandler.GoalRefreshCooldown;
            widget.ReloadPanel();
        }

        private bool ReloadEntityIfSelected(Entity entity) {
            if (S.Query.IsExclusivelySelected.Ask(entity)) {
                widget.ReloadPanel();
                S.UIQuery.RebuildEntityActions.Ask();
                return true;
            }
            return false;
        }

        private bool HidePanel() {
            if (widget.IsShowing) {
                widget.Close();
                return true;
            }
            return false;
        }

        private void OnBeingRenamed(BeingData being) {
            widget.BeingRenamed(being);
        }

        private bool ShowTilePanel(Tile tile) {
            return widget.ShowTile(S, tile);
        }

        private bool ShowObjPanel(ObjData obj) {
            return widget.ShowObj(S, obj);
        }

        private bool ShowBeingPanel(BeingData being) {
            return widget.ShowBeing(S, being);
        }
    }
}
