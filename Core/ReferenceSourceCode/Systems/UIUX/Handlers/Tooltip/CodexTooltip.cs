using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexTooltip {
        public readonly Def Def;
        public readonly WBase Invoker;
        public readonly bool Optional;
        public readonly float ShowDelay;
        public readonly bool AnchorTop;
        public bool IsChartMode;
        public Lifetime Life;
        public bool IsDisposed {
            get {
                return Life?.IsDisposed != false || Invoker?.IsDisposed == true;
            }
        }

        private CodexTooltip(WBase invoker, Def def, bool optional, float showDelay, bool anchorTop) {
            Invoker = invoker;
            Def = def;
            Optional = optional;
            ShowDelay = showDelay;
            AnchorTop = anchorTop;
        }

        public static CodexTooltip CreateFor(WBase invoker, Def def,
                bool optional = true, float showDelay = 0f, bool anchorTop = true) {
            return new CodexTooltip(invoker, def, optional, showDelay, anchorTop);
        }

    }
}
