using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BeingStorageTab : BaseEntityTab {
        public const string TabId = "BeingStorage";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BeingStorageTab());
        }

        private BeingStorageComp storage;
        private BeingHaulTargetComp haulTarget;
        private GameState S;
        private Lifetime contentsLifetime;
        private WGroup contentsBlock;
        private HashSet<int> expandedGroups;
        public override string TabName => T.Storage;

        public override int Order => 30;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent(out storage)) { return false; }
            S = s;
            tile.TryGetComponent(out haulTarget);
            return true;
        }

        public override bool LoadTopControls(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContent2HeaderProgress, block.Container, idx++);
            header.Label.Load(T.Contents);
            UIUtils.AddTip(lifetime, header.Progress, T.StorageCapacity);
            UpdateCapacityHeader(header);
            storage.OnChange.Bind(lifetime, () => UpdateCapacityHeader(header), false);

            return true;
        }

        public override bool LoadContent(WGroup group) {
            contentsBlock = UISpawn.Block(lifetime, group, 0);
            BuildContentsList();

            storage.OnChange.Bind(contentsLifetime, () => {
                BuildContentsList();
            }, false);

            return true;
        }

        private void UpdateCapacityHeader(WProgressWithLabel header) {
            float pct = storage.MaxCapacity > 0
                ? (float) storage.UsedCapacity / storage.MaxCapacity
                : 0f;
            header.Progress.SetPercent(pct, false);
            header.Progress.SetText(Units.XOutOfY(storage.UsedCapacity, storage.MaxCapacity));
        }

        private WGroup filterBlock;
        private Lifetime filterBlockLife;
        private WButtonIconText filterBtn;

        private string FilterBtnText() {
            var name = haulTarget.FilterModeName;
            if (haulTarget.Mode == StorageFilterMode.AllowSelected
                    || haulTarget.Mode == StorageFilterMode.ForbidSelected) {
                return $"{name} ({haulTarget.SelectedFilterCount})";
            }
            return name;
        }

        private void RefreshFilterBtn() {
            if (filterBtn != null) {
                filterBtn.SetText(FilterBtnText());
            }
        }

        private void ToggleFilterPanel(Lifetime lifetime, WButtonIconText btn) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            // same as MatStorageTab: toggle off here, spawn into the sub panel's lifetime
            if (subPanel.IsShowingFor(btn)) {
                subPanel.Close();
                return;
            }
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.FilterMode);

            filterBlock = UISpawn.Block(l, subPanel.Container, 0);
            LoadFilterBlock(l);
            subPanel.Show(btn);
        }

        private void LoadFilterBlock(Lifetime lifetime) {
            if (filterBlockLife != null) {
                filterBlockLife.Dispose();
            }
            filterBlockLife = lifetime.CreateChild("BeingStorage.Filter");
            var life = filterBlockLife;
            int idx = 0;
            var block = filterBlock;

            var minQ = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            minQ.Item.Load(IconId.CPercentRed, T.MinQuality, null);
            minQ.Slider.Load(storage.MinQuality, (v) => {
                storage.UpdateQualityRange(v);
            });

            storage.QualityChanged.Bind(life, () => {
                minQ.Slider.SetValue(storage.MinQuality, false);
                minQ.Item.SetText2(Units.Percentage(storage.MinQuality));
            });

            var filterMode = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, block.Container, idx++);
            filterMode.Icon.Load(IconId.CFilter);
            filterMode.Dropdown.Load(StorageUtils.FilterModeOptions,
                (int) haulTarget.Mode, (m) => {
                    haulTarget.SetFilterMode((StorageFilterMode) m);
                    LoadFilterBlock(lifetime);
                });

            if (haulTarget.Mode == StorageFilterMode.AllowSelected
                    || haulTarget.Mode == StorageFilterMode.ForbidSelected) {
                var isBio = storage.Flags.HasFlag(BeingStorageFlags.Biological);
                var isRobo = storage.Flags.HasFlag(BeingStorageFlags.Robotic);
                var speciesList = new List<Species>();
                foreach (var s in Species.Known.Values) {
                    if (!(isBio && isRobo)) {
                        if (isBio != s.IsBiological) {
                            continue;
                        }
                    }
                    speciesList.Add(s);
                }
                speciesList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

                foreach (var species in speciesList) {
                    var def = species.Def;
                    var toggle = UISpawn.Widget<WToggleWithIconText2>(life,
                        UIPrefabId.WContent2Text2Toggle, block.Container, idx++);
                    toggle.Item.Load(def.Preview, def.NameT, null);
                    toggle.Toggle.Load(haulTarget.GetFilterState(def), (v) => {
                        haulTarget.SetFilterState(def, v);
                        RefreshFilterBtn();
                    });
                    UIUtils.AddCodexTooltip(life, toggle.gameObject, toggle, def);
                }
            }
        }

        private void BuildContentsList() {
            if (expandedGroups == null) {
                expandedGroups = new HashSet<int>();
            }
            if (contentsLifetime != null) {
                contentsLifetime.Dispose();
            }
            var container = contentsBlock.Container;
            contentsLifetime = lifetime.CreateChild("BeingStorage.Contents");
            int idx = 0;

            if (storage.UsedCapacity < 1) {
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CInfo, T.StorageEmpty);
                return;
            }

            var pool = ArrayPool<BeingData>.Shared;
            var beings = pool.Rent(storage.UsedCapacity);
            var found = S.Query.FindStoredBeingData.Ask(
                storage.StorageId, beings, null, BeingData.BeingSortMode);

            if (found == 0) {
                pool.Return(beings, true);
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CInfo, T.StorageEmpty);
                return;
            }

            // Group beings by species
            var grouped = new Dictionary<Species, List<BeingData>>();
            for (int i = 0; i < found; i++) {
                var bd = beings[i];
                if (!grouped.TryGetValue(bd.Species, out var list)) {
                    list = new List<BeingData>();
                    grouped[bd.Species] = list;
                }
                list.Add(bd);
            }
            pool.Return(beings, true);

            // Render all groups alphabetically by species name
            var sortedGroups = grouped
                .OrderBy(g => g.Key.Name)
                .ToList();

            foreach (var kvp in sortedGroups) {
                var species = kvp.Key;
                var items = kvp.Value;

                var list = UISpawn.Widget<WContentListButton>(contentsLifetime,
                    UIPrefabId.WContent2ListButton, container, idx++);
                list.Load(species.Def.Preview, species.Name, Units.Num(items.Count));

                var capturedItems = items;
                var groupKey = species.IdH;
                Lifetime groupLife = null;
                bool groupExpanded = expandedGroups.Contains(groupKey);
                list.LoadOnToggle(contentsLifetime, (toggleLife, toggleContainer, willShow, wasLoaded) => {
                    if (willShow) {
                        expandedGroups.Add(groupKey);
                    } else {
                        expandedGroups.Remove(groupKey);
                        return wasLoaded;
                    }
                    if (groupLife != null) {
                        groupLife.Dispose();
                    }
                    groupLife = toggleLife.CreateChild("BeingStorage.Group");

                    int beingIdx = 0;
                    foreach (var bd in capturedItems) {
                        SpawnBeingRow(groupLife, toggleContainer, ref beingIdx, bd);
                    }
                    return true;
                }, expanded: groupExpanded);
                UIUtils.AddCodexTooltip(contentsLifetime, list.PopupArea.gameObject,
                    list, species.Def);
            }

            var ejectAllBtn = UISpawn.Widget<WButtonText>(contentsLifetime,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            ejectAllBtn.Load(Texts.Italic(T.StorageEjectAll), () => {
                storage.EjectAll();
                ReloadTab();
            });

            var disposeBtn = UISpawn.Widget<WButtonText>(contentsLifetime,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            disposeBtn.Load(Texts.Italic(T.VerbDispose), () => {
                var counts = Caches.MapIntInt;
                counts.Clear();
                var total = storage.ForEachStored((being) => {
                    var quality = being.Body.BodySlot.Quality;
                    for (int i = 1; i < 10; i++) {
                        if (quality < i / 10f) {
                            counts.GetIncrement(i);
                        }
                    }
                });
                var qc = QuickChoice.Create(T.NounQuality);
                for (int i = 1; i < 10; i++) {
                    if (counts.TryGetValue(i, out var cnt)) {
                        var iCapt = i;
                        qc.AddButton($"< {i * 10}% {Texts.GreyParentheses(Units.XNum(cnt))}", () => {
                            storage.DisposeByQuality(iCapt / 10f);
                            ReloadTab();
                        });
                    }
                }
                qc.AddButton($"{T.All} {Texts.GreyParentheses(Units.XNum(total))}", () => {
                    storage.DisposeByQuality(1000f);
                    ReloadTab();
                });
                A.S.UIQuery.ShowQuickChoice.Ask(qc);
            });
        }

        private void SpawnBeingRow(Lifetime life, RectTransform container,
                ref int idx, BeingData data) {
            float quality = data.Body.BodySlot.Quality;
            var name = BeingUtils.DecoratedBeingName(data, false);
            var w = UISpawn.Widget<WButtonIconText2Progress>(life,
                UIPrefabId.WContent2ProgressButton, container, idx++);
            w.Load(null, name, quality, () => {
                storage.Eject(data);
                BuildContentsList();
            });
            data.Avatar.Load(w.Item.Image);
            UIUtils.AddTipFunc(life, w, () => BeingUtils.BodyTooltip(data, true));
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            var sort = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, group.Container, idx++);
            sort.Load(BeingData.SortModeOptions,
                (int) BeingData.BeingSortMode, (i) => {
                    BeingData.BeingSortMode = (BeingData.SortMode) i;
                    BuildContentsList();
                });
            UIUtils.AddTip(lifetime, sort, T.Sort);

            if (haulTarget != null) {
                filterBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                    UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                filterBtn.Load(IconId.CFilter, FilterBtnText(),
                    () => ToggleFilterPanel(lifetime, filterBtn));
                UIUtils.AddTipFunc(lifetime, filterBtn, () => {
                    var modeName = haulTarget.FilterModeName;
                    var selected = haulTarget.SelectedFilterDefs()
                        .OrderBy(d => d.NameT).ToArray();

                    if (haulTarget.Mode == StorageFilterMode.AllowAll
                            || haulTarget.Mode == StorageFilterMode.ForbidAll
                            || selected.Length == 0) {
                        return $"{T.FilterMode}: {modeName}";
                    }

                    var shown = selected.Take(20).Select(d => d.CodexLink);
                    var list = string.Join(", ", shown);
                    if (selected.Length > 20) {
                        list += $" +{selected.Length - 20}";
                    }
                    return $"{T.FilterMode}: {modeName}<br>{list}";
                });
                haulTarget.ModeB.Bind(lifetime, (_) => {
                    RefreshFilterBtn();
                });

                var prio = UISpawn.Widget<WPriorityUpDown>(lifetime,
                    UIPrefabId.WContent4Priority, group.Container, idx++);
                prio.Up.Load(IconId.WArrowUp, () => {
                    haulTarget.HaulPriority = UIUtils.AdjustPriority0To5(
                        haulTarget.HaulPriority, 1);
                    prio.Texts.SetText2(Units.XOutOfY(haulTarget.HaulPriority, 5));
                });
                prio.Down.Load(IconId.WArrowDown, () => {
                    haulTarget.HaulPriority = UIUtils.AdjustPriority0To5(
                        haulTarget.HaulPriority, -1);
                    prio.Texts.SetText2(Units.XOutOfY(haulTarget.HaulPriority, 5));
                });
                prio.Texts.Load(T.HaulPriority,
                    Units.XOutOfY(haulTarget.HaulPriority, 5));
                UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);
            }

            return true;
        }

        protected override void Cleanup() {
            storage = null;
            haulTarget = null;
            S = null;
            filterBlockLife = null;
            contentsLifetime = null;
            expandedGroups = null;
            filterBtn = null;
        }

        public override string Id => TabId;
    }
}
