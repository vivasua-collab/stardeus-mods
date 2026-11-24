using Game.Data;
using Game.Systems.Twitch;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public abstract class TwitchTabBase {
        protected readonly GameState S;
        protected readonly TwitchSys Sys;
        protected readonly TwitchContext Ctx;
        protected readonly TwitchWidget Widget;

        protected TwitchTabBase(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget) {
            S = s; Sys = sys; Ctx = ctx; Widget = widget;
        }

        public abstract void Load(Lifetime life);
    }
}
