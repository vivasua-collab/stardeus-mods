using Game.Data;
using KL.Utils;

namespace Game.Systems.UIUX {
    public abstract class BaseWidgetHandler<T> where T : BaseWidget {
        protected readonly T widget;
        protected readonly GameState S;
        public BaseWidgetHandler(GameState s, T widget) {
            S = s;
            if (widget == null) {
                D.Err("Widget is null! {0}", this);
            }
            this.widget = widget;
        }
    }
}
