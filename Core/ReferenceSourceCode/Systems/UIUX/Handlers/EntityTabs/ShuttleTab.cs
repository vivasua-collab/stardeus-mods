using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ShuttleTab : BaseEntityTab {
        public const string TabId = "Shuttle";

        [SelfInit]
        private static void Register() {
            RegisterTab(new ShuttleTab());
        }

        private Tile tile;
        private ShuttleComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.Shuttle;

        public override bool ShowFor(GameState s, Tile tile) {
            tile = EntityUtils.ParentObj(tile);
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<ShuttleComp>(out var c)) { return false; }
            S = s;
            this.tile = tile;
            comp = c;
            return true;
        }
        private bool OnConfirm(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                The.SysSig.ShowTooltipWithTimeout.Send(
                    Texts.Red(T.EnterName), true, 1f);
                return false;
            }
            comp.NameB.Set(name);
            var so = comp.SpaceObject;
            so?.UpdateName(name);
            return true;
        }
        public void BeginRename() {
            var qc = QuickChoice.Create(comp.Name);
            string newName = comp.Name;
            Func<bool> confirm = () => OnConfirm(newName);
            Action<string> submit = (v) => {
                if (confirm()) {
                    S.UIQuery.CancelQuickChoice.Ask();
                } else {
                    UISounds.PlayActionDenied();
                }
            };
            var in1 = new QuickChoice.Input() {
                AutoFocus = true,
                Value = newName,
                OnChange = (v) => newName = v,
                OnSubmit = submit,
            };
            qc.AddInput(T.EnterName, in1);
            var btn = new QuickChoice.Button() {
                OnClickValidate = confirm,
                AutoClose = true,
            };
            qc.AddButton(T.Confirm, btn);
            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public override bool LoadContent(WGroup group) {
            comp.StateChangedB.Bind(lifetime, () => Coroutines.OnNextFrame(ReloadTab), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            var exp = S.Sys.Expedition.FindForShuttle(comp.EntityId);

            var header = UISpawn.Widget<WButtonText2>(lifetime,
                UIPrefabId.WContent2HeaderButton, block.Container, idx++);
            header.Load(comp.Name, BeginRename, T.Rename);
            comp.NameB.Bind(lifetime, n => header.SetText(n));

            // Status row — readiness or current expedition target
            var statusRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            statusRow.Load(ReadinessIcon(exp), ReadinessText(exp));
            UIUtils.AddTip(lifetime, statusRow, comp.MiningPowerText);

            // Expedition stage row — shown only while on an expedition
            if (exp != null) {
                var expRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                expRow.Load(IconId.CPickaxe, exp.CurrentStage.Title, null);
                UIUtils.AddTipFunc(lifetime, expRow,
                    () => exp.CurrentStage?.TitleTooltip ?? comp.MiningPowerText);

                // Live stage updates — rebind subtitle on each stage change
                Lifetime subtitleLife = null;
                exp.OnChange.Bind(lifetime, () => {
                    if (exp.IsDone) {
                        ReloadTab();
                        return;
                    }
                    subtitleLife?.Dispose();
                    if (expRow.IsDisposed) { return; }
                    statusRow.Load(ReadinessIcon(exp), ReadinessText(exp));
                    var stage = exp.CurrentStage;
                    subtitleLife = lifetime.CreateChild("ShuttleTab.ExpStage");
                    if (stage.TitleB == null) {
                        expRow.SetText(stage.Title);
                    } else {
                        stage.TitleB.Bind(subtitleLife, t => expRow.SetText(t ?? T.GridUpdating));
                    }
                    // stage.Subtitle.Bind(subtitleLife, s => expRow.SetText2(s));
                }, false);
            }

            // Storage fill row — live-updated from storage changes
            var storage = comp.Tile.MaterialStorage;
            if (storage != null) {
                var storageRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                    UIPrefabId.WContent2Progress, block.Container, idx++);
                UpdateStorageRow(storageRow, storage);
                storage.OnChange.Bind(lifetime,
                    () => UpdateStorageRow(storageRow, storage), false);
            }

            // Mining power row — live-bound to comp.MiningPowerB
            var powerRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            comp.MiningPowerB.Bind(lifetime, v => {
                powerRow.Load(IconId.CDrill, T.MiningPower, Units.Num(v));
            });

            // Mining depth row — live-bound to comp.MiningDepthB
            var depthRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            comp.MiningDepthB.Bind(lifetime, v => {
                depthRow.Load(IconId.CDrill, T.MiningDepthMax, Units.Depth(v));
            });

            // Estimated mining rate — shown for resource expeditions only
            // if (exp != null && !exp.IsExploration && exp.Target != null) {
            //     if (S.Sys.Planets.TryEstimateExpeditionMiningRate(
            //             exp.Target, exp.MiningFactor, out var rate, out _) && rate > 0f) {
            //         var rateRow = UISpawn.Widget<WIconText2>(lifetime,
            //             UIPrefabId.WContent2Text2, block.Container, idx++);
            //         rateRow.Load(IconId.CPickaxe, T.NounMiningRate,
            //             T.EstMiningRate_V1.F(rate));
            //     }
            // }

            return true;
        }

        private string ReadinessIcon(Expedition exp) {
            if (comp.IsLost) { return IconId.CWarning; }
            if (tile.Health.IsDamaged) { return IconId.CWarning; }
            if (exp != null || comp.IsInUse) { return IconId.CDestination; }
            return IconId.CCheck;
        }

        private string ReadinessText(Expedition exp) {
            if (comp.IsLost) { return Texts.Red(T.ShuttleLostInfo); }
            if (tile.Health.IsDamaged) { return Texts.Red(T.Damaged); }
            if (exp != null) { return exp.Target.StarmapLink; }
            if (comp.IsInUse) { return Texts.Yellow(T.AdjBusy); }
            return Texts.Green(T.ReadyForExpedition);
        }

        private static void UpdateStorageRow(WIconText2Progress storageRow,
                MatStorageComp storage) {
            var used = storage.UsedCapacity;
            var total = storage.MaxCapacity;
            storageRow.Load(IconId.COpenPackage, T.Storage,
                total > 0 ? (float) used / total : 0f,
                Units.XOutOfY(used, total));
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            btn.Load(IconId.CPickaxe, T.Expeditions, () => {
                var exp = S.Sys.Expedition.FindForShuttle(comp.EntityId);
                if (exp != null) {
                    A.S.UIQuery.ShowExpedition.Ask(exp);
                } else {
                    A.S.UIQuery.ShowExpeditions.Ask(!UIShowing.Expeditions);
                }
            });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
