using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures.Surgeries {
    public class SurgeryChoiceView : IContextPopupWithWidth {

        private readonly GameState S;
        private readonly BeingData being;
        private readonly BodyPartSlot slot;
        private readonly Action onClose;

        public SurgeryChoiceView(GameState s, BodyPartSlot slot, Action onClose) {
            S = s;
            this.slot = slot;
            being = slot.Body.Owner;
            this.onClose = onClose;
        }

        public void Show() {
            S.UIQuery.ShowContextPopup.Ask(this, true);
        }
        public void Hide() {
            S.UIQuery.ShowContextPopup.Ask(this, false);
        }

        public int UniquePopupHash => JobTypeIdH.Medical;
        public bool ShowInScreenCenter => false;

        public int PopupWidth => 600;

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            wFooter.SetActiveFast(false);

            var op = A.S.Query.FindSurgicalOperation.Ask(slot, true);
            if (op != null) {
                return ShowPendingSurgeries(lifetime, wContent, wFooter);
            }

            int bIdx = 0;
            var block = UISpawn.Block(lifetime, wContent, bIdx++);
            var cont = block.Container;
            int idx = 0;

            if (slot.IsDestroyed) {
                var destroyed = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, cont, idx++);
                destroyed.Load(Texts.Red(T.BodyPartSlotDestroyedWarning));
                return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
            }
            if (!slot.IsPartRemoved && slot.State.IsWelded) {
                var welded = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, cont, idx++);
                welded.Load(Texts.Red(T.BodyPartWeldedWarning));
                return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
            }

            if (!slot.IsPartRemoved && slot.CanRemovePart(false)) {
                var removeBtn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WFooterItem_DeleteButton, wFooter.Container, idx++);
                removeBtn.Load(T.BodyOpRemove, () => {
                    if (!TryOrderRemove(out var failReason)) {
                        if (failReason != null) {
                            S.Sig.ShowErrorMessage.Send(failReason);
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    } else {
                        Hide();
                    }
                });

                UIUtils.AddTipFunc(lifetime, removeBtn, () => {
                    var sb = Caches.StringBuilder3;
                    sb.Clear();
                    sb.Append(Texts.SubTitle(T.NounSuccessChance));
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

                wFooter.SetActiveFast(true);
            }

            // nothing below a slot that can never work again works, so a part there only
            // comes out (STA-1749)
            var deadEnd = slot.BeyondRepairAncestor;
            if (deadEnd != null) {
                var unusable = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, cont, idx++);
                unusable.Load(Texts.Red(T.BodyPartUnusableViaParent_V1.F(deadEnd.NameT)));
                return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
            }

            Caches.ObjDataList.Clear();
            S.Query.InstallableBodyParts.Ask(slot, Caches.ObjDataList);

            var sorted = ObjData.SortedBy(S, Caches.ObjDataList, ObjData.SortMode.Name);
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

            bool addedParts = false;

            if (nativeGroups.Count > 0) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartNativeParts);
                AddGroupedInstallOptions(lifetime, cont, nativeGroups, ref idx);
                addedParts = true;
            }

            if (compatGroups.Count > 0) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartCompatibleParts);
                AddGroupedInstallOptions(lifetime, cont, compatGroups, ref idx);
                addedParts = true;
            }

            if (!addedParts) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartCompatibleParts);
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, cont, idx++);
                empty.Load(IconId.CInfo, T.NotAvailable);
            }


            return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
        }

        private string ShowPendingSurgeries(Lifetime lifetime, WGroup wContent,
                WGroup wFooter) {
            var numOps = A.S.Query.GetSurgicalOperationsFor.Ask(slot.Body, Caches.SurgicalOps);
            int idx = 0;
            var block = UISpawn.Block(lifetime, wContent, 0);
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.SurgicalPendingProcedures);
            bool anyForSlot = false;
            for (int i = 0; i < numOps; i++) {
                var op = Caches.SurgicalOps[i];
                if (!OpAffectsSlot(op)) { continue; }
                anyForSlot = true;
                AddSurgeryEntry(lifetime, op, block.Container, ref idx);
            }
            if (!anyForSlot) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }
            var cancelBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, wFooter.Container, 0);
            cancelBtn.Load(T.CancelProcedures, () => {
                S.Query.ClearSurgicalOpsFor.Ask(being);
                Hide();
            });
            wFooter.SetActiveFast(true);
            return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
        }

        private bool OpAffectsSlot(SurgicalOperation op) {
            if (op.TargetSlot == slot) { return true; }
            if (op.TargetSlot != null) { return false; }
            // mass op — replicate SurgicalQueue.AffectsSlot logic
            if (slot.IsDestroyed) { return false; }
            if (!slot.CanRemovePart(false)) { return false; }
            if (slot.Type.IsOptional && op.OpType == SurgicalOpType.FillMissing) { return false; }
            var deadEnd = slot.BeyondRepairAncestor != null;
            switch (op.OpType) {
                case SurgicalOpType.RemoveAll:
                    return !slot.IsPartRemoved;
                case SurgicalOpType.RemoveIrreparable:
                    return !slot.IsPartRemoved
                        && (slot.State.WoundSeverity >= Severity.Irreparable || deadEnd);
                case SurgicalOpType.FillMissing:
                    return slot.IsPartRemoved && !deadEnd;
                case SurgicalOpType.ReplaceIrreparableAndMissing:
                    return slot.IsPartRemoved
                        ? !deadEnd
                        : slot.State.WoundSeverity >= Severity.Irreparable || deadEnd;
                default:
                    return false;
            }
        }

        private void AddSurgeryEntry(Lifetime life, SurgicalOperation op,
                RectTransform container, ref int idx) {
            op.GetOpTexts(out _, out var name, out var side, out var tip);
            var row = UISpawn.Widget<WContentTextWithX>(life,
                UIPrefabId.WContentTextX, container, idx++);
            op.State.Bind(life, _ => {
                var displayName = op.TargetSlot != null
                    ? $"{op.TargetSlot.NameT}: {name}"
                    : name;
                if (side != null) {
                    displayName = $"{displayName} {Texts.GreyParentheses(side)}";
                }
                row.Text.Load(op.Icon, displayName);
            });
            row.XButton.Load(() => {
                if (!S.Query.RegisterSurgicalOperation.Ask(op, false)) {
                    if (op.FailReason != null) {
                        S.Sig.ShowErrorMessage.Send(op.FailReason);
                    } else {
                        UISounds.PlayActionDenied();
                    }
                } else {
                    UISounds.PlayDigitalConfirm();
                    row.Dispose();
                }
            });
            if (tip != null) {
                UIUtils.AddTip(life, row, tip);
            }
        }

        private const int InstallPageSize = 20;

        private void AddGroupedInstallOptions(Lifetime lifetime, RectTransform cont,
                List<KeyValuePair<Def, List<ObjData>>> groups, ref int idx) {
            foreach (var group in groups) {
                var items = group.Value;
                if (items.Count == 1) {
                    AddInstallOption(lifetime, cont, items[0], idx++);
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
                    var listBtn = UISpawn.Widget<WContentListButton>(lifetime,
                        UIPrefabId.WContent2ListButton, cont, idx++);
                    listBtn.Load(firstPart.IconId, firstPart.Def.NameT, Units.Num(items.Count));
                    listBtn.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                        if (!willShow) { return wasLoaded; }
                        if (wasLoaded) { return true; }
                        int pageCount = Math.Min(InstallPageSize, items.Count);
                        for (int i = 0; i < pageCount; i++) {
                            AddInstallOption(life, container, items[i], i);
                        }
                        if (items.Count > InstallPageSize) {
                            var moreBtn = UISpawn.Widget<WButtonText>(life,
                                UIPrefabId.WContent2ButtonSimple, container, pageCount);
                            LoadMoreInstallOptions(moreBtn, life, container, items, pageCount);
                        }
                        return true;
                    }, false);
                }
            }
        }

        private void LoadMoreInstallOptions(WButtonText btn, Lifetime life,
                RectTransform container, List<ObjData> items, int loaded) {
            int remaining = items.Count - loaded;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(InstallPageSize, remaining);
                for (int i = 0; i < batch; i++) {
                    AddInstallOption(life, container, items[loaded + i], sibIdx + i);
                }
                int newLoaded = loaded + batch;
                if (newLoaded < items.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreInstallOptions(btn, life, container, items, newLoaded);
                } else {
                    btn.SetActiveFast(false);
                }
                var scroll = container.GetComponentInParent<UnityEngine.UI.ScrollRect>();
                if (scroll != null) { UIUtils.RebuildScroll(scroll, false); }
            });
        }

        private void AddInstallOption(Lifetime life, RectTransform cont,
                ObjData data, int idx) {
            var def = data.BodyPart;
            string installLabel;
            if (slot.IsPartRemoved) {
                installLabel = being.Species.IsBiological
                    ? T.VerbImplant
                    : T.Install;
            } else {
                installLabel = T.Replace;
            }
            float quality = data.BodyPartState == null ? data.Quality : data.BodyPartState.Quality;

            var w = UISpawn.Widget<WButtonIconText2Progress>(life,
                UIPrefabId.WContent2ProgressButton, cont, idx);
            w.Load(def.IconId, data.ListName, quality, () => {
                var qc = QuickChoice.Create();
                qc.AddButton(installLabel, () => {
                    if (!TryOrderInstall(data, out var failReason)) {
                        if (failReason != null) {
                            S.Sig.ShowErrorMessage.Send(failReason);
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    } else {
                        Hide();
                    }
                });
                qc.Show();
            }, null);

            UIUtils.AddTipFunc(life, w, () => {
                var value = ObjData.ObjValue(S, data);
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(T.NounSuccessChance));
                if (!slot.IsPartRemoved) {
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

        private bool TryOrderRemove(out string failReason) {
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
            var isAlive = being.HasStateFlag(BeingState.Alive);
            if (isAlive && !being.IsPlayer) {
                if (!slot.Body.IsIncapacitated) {
                    failReason = $"{T.NounEnemy}: {T.BodyStateHealthy}";
                    return false;
                }
            }
            var species = being.Species;
            if (species.IsBiological && isAlive && species.IsHumanoid && being.IsPlayer) {
                if (!slot.Type.IsOptional && !slot.State.IsEthicallyRemovable && !A.HasAsimovOverride) {
                    failReason = T.AsimovViolation1;
                    return false;
                }
            }
            var op = new SurgicalOperation {
                TargetBeing = being,
                TargetSlot = slot,
                OpType = SurgicalOpType.Remove,
            };
            if (!S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                failReason = op.FailReason;
                return false;
            }
            failReason = null;
            return true;
        }

        private bool TryOrderInstall(ObjData data, out string failReason) {
            var species = being.Species;
            if (data.Def.IdH == DefIdsH.ObjPartsImplantObeyChip
                    && species.IsBiological && species.IsHumanoid
                    && !A.HasAsimovOverride) {
                failReason = T.AsimovViolation1;
                return false;
            }
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
                    AIGoal.Cancel(data.Obj.Goal, G.Ticks, T.MaterialReserved_V1.F(T.NounSurgery));
                }
            }
            var op = new SurgicalOperation {
                TargetBeing = being,
                TargetSlot = slot,
                ReservedPart = data,
                OpType = slot.IsPartRemoved ? SurgicalOpType.Install : SurgicalOpType.Swap,
            };
            if (!S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                failReason = op.FailReason;
                return false;
            }
            failReason = null;
            return true;
        }

        public void OnPopupClosed() {
            onClose?.Invoke();
        }

        public void OnPopupOpened() {
        }
    }
}
