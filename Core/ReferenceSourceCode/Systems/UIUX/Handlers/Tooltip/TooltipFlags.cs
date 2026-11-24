using System;

namespace Game.Systems.UIUX {
    [Flags]
    public enum TooltipFlags {
        None = 0,
        /// <summary>
        /// for marking tooltips as destroyed
        /// </summary>
        Destroyed = 1 << 0,
        /// <summary>
        /// Displayed instantly under mouse cursor until null tooltip is removed
        /// </summary>
        Temporal = 1 << 1,
        /// <summary>
        /// Has function instead of fixed string tip
        /// </summary>
        Function = 1 << 2,
    }
}
