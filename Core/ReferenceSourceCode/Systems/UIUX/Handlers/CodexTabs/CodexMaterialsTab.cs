using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.BodyParts;
using Game.Research;
using Game.Systems.Inventory;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class CodexMaterialsTab : BaseCodexTab {
        public override string Id => "Materials";
        public override int Order => 15;
        public override string TabName => T.MenuMaterials;
        public override string TabIcon => IconId.WMaterials;
        public override bool HasFooter => selected != null && selected.Count > 0;

        private readonly Dictionary<MatType, int> materialCounts = new();
        private readonly Dictionary<Def, CodexMaterialEntry> entriesByDef = new();
        private readonly Dictionary<string, List<CodexMaterialEntry>> entriesByGroup = new();
        private readonly List<string> orderedGroups = new();
        private readonly List<ObjData> foundObjects = new(64);

        private string currentGroup;
        private CodexMaterialEntry selected;
        private WButtonIconText2 selectedButton;
        private int findCycleIdx;
        private Def lastFindDef;

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            BuildVisibleEntries();

            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            foreach (var groupName in orderedGroups) {
                if (!entriesByGroup.TryGetValue(groupName, out var items)) {
                    continue;
                }

                var listButton = UISpawn.Widget<WContentListButton>(Lifetime,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listButton.Load(items[0].IconId, groupName, Units.Num(items.Count));

                var capturedGroup = groupName;
                listButton.LoadOnToggle(Lifetime, (life, container, willShow, wasLoaded) => {
                    return ToggleGroup(life, container, willShow, wasLoaded, capturedGroup);
                }, currentGroup != null && currentGroup == groupName);
            }
        }

        private bool ToggleGroup(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded, string groupName) {
            if (!willShow || wasLoaded) {
                return wasLoaded;
            }

            if (!entriesByGroup.TryGetValue(groupName, out var items)) {
                return false;
            }

            int idx = 0;
            foreach (var entry in items) {
                var capturedEntry = entry;
                var item = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                bool isSelected = entry == selected;
                var title = isSelected
                    ? Texts.Accented(entry.ListName, bold: false)
                    : entry.ListName;
                if (isSelected) {
                    selectedButton = item;
                }
                item.Load(entry.IconId, title,
                    entry.Count > 0 ? Units.XNum(entry.Count) : null, () => {
                    if (selected == capturedEntry) {
                        return;
                    }

                    var previous = selected;
                    var previousButton = selectedButton;
                    currentGroup = capturedEntry.GroupName;
                    selected = capturedEntry;
                    selectedButton = item;
                    previousButton?.SetText(previous.ListName);
                    item.SetText(Texts.Accented(capturedEntry.ListName, bold: false));
                    RefreshContentAndFooter();
                });
                if (entry.HasCustomTint) {
                    item.SetTint(entry.IconTint);
                }
                UIUtils.AddCodexTooltip(life, item.gameObject, item, entry.Def);
            }

            return true;
        }

        public override void LoadContent(WGroup content) {
            if (selected == null) {
                return;
            }

            var block = UISpawn.Block(Lifetime, content, 0);
            int idx = 0;

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(selected.Def.NameWithPrice(S, false));

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(Texts.Beautify(selected.Def.ExtendedDesc(false)));
            descW.RebuildTextLayout();

            if (selected.Def.PreviewSprite != null) {
                var capturedSprite = selected.Def.PreviewSprite;
                var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                    UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                imgW.Load(null, () => {
                    The.SysSig.ShowImageBlowout.Send(capturedSprite);
                }, 256);
                imgW.SetSprite(capturedSprite);
                UIUtils.AddTip(Lifetime, imgW, T.ClickToEnlarge);
                Coroutines.OnNextFrame(content.RebuildLayout);
            }
        }

        public override void LoadFooter(WGroup footer) {
            if (selected == null || selected.Count <= 0) {
                return;
            }

            var findBtn = UISpawn.Widget<WButtonText>(Lifetime,
                UIPrefabId.WPanelButton, footer.Container, 0);
            findBtn.Load(T.Find, FindSelected);
        }

        private void BuildVisibleEntries() {
            entriesByDef.Clear();
            entriesByGroup.Clear();
            orderedGroups.Clear();

            S.Sys.Inventory.LoadRemainingMaterials(materialCounts);
            var objCounts = S.Query.ObjCounts.Ask();

            foreach (var matType in MatType.Known.Values) {
                var def = matType.Def;
                if (def == null) {
                    continue;
                }
                if (!Researchable.IsEmptyPrereq(def)
                        && !S.Research.IsDiscovered(Hashes.S(def.Researchable.Prerequisites[0]))) {
                    continue;
                }

                entriesByDef[def] = new CodexMaterialEntry {
                    Def = def,
                    ListName = matType.NameT,
                    GroupName = matType.GroupNameT,
                    IconId = matType.IconId,
                    IconTint = matType.IconTint,
                    HasCustomTint = true,
                    Count = materialCounts.TryGetValue(matType, out var materialCount)
                        ? materialCount
                        : 0,
                };
            }

            foreach (var inventoryItem in InventoryItemDef.All) {
                AddDefEntry(inventoryItem.Value.Def, objCounts);
            }
            foreach (var bodyPart in BodyPartDef.All) {
                AddDefEntry(bodyPart.Value.Def, objCounts);
            }

            foreach (var entry in entriesByDef.Values
                    .OrderBy(value => value.GroupName)
                    .ThenBy(value => value.ListName)) {
                if (!MatchesSearch(entry.ListName, entry.GroupName, entry.Def?.NameT)) {
                    continue;
                }

                if (!entriesByGroup.TryGetValue(entry.GroupName, out var groupEntries)) {
                    groupEntries = new List<CodexMaterialEntry>();
                    entriesByGroup[entry.GroupName] = groupEntries;
                    orderedGroups.Add(entry.GroupName);
                }
                groupEntries.Add(entry);
            }

            if (selected != null && !entriesByDef.TryGetValue(selected.Def, out selected)) {
                selected = null;
            }
            selected ??= entriesByDef.Values
                .OrderBy(value => value.GroupName)
                .ThenBy(value => value.ListName)
                .FirstOrDefault();
            currentGroup = selected?.GroupName;
        }

        private void AddDefEntry(Def def, Dictionary<Def, int> objCounts) {
            if (def == null || entriesByDef.ContainsKey(def)) {
                return;
            }
            if (!Researchable.IsEmptyPrereq(def)
                    && !S.Research.IsDiscovered(Hashes.S(def.Researchable.Prerequisites[0]))) {
                return;
            }

            entriesByDef[def] = new CodexMaterialEntry {
                Def = def,
                ListName = def.NameT,
                GroupName = ResolveGroupName(def),
                IconId = def.Preview,
                IconTint = Consts.ColorWhite,
                Count = objCounts.TryGetValue(def, out var count) ? count : 0,
            };
        }

        private static string ResolveGroupName(Def def) {
            var marketItem = MarketItem.FindFor(def);
            if (marketItem?.FirstGroup != null) {
                return marketItem.FirstGroup.NameT;
            }
            if (InventoryItemDef.HasForDef(def.IdH)) {
                return T.MarketGroupNameEquipment;
            }
            if (BodyPartDef.HasForDef(def.IdH)) {
                return T.MarketGroupNameBodypart;
            }
            return T.UiGroupMisc;
        }

        private void RefreshContentAndFooter() {
            LoadContentSection(widget.Content);
            widget.ShowFooter(HasFooter);
            if (HasFooter) {
                LoadFooterSection(widget.Footer);
            }
        }

        private void FindSelected() {
            if (selected?.Def == null) {
                return;
            }

            if (selected.Def.MatType != null) {
                FindMaterial(selected.Def.MatType);
                return;
            }

            FindObject(selected.Def);
        }

        private void FindMaterial(MatType matType) {
            var storage = S.Sys.Inventory.FindNextMat(matType, true);
            if (storage == null) {
                return;
            }

            S.Sig.SelectEntity.Send(storage.Entity);
            EntityUtils.CameraFocusOn(storage.Entity);
        }

        private void FindObject(Def def) {
            if (lastFindDef != def) {
                lastFindDef = def;
                findCycleIdx = 0;
                foundObjects.Clear();
                S.Query.FindObjs.Ask(foundObjects, objData => objData.Def == def);
            }

            if (foundObjects.Count == 0) {
                return;
            }

            var objData = foundObjects[findCycleIdx++];
            var entity = EntityFor(objData);
            if (entity == null) {
                return;
            }

            S.Sig.SelectEntity.Send(entity);
            EntityUtils.CameraFocusOn(entity);
            if (findCycleIdx >= foundObjects.Count) {
                findCycleIdx = 0;
            }
        }

        private Entity EntityFor(ObjData objData) {
            var entity = objData.Obj;
            if (entity != null) {
                return entity;
            }
            if (objData.State == ObjState.Stored) {
                return S.FindEntity<Tile>(objData.StorageId);
            }
            if (objData.State == ObjState.Equipped) {
                var ownerData = S.Query.FindBeingData.Ask(objData.BeingDataId);
                return ownerData?.Being;
            }
            return null;
        }

        private sealed class CodexMaterialEntry {
            public Def Def;
            public string ListName;
            public string GroupName;
            public string IconId;
            public Color IconTint;
            public bool HasCustomTint;
            public int Count;
        }
    }
}
