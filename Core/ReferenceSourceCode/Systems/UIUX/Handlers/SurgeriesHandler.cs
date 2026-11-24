using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using KL.Randomness;
using Game.Systems.Creatures.Surgeries;
using System.Text;
using KL.Collections;

namespace Game.Systems.UIUX {
    public sealed class SurgeriesHandler : BaseWidgetHandler<SurgeriesWidget> {
        private enum BodyTabId { Head, Body, Organs }

        private const int MaxListResults = 30;

        private readonly BeingsSys beings;
        private Lifetime showLifetime;
        private Lifetime listLifetime;
        private Lifetime tabLifetime;
        private int filterIdx;
        private bool scrollToTop;
        private BeingData selectedBeingData;
        private BodyTabId activeTab = BodyTabId.Head;
        private SurgeryListView surgeryList;
        private string searchQuery;

        // Index into FilterOptions
        public const int FilterWorker = 2;
        private static readonly List<string> FilterOptions = new() {
            T.All, T.NounColonist, T.NounWorker, T.BeingGroupAnimal
        };

        public SurgeriesHandler(GameState s, SurgeriesWidget widget) : base(s, widget) {
            beings = s.Sys.Get<BeingsSys>(BeingsSys.SysId);
            widget.Initialize(s, OnSearch);
            SetupButtons();
            widget.OnShow = OnShow;
            s.UIQuery.ToggleSurgeries.SetResponder(OnToggle);
            s.UIQuery.ToggleSurgeriesFor.SetResponder(OnToggleFor);
            s.UIQuery.ToggleSurgeriesFiltered.SetResponder(OnToggleFiltered);
            s.UIQuery.ShowSurgeriesFor.SetResponder(OnShowFor);
            s.UISig.ShowingBeingPanel.AddListener(OnShowingBeingPanel);
            s.Sig.BeingDied.AddListener(OnBeingListChanged);
            s.Sig.BeingInitialized.AddListener(OnBeingListChanged);
            s.Sig.BeingUninitialized.AddListener(OnBeingListChanged);
            s.Sig.SurgicalOperationRegistered.AddListener(OnSurgeryRegistered);
        }

        // Selecting a being anywhere shows their body here too (STA-1772). A click in this
        // window's list selects the being in turn (OnBeingSelected shows their panel, which
        // sends ShowingBeingPanel back), so the same being must end the round trip, and this
        // side must not show the panel again. Without that the two directions called each
        // other, and the listener was removed (d4ff0904a3) instead.
        private void OnShowingBeingPanel(BeingData being) {
            if (!UIShowing.Surgeries || being == null || being == selectedBeingData) { return; }
            EnsureFilterShows(being);
            selectedBeingData = being;
            RefreshList();
        }


        private void OnSurgeryRegistered(SurgicalOperation op, bool yes) {
            if (!UIShowing.Surgeries) { return; }
            if (op.TargetBeing == selectedBeingData) {
                RefreshList();
            } else {
                UpdateFooterButton();
            }
        }

        private void UpdateFooterButton() {
            var allSurgeries = S.Query.GetAllSurgeries.Ask();
            var cnt = allSurgeries.Count;
            if (cnt > 0) {
                widget.FooterButton.SetText($"{T.SurgicalPendingProcedures} ({cnt})");
                widget.ListFooter.SetActiveFast(true);
            } else {
                widget.ListFooter.SetActiveFast(false);
            }
        }


        private void SetupButtons() {
            widget.TabHead.Load(T.BodypartTypeHead, () => SelectTab(BodyTabId.Head), null);
            widget.TabBody.Load(T.BodypartTypeBody, () => SelectTab(BodyTabId.Body), null);
            widget.TabOrgans.Load(T.DamageTypeOrgan, () => SelectTab(BodyTabId.Organs), null);
            widget.FooterButton.Load(T.SurgicalPendingProcedures, OnShowPendingSurgeries);
        }

