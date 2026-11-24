using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class RefillableStorageTab : BaseEntityTab {
        public const string TabId = "RefillableStorage";

        [SelfInit]
        private static void Register() {
            RegisterTab(new RefillableStorageTab());
        }

        private Tile tile;
        private RefillableStorageComp storage;
        private GameState S;

        public override string TabName => T.Refill;

        public override int Order => 40;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent(out storage)) {
                return false;
            }
            this.tile = tile;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            storage.UIChanged.Bind(lifetime, ReloadTab, false);
            var block = UISpawn.Block(lifetime, group, 0);
            var idx = 0;

            if (storage.IsConfigured) {
                LoadConfigured(block, ref idx);
            } else {
                LoadUnconfigured(block, ref idx);
            }
            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (!storage.IsConfigured) { return false; }
            int idx = 0;
            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime,
                UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => storage.AdjustRefillPriority(+1));
            prio.Down.Load(IconId.WArrowDown, () => storage.AdjustRefillPriority(-1));
            prio.Texts.Load(T.RefillPriority, Units.XOutOfY(storage.RefillPriority, 5));
            UIUtils.AddTip(lifetime, prio, T.RefillPriorityHelp);

            var target = UISpawn.Widget<WPriorityUpDown>(lifetime,
                UIPrefabId.WContent4Priority, group.Container, idx++);
            target.Texts.Load(T.AutoRefillAmount,
                Units.XOutOfY(storage.TargetStock, storage.MaterialCapacity));
            target.Up.Load(IconId.WArrowUp, () => {
                storage.SetTargetStockByUI(storage.TargetStock + 1);
            });
            target.Down.Load(IconId.WArrowDown, () => {
                storage.SetTargetStockByUI(storage.TargetStock - 1);
            });
            UIUtils.AddTip(lifetime, target, T.AutoRefillAmountHelp);

            if (!storage.HasLockedMaterial) {
                var stopButton = UISpawn.Widget<WButtonIconText>(lifetime,
                    UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                stopButton.Load(IconId.CStop, storage.StopActionText, () => {
                    storage.Stop();
                    ReloadTab();
                });
            }

            return true;
        }

        private void LoadConfigured(WContentBlock block, ref int idx) {
            var mat = storage.MatType;
            if (mat == null) {
                D.Err("Storage {0} is configured but has no material?", storage);
                return;
            }
            var stock = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress_Green,
                block.Container, idx++);
            stock.Label.Load(storage.PurposeT);
            storage.StockB.Bind(lifetime, (amt) => {
                stock.Progress.Load(
                    $"{amt:0.00}/{storage.TargetStock:0}",
                    (float) amt / storage.TargetStock);
            });

            var fuelCountB = S.UIQuery.GetBindableMatCountFor.Ask(mat);
            var matRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            matRow.Load(mat.IconId, mat.NameT, null);
            fuelCountB.Bind(lifetime, (cnt) => {
                matRow.SetText2(Units.XNum(cnt));
            });
            UIUtils.AddCodexTooltip(lifetime, matRow.gameObject, matRow, mat.Def);

            if (storage.TrackBurnRate) {
                var burnRate = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, block.Container, idx++);
                burnRate.Load(IconId.CTime, T.FuelBurnRate, null);
                storage.BurnRatePerH.Bind(lifetime, (rate) => {
                    if (rate < 0.001f) {
                        burnRate.SetText2($"{rate:0.#####}/{T.UnitShortHour}");
                    } else {
                        burnRate.SetText2(T.FuelBurnRateH_V1.F(rate));
                    }
                });
            }
        }

        private void LoadUnconfigured(WContentBlock block, ref int idx) {
            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header,
                block.Container, idx++);
            header.Load($"select.{storage.Purpose}".T());

            var availableMaterials = new Dictionary<MatType, int>();
            S.Sys.Inventory.LoadRemainingMaterials(availableMaterials,
                storage.PropertyFilter, storage.GroupFilter, true);

            if (availableMaterials.Count == 0) {
                var noMat = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContent2Text2,
                    block.Container, idx++);
                noMat.Load(IconId.CWarning, T.NoMaterialsAvailable, null);
                UIUtils.AddTip(lifetime, noMat, storage.GetCompatibleItemsTip());
                return;
            }

            foreach (var pair in availableMaterials.OrderByDescending(m => m.Value)) {
                var mat = pair.Key;
                var owned = Units.XNum(pair.Value);
                if (storage.ShowFuelEfficiency) {
                    owned = $"{Texts.Green(Units.EnergyOutput(mat.EnergyOutput, false))} {owned}";
                }

                var row = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                row.Load(mat.IconId, mat.NameT, owned, () => {
                    if (storage.SetTargetMaterial(mat)) {
                        ReloadTab();
                    }
                });

                UIUtils.AddCodexTooltip(lifetime, row.gameObject, row, mat.Def);
            }
        }

        protected override void Cleanup() {
            tile = null;
            storage = null;
            S = null;
        }

        public override string Id => TabId;
    }
}
