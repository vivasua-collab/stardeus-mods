using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class TwitchTab_Roles : TwitchTabBase {
        public TwitchTab_Roles(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            foreach (var r in TwitchRole.All.Values.OrderBy(v => v.Id)) {
                var capturedRole = r;
                var roleBtn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                roleBtn.Load(IconId.WPerson, $"Role: {r.Id}", T.VerbConfigure, () => {
                    Ctx.ShowRoleConfigPopup?.Invoke(capturedRole);
                });
            }
        }
    }
}
