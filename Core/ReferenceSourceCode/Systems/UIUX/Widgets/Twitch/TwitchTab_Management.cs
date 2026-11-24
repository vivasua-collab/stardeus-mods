using Game.Constants;
using Game.Data;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class TwitchTab_Management : TwitchTabBase {
        public TwitchTab_Management(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            var delAllBtn = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            delAllBtn.Load(IconId.CWarning, "Delete All Viewer Data", null, () => {
                Sys.DeleteAllViewers();
                Ctx.RebuildLeft?.Invoke();
            });

            var resetRepBtn = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            resetRepBtn.Load(IconId.CWarning, "Reset All Reputation", null, () => {
                Sys.ResetAllPoints();
                Ctx.RebuildLeft?.Invoke();
            });
        }
    }
}
