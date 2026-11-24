using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DisposerTab : BaseEntityTab {
        public const string TabId = "Disposer";

        [SelfInit]
        private static void Register() {
            RegisterTab(new DisposerTab());
        }

        private Tile tile;
        private DisposerComp disposer;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.VerbDispose;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent(out disposer)) {
                return false;
            }
            this.tile = tile;
            S = s;
            return true;
        }

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            widget.SetText(disposer.SubmenuTitle);
        }

        public override bool LoadContent(WGroup group) {
            disposer.DisposerStateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var settingsBlock = UISpawn.Block(lifetime, group, 0);
            int idx = 0;


            if (disposer.SlotDesignation != SlotDesignationH.Disposer_Organic) {
                var settingsHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, settingsBlock.Container, idx++);
                settingsHeader.Load(T.MenuSettings);

                var quality = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, settingsBlock.Container, idx++);
                quality.Item.Load(IconId.CCog, T.PartQualityThreshold,
                    Units.Percentage(disposer.PartQualityThreshold));
                quality.Slider.Load(disposer.PartQualityThreshold, (v) => {
                    disposer.PartQualityThreshold = v;
                    quality.Item.SetText2(Units.Percentage(disposer.PartQualityThreshold));
                });
                UIUtils.AddTip(lifetime, quality, T.PartQualityThresholdTip);
            }

            var queueBlock = UISpawn.Block(lifetime, group, 1);
            idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContent2HeaderProgress, queueBlock.Container, idx++);
            header.Label.Load(T.NounQueue);
            header.Progress.SetPercent(disposer.MaxCapacity > 0
                ? (float) disposer.Queue.Count / disposer.MaxCapacity
                : 0f, false);
            header.Progress.SetText(Units.XOutOfY(disposer.Queue.Count, disposer.MaxCapacity));
            UIUtils.AddTip(lifetime, header.Progress, T.NounQueue);

            if (disposer.Queue.Count == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, queueBlock.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return true;
            }

            foreach (var item in disposer.Queue) {
                var row = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, queueBlock.Container, idx++);
                if (item.BeingDataId > 0) {
                    var bd = S.Query.FindBeingData.Ask(item.BeingDataId);
                    if (bd != null) {
                        row.Load((Sprite) null, bd.Mind.Name.FullName);
                        bd.Avatar.Load(row.Image);
                        continue;
                    }
                }

                var def = item.Def;
                row.Load(def?.Preview ?? IconId.CInfo, def?.NameT ?? T.Empty);
            }

            return true;
        }

        protected override void Cleanup() {
            tile = null;
            disposer = null;
            S = null;
        }
    }
}
