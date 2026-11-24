using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Space;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipConfigContext {
        public RectTransform Container;
        public string SelectedTab;
        public Lifetime MainLifetime;
        public Lifetime MenuLifetime;
        public Action AfterLoadWidget;

        public void ShowTab(string name) {
            if (SelectedTab == name) { return; }
            SelectedTab = name;
            A.S.UIQuery.RebuildShipConfig.Ask();
        }
    }
}
