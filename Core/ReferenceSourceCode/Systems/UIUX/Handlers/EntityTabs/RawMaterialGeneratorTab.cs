using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class RawMaterialGeneratorTab : BaseEntityTab {
        public const string TabId = "RawMaterialGenerator";

        [SelfInit]
        private static void Register() {
            RegisterTab(new RawMaterialGeneratorTab());
        }

        private readonly Dictionary<MatType, int> availableMaterials = new();
        private RawMaterialGeneratorComp generator;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 40;
        public override string TabName => T.ParticleCollector;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent(out generator)) {
                return false;
            }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            generator.UIChanged.Bind(lifetime, ReloadTab, false);

            int blockIdx = 0;

            if (generator.IsConfigured && generator.TargetMaterial != null) {
                LoadConfiguredBlock(group, blockIdx++);
            } else {
                LoadMaterialChoices(group, blockIdx);
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (!generator.IsConfigured) {
                var toggle = UISpawn.Widget<WToggleText>(lifetime,
                    UIPrefabId.WContent4Toggle, group.Container, 0);
                toggle.Load(T.DeviceIgnoreUnconfigured, generator.IsIgnoreUnconfiguredOn, (y) => {
                    generator.IsIgnoreUnconfiguredOn = y;
                    if (y) {
                        generator.HideUnconfiguredWarning();
                    }
                });
                toggle.SetInteractable(!BlockActions);
                return true;
            }

            var stopButton = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            stopButton.Load(IconId.CStop, T.Stop, () => {
                generator.Stop();
                ReloadTab();
            });
            return true;
        }

        private void LoadConfiguredBlock(WGroup group, int blockIdx) {
            var block = UISpawn.Block(lifetime, group, blockIdx);
            int idx = 0;
            var material = generator.TargetMaterial;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            header.Label.Load(T.NounProgress);
            header.Progress.SetPercent(generator.ProgressB, false);
            header.Progress.SetText(Units.Percentage(generator.ProgressB));

            var current = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            current.Load(material.IconId,
                $"{T.CollectingMat_V1.F(material.NameT)} {EnergyCostText(material)}",
                null);

            generator.ProgressB.Bind(lifetime, (progress) => {
                header.Progress.SetPercent(progress, false);
                header.Progress.SetText(Units.Percentage(progress));
                current.SetText2(Units.NumColoredAbbr(S.Sys.Inventory.AvailableCount(material)));
            });

            UIUtils.AddTipFunc(lifetime, current, () => material.Def.ExtendedTooltip(S));
        }

        private void LoadUnconfiguredBlock(WGroup group, int blockIdx) {
            var block = UISpawn.Block(lifetime, group, blockIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ParticleCollector);

            var warning = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            warning.Load(IconId.CWarning, T.Unconfigured, T.SelectTargetMaterial);
        }

        private void LoadMaterialChoices(WGroup group, int blockIdx) {
            var block = UISpawn.Block(lifetime, group, blockIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.SelectTargetMaterial);

            availableMaterials.Clear();
            S.Sys.Inventory.LoadRemainingMaterials(availableMaterials);

            foreach (var pair in MatType.RawByRarity.OrderBy(m => RawMaterialGeneratorComp.GetEnergyCost(m.Key))) {
                var material = pair.Key;
                var row = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                row.Load(material.IconId, MaterialTitle(material),
                    Units.NumColoredAbbr(material.GetCount(availableMaterials)), () => {
                        if (generator.SetTargetMaterial(material)) {
                            ReloadTab();
                        }
                    });
                // UIUtils.AddTipFunc(lifetime, row, () => material.Def.ExtendedTooltip(S));
                UIUtils.AddCodexTooltip(lifetime, row.gameObject, row, material.Def);
            }
        }

        private string MaterialTitle(MatType material) {
            var title = $"{material.NameT} {EnergyCostText(material)}";
            if (generator.TargetMaterial == material) {
                title = $"{title} {Texts.Accented(GlyphMap.TriangleLeft, bold: false)}";
            }
            return title;
        }

        private string EnergyCostText(MatType material) {
            var gridOut = S.Situation.GridMaxOutput;
            var gridUse = S.Situation.GridConsumption;
            var gridSpare = Mathf.Clamp(gridOut - gridUse, 0, gridOut);
            var cost = RawMaterialGeneratorComp.GetEnergyCost(material);
            string color;
            if (cost > gridOut) {
                color = Texts.TMPColorRed;
            } else if (cost > gridSpare) {
                color = Texts.TMPColorYellow;
            } else {
                color = Texts.TMPColorGreen;
            }
            return Texts.GreyParentheses(Texts.WithColor(Units.Electricity(cost), color));
        }

        protected override void Cleanup() {
            availableMaterials.Clear();
            generator = null;
            S = null;
        }
    }
}
