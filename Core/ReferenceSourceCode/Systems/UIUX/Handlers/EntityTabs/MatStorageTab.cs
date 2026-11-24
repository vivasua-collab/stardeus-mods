using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class MatStorageTab : BaseEntityTab {
        public const string TabId = "MatStorage";

        [SelfInit]
        private static void Register() {
            RegisterTab(new MatStorageTab());
        }

        private MatStorageComp storage;
        /// <summary>
        /// May be null, for example in a Shuttle
        /// </summary>
        private MatHaulTargetComp haulTarget;
        private GameState S;
        private Lifetime contentsLifetime;
        private WGroup contentsBlock;
        public override string TabName => T.Storage;

        public override int Order => 30;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent(out storage)) { return false; }
            if (storage.StorageType == StorageType.TakeOnly) { return false; }
            if (!storage.IsPublic) { return false; }
            if (storage.HideUI) { return false; }
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

            storage.OnChange.Bind(lifetime, () => {
                BuildContentsList();
            }, false);

            // var resBlock = UISpawn.Block(lifetime, group, blockIdx++);
            // var res = UISpawn.Widget<WButtonIconText2>(lifetime,
            //     UIPrefabId.WContent2Button, resBlock.Container, idx++);
            // res.Load(IconId.CInfo, T.Reservations,
            //     Units.Num(storage.Reservations.Count), () => {
            //         S.Sig.ShowContextMenu.Send(storage.Reservations);
            //     });

            return true;
        }

        private void UpdateCapacityHeader(WProgressWithLabel header) {
            header.Progress.SetPercent(storage.FillPercent, false);
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
            // Toggle off here and spawn into the sub panel's own lifetime, like every other
            // sub panel: spawned into the tab's, each open left a block behind in the panel,
            // and the filter stayed above whatever the panel showed next
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
            filterBlockLife = lifetime.CreateChild("MatStorage.Filter");
            var life = filterBlockLife;
            int idx = 0;
            var block = filterBlock;

            var filterMode = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, block.Container, idx++);
            filterMode.Icon.Load(IconId.CFilter);
            filterMode.Dropdown.Load(StorageUtils.FilterModeOptions,
                (int) haulTarget.Mode, (m) => {
                    haulTarget.SetFilterMode((StorageFilterMode)m);
                    LoadFilterBlock(lifetime);
                });

            if (haulTarget.Mode == StorageFilterMode.AllowSelected
                    || haulTarget.Mode == StorageFilterMode.ForbidSelected) {
                var grouped = S.Sys.Inventory.KnownMaterials
                    .Where(t => storage.AcceptsGroup(t.GroupH))
                    .GroupBy(t => t.GroupNameT)
                    .OrderBy(g => g.Key);

                // Snapshot of currently-selected types, read without mutating the
                // underlying filter dict (unlike GetFilterState, which lazily inserts
                // an entry for every type it's asked about).
                var selectedTypes = new HashSet<MatType>(haulTarget.SelectedFilterTypes());

                foreach (var group in grouped) {
                    var materialTypes = group
                        .OrderBy(t => t.NameT)
                        .ToArray();

                    var list = UISpawn.Widget<WContentListButton>(life,
                        UIPrefabId.WContent2ListButton, block.Container, idx++);
                    list.Load(IconId.CFilter, group.Key, null);
                    var countB = new BInt(materialTypes.Count(selectedTypes.Contains));
                    countB.Bind(life, (n) =>
                        list.SetText2(Units.XOutOfY(n, materialTypes.Length)));
                    list.LoadOnToggle(life, (toggleLife, container, willShow, wasLoaded) => {
                        if (!willShow || wasLoaded) {
                            return wasLoaded;
                        }

                        int matIdx = 0;
                        foreach (var matType in materialTypes) {
                            var toggle = UISpawn.Widget<WToggleWithIconText2>(toggleLife,
                                UIPrefabId.WContent2Text2Toggle, container, matIdx++);
                            toggle.Item.Load(matType.IconId, matType.NameT, null);
                            toggle.Toggle.Load(haulTarget.GetFilterState(matType), (v) => {
                                haulTarget.SetFilterState(matType, v);
                                if (v) {
                                    selectedTypes.Add(matType);
                                } else {
                                    selectedTypes.Remove(matType);
                                }
                                countB.Set(materialTypes.Count(selectedTypes.Contains));
                                RefreshFilterBtn();
                            });
                            UIUtils.AddCodexTooltip(toggleLife, toggle.gameObject, toggle, matType.Def);
                        }

                        return true;
                    }, expanded: false);
                }
            }
        }

        private void BuildContentsList() {
            if (contentsLifetime != null) {
                contentsLifetime.Dispose();
            }
            var container = contentsBlock.Container;
            contentsLifetime = lifetime.CreateChild("MatStorage.Contents");
            int idx = 0;
            var comp = storage;
            var sorted = storage.SortedContents;


            if (sorted == null || sorted.Count() == 0) {
                // var header = UISpawn.Widget<WText>(contentsLifetime,
                //     UIPrefabId.WContentHeader, container, idx++);
                // header.Load(T.Contents);
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CInfo, T.StorageEmpty);
                return;
            // } else {
            //     var header = UISpawn.Widget<WButtonText2>(contentsLifetime,
            //         UIPrefabId.WContent2HeaderButton, container, idx++);
            //     header.Load(T.Contents, () => {
            //         storage.DumpContents();
            //         ReloadTab();
            //     }, T.StorageEjectAll);

            }

            foreach (var kvp in sorted) {
                var matType = kvp.Key;
                var w = UISpawn.Widget<WButtonIconText2>(contentsLifetime,
                    UIPrefabId.WContent2Button, container, idx++);
                w.Load(matType.IconId, matType.NameT, Units.Num(kvp.Value), () => {
                    var qc = QuickChoice.Create(T.Eject);
                    // qc.Widget = w.Rect;
                    var cur = comp.Contents.TryGetValue(matType, out var c) ? c : 0;
                    foreach (var num in StorageUtils.QuantityChoiceOptions(cur, false, false)) {
                        string label = StorageUtils.QuantityChoiceLabel(cur, num);
                        qc.AddButton(label, () => {
                            var ejected = comp.Eject(matType, true, amount: num);
                            if (ejected != null) {
                                ejected.IsForbidden = true;
                                ejected.UpdateForbiddenIcon(true);
                            }
                            // Eject already rebuilt this list through the storage's change
                            // signal, and the pool handed this row to another material: it
                            // is not disposed, so hiding or relabelling it changed that one
                            // ("eject all" made another material's row vanish)
                        });
                    }
                    S.UIQuery.ShowQuickChoice.Ask(qc);
                });
                UIUtils.AddCodexTooltip(contentsLifetime, w.gameObject, w, matType.Def);
            }

            var header = UISpawn.Widget<WButtonText>(contentsLifetime,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            header.Load(Texts.Italic(T.StorageEjectAll), () => {
                storage.DumpContents(true);
                ReloadTab();
            });
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            var sort = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, group.Container, idx++);
            sort.Load(storage.SortOptions,
                    (int) MatStorageComp.Sort, (i) => {
                var mode = (MatStorageComp.SortMode) i;
                MatStorageComp.Sort = mode;
                BuildContentsList();
            });
            UIUtils.AddTip(lifetime, sort, T.Sort);

            if (haulTarget == null) {
                return true;
            }

            filterBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            filterBtn.Load(IconId.CFilter, FilterBtnText(),
                () => ToggleFilterPanel(lifetime, filterBtn));
            UIUtils.AddTipFunc(lifetime, filterBtn, () => {
                var modeName = haulTarget.FilterModeName;
                var selected = haulTarget.SelectedFilterTypes()
                    .OrderBy(m => m.NameT).ToArray();

                if (haulTarget.Mode == StorageFilterMode.AllowAll
                        || haulTarget.Mode == StorageFilterMode.ForbidAll
                        || selected.Length == 0) {
                    return $"{T.FilterMode}: {modeName}";
                }

                var shown = selected.Take(20).Select(m => m.Def.CodexLink);
                var list = string.Join(", ", shown);
                if (selected.Length > 20) {
                    list += $" +{selected.Length - 20}";
                }
                return $"{T.FilterMode}: {modeName}<br>{list}";
            });
            haulTarget.ModeB.Bind(lifetime, (_) => {
                RefreshFilterBtn();
            });

            var autoHaul = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent4Toggle, group.Container, idx++);
            autoHaul.Load(T.AutoHaul, haulTarget.IsAutoHaul, (v) => {
                haulTarget.IsAutoHaul = v;
            });
            UIUtils.AddTip(lifetime, autoHaul, T.AutoHaulInfo);

            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime,
                UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => {
                haulTarget.HaulPriority = UIUtils.AdjustPriority0To5(haulTarget.HaulPriority, 1);
                prio.Texts.SetText2(Units.XOutOfY(haulTarget.HaulPriority, 5));
            });
            prio.Down.Load(IconId.WArrowDown, () => {
                haulTarget.HaulPriority = UIUtils.AdjustPriority0To5(haulTarget.HaulPriority, -1);
                prio.Texts.SetText2(Units.XOutOfY(haulTarget.HaulPriority, 5));
            });
            prio.Texts.Load(T.HaulPriority, Units.XOutOfY(haulTarget.HaulPriority, 5));
            UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);


            return true;
        }

        protected override void Cleanup() {
            storage = null;
            haulTarget = null;
            S = null;
            filterBlockLife = null;
            contentsLifetime = null;
            filterBtn = null;
        }

        public override string Id => TabId;
    }
}
