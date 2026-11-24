using System;
using Game.Components;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;

namespace Game.Data.Space {
    public struct SOActionContext {
        public GameState S;
        public enum SOActionUIType {
            Starmap,
            Expedition,
        }
        public SOActionUIType UIType;
        public SpaceObject SO;
        public UISpaceObject UISO;
        public SpaceObject Player;
        public Expedition Expedition;

        public void CloseStarmap() {
            if (UIShowing.Starmap) {
                S.UIQuery.ShowStarmap.Ask(false);
            }
        }
    }
}
