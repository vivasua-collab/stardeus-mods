using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;

using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ObjStorageTab : BaseEntityTab {
        public const string TabId = "ObjStorage";

        [SelfInit]
        private static void Register() {
            RegisterTab(new ObjStorageTab());
        }

        private ObjStorageComp storage;
        private ObjHaulTargetComp haulTarget;
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
            filterBlockLife = lifetime.CreateChild("ObjStorage.Filter");
            var life = filterBlockLife;
            int idx = 0;
            var block = filterBlock;

            // Only the filters that can affect what this storage holds - an urn's ashes
            // have neither a meaningful quality nor an owner history.
            if (storage.HasQuality) {
                idx = SpawnQualityFilterRows(life, block.Container, idx);
            }
            if (storage.TracksOwnership) {
                idx = SpawnOwnershipFilterRows(life, block.Container, idx);
            }
            if (storage.HoldsBodyParts) {
                idx = SpawnCrippledFilterRow(life, block.Container, idx);
                idx = SpawnScarredFilterRow(life, block.Container, idx);
            }

            var filterMode = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, block.Container, idx++);
            filterMode.Icon.Load(IconId.CFilter);
            filterMode.Dropdown.Load(StorageUtils.FilterModeOptions,
                (int) haulTarget.Mode, (m) => {
                    haulTarget.SetFilterMode((StorageFilterMode) m);
                    LoadFilterBlock(lifetime);
                    var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                    subPanel.Container.RebuildLayout();
                });

            if (haulTarget.Mode == StorageFilterMode.AllowSelected
                    || haulTarget.Mode == StorageFilterMode.ForbidSelected) {
                Caches.DefList.Clear();
                S.Query.ListKnownStorableObjDefsForGroup.Ask(
                    Caches.DefList, storage.Groups);

                if (Caches.DefList.Count > 0) {
                    var defs = Caches.DefList
                        .OrderBy(d => d.NameT)
                        .ToArray();

                    foreach (var def in defs) {
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
        }

        private int SpawnQualityFilterRows(Lifetime life, RectTransform container, int idx) {
            var minQ = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, container, idx++);
            minQ.Item.Load(IconId.CPercentRed, T.MinQuality, null);
            minQ.Slider.Load(storage.MinQuality, (v) => {
                storage.UpdateQualityRange(v, Mathf.Max(v, storage.MaxQuality));
            });

            var maxQ = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, container, idx++);
            maxQ.Item.Load(IconId.CPercentGreen, T.MaxQuality, null);
            maxQ.Slider.Load(storage.MaxQuality, (v) => {
                storage.UpdateQualityRange(Mathf.Min(v, storage.MinQuality), v);
            });

            storage.QualityChanged.Bind(life, (_) => {
                minQ.Slider.SetValue(storage.MinQuality, false);
                maxQ.Slider.SetValue(storage.MaxQuality, false);
                minQ.Item.SetText2(Units.Percentage(storage.MinQuality));
                if (storage.MaxQuality >= 1f) {
                    maxQ.Item.SetText2("∞");
                } else {
                    maxQ.Item.SetText2(Units.Percentage(storage.MaxQuality));
                }
            });
            return idx;
        }

        // Unlike the equipment policy's version of these toggles, there's no wearer
        // and no species to check - a bin either accepts the gear or it doesn't.
        private int SpawnOwnershipFilterRows(Lifetime life, RectTransform container, int idx) {
            var used = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            used.Item.Load(IconId.CRecycling, T.PolicyForbidSecondHand,
                haulTarget.ForbidSecondHand ? T.Yes : T.No);
            used.Toggle.Load(haulTarget.ForbidSecondHand, on => {
                haulTarget.ForbidSecondHand = on;
                used.Item.SetText2(on ? T.Yes : T.No);
            });
            // UIUtils.AddTip(life, used, T.StorageForbidSecondHandTip);

            var dead = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            dead.Item.Load(IconId.CSkull, T.PolicyForbidDeadMans,
                haulTarget.ForbidDeadMans ? T.Yes : T.No);
            dead.Toggle.Load(haulTarget.ForbidDeadMans, on => {
                haulTarget.ForbidDeadMans = on;
                dead.Item.SetText2(on ? T.Yes : T.No);
            });
            // UIUtils.AddTip(life, dead, T.StorageForbidDeadMansTip);
            return idx;
        }

        // Checked forbids, like the ownership rows above
        private int SpawnCrippledFilterRow(Lifetime life, RectTransform container, int idx) {
            var crippled = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            crippled.Item.Load(IconId.CCrutch, T.BodyPartCrippled,
                haulTarget.ForbidCrippled ? T.VerbForbid : T.VerbAllow);
            crippled.Toggle.Load(haulTarget.ForbidCrippled, on => {
                haulTarget.ForbidCrippled = on;
                crippled.Item.SetText2(on ? T.VerbForbid : T.VerbAllow);
            });
            UIUtils.AddTip(life, crippled, T.BodyPartCrippledTip_V1.F(
                Units.Percentage(Tunables.CrippledPartMaxCondition)));
            return idx;
        }

        private int SpawnScarredFilterRow(Lifetime life, RectTransform container, int idx) {
            var scarred = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            scarred.Item.Load(IconId.CScar, T.NounScars,
                haulTarget.ForbidScarred ? T.VerbForbid : T.VerbAllow);
            scarred.Toggle.Load(haulTarget.ForbidScarred, on => {
                haulTarget.ForbidScarred = on;
                scarred.Item.SetText2(on ? T.VerbForbid : T.VerbAllow);
            });
            return idx;
        }

        private void BuildContentsList() {
            if (expandedGroups == null) {
                expandedGroups = new HashSet<int>();
            }
            if (contentsLifetime != null) {
                contentsLifetime.Dispose();
            }
            var container = contentsBlock.Container;
            contentsLifetime = lifetime.CreateChild("ObjStorage.Contents");
            int idx = 0;

            if (storage.UsedCapacity < 1) {
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CInfo, T.StorageEmpty);
                return;
            }

            var pool = ArrayPool<ObjData>.Shared;
            var objs = pool.Rent(storage.UsedCapacity);
            var found = S.Query.FindStoredObjData.Ask(
                storage.EntityId, objs, null, ObjData.ObjSortMode);

            if (found == 0) {
                pool.Return(objs, true);
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CInfo, T.StorageEmpty);
                return;
            }

            // Group objects by Def
            var grouped = new Dictionary<Def, List<ObjData>>();
            for (int i = 0; i < found; i++) {
                var obj = objs[i];
                if (!grouped.TryGetValue(obj.Def, out var list)) {
                    list = new List<ObjData>();
                    grouped[obj.Def] = list;
                }
                list.Add(obj);
            }
            pool.Return(objs, true);

            // Render all groups alphabetically
            var sortedGroups = grouped
                .OrderBy(g => g.Key.NameT)
                .ToList();

            foreach (var kvp in sortedGroups) {
                var def = kvp.Key;
                var items = kvp.Value;

                var list = UISpawn.Widget<WContentListButton>(contentsLifetime,
                    UIPrefabId.WContent2ListButton, container, idx++);
                // No wearer to judge against and no sparkle for fresh stock - a shelf
                // of new uniforms should stay visually quiet, unlike the equip picker.
                list.Load(def.Preview,
                    ItemOwnership.OriginLabelFor(items, null, def.NameT, markNew: false),
                    Units.Num(items.Count));

                var capturedItems = items;
                var groupKey = def.IdH;
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
                    groupLife = toggleLife.CreateChild("ObjStorage.Group");

                    int objIdx = 0;
                    foreach (var obj in capturedItems) {
                        SpawnObjRow(groupLife, toggleContainer, ref objIdx, obj);
                    }
                    return true;
                }, expanded: groupExpanded);
                UIUtils.AddCodexTooltip(contentsLifetime, list.PopupArea.gameObject, list, def);
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
                int deadCount = 0, usedCount = 0, tatteredCount = 0, scarredCount = 0;
                // Quality buckets would sort ashes by a random number
                var hasQuality = storage.HasQuality;
                var total = storage.ForEachStored((obj) => {
                    if (hasQuality) {
                        for (int i = 1; i < 10; i++) {
                            if (obj.Quality < i / 10f) { counts.GetIncrement(i); }
                        }
                    }
                    if (ItemOwnership.IsDeadMansGear(obj)) { deadCount++; }
                    if (ItemOwnership.IsUsed(obj, null)) { usedCount++; }
                    if (obj.IsTattered) { tatteredCount++; }
                    if (ObjHaulTargetComp.IsScarred(obj)) { scarredCount++; }
                });
                var qc = QuickChoice.Create(hasQuality ? T.NounQuality : T.VerbDispose);
                for (int i = 1; i < 10; i++) {
                    if (counts.TryGetValue(i, out var cnt)) {
                        var iCapt = i; // capture i for lambda
                        qc.AddButton($"< {i * 10}% {Texts.GreyParentheses(Units.XNum(cnt))}", () => {
                            storage.DisposeByQuality(iCapt / 10f);
                            ReloadTab();
                        });
                    }
                }
                if (deadCount > 0) {
                    qc.AddButton(
                        $"{GlyphMap.SpriteSkull} {T.ItemBelongedToDead} {Texts.GreyParentheses(Units.XNum(deadCount))}",
                        () => {
                            storage.DisposeWhere(ItemOwnership.IsDeadMansGear, T.ItemBelongedToDead);
                            ReloadTab();
                        });
                }
                if (usedCount > 0) {
                    qc.AddButton(
                        $"{GlyphMap.SpriteRecycle} {T.ItemSecondHand} {Texts.GreyParentheses(Units.XNum(usedCount))}",
                        () => {
                            storage.DisposeWhere(obj => ItemOwnership.IsUsed(obj, null), T.ItemSecondHand);
                            ReloadTab();
                        });
                }
                if (tatteredCount > 0) {
                    qc.AddButton(
                        $"{GlyphMap.SpriteTattered} {T.ItemTattered} {Texts.GreyParentheses(Units.XNum(tatteredCount))}",
                        () => {
                            storage.DisposeWhere(obj => obj.IsTattered, T.ItemTattered);
                            ReloadTab();
                        });
                }
                if (scarredCount > 0) {
                    qc.AddButton(
                        $"{T.NounScars} {Texts.GreyParentheses(Units.XNum(scarredCount))}",
                        () => {
                            storage.DisposeWhere(ObjHaulTargetComp.IsScarred, T.NounScars);
                            ReloadTab();
                        });
                }
                qc.AddButton($"{T.All} {Texts.GreyParentheses(Units.XNum(total))}", () => {
                    storage.DisposeByQuality(1000f);
                    ReloadTab();
                });
                A.S.UIQuery.ShowQuickChoice.Ask(qc);
            });
        }

        private void SpawnObjRow(Lifetime life, UnityEngine.RectTransform container,
                ref int idx, ObjData obj) {
            // No wearer to judge against here - same "has anyone worn this" reading as
            // the hover tooltip below, minus the sparkle so fresh stock stays quiet.
            var name = ItemOwnership.DecoratedItemName(obj, null, obj.ListName);
            Action eject = () => {
                storage.Eject(obj, false);
                BuildContentsList();
            };
            WBase w;
            if (storage.HasQuality) {
                float quality = obj.Quality;
                if (quality < 0f) {
                    quality = 0f;
                }
                var row = UISpawn.Widget<WButtonIconText2Progress>(life,
                    UIPrefabId.WContent2ProgressButton, container, idx++);
                row.Load(obj.Def.Preview, name, quality, eject);
                row.Item.SetTint(obj.Color);
                w = row;
            } else {
                // A quality bar here would show the random number ashes are born with
                var row = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                row.Load(obj.Def.Preview, name, null, eject);
                row.SetTint(obj.Color);
                w = row;
            }
            // ObjData.Tooltip only describes items and body parts, and errors on anything
            // else (ashes); the codex tooltip still covers those.
            if (obj.Item != null || obj.BodyPart != null) {
                UIUtils.AddTipFunc(life, w, () => obj.Tooltip(S));
            }
            UIUtils.AddCodexTooltip(life, w.gameObject, w, obj.Def);
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            var sort = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, group.Container, idx++);
            sort.Load(ObjData.SortModeOptions,
                (int) ObjData.ObjSortMode, (i) => {
                    ObjData.ObjSortMode = (ObjData.SortMode) i;
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

                var autoHaul = UISpawn.Widget<WToggleText>(lifetime,
                    UIPrefabId.WContent4Toggle, group.Container, idx++);
                autoHaul.Load(T.AutoHaul, haulTarget.IsAutoHaul, (v) => {
                    haulTarget.IsAutoHaul = v;
                });
                UIUtils.AddTip(lifetime, autoHaul, T.AutoHaulInfo);

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
