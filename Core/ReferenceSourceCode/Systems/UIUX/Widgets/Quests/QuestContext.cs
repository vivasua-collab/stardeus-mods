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
    public class QuestContext {
        public RectTransform Container;
        public string SelectedTab;
        public Lifetime MainLifetime;
        public Lifetime MenuLifetime;
        public Quest SelectedQuest;
        public Action AfterLoadWidget;
        public List<Quest> HubQuests;

        public void ShowTab(string name) {
            if (SelectedTab == name) { return; }
            SelectedTab = name;
            A.S.UISig.ReloadQuests.Send();
        }

        public void ClearHub() {
            HubQuests = null;
        }
    }
}
