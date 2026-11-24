using System;
using Game.Constants;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Notify {
    public interface INotificationUIProvider {
        public WBase SpawnNotificationUI(Lifetime life, NotificationData data,
            RectTransform container, int idx);

    }
}
