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
    public sealed class TwitchTab_Commands : TwitchTabBase {
        private static readonly List<string> prefixOpts = new() {
            "!", "!s", "!sd", "!x", "!g",
        };

        public TwitchTab_Commands(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            int cmdPrefixIdx = 0;
            var custPref = Sys.Settings.CommandPrefix;
            if (custPref != null) {
                cmdPrefixIdx = prefixOpts.IndexOf(custPref);
                if (cmdPrefixIdx == -1) {
                    cmdPrefixIdx = 0;
                    Sys.Settings.CommandPrefix = null;
                }
            }

            var prefixW = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, container, idx++);
            prefixW.Icon.SetImage(IconId.WList);
            prefixW.Dropdown.Load(prefixOpts, cmdPrefixIdx, v => {
                Sys.Settings.CommandPrefix = prefixOpts[v];
                Ctx.RebuildRight?.Invoke();
            });
            UIUtils.AddTip(life, prefixW, "Command Prefix");

            var repGainRow = UISpawn.Widget<WInputAmtIconText2>(life,
                UIPrefabId.WContent2Text2Amt, container, idx++);
            repGainRow.Item.Load(IconId.CUpgrade, "Reputation Gain", "Per Hour:");
            repGainRow.Amt.Load(Sys.Settings.RepGainPerHour, v => {
                Sys.Settings.RepGainPerHour = v;
            }, v => Maths.Clamp(v, 0, 999999));
            UIUtils.AddTip(life, repGainRow,
                "Viewers will gain this amount of Reputation per hour of active watch time");

            foreach (var cmd in TwitchSys.CommandMap.OrderBy(kv => kv.Value)) {
                var capturedCmd = cmd;
                var enabled = Sys.Settings.IsCommandEnabled(cmd.Key);
                var cost = Sys.CommandCost(cmd.Key);
                var cmdName = Sys.Settings.AddPrefix(cmd.Value, true);

                var cmdW = UISpawn.Widget<WInputAmtText2Toggle>(life,
                    UIPrefabId.WContent2Text2AmtToggle, container, idx++);
                cmdW.Texts.Load(enabled ? cmdName : Texts.Strike(cmdName), "Rep Cost:");
                cmdW.Toggle.Load(enabled, isOn => {
                    Sys.Settings.SetCommandEnabled(capturedCmd.Key, isOn);
                    cmdW.Texts.SetText(isOn
                        ? Sys.Settings.AddPrefix(capturedCmd.Value, true)
                        : Texts.Strike(Sys.Settings.AddPrefix(capturedCmd.Value, true)));
                });
                UIUtils.AddTip(life, cmdW, TwitchSys.HelpLines[cmd.Key]);
                cmdW.Amt.Load(cost, v => {
                    var costs = TwitchSys.GlobalData.CommandCosts;
                    costs[capturedCmd.Key] = v;
                    TwitchSys.CommandCosts[capturedCmd.Key] = v;
                }, v => Maths.Clamp(v, 0, 999999));
                cmdW.Resolver.AutoResize();
            }
        }
    }
}
