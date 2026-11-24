#if !STARDEUS_DEMO
using System;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_Tunables : DevMenuTab {
        public const string TabIdConst = "Tunables";
        public override string TabId => TabIdConst;
        public override string TabName => "Tunables";

        public DevMenuTab_Tunables(GameState s) : base(s) { }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            var block = UISpawn.Block(life, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Tunables");

            var tutBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            tutBtn.Load(IconId.CInfo, "How To Mod Tunables", Texts.ExternalLink("Open"), () => {
                KL.Utils.Files.OpenOSUrl("https://www.youtube.com/watch?v=uy51KADe9_k");
            });
            UIUtils.AddTip(life, tutBtn, "Opens a YouTube tutorial: https://www.youtube.com/watch?v=uy51KADe9_k");

            // Float tunables
            var floatBlock = UISpawn.Block(life, group, gIdx++);
            int fi = 0;
            var floatHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, floatBlock.Container, fi++);
            floatHeader.Load("Float Tunables");
            foreach (var tun in Tunable.AllFloats.OrderBy(n => n)) {
                var tunH = Hashes.S(tun);
                var tunName = tun;
                var row = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, floatBlock.Container, fi++);
                row.Load(IconId.CCount, tunName,
                    string.Format("{0:0.######}", Tunable.Float(tunH)),
                    () => {
                        var qc = QuickChoice.Create(tunName);
                        qc.AddInput("Enter float value", new QuickChoice.Input() {
                            Value = Tunable.Float(tunH).ToString(),
                            Placeholder = "123.345",
                            OnSubmit = (str) => {
                                if (float.TryParse(str, out var newVal)) {
                                    Tunable.SetFloat(tunH, newVal);
                                    reload();
                                } else {
                                    UISounds.PlayActionDenied();
                                }
                            }
                        });
                        qc.Show();
                    });
            }

            // Int tunables
            var intBlock = UISpawn.Block(life, group, gIdx++);
            int ii = 0;
            var intHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, intBlock.Container, ii++);
            intHeader.Load("Int Tunables");
            foreach (var tun in Tunable.AllInts.OrderBy(n => n)) {
                var tunH = Hashes.S(tun);
                var tunName = tun;
                var row = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, intBlock.Container, ii++);
                row.Load(IconId.CCount, tunName,
                    Tunable.Int(tunH).ToString(),
                    () => {
                        var qc = QuickChoice.Create(tunName);
                        qc.AddInput("Enter int value", new QuickChoice.Input() {
                            Value = Tunable.Float(tunH).ToString(),
                            Placeholder = "123",
                            OnSubmit = (str) => {
                                if (int.TryParse(str, out var newVal)) {
                                    Tunable.SetInt(tunH, newVal);
                                    reload();
                                } else {
                                    UISounds.PlayActionDenied();
                                }
                            }
                        });
                        qc.Show();
                    });
            }

            // Bool tunables
            var boolBlock = UISpawn.Block(life, group, gIdx++);
            int bi = 0;
            var boolHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, boolBlock.Container, bi++);
            boolHeader.Load("Bool Tunables");
            foreach (var tun in Tunable.AllBools.OrderBy(n => n)) {
                var tunH = Hashes.S(tun);
                var tunName = tun;
                var toggle = UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, boolBlock.Container, bi++);
                toggle.Load(tunName, Tunable.Bool(tunH), isOn => {
                    Tunable.SetBool(tunH, isOn);
                });
            }
        }
    }
}
#endif
