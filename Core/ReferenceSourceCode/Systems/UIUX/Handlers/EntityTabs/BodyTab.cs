#if false
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BodyTab : BaseEntityTab {
        public const string TabId = "Body";
        private const int InstallPageSize = 20;
        [SelfInit]
        private static void Register() {
            RegisterTab(new BodyTab());
        }

        [ExecuteOnReload]
        private static void Reset() {
            slotExpanded.Clear();
        }

        private BeingData being;
        private Body body;
        private BodyComp bodyComp;
        private GameState S;

        private static Dictionary<int, bool> slotExpanded = new Dictionary<int, bool>();

        public override string Id => TabId;
        public override int Order => 15;
        public override string TabName => T.NounBody;

        public override bool ShowFor(GameState s, BeingData being) {
            S = s;
            if (being.Body != null && being.Being != null
                    && being.Being.TryGetComponent<BodyComp>(out var bc)) {
                this.being = being;
                this.body = being.Body;
                this.bodyComp = bc;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            int gidx = 0;
            LoadSurgeries(group, gidx++);
            LoadBodyParts(group, gidx++);
            return true;
        }

        private void LoadBodyParts(WGroup group, int gidx) {
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.BodyConfiguration);

            // Find the root body slot (no parent)
            BodyPartSlot rootSlot = null;
            var slots = body.Slots;
            for (int i = 0; i < slots.Length; i++) {
                if (slots[i].AttachedToPart == null) {
                    rootSlot = slots[i];
                    break;
                }
            }

            if (rootSlot == null) { return; }

            // Show non-removable children of root (e.g. Head) as expandables first
            if (!Arrays.IsEmpty(rootSlot.AttachedParts)) {
                for (int i = 0; i < rootSlot.AttachedParts.Length; i++) {
                    var child = rootSlot.AttachedParts[i];
                    if (!child.Type.IsRemovable && !Arrays.IsEmpty(child.AttachedParts)) {
                        AddExpandableSlotWidget(lifetime, block.Container, child, idx++);
                    }
                }
            }

            // Then show the root (Body) as expandable
            AddExpandableSlotWidget(lifetime, block.Container, rootSlot, idx++);
        }

        private void AddSlotWidget(RectTransform container, BodyPartSlot slot, int idx) {
            AddSlotWidget(lifetime, container, slot, idx);
        }

        private void AddSlotWidget(Lifetime life, RectTransform container,
                BodyPartSlot slot, int idx) {
            string icon;
            string name;
            string text2;
            GetSlotDisplayInfo(slot, out icon, out name, out text2);

            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx);
            w.Load(icon, name, text2, () => ShowSubPanelFor(w, slot));

            UIUtils.AddTipFunc(life, w, () => GetSlotTooltip(slot));
        }

        private void AddExpandableSlotWidget(Lifetime parentLife,
                RectTransform container, BodyPartSlot slot, int idx) {
            string icon;
            string name;
            string text2;
            GetSlotDisplayInfo(slot, out icon, out name, out text2);

            var slotTypeId = slot.Type.IdH;
            if (!slotExpanded.TryGetValue(slotTypeId, out var preExpanded)) {
                preExpanded = slotTypeId == BodyPartSlotIdH.Body || slotTypeId == BodyPartSlotIdH.Head;
            }

            var listButton = UISpawn.Widget<WContentListButton>(parentLife,
                UIPrefabId.WContent2ListButton, container, idx);
            listButton.Load(icon, name, text2);
            listButton.LoadOnToggle(parentLife, (life, cont, willShow, wasLoaded) => {
                slotExpanded[slotTypeId] = willShow;
                return ToggleAttachedParts(life, cont, willShow, wasLoaded, slot);
            }, preExpanded);
            UIUtils.AddTipFunc(parentLife, listButton.PopupArea, () => GetSlotTooltip(slot));
        }

        private bool ToggleAttachedParts(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded, BodyPartSlot parentSlot) {
            if (!willShow) { return wasLoaded; }
            if (wasLoaded) { return true; }

            int idx = 0;

            // Add a self-action item for install/replace/remove on the parent part
            if (parentSlot.Type.IsRemovable) {
                AddSlotSelfActionWidget(life, container, parentSlot, idx++);
            }

            // Add direct children in body.Slots order
            AddDirectChildrenInSlotOrder(life, container, parentSlot, ref idx);
            return true;
        }

        private void AddSlotSelfActionWidget(Lifetime life, RectTransform container,
                BodyPartSlot slot, int idx) {
            string icon;
            string name;
            string text2;
            GetSlotDisplayInfo(slot, out icon, out name, out text2);

            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx);
            w.Load(icon, name, text2, () => ShowSubPanelFor(w, slot));
            UIUtils.AddTipFunc(life, w, () => GetSlotTooltip(slot));
        }

        private void AddDirectChildrenInSlotOrder(Lifetime life, RectTransform container,
                BodyPartSlot parentSlot, ref int idx) {
            var slots = body.Slots;
            for (int i = 0; i < slots.Length; i++) {
                var slot = slots[i];
                if (slot.AttachedToPart != parentSlot) { continue; }
                // Skip non-removable children with their own expandable at top level
                if (!slot.Type.IsRemovable && !Arrays.IsEmpty(slot.AttachedParts)
                        && parentSlot.AttachedToPart == null) {
                    continue;
                }
                if (!Arrays.IsEmpty(slot.AttachedParts)) {
                    AddExpandableSlotWidget(life, container, slot, idx++);
                } else {
                    AddSlotWidget(life, container, slot, idx++);
                }
            }
        }

        private void GetSlotDisplayInfo(BodyPartSlot slot, out string icon,
                out string name, out string text2) {
            if (slot.InstalledDef == null) {
                if (slot.IsDestroyed) {
                    icon = IconId.CCross;
                    name = $"{Texts.Grey(Texts.Strike(slot.NameT))} {Texts.Yellow(GlyphMap.ArrowRight)} {Texts.Grey(T.BodyPartDestroyed)}";
                } else if (slot.Type.IsOptional) {
                    icon = IconId.CInfo;
                    name = $"{Texts.Grey(slot.NameT)} {Texts.Yellow(GlyphMap.ArrowRight)} {Texts.Grey(T.Empty)}";
                } else {
                    icon = IconId.CCrutch;
                    name = $"{Texts.Grey(slot.NameT)} {Texts.Yellow(GlyphMap.ArrowRight)} {Texts.Red(T.Missing)}";
                }
                text2 = null;
            } else {
                var partName = slot.InstalledDef.NameT;
                icon = slot.InstalledDef.IconId;
                if (slot.State.IsInfected) {
                    partName = Texts.Yellow(partName);
                    icon = IconId.CPoison;
                } else if (!slot.State.IsUsableSelf) {
                    if (slot.State.IsDestroyed) {
                        partName = Texts.Red(Texts.Strike(partName));
                        icon = IconId.CCrutch;
                    } else {
                        partName = Texts.Yellow(Texts.Strike(partName));
                        icon = IconId.CBlood;
                    }
                } else {
                    if (!slot.State.IsUsableParent) {
                        partName = Texts.Strike(partName);
                    }
                    if (Arrays.Count(slot.State.Wounds) > 0) {
                        partName = Texts.Yellow(partName);
                        icon = IconId.CBlood;
                    }
                }
                var hasPendingOp = S.Query.FindSurgicalOperation.Ask(slot, true) != null;
                if (hasPendingOp) {
                    name = $"{Texts.Accented(GlyphMap.TriangleRight, bold: false)} {slot.NameT} {Texts.Yellow(GlyphMap.ArrowRight)} {partName}";
                } else {
                    name = $"{slot.NameT} {Texts.Yellow(GlyphMap.ArrowRight)} {partName}";
                }
                text2 = Units.PercentageColored(slot.State.Quality, clamp: false);
                if (slot.State.IsWelded) {
                    icon = IconId.CChainLink;
                }
                if (slot.IsDestroyed) {
                    icon = IconId.CCross;
                }
            }
            if (slot.IsDestroyed) {
                name = Texts.Strike(name);
            }
        }

        private string GetSlotTooltip(BodyPartSlot slot) {
            return ShowBodyPartTooltip(slot);
        }

        private void ShowSubPanelFor(WBase itemWidget, BodyPartSlot slot) {
            if (being.Being == null) {
                S.Sig.ShowErrorMessage.Send(T.NotAvailable);
                return;
            }
            var surgery = S.Query.FindSurgicalOperation.Ask(slot, true);
            if (surgery != null) {
                // Already has a pending surgery, show the surgery details
                ShowSurgerySubPanel(itemWidget, slot, surgery);
                return;
            }
            ShowPartChoiceSubPanel(itemWidget, slot);

        }

        private void ShowSurgerySubPanel(WBase itemWidget, BodyPartSlot slot,
                SurgicalOperation surgery) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle($"{slot.NameT}: {T.SurgicalPendingProcedures}");

            var block = UISpawn.Block(l, subPanel.Container, 0);
            var cont = block.Container;
            int idx = 0;

            // show surgery info
            surgery.GetOpTexts(out var opName, out var sName, out var side, out var tip);
            var info = UISpawn.Widget<WIconText2>(l,
                UIPrefabId.WContent2Text2, cont, idx++);
            info.Load(surgery.Icon ?? IconId.CBodyReplaceBad, sName ?? opName, side);
            if (tip != null) {
                UIUtils.AddTip(l, info, tip);
            }

            // cancel button
            var cancelBtn = UISpawn.Widget<WButtonText>(l,
                UIPrefabId.WContent2ButtonSimple, cont, idx++);
            cancelBtn.Load($"{T.Cancel}: {opName}", () => {
                if (!S.Query.RegisterSurgicalOperation.Ask(surgery, false)) {
                    if (surgery.FailReason != null) {
                        S.Sig.ShowErrorMessage.Send(surgery.FailReason);
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
                subPanel.Close();
                ReloadTab();
            });

            subPanel.Show(itemWidget, null);
        }

        private void ShowPartChoiceSubPanel(WBase itemWidget, BodyPartSlot slot) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle($"{slot.NameT}: {T.Select}");

            int bIdx = 0;
            var block = UISpawn.Block(l, subPanel.Container, bIdx++);
            var cont = block.Container;
            int idx = 0;

            var devMode = A.InstantBuild;
            #if UNITY_EDITOR
            devMode = true;
            #endif
            if (devMode) {
                var devBtn = UISpawn.Widget<WButtonText>(l,
                    UIPrefabId.WContent2ButtonSimple, cont, idx++);
                devBtn.Load("Dev Tools", () => {
                    var qc = QuickChoice.Create();
                    if (!slot.IsPartRemoved) {
                        qc.AddButton("Remove Part", () => {
                            if (!slot.TryRemovePart(out _, out _, false)) {
                                UISounds.PlayActionDenied();
                            } else {
                                subPanel.Close();
                                ReloadTab();
                            }
                        });
                    } else {
                        qc.AddButton("Install vanilla part", () => {
                            var cfg = BodyConfiguration.ForSpecies(body.Owner.Species);
                            if (!cfg.SlotAssignments.TryGetValue(slot.Id, out var part)) {
                                UISounds.PlayActionDenied();
                                return;
                            }
                            if (!slot.InstallPart(A.S, part, BodyPartState.BrandNew())) {
                                UISounds.PlayActionDenied();
                            } else {
                                subPanel.Close();
                                ReloadTab();
                            }
                        });
                    }
                    qc.AddButton("Destroy Slot", () => {
                        slot.IsDestroyed = true;
                        subPanel.Close();
                        ReloadTab();
                    });
                    qc.Show();
                });
            }

            if (slot.IsDestroyed) {
                var destroyed = UISpawn.Widget<WText>(l,
                    UIPrefabId.WContentTextBlock, cont, idx++);
                destroyed.Load(Texts.Red(T.BodyPartSlotDestroyedWarning));
                // if we return without showing, the block will never get cleaned up
                subPanel.Show(itemWidget, null);
                return;
            }
            if (!slot.IsPartRemoved && slot.State.IsWelded) {
                var welded = UISpawn.Widget<WText>(l,
                    UIPrefabId.WContentTextBlock, cont, idx++);
                welded.Load(Texts.Red(T.BodyPartWeldedWarning));
                // if we return without showing, the block will never get cleaned up
                subPanel.Show(itemWidget, null);
                return;
            }

            // remove button if part is installed and removable
            if (!slot.IsPartRemoved && slot.CanRemovePart(false)) {
                var removeBtn = UISpawn.Widget<WButtonText>(l,
                    UIPrefabId.WContent2ButtonSimple, cont, idx++);
                removeBtn.Load(Texts.Italic($"{slot.InstalledDef.NameT}: {T.Remove}"), () => {
                    if (!TryOrderRemove(slot, out var failReason)) {
                        if (failReason != null) {
                            S.Sig.ShowErrorMessage.Send(failReason);
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    } else {
                        subPanel.Close();
                        ReloadTab();
                    }
                });
                block = UISpawn.Block(l, subPanel.Container, bIdx++);
                cont = block.Container;
                idx = 0;

                UIUtils.AddTipFunc(l, removeBtn, () => {
                    var sb = Caches.StringBuilder3;
                    sb.Clear();
                    sb.Append(Texts.SubTitle(T.NounSuccessChance));
                    // estimate existing part removal chance of success
                    float extractChance = slot.EstimateChanceOfSuccess(null, slot.State, true, true);
                    sb.Append(T.Remove);
                    sb.Append(':');
                    sb.Append(' ');
                    sb.Append(slot.InstalledDef.NameT);
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                    sb.Append('~');
                    sb.AppendLine(Units.PercentageColored(extractChance));
                    SurgeryUtils.AppendAlsoRemoved(sb, slot);
                    sb.Append(Texts.Accented($"{T.Warning}!"));
                    sb.Append(' ');
                    sb.AppendLine(T.BodyPartReplacementWarning);
                    return sb.ToString();
                });
            }

            // list installable parts
            Caches.ObjDataList.Clear();
            if (!S.Query.InstallableBodyParts.Ask(slot, Caches.ObjDataList)) {
                if (slot.IsPartRemoved) {
                    if (idx == 0) {
                        var empty = UISpawn.Widget<WIconText>(l,
                            UIPrefabId.WContent2Text, cont, idx++);
                        empty.Load(IconId.CInfo, T.Empty);
                    }
                    subPanel.Show(itemWidget, null);
                    return;
                }
            }

            bool hasNativeLabel = false;
            bool hasCompatLabel = false;
            var sorted = ObjData.SortedBy(S, Caches.ObjDataList, ObjData.SortMode.Name);

            // Group parts by Def for cleaner display
            var nativeGroups = new List<KeyValuePair<Def, List<ObjData>>>();
            var compatGroups = new List<KeyValuePair<Def, List<ObjData>>>();
            var nativeGroupMap = new Dictionary<Def, List<ObjData>>();
            var compatGroupMap = new Dictionary<Def, List<ObjData>>();

            foreach (var data in sorted) {
                var part = data.BodyPart;
                if (!part.IsCompatibleWith(slot, out var isVanilla)) { continue; }
                var map = isVanilla ? nativeGroupMap : compatGroupMap;
                var groups = isVanilla ? nativeGroups : compatGroups;
                if (!map.TryGetValue(data.Def, out var list)) {
                    list = new List<ObjData>();
                    map[data.Def] = list;
                    groups.Add(new KeyValuePair<Def, List<ObjData>>(data.Def, list));
                }
                list.Add(data);
            }

            if (nativeGroups.Count > 0) {
                hasNativeLabel = true;
                var label = UISpawn.Widget<WText>(l,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartNativeParts);
                AddGroupedInstallOptions(l, cont, slot, nativeGroups, ref idx);
            }

            if (compatGroups.Count > 0) {
                hasCompatLabel = true;
                var label = UISpawn.Widget<WText>(l,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartCompatibleParts);
                AddGroupedInstallOptions(l, cont, slot, compatGroups, ref idx);
            }

            if (!hasNativeLabel && !hasCompatLabel && idx == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, cont, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }

            subPanel.Show(itemWidget, null);
        }

        private void AddGroupedInstallOptions(Lifetime l, RectTransform cont,
                BodyPartSlot slot, List<KeyValuePair<Def, List<ObjData>>> groups,
                ref int idx) {
            foreach (var group in groups) {
                var items = group.Value;
                if (items.Count == 1) {
                    AddInstallOption(l, cont, slot, items[0], idx++);
                } else {
                    items.Sort((a, b) => {
                        float qa = a.BodyPartState?.Quality ?? a.Quality;
                        float qb = b.BodyPartState?.Quality ?? b.Quality;
                        int cmp = qb.CompareTo(qa);
                        if (cmp != 0) { return cmp; }
                        float ca = a.BodyPartState?.Condition ?? 1f;
                        float cb = b.BodyPartState?.Condition ?? 1f;
                        return cb.CompareTo(ca);
                    });
                    var firstPart = items[0].BodyPart;
                    var listBtn = UISpawn.Widget<WContentListButton>(l,
                        UIPrefabId.WContent2ListButton, cont, idx++);
                    listBtn.Load(firstPart.IconId, firstPart.Def.NameT,
                        Units.Num(items.Count));
                    listBtn.LoadOnToggle(l, (life, container, willShow, wasLoaded) => {
                        if (!willShow) { return wasLoaded; }
                        if (wasLoaded) { return true; }
                        int pageCount = Math.Min(InstallPageSize, items.Count);
                        for (int i = 0; i < pageCount; i++) {
                            AddInstallOption(life, container, slot, items[i], i);
                        }
                        if (items.Count > InstallPageSize) {
                            var moreBtn = UISpawn.Widget<WButtonText>(life,
                                UIPrefabId.WContent2ButtonSimple, container, pageCount);
                            LoadMoreInstallOptions(moreBtn, life, container, slot, items, pageCount);
                        }
                        return true;
                    }, false);
                }
            }
        }

        private void LoadMoreInstallOptions(WButtonText btn, Lifetime life,
                RectTransform container, BodyPartSlot slot,
                List<ObjData> items, int loaded) {
            int remaining = items.Count - loaded;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(InstallPageSize, remaining);
                for (int i = 0; i < batch; i++) {
                    AddInstallOption(life, container, slot, items[loaded + i], sibIdx + i);
                }
                int newLoaded = loaded + batch;
                if (newLoaded < items.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreInstallOptions(btn, life, container, slot, items, newLoaded);
                } else {
                    btn.SetActiveFast(false);
                }
                var scroll = container.GetComponentInParent<ScrollRect>();
                if (scroll != null) { UIUtils.RebuildScroll(scroll, false); }
            });
        }

        private void AddInstallOption(Lifetime l, RectTransform cont,
                BodyPartSlot slot, ObjData data, int idx) {
            var def = data.BodyPart;
            string installLabel;
            if (slot.IsPartRemoved) {
                if (slot.Body.Owner.Species.IsBiological) {
                    installLabel = Texts.Grey(T.VerbImplant);
                } else {
                    installLabel = Texts.Grey(T.Install);
                }
            } else {
                installLabel = Texts.Grey(T.Replace);
            }
            float quality = data.BodyPartState == null ? data.Quality :
                data.BodyPartState.Quality;

            var w = UISpawn.Widget<WButtonIconText2Progress>(l,
                UIPrefabId.WContent2ProgressButton, cont, idx);
            w.Load(def.IconId, data.ListName, quality, () => {
                var qc = QuickChoice.Create();
                qc.AddButton(installLabel, () => {
                    if (!TryOrderInstall(slot, data, out var failReason)) {
                        if (failReason != null) {
                            S.Sig.ShowErrorMessage.Send(failReason);
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    } else {
                        var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                        subPanel.Close();
                        ReloadTab();
                    }
                });
                qc.Show();
            }, null);

            UIUtils.AddTipFunc(l, w, () => {
                var value = ObjData.ObjValue(S, data);
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(T.NounSuccessChance));
                if (!slot.IsPartRemoved) {
                    // estimate existing part removal chance of success
                    float extractChance = slot.EstimateChanceOfSuccess(null, slot.State, true, true);
                    sb.Append(T.Remove);
                    sb.Append(':');
                    sb.Append(' ');
                    sb.Append(slot.InstalledDef.NameT);
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                    sb.Append('~');
                    sb.AppendLine(Units.PercentageColored(extractChance));
                }
                float installChance = slot.EstimateChanceOfSuccess(null, data.BodyPartState, false, true);
                sb.Append(T.Install);
                sb.Append(':');
                sb.Append(' ');
                sb.Append(def.NameT);
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.Append('~');
                sb.AppendLine(Units.PercentageColored(installChance));
                sb.Append(Texts.Accented($"{T.Warning}!"));
                sb.Append(' ');
                sb.AppendLine(T.BodyPartReplacementWarning);
                sb.AppendLine();

                sb.Append(Texts.SubTitle(T.NounBodyPart));
                sb.Append(T.NounValue);
                sb.Append(": ");
                sb.Append(Texts.Green(Wallet.ToValueString(value)));
                return data.Tooltip(S, sb.ToString());
            });
        }

        private bool TryOrderRemove(BodyPartSlot slot, out string failReason) {
            if (slot.IsPartRemoved) {
                failReason = T.Missing;
                return false;
            }
            if (slot.State.Flags.HasFlag(BodyPartStateFlags.Welded)) {
                failReason = T.BodyPartWeldedWarning;
                return false;
            }
            var surgery = S.Query.FindSurgicalOperation.Ask(slot, true);
            if (surgery != null) {
                failReason = T.AnotherTaskInProgress_V1.F(surgery.Tooltip(false));
                return false;
            }
            var bData = slot.Body.Owner;
            var isAlive = bData.HasStateFlag(BeingState.Alive);
            if (isAlive && !bData.IsPlayer) {
                if (!slot.Body.IsIncapacitated) {
                    failReason = $"{T.NounEnemy}: {T.BodyStateHealthy}";
                    return false;
                }
            }
            var species = slot.Body.Owner.Species;
            if (species.IsBiological) {
                if (isAlive && species.IsHumanoid && bData.IsPlayer) {
                    if (!slot.Type.IsOptional && !slot.State.IsEthicallyRemovable
                            && !A.HasAsimovOverride) {
                        failReason = T.AsimovViolation1;
                        return false;
                    }
                }
            }
            var op = new SurgicalOperation();
            op.TargetBeing = slot.Body.Owner;
            op.TargetSlot = slot;
            op.OpType = SurgicalOpType.Remove;
            if (!S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                failReason = op.FailReason;
                return false;
            }
            failReason = null;
            return true;
        }

        private bool TryOrderInstall(BodyPartSlot slot, ObjData data,
                out string failReason) {
            data.Validate(S);
            if (!data.IsAvailable) {
                failReason = T.AdRejCouldNotReserve;
                return false;
            }
            if (slot.IsDestroyed) {
                failReason = T.BodyPartSlotDestroyedWarning;
                return false;
            }
            var surgery = S.Query.FindSurgicalOperation.Ask(slot, true);
            if (surgery != null) {
                failReason = T.AnotherTaskInProgress_V1.F(surgery.Tooltip(false));
                return false;
            }
            if (data.Obj != null) {
                data.Obj.StorableObj.IsForbidden = true;
                if (data.Obj.Goal != null) {
                    AIGoal.Cancel(data.Obj.Goal, G.Ticks, T.MaterialReserved_V1.F(
                        T.NounSurgery));
                }
            }
            var op = new SurgicalOperation();
            op.TargetBeing = slot.Body.Owner;
            op.TargetSlot = slot;
            op.ReservedPart = data;
            op.OpType = slot.IsPartRemoved ? SurgicalOpType.Install : SurgicalOpType.Swap;
            if (!S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                failReason = op.FailReason;
                return false;
            }
            failReason = null;
            return true;
        }

        private void LoadSurgeries(WGroup group, int gidx) {
            var numSurgeries = S.Query.GetSurgicalOperationsFor.Ask(
                body, Caches.SurgicalOps);
            if (numSurgeries == 0) { return; }

            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.SurgicalPendingProcedures);

            var listBtn = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContent2ListButton, block.Container, idx++);
            listBtn.Load(IconId.CBodyReplaceBad, T.SurgicalPendingProcedures,
                Units.Num(numSurgeries));
            listBtn.LoadOnToggle(lifetime, ToggleSurgeries, false);
        }

        private bool ToggleSurgeries(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded) {
            if (!willShow) { return wasLoaded; }
            if (wasLoaded) { return true; }

            int idx = 0;
            var numSurgeries = S.Query.GetSurgicalOperationsFor.Ask(
                body, Caches.SurgicalOps);
            for (int i = 0; i < numSurgeries; i++) {
                var op = Caches.SurgicalOps[i];
                op.GetOpTexts(out var opName, out var sName, out var side, out var tip);
                var w = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                var displayName = sName ?? opName;
                w.Load(op.Icon ?? IconId.CBodyReplaceBad, displayName, null, () => {
                    var qc = QuickChoice.Create();
                    qc.AddButton(T.Cancel, () => {
                        if (!S.Query.RegisterSurgicalOperation.Ask(op, false)) {
                            if (op.FailReason != null) {
                                S.Sig.ShowErrorMessage.Send(op.FailReason);
                            } else {
                                UISounds.PlayActionDenied();
                            }
                        }
                        ReloadTab();
                    });
                    qc.Show();
                });
                if (tip != null) {
                    UIUtils.AddTip(life, w, tip);
                }
            }
            return true;
        }

        public override bool LoadFooter(WGroup group) {
            D.Warn("Block actions = {0}", BlockActions);
            if (BlockActions) { return false; }

            int idx = 0;
            idx = AddModifyBodyButton(group, idx);
            idx = AddMassOperationButton(group, IconId.CBodyRemoveAll,
                SurgicalOpType.RemoveAll, idx);
            idx = AddMassOperationButton(group, IconId.CBodyRemoveBad,
                SurgicalOpType.RemoveIrreparable, idx);
            idx = AddMassOperationButton(group, IconId.CBodyInstallMissing,
                SurgicalOpType.FillMissing, idx);
            idx = AddMassOperationButton(group, IconId.CBodyReplaceBad,
                SurgicalOpType.ReplaceIrreparableAndMissing, idx);
            idx = AddEuthanizeButton(group, idx);
            idx = AddClearSurgeriesButton(group, idx);
            return idx > 0;
        }

        private int AddMassOperationButton(WGroup group, string iconId,
                SurgicalOpType opType, int idx) {
            if (!being.Body.CanPerformMassAction(opType)) { return idx; }
            if (!body.HasSlotsForOperation(opType)) { return idx; }
            if (!S.Query.CanEnqueueMassSurgicalOp.Ask(being, opType)) { return idx; }

            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            var opName = SurgicalOperation.NameFor(opType, false);
            btn.Load(iconId, opName, () => {
                if (!TryOrderBulkOperation(opType, iconId, out var failReason)) {
                    if (failReason == null) {
                        failReason = T.OperationFailed;
                    }
                    S.Sig.ShowErrorMessage.Send(failReason);
                    return;
                }
                ReloadTab();
            });
            UIUtils.AddTipFunc(lifetime, btn, () => SurgicalOperation.GetMassOpTooltip(body, opType));
            return idx;
        }

        public bool TryOrderBulkOperation(SurgicalOpType opType, string iconId,
                out string failReason) {
            var data = being;
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

        private int AddEuthanizeButton(WGroup group, int idx) {
            if (!bodyComp.CanEuthanize) { return idx; }

            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            btn.Load(IconId.CKnife, T.VerbEuthanize, () => {
                if (bodyComp.CreateEuthanizeGoal()) {
                    ReloadTab();
                }
            });
            return idx;
        }
        private int AddModifyBodyButton(WGroup group, int idx) {
            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            btn.Load(IconId.CBodyGeneric, T.NounSurgery, () => {
                S.UIQuery.ToggleSurgeriesFor.Ask(being);
            });
            return idx;
        }

        private int AddClearSurgeriesButton(WGroup group, int idx) {
            if (!S.Query.HasPendingSurgeries.Ask(being, null)) { return idx; }

            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            btn.Load(IconId.CStop, T.CancelProcedures, () => {
                S.Query.ClearSurgicalOpsFor.Ask(being);
                ReloadTab();
            });
            return idx;
        }
        private string ShowBodyPartTooltip(BodyPartSlot slot) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var surgery = A.S.Query.FindSurgicalOperation.Ask(slot, true);
            if (surgery != null) {
                sb.Append(Texts.SubTitle(T.SurgicalPendingProcedures));
                sb.Append(Texts.Accented(GlyphMap.TriangleRight, bold: false));
                sb.Append(' ');
                sb.Append(surgery.Tooltip(false));
                sb.AppendFormat(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(slot.NameT));
            if (!slot.IsPartRemoved && (slot.InstalledDef == null || slot.InstalledDef.Def == null)) {
                D.Err("Body part slot has state but no installed def! {0}", slot);
                return sb.ToString();
            }
            if (slot.IsPartRemoved) {
                if (slot.Type.IsOptional) {
                    sb.Append($"{T.NounBodyPart} ({T.Optional}): {T.Empty} \n");
                } else {
                    sb.Append(Texts.Red($"{T.NounBodyPart}: {T.Missing}\n"));
                }
            } else {
                sb.AppendFormat("{0}: <b>{1}</b>\n",
                    T.PartInstalled, slot.InstalledDef?.Def?.CodexLink);
                if (slot.HoldsMind) {
                    var mind = slot.Body.Owner?.Mind;
                    if (mind != null) {
                        var speciesName = mind.Species?.Name ?? T.Unknown;
                        if (mind.Name != null) {
                            if (mind.Name.DisplayName == null) {
                                mind.Name.UpdateName();
                            }
                            if (mind.Faction == null || mind.Faction.IsPlayer || mind.Faction.IsInternal) {
                                sb.AppendFormat("{0}: {1}", speciesName,
                                    Texts.Green(mind.Name.DisplayName));
                            } else {
                                sb.AppendFormat("{0}: {1} ({2})", speciesName,
                                    Texts.Red(mind.Name.DisplayName),
                                        Texts.Red(mind.Faction.NameT));
                            }
                            sb.Append("\n");
                        }
                    }
                }

                sb.AppendFormat("{0}: <b>{1}</b>\n", T.NounQuality, Units.Percentage(slot.State.Quality, clamp: false));
                sb.AppendFormat("{0}: <b>{1}</b>\n", T.PartCondition, Units.Percentage(slot.State.Condition));
                if (slot.State.IsInfected) {
                    sb.AppendFormat("{0}\n", Texts.Red(T.AdjInfected));
                }
                if (!slot.State.IsUsableSelf || !slot.State.IsUsableParent) {
                    sb.AppendFormat("{0}: <b>{1}</b>\n", T.PartUsableSelf, slot.State.IsUsableSelf ? T.Yes : T.No);
                    if (slot.AttachedToPart != null) {
                        sb.AppendFormat("{0}: <b>{1}</b>\n",
                            T.PartUsableParent,
                                slot.State.IsUsableParent ? T.Yes : T.No);
                    }
                }

                var owner = slot.Body.Owner;
                var species = owner?.Mind?.Species ?? owner?.Species;
                slot.InstalledDef?.AddInfo(sb, species, slot.State.Quality, false);
            }

            if (!slot.IsPartRemoved) {
                if (!Arrays.IsEmpty(slot.State.Wounds)) {
                    sb.Append(GlyphMap.BRHR);
                    var isBio = slot.InstalledDef.IsBiological;
                    foreach (var w in slot.State.Wounds) {
                        sb.Append("\n");
                        sb.AppendFormat("{0}\n", Wound.TooltipFor(w, isBio));
                    }
                }
                if (!Arrays.IsEmpty(slot.State.Scars)) {
                    sb.Append(GlyphMap.BRHR);
                    sb.AppendLine(Texts.Accented(T.NounScars));
                    foreach (var scar in slot.State.Scars) {
                        sb.AppendLine(Scar.TooltipFor(scar));
                    }
                }
            }
            #if !PRODUCTION_BUILD
            // sb.Append(D.DumpInstance(slot.State));
            #endif
            return sb.ToString();
        }

        protected override void Cleanup() {
            being = null;
            body = null;
            bodyComp = null;
            S = null;
        }
    }
}
#endif
