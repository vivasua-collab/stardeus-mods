using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Research;
using Game.Systems.Creatures;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ExpeditionMenuBlock_Destination : ExpeditionMenuBlock {
        public override int Order => 0;
        private Lifetime lifetime;
        private Lifetime destLife;
        private Lifetime destChoiceLife;
        private WGroup block;

        public ExpeditionMenuBlock_Destination(ExpeditionsWidget parent,
            GameState s) : base(parent, s) { }

        public override void Load(int siblingIdx, ShuttleComp shuttle, Expedition exp) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Exp.Dest");
            block = UISpawn.Block(lifetime, parent.SubPanel.Container, siblingIdx);
            ctx.OnCrewChanged.Bind(lifetime, LoadChosenDest);
        }

        private void LoadChosenDest() {
            if (destLife != null) {
                destLife.Dispose();
            }
            destLife = lifetime.CreateChild("Exp.Dest.Item");
            int idx = 0;
            var header = UISpawn.Widget<WText>(destLife,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounCurrentDestination);
            var so = ctx.TargetSO.Value;
            if (so != null) {
                var w = UISpawn.Widget<WButtonIconText2>(destLife,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                var exp = ctx.Expedition;
                if (exp != null) {
                    new C2<SpaceObject, int>(destLife, ctx.TargetSO, ctx.Expedition.OnChange, (so, _i) => {
                        UpdateDestWidget(so, w);
                    });
                } else {
                    ctx.TargetSO.Bind(destLife, (so) => {
                        UpdateDestWidget(so, w);
                    });
                }
                if (!ctx.IsExploration && ctx.IsSetup) {
                    var title = T.ExpectedMineRate;
                    var expW = UISpawn.Widget<WIconText2>(destLife, UIPrefabId.WContent2Text2,
                        block.Container, idx++);
                    expW.Load(IconId.CPickaxe, title, null);
                    var est = ctx.EstimateMiningRate(out _);
                    ctx.OnCrewChanged.Bind(destLife, () => {
                        est = ctx.EstimateMiningRate(out _);
                        expW.SetText2(T.RatePerDay_V1.F(Mathf.RoundToInt(est)));
                    });
                }
            } else {
                var w2 = UISpawn.Widget<WButtonText>(destLife,
                    UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
                w2.Load(Texts.Italic(T.SelectTarget), ShowDestChoicePopup);
                // ShowDestChoicePopup();
            }

        }

        private void UpdateDestWidget(SpaceObject so, WButtonIconText2 w) {
            var name = so.DisplayName;
            if (!ctx.CheckDepth(out _, out _, out var depthText, out _, out _)) {
                name = Texts.Red(name);
            }
            w.Load(so.Icon, name, depthText, () => {
                if (ctx.IsSetup) {
                    ShowDestChoicePopup();
                } else {
                    S.UIQuery.StarmapFocusOn.Ask(so, true);
                }
            });
            UIUtils.AddTipFunc(lifetime, w, () => ctx.DestinationTip(so));
        }

        private void ShowDestChoicePopup() {
            if (destChoiceLife != null) {
                destChoiceLife.Dispose();
            }
            destChoiceLife = lifetime.CreateChild("Exp.Dest.Choice");
            var choiceWidget = UISpawn.Widget<WPanel>(
                    destChoiceLife, UIPrefabId.WListChoicePopup,
                    UIPoolRoot.PopupContainer, -1);
            choiceWidget.ShowFooter(false);
            choiceWidget.Load(T.SelectTarget, destChoiceLife.Dispose);
            UIUtils.AfterShowPopup(choiceWidget.Rect);

            var block = UISpawn.Block(destChoiceLife, choiceWidget.Content);
            int idx = 0;

            var header = UISpawn.Widget<WText>(destChoiceLife,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.LocationNearby);

            bool hasNearby = false;
            List<SpaceObject> allNearby = null;
            allNearby = S.Universe.Player.Parent.Children;

            foreach (var nearby in allNearby) {
                if (!nearby.Type.CanBeExplored) { continue; }
                if (S.Sys.Expedition.HasExpeditionTo(nearby)) {
                    continue;
                }
                hasNearby = true;
                var w = UISpawn.Widget<WButtonIconText2>(destChoiceLife,
                        UIPrefabId.WContent2Button, block.Container, idx++);

                var name = nearby.DisplayName;
                if (!ctx.CheckDepth(out _, out _, out var depthText,
                        out var diffTxt, out int ownedCnt, nearby)) {
                    name = Texts.Red(name);
                }

                w.Load(nearby.Icon, name, depthText, () => {
                    ctx.TargetSO.Set(nearby);
                    destChoiceLife.Dispose();
                    LoadChosenDest();
                });
                UIUtils.AddTipFunc(destChoiceLife, w, () => ctx.DestinationTip(nearby));
            }

            if (!hasNearby) {
                var w = UISpawn.Widget<WIconText>(destChoiceLife,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                w.Load(IconId.CInfo, T.Empty);
            }
        }

    }
}
