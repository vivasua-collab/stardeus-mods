using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class InventoryHandler : BaseWidgetHandler<InventoryWidget> {
        private readonly Dictionary<MatType, FavMatBlock> favMats = new();
        private readonly List<MatType> foodMats = new();
        private readonly Dictionary<MatType, int> favMatsWithThresholds = new();

        // food is handled though group
        private readonly MatType matWater;
        private readonly MatType matFuel;
        private readonly MatType matMissile;
        private readonly MatType matInterceptorDart;

        private int numFood;
        private int numWater;
        private int numFuel;
        private int numMissile;
        private int numDart;
        private bool inCombatMode;

        public InventoryHandler(GameState s, InventoryWidget widget) : base(s, widget) {
            s.Sig.MatCountChanged.AddListener(OnMatCountChanged);
            s.Query.IsFavMat.SetResponder(IsFavMat);
            s.Sig.SetFavMat.AddListener(SetFavMat);

            matWater = MatType.FastGet(MatIdH.Water);
            matFuel = MatType.FastGet(MatIdH.FusionCell);
            matMissile = MatType.FastGet(MatIdH.Missile);
            matInterceptorDart = MatType.FastGet(MatIdH.InterceptorDart);
            s.UISig.ToggleShipCombatMode.AddListener(OnToggleShipCombatMode);

            foreach (var mat in MatType.Known.Values) {
                if (mat.GroupH == MatGroupIdH.Food) {
                    foodMats.Add(mat);
                }
            }

            widget.RigInventoryButton(ToggleInventoryPanel);
            widget.RigExpandButton(OnExpand);
            widget.Initialize(favMats, favMatsWithThresholds);
            widget.EnableContainers(false);

            LoadTopItems();
            UpdateColumns();

            // Load initial counts
            Ready.When(WhenH.AllSystemsLoaded, () => {
                var mats = Caches.MatCounts;
                mats.Clear();
                s.Sys.Inventory.LoadRemainingMaterials(mats);
                BuildFavMats(s);
                UpdateColumns();
                OnMatCountChanged(mats);
            });
        }

        private (bool isFav, int countThreshold) IsFavMat(MatType mat) {
            if (favMatsWithThresholds.TryGetValue(mat, out var threshold)) {
                return (true, threshold);
            } else {
                return (false, 0);
            }
        }

        private void BuildFavMats(GameState s) {
            foreach (var kv in favMatsWithThresholds) {
                var mat = kv.Key;
                var val = kv.Value;
                var block = CreateMatBlockFor(mat);
                favMats[mat] = block;
            }
        }

        private void LoadTopItems() {
            var zero = Units.NumColoredAbbr(0);
            widget.SetTopItem(0, IconId.CFood, zero, FoodTooltip);
            widget.SetTopItem(1, matWater.IconId, zero, WaterTooltip);
            widget.SetTopItem(2, matFuel.IconId, zero, FuelTooltip);
        }

        private void OnToggleShipCombatMode(bool on) {
            inCombatMode = on;
            if (on) {
                numMissile = -1;
                numDart = -1;
                widget.SetTopItem(0, matMissile.IconId, Units.NumColoredAbbr(0), MissileTooltip);
                widget.SetTopItem(1, matInterceptorDart.IconId, Units.NumColoredAbbr(0), DartTooltip);
            } else {
                numFood = -1;
                numWater = -1;
                numFuel = -1;
                LoadTopItems();
            }
            var mats = Caches.MatCounts;
            mats.Clear();
            S.Sys.Inventory.LoadRemainingMaterials(mats);
            OnMatCountChanged(mats);
        }

        private string MissileTooltip() {
            var sb = BeginTooltip($"{T.Ammo}: {T.ObjectsDevicesRocketpod}");
            AppendDefCount(sb, matMissile.Def, A.S.Sys.Inventory.CountOf(matMissile.Def));
            sb.AppendLine();
            return sb.ToString();
        }

        private string DartTooltip() {
            var sb = BeginTooltip($"{T.Ammo}: {T.WeaponTypePointdefense}");
            AppendDefCount(sb, matInterceptorDart.Def, A.S.Sys.Inventory.CountOf(matInterceptorDart.Def));
            sb.AppendLine();
            return sb.ToString();
        }

        private string FuelTooltip() {
            var sb = BeginTooltip(T.Fuel);
            AppendDefCount(sb, DefIdsH.ObjMaterialsFusionCell);

            var def = The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCellHeavy);
            if (S.Research.IsDefUnlocked(def)) {
                AppendDefCount(sb, DefIdsH.ObjMaterialsFusionCellHeavy);
            }

            AppendSubTitle(sb, T.Ingredients);
            AppendDefCount(sb, DefIdsH.ObjMaterialsPhasium);
            sb.AppendLine();
            return sb.ToString();
        }

        private string WaterTooltip() {
            var sb = BeginTooltip(T.Water);
            AppendDefCount(sb, DefIdsH.ObjMaterialsWater);
            AppendSubTitle(sb, T.Ingredients);
            AppendDefCount(sb, DefIdsH.ObjMaterialsIce);
            sb.AppendLine();
            return sb.ToString();
        }

        private string FoodTooltip() {
            var sb = BeginTooltip(T.Food);
            var counts = Caches.MatCounts;
            counts.Clear();
            A.S.Sys.Inventory.LoadRemainingMaterials(counts);
            foreach (var mc in counts) {
                if (mc.Key.GroupH == MatGroupIdH.Food) {
                    AppendDefCount(sb, mc.Key.Def, mc.Value);
                }
            }
            AppendSubTitle(sb, T.Ingredients);
            foreach (var mc in counts) {
                if (mc.Key.GroupH == MatGroupIdH.RawFood || mc.Key.IdH == MatIdH.Water) {
                    AppendDefCount(sb, mc.Key.Def, mc.Value);
                }
            }
            sb.AppendLine();

            return sb.ToString();
        }

        private StringBuilder BeginTooltip(string title) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.Title(title));
            return sb;
        }

        private void AppendSubTitle(StringBuilder sb, string text) {
            sb.AppendLine();
            sb.Append(Texts.SubTitle(text));
        }

        private void AppendDefCount(StringBuilder sb, int defId) {
            var def = The.Defs.FastGet(defId);
            AppendDefCount(sb, def, A.S.Sys.Inventory.CountOf(def));
        }

        private void AppendDefCount(StringBuilder sb, Def def, int count) {
            sb.Append(def.CodexLink);
            sb.Append(": ");
            sb.AppendLine(Units.NumColoredAbbr(count));
        }

        private void UpdateColumns() {
            var cols = favMats.Count < 12 ? 1 : 3;
            widget.SetColumns(cols);
        }

        private void OnMatCountChanged(Dictionary<MatType, int> counts) {
            foreach (var fav in favMats) {
                var mat = fav.Key;
                var cnt = counts.TryGetValue(mat, out var c) ? c : 0;
                fav.Value.SetCount(cnt);
                if (favMatsWithThresholds.TryGetValue(mat, out var threshold)) {
                    fav.Value.UpdateVisibility(threshold);
                }
            }

            // Update top items
            if (inCombatMode) {
                var cntM = counts.TryGetValue(matMissile, out var m) ? m : 0;
                if (numMissile != cntM) {
                    numMissile = cntM;
                    widget.SetTopItemText(0, Units.NumColoredAbbr(cntM));
                }
                var cntD = counts.TryGetValue(matInterceptorDart, out var d) ? d : 0;
                if (numDart != cntD) {
                    numDart = cntD;
                    widget.SetTopItemText(1, Units.NumColoredAbbr(cntD));
                }
            } else {
                int cntFood = 0;
                foreach (var f in foodMats) {
                    if (counts.TryGetValue(f, out var cnt)) {
                        cntFood += cnt;
                    }
                }
                if (numFood != cntFood) {
                    numFood = cntFood;
                    widget.SetTopItemText(0, Units.NumColoredAbbr(cntFood));
                }
                var cntW = counts.TryGetValue(matWater, out var w) ? w : 0;
                if (numWater != cntW) {
                    numWater = cntW;
                    widget.SetTopItemText(1, Units.NumColoredAbbr(cntW));
                }
            }
            var cntF = counts.TryGetValue(matFuel, out var f2) ? f2 : 0;
            if (numFuel != cntF) {
                numFuel = cntF;
                widget.SetTopItemText(2, Units.NumColoredAbbr(cntF));
            }
        }

        private FavMatBlock CreateMatBlockFor(MatType mat) {
            return new FavMatBlock(mat);
        }

        private void OnExpand() {
            widget.ToggleContainers();
        }

        private void ToggleInventoryPanel() {
            S.UIQuery.ToggleInventoryPanel.Ask();
        }


        private void SetFavMat(MatType mat, bool yes, int threshold) {
            if (yes) {
                favMatsWithThresholds[mat] = threshold;
                if (!favMats.ContainsKey(mat)) {
                    var block = CreateMatBlockFor(mat);
                    favMats[mat] = block;
                    block.SetCount(S.Sys.Inventory.AvailableCount(mat));
                    block.UpdateVisibility(threshold);
                } else {
                    favMats[mat].UpdateVisibility(threshold);
                }
                if (!widget.IsExpanded) {
                    widget.Toggle();
                }
            } else {
                favMatsWithThresholds.Remove(mat);
                if (favMats.TryGetValue(mat, out var block)) {
                    favMats.Remove(mat);
                    block.Dispose();
                }
            }
            // s.Sys.Inventory.RefreshMatUDBFor(mat);
            UpdateColumns();
            widget.RebuildItems();
        }

        public void OnSave(UIUXSysData data) {
            data.FavMats = new();
            foreach (var fm in favMatsWithThresholds) {
                data.FavMats.Add(fm.Key.Id, fm.Value);
            }
        }

        public void OnLoad(UIUXSysData data) {
            favMatsWithThresholds.Clear();
            if (data.FavMats != null) {
                foreach (var fm in data.FavMats) {
                    var mat = MatType.Get(fm.Key);
                    if (mat != null) {
                        favMatsWithThresholds[mat] = fm.Value;
                    }
                }
            }
        }
    }
}
