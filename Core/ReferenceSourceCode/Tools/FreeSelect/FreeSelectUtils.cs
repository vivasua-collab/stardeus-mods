using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;

namespace Game.Tools {
    public static class FreeSelectUtils {
        public static bool IsHoveringCloseEnough => RenderUtils.CameraOrtho <
            Tunables.FreeSelectHoverMaxDistance;

        // RMB behavior 0 == context menu when hovering an interesting object, 1 == radial
        public static bool TryShowHoveredEntityContextMenuOnRMB =>
            The.Prefs.GetInt(Pref.GRMBBehavior, PrefH.GRMBBehavior, 0) == 0;
    }
}
