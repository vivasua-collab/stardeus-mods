#if !STARDEUS_DEMO
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_Situations : DevMenuTab {
        public const string TabIdConst = "Situations";
        public override string TabId => TabIdConst;
        public override string TabName => "Situations";

        public DevMenuTab_Situations(GameState s) : base(s) { }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            var block = UISpawn.Block(life, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Create Situation");

            foreach (var sit in DevSituations.All) {
                var sit2 = sit;
                var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
                btn.Load(sit.Icon, sit.Title, "Create", () => DevSituations.CreateSituation(S, sit2));
                UIUtils.AddTip(life, btn, sit.Tooltip);
            }
        }
    }
}
#endif
