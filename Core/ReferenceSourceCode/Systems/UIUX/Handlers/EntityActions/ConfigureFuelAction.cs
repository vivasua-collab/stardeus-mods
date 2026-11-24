using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ConfigureFuelAction : BaseEntityAction {
        public const string ActionId = "ConfigureFuel";

        [SelfInit]
        private static void Register() {
            RegisterAction(new ConfigureFuelAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!CountConfigurable(tiles, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CCog, T.VerbConfigure,
                () => ShowMaterialChoice(s, tiles));
            return widget;
        }

        private static void ShowMaterialChoice(GameState s, List<Tile> tiles) {
            var storages = new List<RefillableStorageComp>();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanConfigure(p, out var storage)) {
                    storages.Add(storage);
                }
            }
            if (storages.Count == 0) {
                return;
            }

            var first = storages[0];
            var availableMaterials = new Dictionary<MatType, int>();
            s.Sys.Inventory.LoadRemainingMaterials(
                availableMaterials, first.PropertyFilter, first.GroupFilter, true);

            if (availableMaterials.Count == 0) {
                return;
            }

            var qc = QuickChoice.Create(T.SelectMaterial);
            var allMats = availableMaterials.OrderByDescending(m => m.Value);
            foreach (var mat in allMats.Take(10)) {
                var m = mat.Key;
                var details = Units.XNum(mat.Value);
                if (first.ShowFuelEfficiency) {
                    details = $"{Texts.Green(Units.EnergyOutput(m.EnergyOutput, false))} {details}";
                }
                var title = $"{m.NameT} ({details})";
                var matKey = m;
                qc.AddButton(title, () => {
                    SetMaterial(storages, matKey);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (allMats.Count() > 10) {
                qc.AddLabel(T.MoreOptionsAvailableIndividualDevice);
            }
            qc.Show();
        }

        private static void SetMaterial(List<RefillableStorageComp> storages, MatType mat) {
            foreach (var storage in storages) {
                if (storage.Entity.IsActive) {
                    storage.SetTargetMaterial(mat);
                }
            }
        }

        private static bool CountConfigurable(List<Tile> tiles, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanConfigure(p, out _)) {
                    c++;
                }
            }
            cnt = c;
            return cnt > 0;
        }

        private static bool CanConfigure(Tile tile, out RefillableStorageComp storage) {
            storage = null;
            if (!tile.IsActive || !tile.IsConstructed) {
                return false;
            }
            if (!tile.TryGetComponent(out storage)) {
                return false;
            }
            return !storage.IsConfigured;
        }
    }
}
