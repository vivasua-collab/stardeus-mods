using System;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_Overview : DevMenuTab {
        public const string TabIdConst = "Overview";
        public override string TabId => TabIdConst;
        public override string TabName => "Overview";

        private bool isWatermarksRemoved;
        private bool hasWarnedAboutAchievements;

        public DevMenuTab_Overview(GameState s) : base(s) { }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            var block = UISpawn.Block(life, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Dev Menu");

            // Sandbox mode toggle
            var sandboxToggle = UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, block.Container, idx++);
            sandboxToggle.Load("Sandbox Mode", A.InstantBuild, isOn => {
                DevMenu.ToggleInstantBuild(isOn);
                reload();
            });

            // Remove watermarks
            if (!isWatermarksRemoved) {
                var watermarkBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
                watermarkBtn.Load(IconId.WDev, "Remove Watermarks", "Enter Password", () => {
                    var qc = QuickChoice.Create("Remove Watermarks");
                    qc.AddInput("Password", new QuickChoice.Input() {
                        OnSubmit = (s) => {
                            if (Hashes.SHA256(s) == "wpJ39V+zGuuNQCYTLlNROh+3VeXZwTvyFAW3K5VwNz8=") {
                                UnityEngine.GameObject.Destroy(UnityEngine.GameObject.Find("VersionInfo"));
                                UISounds.PlayAchievement();
                                isWatermarksRemoved = true;
                                reload();
                            } else {
                                UISounds.PlayDigitalCancel();
                            }
                        }
                    });
                    qc.Show();
                });
            }

            // Unlock Bookmarked Research
            var unlockBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            unlockBtn.Load(IconId.CResearch, "Unlock Bookmarked Research", "Unlock", () => {
                int unlocked = S.Sys.Research.UnlockCurrentQueue();
                if (unlocked == 0) {
                    WPopup.Create(false, IconId.CWarning, "Nothing to unlock",
                        "Research Queue was empty, add something to it first!").Spawn();
                } else {
                    S.IsAchievementsBlocked = true;
                    UISounds.PlayResearchComplete();
                }
            });

            // Start Trade
            var tradeBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            tradeBtn.Load(IconId.CStarcreds, "Start Trade", "Start", () => {
                S.IsAchievementsBlocked = true;
                The.R.Query.CreateRandomTrade.Ask(Rng.Unseeded, FactionDef.Get("Capitalist"));
            });
        }

        public void Show() {
            if (!hasWarnedAboutAchievements && !S.IsAchievementsBlocked) {
                WPopup.Create(true, IconId.WDev, T.Warning, null, T.DevMenuAchievementsWarning)
                    .WithAction(T.Confirm, () => {
                        hasWarnedAboutAchievements = true;
                        S.UIQuery.ToggleDevMenu.Ask(true);
                    }).WithAction2(T.Cancel, null)
                    .Spawn(center: true);
            } else {
                hasWarnedAboutAchievements = true;
                S.UIQuery.ToggleDevMenu.Ask(true);
            }
        }
    }
}
