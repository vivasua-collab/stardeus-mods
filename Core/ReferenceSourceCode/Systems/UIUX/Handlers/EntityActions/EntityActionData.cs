using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public readonly struct EntityActionData {
        public delegate WBase ActionFactory(Lifetime life, RectTransform parent, int siblingIdx);
        public static readonly EntityActionData Unavailable = new EntityActionData(null, null);
        public EntityActionData(BaseEntityAction act, ActionFactory fac,
            Priority prio = Priority.Normal, int order = 0) {
            Action = act;
            Priority = prio;
            Order = order;
            Factory = fac;
        }
        public readonly Priority Priority;
        public readonly BaseEntityAction Action;
        public readonly int Order;
        public readonly ActionFactory Factory;
        public override string ToString() {
            return $"EntityActionData[{Action.Id}|Fac:{Factory?.Target}]";
        }
    }
}
