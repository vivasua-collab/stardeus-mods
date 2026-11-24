using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Creatures;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BeingsListWidget : BaseWidget {
        [SerializeField] private BeingGroupsUI groupsUI;
        public Dictionary<BeingData, WBeingAvatar> SpawnedAvatars => groupsUI.SpawnedAvatars;
        public BeingGroupsUI GroupsUI => groupsUI;
        public void Initialize() {
            Register(UIWidgetId.BeingsList, this);
        }
    }
}
