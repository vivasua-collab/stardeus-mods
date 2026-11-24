using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
namespace Game.Tools {
    public sealed class ToolStore : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolStore());
        }
        public const string ToolId = "store";
        public override string Id => ToolId;
        public override string MenuIcon => IconId.WStore;
        public override bool IsConfigurable => false;
        public override bool IsDevTool => false;
        public override ActionType Shortcut => ActionType.ShortcutStore;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/h" };
        public override int Cursor => CursorTypeH.CursorStore;
        private new Vector2 dragStart;
        private new Vector2 dragEnd;
        private readonly Dictionary<Def, Obj> noStorageFor = new();
        private int noStorageCount;
        private int hadStorageCount;
        private int failedCount;
        private string lastFailReason;
        private readonly Dictionary<MatType, bool> matStorageCache = new();

        private int haulPos = Pos.Invalid;

        public override void AddParticles(Dictionary<int, XParticles> x) {
            if (x.TryGetValue(ParticlesIdH.MouseRect, out var mr)) {
                var count = AddDragRectParticles(mr.Particles, Consts.YellowRectColor,
                    dragStart, dragEnd);
                mr.ShowParticles(count, false);
            }
        }

        private void ShowNoStorageWarning() {
            if (noStorageFor.Count == 0) { return; }
            bool fullWarning = S.Prefs.GetBool(Pref.ShowNoStorageWarning, PrefH.ShowNoStorageWarning, true);

            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (fullWarning) {
                sb.Append(T.StorageUnavailableDesc_V1.F(noStorageCount));
                sb.Append(GlyphMap.BRHR);
                sb.AppendLine(Texts.Accented(T.CompatibleStorage));
            } else {
                sb.AppendLine(T.StorageUnavailable);
                sb.Append("<size=50%>");
                sb.Append(Texts.Accented(T.CompatibleStorage));
                sb.AppendLine(": ");
            }
            int j = 0;
            foreach (var obj in noStorageFor) {
                var def = obj.Key;
                var storages = def.CompatibleStorages(obj.Value);
                if (storages == null) {
                    D.Err("No storages for def: {0}", def.Id);
                    continue;
                }
                sb.Append(def.CodexLink);
                if (fullWarning) {
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                } else {
                    sb.Append(" <size=40%>→</size> ");
                }

                for (int i = 0; i < storages.Count; i++) {
                    Def storage = storages[i];
                    sb.Append(Texts.Yellow(storage.CodexLink));
                    if (i < storages.Count - 1) {
                        sb.Append(", ");
                    }
                }
                if (!fullWarning && j++ > 1) {
                    break;
                }
                sb.AppendLine();
            }

            if (!fullWarning) {
                S.Sig.ShowErrorMessage.Send(sb.ToString());
                return;
            }
            WPopup.Create(false, IconId.CWarning,
                T.StorageUnavailable, null, sb.ToString())
                .WithAction(T.Ok, null)
                .WithAction2(T.DoNotShowAgain, () => {
                    S.Prefs.SetBool(Pref.ShowNoStorageWarning, false);
                }).Spawn();
        }

        private bool isShowingNoStorageUnitTip;

        private static bool IsHaulTarget(Obj obj) {
            if (obj.IsCarriedByAny) { return false; }
            var pile = obj.Pile;
            if (pile != null && !pile.TreatAsObj) {
                return pile.UnreservedCount == pile.StackSize;
            }
            return obj.StorableObj != null;
        }

        private static bool HasStorageFor(Obj obj) {
            var pile = obj.Pile;
            if (pile != null && !pile.TreatAsObj) {
                return pile.HasCompatibleStorage();
            }
            return obj.StorableObj?.HasCompatibleStorage() == true;
        }

        protected override void HideHover() {
            base.HideHover();
            haulPos = Pos.Invalid;
            ClearNoStorageTip();
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            if (isDragging) {
                OnHoverDrag();
                return;
            }

            var pos = EntityUtils.ToPosSafe(coords);
            if (pos != lastHoverPos || haulPos == Pos.Invalid) {
                haulPos = pos;
                var closest = S.Objs.FindClosestVisual(coords, includeProxy: false);
                if (closest != null && IsHaulTarget(closest)) {
                    if (!HasStorageFor(closest)) {
                        if (!isShowingNoStorageUnitTip) {
                            isShowingNoStorageUnitTip = true;
                            The.SysSig.ShowTooltip.Send(Texts.WithColor(
                                T.StorageUnavailable, Texts.TMPColorRed), false);
                        }
                    } else {
                        ClearNoStorageTip();
                    }
                    // FIXME - doesn't work
                    ShowGreenTile(pos);
                } else {
                    ClearNoStorageTip();
                    ClearPreview();
                }
            }
            lastHoverPos = pos;
        }

        private void ClearNoStorageTip() {
            noStorageFor.Clear();
            if (isShowingNoStorageUnitTip) {
                isShowingNoStorageUnitTip = false;
                The.SysSig.HideTooltip.Send();
            }
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dist) {
            isDragging = true;
            dragStart = start;
            dragEnd = end;
        }

        public override bool Cancel() {
            haulPos = Pos.Invalid;
            return false;
        }

        public override void Deactivate() {
            haulPos = Pos.Invalid;
            ClearNoStorageTip();
            base.Deactivate();
        }

        private void FindHaulableObjs(Vector2 start, Vector2 end, List<Entity> objs) {
            S.Objs.FindVisualInRect(start, end, objs, o => {
                return o is Obj obj && IsHaulTarget(obj);
            });
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            var toHaul = new List<Entity>();
            noStorageFor.Clear();
            noStorageCount = 0;
            hadStorageCount = 0;
            failedCount = 0;
            lastFailReason = null;
            matStorageCache.Clear();
            if (isDragging) {
                FindHaulableObjs(startPos, position, toHaul);
            } else {
                FindHaulableObjs(position + new Vector2(-0.5f, -0.5f),
                    position + new Vector2(0.5f, 0.5f), toHaul);
                lastHoverPos = Pos.Invalid;
            }
            if (toHaul.Count > 0) {
                foreach (var h in toHaul) {
                    EnqueueStore(h);
                }
            } else {
                UISounds.PlayActionDenied();
            }
            EndEnqueue();
            ClearDragPositions();
            isDragging = false;
        }

        private void EndEnqueue() {
            if (hadStorageCount > 0) {
                // FIXME add new sound
                Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
            }
            if (noStorageCount > 0) {
                if (hadStorageCount == 0) {
                    UISounds.PlayActionDenied();
                }
                ShowNoStorageWarning();
            } else if (failedCount > 0 && hadStorageCount == 0) {
                if (lastFailReason != null) {
                    S.Sig.ShowErrorMessage.Send(lastFailReason);
                } else {
                    UISounds.PlayActionDenied();
                }
            }
        }

        private void EnqueueStore(Entity obj) {
            // if (obj.TryGetComponent<PlantComp>(out var pl)) {
            //     if (pl.TryOrderHarvest()) {
            //         return;
            //     }
            // }
            UnstoredMatComp m;
            if (obj is not Obj o) { return; }
            if (o.StorableObj != null && o.Pile?.TreatAsObj != false) {
                if (o.StorableObj.HasCompatibleStorage()) {
                    o.StorableObj.IsForbidden = false;
                    if (o.StorableObj.TryOrderStoring(true, false, out var reason)) {
                        hadStorageCount++;
                    } else {
                        failedCount++;
                        lastFailReason = reason;
                    }
                } else {
                    noStorageFor.TryAdd(o.Definition, o);
                    noStorageCount++;
                }
                return;
            }
            m = o.Pile;
            if (m == null) { return; }
            if (m.IsFullyReserved) { return; }
            if (m.TreatAsObj) { return; }
            bool hasStorage;
            if (!matStorageCache.TryGetValue(m.Type, out hasStorage)) {
                hasStorage = m.HasCompatibleStorage();
                matStorageCache.Add(m.Type, hasStorage);
            }
            if (hasStorage) {
                var goal = m.CreateStoringGoal(true, false);
                if (goal != null) {
                    m.Entity.TrySetGoal(goal);
                }
                m.IsForbidden = false;
                hadStorageCount++;
            } else {
                noStorageFor.TryAdd(o.Definition, o);
                noStorageCount++;
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
