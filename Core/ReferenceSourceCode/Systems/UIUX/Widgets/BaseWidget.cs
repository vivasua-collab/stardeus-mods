using Game.Data;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public abstract class BaseWidget : WBase {
        protected void Register(string id, BaseWidget instance) {
            Registries.UIWidgets.Add(id, instance);
        }

        public virtual void Toggle() {
            Toggle(!gameObject.activeSelf);
        }
        public virtual void Toggle(bool show) {
            gameObject.SetActiveFast(show);
        }
    }
}