        private void OnShowPendingSurgeries() {
            surgeryList ??= new SurgeryListView(S);
            if (surgeryList.SetBeing(null)) {
                surgeryList.Show();
            } else {
                surgeryList.Toggle();
            }
        }
        private string ShowPendingProceduresTip() {
            var found = S.Query.GetSurgicalOperationsFor.Ask(selectedBeingData.Body, Caches.SurgicalOps);
            var sb = Caches.StringBuilder3;
            sb.Clear();
            for (int i = 0; i < found; i++) {
                var op = Caches.SurgicalOps[i];
                sb.Append(op.Tooltip(accent: true));
                if (i < found - 1) {
                    sb.Append(GlyphMap.BRHR);
                }
            }
            return sb.ToString();
        }

        private bool OnToggle(bool show) {
            widget.Toggle(show);
            return true;
        }
        private bool OnToggleFiltered(int filter) {
            if (!UIShowing.Surgeries) {
                filterIdx = filter;
                OnToggle(true);
                return true;
            }
            if (filterIdx == filter) {
                OnToggle(false);
                return false;
            }
            filterIdx = filter;
            widget.ListDropdown.SetValue(filterIdx, notify: false);
            RefreshList();
            return true;
        }

        private bool OnToggleFor(BeingData being, bool closeIfShowing) {
            EnsureFilterShows(being);
            if (UIShowing.Surgeries) {
                if (selectedBeingData != being) {
                    OnBeingSelected(being);
                    return true;
                } else {
                    if (closeIfShowing) {
                        OnToggle(false);
                    }
                    return false;
                }
            }
            selectedBeingData = being;
            OnToggle(true);
            return true;
        }
        private bool OnToggleFor(BeingData being) {
            return OnToggleFor(being, true);
        }

        // The filter is kept between visits. If it hides the being the window is opened for,
        // the list would drop them and select someone else.
        private void EnsureFilterShows(BeingData being) {
            if (being == null) { return; }
            var passes = being.IsPlayer ? PassesFilter(being) : EnemyPassesFilter(being);
            if (passes) { return; }
            filterIdx = 0;
            if (UIShowing.Surgeries) {
                widget.ListDropdown.SetValue(filterIdx, notify: false);
            }
        }

        private bool OnShowFor(BeingData being) {
            return OnToggleFor(being, false);
        }

        private void OnBeingListChanged(BeingData b) {
            if (!UIShowing.Surgeries) { return; }
            if (showLifetime != null) { RefreshList(); }
        }

        private void OnShow(Lifetime life) {
            showLifetime = life;
            listLifetime = null;
            scrollToTop = true;
            widget.ListDropdown.Load(FilterOptions, filterIdx, OnFilterChanged);
            UpdateTabSelection();
            RefreshList();
        }

        private void OnFilterChanged(int idx) {
            filterIdx = idx;
            RefreshList();
        }

        private void OnSearch(string query) {
            searchQuery = Strings.ToSearchString(query, allowNull: true);
            RefreshList();
        }

        private void RefreshList() {
            if (showLifetime == null) { return; }
            listLifetime?.Dispose();
            listLifetime = showLifetime.CreateChild("SurgeriesList");
            LoadBeingsList(listLifetime);
            if (selectedBeingData != null) {
                UpdateBodyTabs(selectedBeingData);
            }
            UpdateFooterButton();
            UIUtils.RebuildScroll(widget.ListScroll, scrollToTop);
            scrollToTop = false;
            widget.BodyTabs.RebuildLayout();
        }

        private bool PassesSearchFilter(BeingData b) {
            if (searchQuery == null) { return true; }
            if (b == selectedBeingData) { return true; }
            return Strings.ToSearchString(b.Mind.Name.FullNameOrNameless, allowNull: false).Contains(searchQuery);
        }

