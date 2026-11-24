using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class TractorBeamTab : BaseEntityTab {
        public const string TabId = "TractorBeam";
        private const string TargetSubId = "TractorTarget";
        private const string ProjectileSubId = "TractorProjectile";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TractorBeamTab());
        }

        private Tile tile;
        private TractorBeamComp beam;
        private GameState S;
        private WButtonIconText2 wTargetRow;
        private WButtonIconText2 wProjectileRow;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.TractorBeam;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<TractorBeamComp>(out var b)) { return false; }
            this.tile = tile;
            this.beam = b;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            beam.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.TractorBeam);

            wTargetRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            if (beam.Target == null) {
                wTargetRow.Load(IconId.CWarning, T.NoTarget, T.Select,
                    ShowTargetSubPanel);
            } else {
                wTargetRow.Load(IconId.CCheck, T.TargetSelected,
                    EntityUtils.PosOrLabelOf(beam.Target.Tile), ShowTargetSubPanel);
            }

            wProjectileRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            if (beam.Projectile == null) {
                wProjectileRow.Load(IconId.CCross, T.NoProjectile, T.Select,
                    ShowProjectileSubPanel);
            } else {
                wProjectileRow.Load(
                    beam.Projectile.Entity.Definition.Preview,
                    beam.Projectile.Entity.Definition.NameT,
                    T.Change, ShowProjectileSubPanel);
            }

            if (beam.Projectile != null) {
                var stopBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                stopBtn.Load(IconId.CCross, T.Stop, null, () => {
                    beam.ReleaseProjectile();
                });
            }

            if (SubPanelWidget.IsOpenFor == TargetSubId && wTargetRow != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowTargetSubPanel();
                var btn = wTargetRow;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            } else if (SubPanelWidget.IsOpenFor == ProjectileSubId && wProjectileRow != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowProjectileSubPanel();
                var btn = wProjectileRow;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        private void ShowTargetSubPanel() {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.SelectTarget);
            int gIdx = 0;

            var block = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var candidates = beam.GetCandidateTargets();
            if (candidates == null || candidates.Length == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                foreach (var spot in candidates) {
                    var isCurrent = beam.Target == spot;
                    var w = UISpawn.Widget<WButtonIconText2>(l,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    w.Load(spot.Entity.Definition.Preview,
                        spot.Entity.Definition.NameT,
                        isCurrent ? Texts.Accented(T.Selected) : EntityUtils.PosOrLabelOf(spot.Tile),
                        () => {
                            beam.SetTargetSpot(spot);
                            EntityUtils.HideEntityLink();
                            S.UIQuery.CloseEntitySubPanel.Ask();
                        });
                    var capSpot = spot;
                    var tip = UIUtils.AddHover(l, w,
                        (h) => EntityUtils.ShowEntityLink(null, capSpot.Tile, h));
                }
            }

            subPanel.Show(wTargetRow, TargetSubId);
        }

        private void ShowProjectileSubPanel() {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.SelectProjectile);
            int gIdx = 0;

            var block = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var candidates = beam.GetCandidateProjectiles();
            if (candidates == null || candidates.Length == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                foreach (var proj in candidates) {
                    var isCurrent = beam.Projectile == proj;
                    var w = UISpawn.Widget<WButtonIconText2>(l,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    w.Load(proj.Entity.Definition.Preview,
                        proj.Entity.Definition.NameT,
                        isCurrent ? Texts.Accented(T.Selected) : Pos.String(proj.Entity.PosIdx),
                        () => {
                            beam.SelectProjectileManual(proj);
                            EntityUtils.HideEntityLink();
                            S.UIQuery.CloseEntitySubPanel.Ask();
                        });
                }
            }

            subPanel.Show(wProjectileRow, ProjectileSubId);
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            var toggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            toggle.Load(T.TractorBeamAutoProjectile, beam.IsAutoTarget, isOn => {
                beam.ToggleAutoTarget();
            });
            UIUtils.AddTip(lifetime, toggle, T.TractorBeamAutoProjectileDesc);
            var helpBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, group.Container, idx++);
            helpBtn.Load(IconId.CInfo, T.HowItWorks, null, () => {
                A.S.UIQuery.ShowCodexEntry.Ask(CodexConceptPage.FindById(
                    TractorBeamPage.PageId));
            });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            beam = null;
            S = null;
            wTargetRow = null;
            wProjectileRow = null;
        }
    }
}
