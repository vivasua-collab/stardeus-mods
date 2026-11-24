using System;
using Game.Constants;
using KL.Utils;

namespace Game.Systems.Notify {
    public class NotificationOccurrence {
        public Action OnClick;
        public Func<string> Tooltip;
    }
}