        private void LoadBeingsList(Lifetime life) {
            var injured = new List<BeingData>();
            var healthy = new List<BeingData>();
            var dead = new List<BeingData>();
            var enemies = new List<BeingData>();

            foreach (var b in beings.All.Values) {
                if (b.Body.HasFlag(BodyFlags.Missing)) { continue; }
                if (!b.IsPlayer) {
                    if (!EnemyPassesFilter(b)) { continue; }
                    if (b.HasStateFlag(BeingState.Stored) || b.HasStateFlag(BeingState.InColony)) { continue; }
                    if (b.HasStateFlag(BeingState.Dead)) { dead.Add(b); continue; }
                    if (IsIncapacitatedEnemy(b)) { enemies.Add(b); }
                    continue;
                }
                if (!PassesFilter(b)) { continue; }
                if (b.HasStateFlag(BeingState.Dead)) {
                    if (!b.HasStateFlag(BeingState.Stored) && !b.HasStateFlag(BeingState.InColony)) {
                        dead.Add(b);
                    }
                    continue;
                }
                if (IsInjured(b)) { injured.Add(b); }
                else if (IsHealthy(b)) { healthy.Add(b); }
            }

            injured.Sort(CompareBeing);
            healthy.Sort(CompareBeing);
            dead.Sort(CompareBeing);
            enemies.Sort(CompareBeing);

            // Validate current selection is still visible
            if (selectedBeingData != null) {
                bool isDead = selectedBeingData.HasStateFlag(BeingState.Dead);
                bool isEnemy = !selectedBeingData.IsPlayer;
                bool stillValid = beings.All.TryGetValue(selectedBeingData.Id, out var cur)
                    && cur == selectedBeingData
                    && (isDead ? dead.Contains(cur)
                        : isEnemy ? enemies.Contains(cur)
                        : PassesFilter(cur));
                if (!stillValid) { selectedBeingData = null; }
            }

            // Auto-select first visible being
            selectedBeingData ??= injured.Count > 0 ? injured[0]
                : healthy.Count > 0 ? healthy[0]
                : enemies.Count > 0 ? enemies[0]
                : dead.Count > 0 ? dead[0]
                : null;

            int blockIdx = 0;
            int budget = MaxListResults;

            SpawnBeingGroup(life, ref blockIdx, T.CondInjured, injured, ref budget);
            SpawnBeingGroup(life, ref blockIdx, T.BodyStateHealthy, healthy, ref budget);
            SpawnBeingGroup(life, ref blockIdx, T.BeingGroupEnemy, enemies, ref budget);
            SpawnBeingGroup(life, ref blockIdx, T.Dead, dead, ref budget);
        }

        private void SpawnBeingGroup(Lifetime life, ref int blockIdx, string header,
                List<BeingData> beings, ref int budget) {
            if (beings.Count == 0) { return; }

            var filtered = new List<BeingData>();
            foreach (var b in beings) {
                if (PassesSearchFilter(b)) { filtered.Add(b); }
            }
            if (filtered.Count == 0) { return; }

            var block = UISpawn.Block(life, widget.ListContainer, blockIdx++, UIPrefabId.WContentBlock_SidePadded);
            int idx = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WText_Header, block.Container, idx++).Load(header);

            if (budget <= 0) {
                UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, block.Container, idx++)
                    .Load(searchQuery == null
                        ? $"+{filtered.Count}. {T.UseSearchHint}"
                        : $"+{filtered.Count}. {T.UseSearchHint}");
                return;
            }

