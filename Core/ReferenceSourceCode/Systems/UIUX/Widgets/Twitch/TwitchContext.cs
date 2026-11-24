using System;
using Game.Systems.Twitch;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class TwitchContext {
        public string SelectedTab;
        public TwitchViewer SelectedViewer;
        public string SearchStr;
        public int FilterValue;
        public Lifetime MainLifetime;

        public Action RebuildLeft;
        public Action RebuildRight;
        public Action<TwitchRole> ShowRoleConfigPopup;
        public Action ShowAssignBeingPopup;

        public void ShowTab(string name) {
            if (SelectedTab == name) { RebuildRight?.Invoke(); return; }
            SelectedTab = name;
            RebuildRight?.Invoke();
        }

        public void SelectViewer(TwitchViewer v) {
            SelectedViewer = v;
            ShowTab(TwitchTabId.Viewer);
            RebuildLeft?.Invoke();
        }
    }
}
