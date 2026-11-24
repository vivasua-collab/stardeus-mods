using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TwitchTab_Connection : TwitchTabBase {
        public TwitchTab_Connection(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            var welcomeW = UISpawn.Widget<WInputWithIcon>(life,
                UIPrefabId.WContent2Input, container, idx++);
            welcomeW.Icon.SetImage(IconId.CWave);
            welcomeW.Input.Load("Custom welcome message", Sys.Settings.WelcomeMsg, v => {
                Sys.Settings.WelcomeMsg = v;
            });
            UIUtils.AddTip(life, welcomeW, "Custom welcome message");

            AddToggle(life, container, ref idx, "Announce Connect/Disconnect",
                () => Sys.Settings.AnnounceConnect,
                v => Sys.Settings.AnnounceConnect = v);
        }

        private void AddToggle(Lifetime life, RectTransform container, ref int idx,
                string name, Func<bool> getValue, Action<bool> setValue) {
            var w = UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, container, idx++);
            w.Load(name, getValue(), isOn => setValue(isOn));
        }
    }
}
