using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TwitchTab_Queue : TwitchTabBase {
        private static readonly List<string> viewerQueueOpts = ((TwitchSettings.ViewerQueueMode[])
            Enum.GetValues(typeof(TwitchSettings.ViewerQueueMode)))
                .Select(v => $"Queue mode: {v}").ToList();

        public TwitchTab_Queue(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            var queueModeW = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, container, idx++);
            queueModeW.Icon.SetImage(IconId.WList);
            queueModeW.Dropdown.Load(viewerQueueOpts, (int)Sys.Settings.QueueMode, v => {
                Sys.Settings.QueueMode = (TwitchSettings.ViewerQueueMode)v;
            });

            AddToggle(life, container, ref idx, "Skip Inactive Viewers",
                () => Sys.Settings.QueueSkipInactive,
                v => Sys.Settings.QueueSkipInactive = v);

            AddToggle(life, container, ref idx, "Prioritize Subscribers",
                () => Sys.Settings.SubQueuePriority,
                v => Sys.Settings.SubQueuePriority = v);
        }

        private void AddToggle(Lifetime life, RectTransform container, ref int idx,
                string name, Func<bool> getValue, Action<bool> setValue) {
            var w = UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, container, idx++);
            w.Load(name, getValue(), isOn => setValue(isOn));
        }
    }
}