            int toShow = Math.Min(filtered.Count, budget);
            for (int i = 0; i < toShow; i++) { SpawnBeingRow(life, block.Container, filtered[i], ref idx); }
            budget -= toShow;
            if (filtered.Count > toShow) {
                var remaining = filtered.Count - toShow;
                UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, block.Container, idx++)
                    .Load(searchQuery == null
                        ? $"+{remaining}. {T.UseSearchHint}"
                        : $"+{remaining}. {T.UseSearchHint}");
            }
        }

        private void SpawnBeingRow(Lifetime life, RectTransform container,
                BeingData b, ref int idx) {
            var w = UISpawn.Widget<WBeingListItem2>(life,
                UIPrefabId.WBeingListItem2, container, idx++);
            var capB = b;
            b.Body.StateB.Bind(life, (st) => {
                // var name = BeingUtils.DecoratedBeingName(b, false);
                // var text2 = selectedBeingData == capB
                //     ? Texts.Accented(GlyphMap.ArrowRightLarge)
                //     : BodyUtils.StateName(st, b.Species.IsBiological);
                w.Item.Load(b, () => OnBeingSelected(capB), decorateName: false);
                var ic = 0;

                var iconSet = Caches.StringSet;
                GetStatusFor(b, iconSet, null);
                if (w.StatusIconsLife != null) {
                    w.StatusIconsLife.Dispose();
                }
                var wl = w.StatusIconsLife = life.CreateChild("StatusIcons");
                foreach (var icon in iconSet) {
                    var w2 = UISpawn.Widget<WImage>(wl, UIPrefabId.WStatusIcon_NoRaycast,
                        w.StatusIcons.Container, ic++);
                    if (ic > 14 && ic < Caches.StringList2.Count) {
                        w2.Load(IconId.CThinking);
                        break;
                    } else {
                        w2.Load(icon);
                    }
                }

                w.StatusIcons.SetActiveFast(true);
            });
            UIUtils.AddTipFunc(life, w, () => {
                return GetStatusFor(b, iconSet: null, sb: Caches.StringBuilder4);
            });
            var isSelected = b == selectedBeingData;
            w.ActiveBar.SetActiveFast(isSelected);
            w.SelectedArrow.SetActiveFast(isSelected);
            if (isSelected) {
                var indent = UISpawn.Widget<WIndentWrapper>(life, UIPrefabId.WIndentWrapper, container, idx++);
                indent.SetIndent(40);
                int subIdx = 0;
                var group = indent.Container;
                subIdx = AddPendingSurgeriesButton(life, group, subIdx);
                subIdx = AddMassOperationButton(life, group, IconId.CBodyRemoveAll,
                    SurgicalOpType.RemoveAll, subIdx);
                subIdx = AddMassOperationButton(life, group, IconId.CBodyRemoveBad,
                    SurgicalOpType.RemoveIrreparable, subIdx);
                subIdx = AddMassOperationButton(life, group, IconId.CBodyInstallMissing,
                    SurgicalOpType.FillMissing, subIdx);
                subIdx = AddMassOperationButton(life, group, IconId.CBodyReplaceBad,
                    SurgicalOpType.ReplaceIrreparableAndMissing, subIdx);
                subIdx = AddEuthanizeButton(life, group, subIdx);
                if (subIdx == 0) {
                    indent.Dispose();
                }
            }
        }

        private string GetStatusFor(BeingData b, HashSet<string> iconSet, StringBuilder sb) {
            sb?.Clear();
            iconSet?.Clear();
            if (!b.Body.IsAlive) {
                sb?.AppendLine(Texts.Red(T.Dead));
                iconSet?.Add(IconId.CSkull);
            }
            if (b.Body.IsHealthy && !b.Body.IsUnhealthy) {
                sb?.AppendLine(Texts.Green(T.BodyStateHealthy));
                iconSet?.Add(IconId.CCheck);
            }
            if (b.Body.IsIncapacitated) {
                sb?.AppendLine(Texts.Green(T.BeingStateIncapacitated));
                iconSet?.Add(IconId.CImmobilized);
            }
            // if (b.Body.IsInjured) {
            //     sb?.AppendLine(b.Species.IsBiological ? T.BodyStateInjured : T.BodyStateDamaged);
            //     iconsList?.Add(IconId.CBlood);
            // }
            if (b.Body.IsRecovering) {
                sb?.AppendLine(T.BodyStateRecovering);
                iconSet?.Add(IconId.CHealth);
            }
            if (b.Body.IsForcedToObey) {
                sb?.AppendLine(T.BodypartImplantObeychip);
                iconSet?.Add(IconId.CRobotEyeGreen);
            }
            if (b.Body.IsUnhealthy) {
                sb?.AppendLine(T.BodyStateUnhealthy);
                iconSet?.Add(IconId.CCrutch);
            }
            foreach (var part in b.Body.Slots) {
                if (part.IsDestroyed) {
                    sb?.AppendLine($"{T.BodyPartDestroyed}: {part.NameT}");
                    iconSet?.Add(IconId.CExplosion);
                } else if (part.IsPartRemoved && !part.Type.IsOptional) {
                    sb?.AppendLine($"{T.Missing}: {part.NameT}");
                    iconSet?.Add(IconId.CCross);
                } else if (!part.IsPartRemoved && !part.IsUsableSelf) {
                    sb?.AppendLine($"{T.AdjUnusable}: {part.NameT}");
                    iconSet?.Add(IconId.CCrutch);
                }
                if (part.IsPartRemoved) { continue; }
                if (part.State.IsInfected) {
                    sb?.AppendLine($"{T.AdjInfected}: {part.NameT}");
                    iconSet?.Add(IconId.CPoison);
                }
                if (part.State.IsWelded) {
                    sb?.AppendLine($"{T.AdjNotRemovable}: {part.NameT}");
                    iconSet?.Add(IconId.CCrutch);
                }
                var scars = part.State.Scars?.Count ?? 0;
                if (scars > 0) {
                    var noun = scars > 1 ? T.NounScars : T.NounScar;
                    sb?.AppendLine($"{noun}: {part.NameT}");
                    iconSet?.Add(IconId.CScar);
                }
                var wounds = part.State.Wounds?.Count ?? 0;
                if (wounds > 0) {
                    var noun = wounds > 1 ? T.NounWounds : T.NounWound;
                    sb?.AppendLine($"{noun}: {part.NameT}");
                    iconSet?.Add(IconId.CBlood);
                }
            }
            return sb?.ToString();
        }


        private int AddPendingSurgeriesButton(Lifetime life, RectTransform container, int idx) {
            var count = S.Query.GetSurgicalOperationsFor.Ask(selectedBeingData.Body, Caches.SurgicalOps);
            if (count < 1) { return idx; }
            var btn = UISpawn.Widget<WButtonText>(life,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            btn.Load(Texts.Italic($"{T.SurgicalPendingProcedures} ({count})"), () => {
                surgeryList ??= new SurgeryListView(S);
                if (surgeryList.SetBeing(selectedBeingData)) {
                    surgeryList.Show();
                } else {
                    surgeryList.Toggle();
                }
            });
            UIUtils.AddTipFunc(life, btn, ShowPendingProceduresTip);
            return idx;
        }


        private int AddEuthanizeButton(Lifetime life, RectTransform container, int idx) {
            var bodyComp = selectedBeingData.Being?.GetComponent<BodyComp>();
            if (bodyComp == null) { return idx; }
            if (!bodyComp.CanEuthanize) { return idx; }

            var btn = UISpawn.Widget<WButtonText>(life,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            btn.Load(Texts.Italic(T.VerbEuthanize), () => {
                if (bodyComp.CreateEuthanizeGoal()) {
                    RefreshList();
                }
            });
            return idx;
        }

        private int AddMassOperationButton(Lifetime lifetime, RectTransform container, string iconId,
                SurgicalOpType opType, int idx) {
            if (!selectedBeingData.Body.CanPerformMassAction(opType)) { return idx; }
            if (!selectedBeingData.Body.HasSlotsForOperation(opType)) { return idx; }
            if (!S.Query.CanEnqueueMassSurgicalOp.Ask(selectedBeingData, opType)) { return idx; }

            var btn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            var opName = SurgicalOperation.NameFor(opType, false);
            btn.Load(Texts.Italic(opName), () => {
                if (!TryOrderBulkOperation(opType, iconId, out var failReason)) {
                    if (failReason == null) {
                        failReason = T.OperationFailed;
                    }
                    S.Sig.ShowErrorMessage.Send(failReason);
                    return;
                }

                RefreshList();
            });
            UIUtils.AddTipFunc(lifetime, btn, () => SurgicalOperation.GetMassOpTooltip(selectedBeingData.Body, opType));
            return idx;
        }

        public bool TryOrderBulkOperation(SurgicalOpType opType, string iconId,
                out string failReason) {
            var data = selectedBeingData;
            if (!S.Query.CanEnqueueMassSurgicalOp.Ask(data, opType)) {
                // Removes ": {0}" from the translation
                failReason = T.AnotherTaskInProgress_V1
                    .Replace(":", string.Empty)
                    .Replace("{0}", string.Empty).Trim();
                return false;
            }
            var op = new SurgicalOperation();
            op.TargetBeing = data;
            op.TargetSlot = null;
            op.OpType = opType;
            op.Icon = iconId;
            if (!S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                failReason = op.FailReason;
                return false;
            }
            failReason = null;
            return true;
        }

        private void OnBeingSelected(BeingData b) {
            selectedBeingData = b;
            RefreshList();
            S.UIQuery.ShowBeingPanel.Ask(b);
        }

        private void SelectTab(BodyTabId tab) {
            if (activeTab == tab) { return; }
            activeTab = tab;
            UpdateTabSelection();
            UpdateBodyContainer();
        }

        private void UpdateTabSelection() {
            widget.TabHead.SetSelected(activeTab == BodyTabId.Head);
            widget.TabBody.SetSelected(activeTab == BodyTabId.Body);
            widget.TabOrgans.SetSelected(activeTab == BodyTabId.Organs);
        }

        private void UpdateBodyTabs(BeingData b) {
            bool hasHead = HasBodyPartSlotType(b.Species, BodyPartSlotId.Head);

            widget.TabHead.gameObject.SetActiveFast(hasHead);
            widget.TabOrgans.Load(b.Species.IsBiological ? T.NounBodyPartsBiologicalPlural : T.NounBodyPartsRoboticPlural);
            widget.SelectedBeingName.Load(b.Mind?.Name.FullNameOrNameless);

            if (activeTab == BodyTabId.Head && !hasHead) {
                activeTab = BodyTabId.Body;
                UpdateTabSelection();
            }

            UpdateBodyContainer();
        }

        private void UpdateBodyContainer() {
            tabLifetime?.Dispose();
            tabLifetime = null;
            if (selectedBeingData == null) {
                widget.BeingPreview.Load((Texture2D)null);
                return;
            }
            tabLifetime = showLifetime.CreateChild("SurgeriesBodyTab");
            var preview = selectedBeingData.SurgeryPreview;
            bool isHeadView = activeTab == BodyTabId.Head
                && HasBodyPartSlotType(selectedBeingData.Species, BodyPartSlotId.Head);
            if (isHeadView) {
                preview.EnsureHeadBuilt(selectedBeingData,
                    () => widget.BeingPreview.Load(preview.HeadTex));
            } else {
                preview.EnsureBodyBuilt(selectedBeingData,
                    () => widget.BeingPreview.Load(preview.BodyTex));
            }
            SpawnBodyPartWidgets(tabLifetime, selectedBeingData);
        }

        private void SpawnBodyPartWidgets(Lifetime life, BeingData b) {
            var slots = b.Body.Slots;
            if (slots == null) { return; }
            var container = widget.BodyContainer.Container;
            foreach (var slot in slots) {
                if (!ShouldShowSlot(slot)) { continue; }
                var w = UISpawn.Widget<WBodyPart>(life, UIPrefabId.WBodyPart, container, -1);
                w.Load(life, slot);
                #if UNITY_EDITOR
                w.name = $"WBodyPart_{slot.Id}";
                #endif
                var rt = w.GetComponent<RectTransform>();
                if (b.Species.LooksDef.BodyPartSlotViewOffsets.TryGetValue(slot.Id, out var offset)) {
                    rt.anchoredPosition = offset;
                    if (b.Species.LooksDef.BodyPartSlotLines.TryGetValue(slot.Id, out var linePoints)) {
                        var line = SpawnBodyPartLine(life, slot, linePoints, container);
                        w.SetLine(line);
                    }
                } else {
                    D.Err("BodyPartSlotViewOffsets doesn't contain {0} for species {1}", slot.Id, b.Species.Id);
                    rt.anchoredPosition = Rng.UInsideUnitCircle().normalized * 200f;
                }
            }
            widget.BeingPreview.SetSiblingIdx(0);
        }

        private UILine SpawnBodyPartLine(Lifetime life, BodyPartSlot slot, Vector2[] linePoints, RectTransform container) {
            var line = UISpawn.Widget<UILine>(life, UIPrefabId.UILineBodyParts, container, 0);
            line.Rect.anchoredPosition = Consts.V2Zero;
            line.name = $"UILineBodyParts_{slot.Id}";
            line.Load(linePoints, 1f, Consts.ColorWhite, Consts.ColorWhite);
            return line;
        }


        private bool ShouldShowSlot(BodyPartSlot slot) {
            return activeTab switch {
                BodyTabId.Head => IsHeadTabSlot(slot),
                BodyTabId.Organs => (slot.IsInternal && !IsHeadTabSlot(slot)) || (!selectedBeingData.Species.IsBiological && IsHeadTabSlot(slot)),
                BodyTabId.Body => !slot.IsInternal && !IsHeadTabSlot(slot),
                _ => false,
            };
        }

        private static bool IsHeadTabSlot(BodyPartSlot slot) {
            return slot.Type.IdH == BodyPartSlotIdH.Head
                || slot.Type.IdH == BodyPartSlotIdH.Eye // for bots
                || slot.AttachedToPart?.Type.IdH == BodyPartSlotIdH.Head;
        }

        private bool PassesFilter(BeingData b) {
            if (!b.IsPlayer) { return false; }
            return filterIdx switch {
                1 => b.Species.IsBiological && !b.Species.IsAnimal && !b.Species.IsPet,
                2 => !b.Species.IsBiological,
                3 => b.Species.IsAnimal || b.Species.IsPet,
                _ => true,
            };
        }

        private bool EnemyPassesFilter(BeingData b) {
            return filterIdx switch {
                1 => b.Species.IsBiological && !b.Species.IsAnimal && !b.Species.IsPet,
                2 => !b.Species.IsBiological,
                3 => b.Species.IsAnimal || b.Species.IsPet,
                _ => true,
            };
        }

        private bool IsIncapacitatedEnemy(BeingData b) {
            return !b.IsPlayer && b.Body.IsIncapacitated && !b.HasStateFlag(BeingState.Dead);
        }

        private static bool HasBodyPartSlotType(Species species, string slotType) {
            var types = species.BodyPartSlotTypes;
            return types != null && Array.IndexOf(types, slotType) >= 0;
        }

        // A destroyed part counts even after its pain settled and it stopped being an injury:
        // it still needs surgery
        private static bool IsInjured(BeingData b) {
            return b.Body.IsInjured || b.Body.HasMissingOrDestroyedParts;
        }

        private static bool IsHealthy(BeingData b) {
            return !b.Body.IsInjured
                && !b.Body.HasMissingOrDestroyedParts
                && !b.HasStateFlag(BeingState.Dead);
        }

        private static int CompareBeing(BeingData a, BeingData b) {
            int cmp = b.Body.InjuryPercent.CompareTo(a.Body.InjuryPercent);
            if (cmp != 0) { return cmp; }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
    }
}
