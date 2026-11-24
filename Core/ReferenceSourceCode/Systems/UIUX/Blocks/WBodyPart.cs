using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WBodyPart : WBase, IPointerEnterHandler,
            IPointerExitHandler, IPointerClickHandler, ICustomPoolable {
        [SerializeField] private WText wSlotName;
        [SerializeField] private WText wPartName;
        [SerializeField] private WProgress wProgQuality;
        [SerializeField] private WProgress wProgCondition;
        [SerializeField] private GameObject overlaySpacer;
        [SerializeField] private GameObject overlay;
        [SerializeField] private WGroup wStatusContainer;
        [SerializeField] private WText wOverlayText;
        [SerializeField] private UIThemedImage headerImgTheme;
        [SerializeField] private UIThemedText headerTxtTheme;
        [SerializeField] private string headerActiveImgTheme;
        [SerializeField] private string headerActiveTxtTheme;
        // for setting color
        [SerializeField] private Image overlayImage;
        private bool isUpdatingSlotData;
        private bool isHovering;
        private string headerIdleImgTheme;
        private Lifetime iconsLife;
        private BodyPartSlot slot;
        private IContextPopupWithWidth view;
        private UILine line;

        public void Load(Lifetime life, BodyPartSlot slot) {
            if (iconsLife != null) {
                iconsLife.Dispose();
                iconsLife = null;
            }
            this.slot = slot;
            // UpdateSlotData(life, slot);
            slot.StateB.Bind(life, (st) => OnStateChanged(life, slot, st));
            UIUtils.AddTipFunc(life, this, slot.ShowBodyPartTooltip);
        }

        public void SetLine(UILine line) {
            this.line = line;
            ResetLine();
        }

        private void ResetLine() {
            if (line != null) {
                var white = Colors.WithA(The.UITheme.GetColor("White", this), 0.25f);
                line.SetThickness(1f);
                line.SetColor(white, white);
            }
        }

        private void UpdateSlotData(Lifetime life, BodyPartSlot slot) {
            if (isUpdatingSlotData) {
                D.Err("Recursion in WBodyPart.UpdateSlotData!");
                return;
            }
            isUpdatingSlotData = true;
            bool showOverlay;
            if (slot.IsDestroyed) {
                wSlotName.Load(slot.NameT);
                showOverlay = true;
                overlayImage.color = Colors.WithA(The.UITheme.GetColor("PanelRed", "WBodyPart"), 0.25f);
                wOverlayText.Load(T.BodyPartDestroyed);
            } else if (slot.IsPartRemoved) {
                wSlotName.Load(slot.NameT);
                showOverlay = true;
                overlayImage.color = Colors.WithA(The.UITheme.GetColor("Grey", "WBodyPart"), 0.25f);
                if (slot.BeyondRepairAncestor != null) {
                    // can never be filled: below a slot that never works again (STA-1749)
                    wOverlayText.Load(Texts.Grey(T.AdjUnusable));
                } else if (slot.Type.IsOptional) {
                    wOverlayText.Load(T.Empty);
                } else {
                    wOverlayText.Load(Texts.Red(T.Missing));
                }
            } else {
                wSlotName.Load(slot.NameT);
                showOverlay = false;
                slot.State.OnChange.Bind(life, () => {
                    wPartName.Load(PartName(slot));
                    if (slot.State != null) {
                        var qCol = BeingUtils.SanityColor(slot.State.Quality);
                        wProgQuality.Load(qCol, slot.State.Quality);
                        var cCol = BeingUtils.SanityColor(slot.State.Condition);
                        wProgCondition.Load(cCol, slot.State.Condition);
                    }
                    BuildSlotIcons(life, slot);
                });
            }

            if (showOverlay) {
                wStatusContainer.SetActiveFast(false);
            }
            wPartName.SetActiveFast(!showOverlay);
            wProgCondition.SetActiveFast(!showOverlay);
            wProgQuality.SetActiveFast(!showOverlay);
            overlaySpacer.SetActiveFast(showOverlay);
            overlay.SetActiveFast(showOverlay);
            RebuildLayout();

            // destroyed slots already get the full-card red overlay above; a beyond-repair
            // slot that still shows its part (welded, unhealable) instead gets a red header
            headerIdleImgTheme = !slot.IsDestroyed && slot.IsBeyondRepair ? "PanelRed" : null;
            if (!isHovering && view == null) {
                ApplyIdleHeaderTheme();
            }

            isUpdatingSlotData = false;
        }

        // An unusable part is struck through in red, so it reads as such at a glance while its
        // bars still show whether it is worth salvaging (STA-1749)
        private static string PartName(BodyPartSlot slot) {
            var name = slot.InstalledDef?.NameT ?? T.Unknown;
            var state = slot.State;
            if (state == null || state.IsUsableWithParent) { return name; }
            return Texts.Red(Texts.Strike(name));
        }

        private void ApplyIdleHeaderTheme() {
            if (headerIdleImgTheme != null) {
                headerImgTheme.Override(headerIdleImgTheme);
            } else {
                headerImgTheme.ClearOverride();
            }
        }

        private void BuildSlotIcons(Lifetime life, BodyPartSlot slot) {
            if (iconsLife != null) {
                iconsLife.Dispose();
            }
            var state = slot.State;
            if (state == null) {
                wStatusContainer.SetActiveFast(false);
                return;
            }
            iconsLife = life.CreateChild("StatusIcons");
            int idx = 0;
            if (slot.IsDestroyed) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CExplosion);
                UIUtils.AddTip(iconsLife, icon, T.BodyPartSlotDestroyedWarning);
            }
            var surgery = A.S.Query.FindSurgicalOperation.Ask(slot, true);
            if (surgery != null) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CKnife);
                UIUtils.AddTipFunc(iconsLife, icon, () => {
                    var sb = Caches.StringBuilder3;
                    sb.Clear();
                    sb.AppendLine(Texts.Accented(T.SurgicalPendingProcedures));
                    sb.Append(Texts.Accented(GlyphMap.TriangleRight, bold: false));
                    sb.Append(' ');
                    sb.Append(surgery.Tooltip(false));
                    return sb.ToString();
                });
            }
            if (slot.HoldsMind) {
                var mind = slot.Body.Owner?.Mind;
                if (mind != null) {
                    var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                    icon.Load(mind.Faction.FlagIcon);
                    UIUtils.AddTipFunc(iconsLife, icon, () => {
                        var sb = Caches.StringBuilder3;
                        sb.Clear();
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
                        return sb.ToString();
                    });
                }
            }
            if (!state.IsUsableSelf || !state.IsUsableParent) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CCrutch);
                UIUtils.AddTipFunc(iconsLife, icon, () => {
                    if (!state.IsUsableSelf) {
                        return T.BodyPartUnusable;
                    }
                    return T.BodyPartUnusableViaParent_V1.F(slot.AttachedToPart.NameT);
                });
            }
            if (state.IsInfected) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CPoison);
                UIUtils.AddTip(iconsLife, icon, T.AdjInfected);
            }
            if ((state.Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CHeartBroken);
                UIUtils.AddTip(iconsLife, icon,
                    T.BodyPartCrippledTip_V1.F(Units.Percentage(Tunables.CrippledPartMaxCondition)));
            }
            if (!slot.Type.IsRemovable) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CChainLink);
                UIUtils.AddTip(iconsLife, icon, T.AdjNotRemovable);
            }
            if (state.IsWelded) {
                var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                icon.Load(IconId.CChainLink);
                UIUtils.AddTip(iconsLife, icon, T.BodyPartWeldedWarning);
            }
            if (!Arrays.IsEmpty(state.Wounds)) {
                foreach (var wound in state.Wounds) {
                    var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                    icon.Load(IconId.CBlood);
                    var wnd = wound;
                    UIUtils.AddTipFunc(iconsLife, icon, () => Wound.LiveTooltipFor(state, wnd,
                        slot.InstalledDef?.IsBiological ?? slot.Body.Owner.Species.IsBiological));
                }
            }
            if (!Arrays.IsEmpty(state.Scars)) {
                foreach (var scar in state.Scars) {
                    var icon = UISpawn.Widget<WImage>(iconsLife, UIPrefabId.WStatusIcon, wStatusContainer.Container, idx++);
                    icon.Load(IconId.CScar);
                    var scr = scar;
                    UIUtils.AddTipFunc(iconsLife, icon, () => Scar.TooltipFor(scar));
                }
            }

            wStatusContainer.SetActiveFast(idx > 0);
        }

        private void OnStateChanged(Lifetime life, BodyPartSlot slot, BodyPartState state) {
            if (state == null) {
                UpdateSlotData(life, slot);
                return;
            }
            state.OnChange.Bind(life, (st) => {
                UpdateSlotData(life, slot);
            });
        }

        public void OnPointerEnter(PointerEventData eventData) {
            isHovering = true;
            headerImgTheme.Override(headerActiveImgTheme);
            headerTxtTheme.Override(headerActiveTxtTheme);
            if (line != null) {
                line.SetThickness(1.5f);
                var col = The.UITheme.GetColor("Accent", this);
                line.SetColor(col, col);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            isHovering = false;
            if (view == null) {
                ApplyIdleHeaderTheme();
                headerTxtTheme.ClearOverride();
                ResetLine();
            }
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (view != null) {
                A.S.UIQuery.ShowContextPopup.Ask(view, false);
                view = null;
            } else {
                OnPointerEnter(null);
                view = A.InstantBuild
                    ? new InstantBodyPartChoiceView(A.S, slot, () => {
                        view = null;
                        OnPointerExit(null);
                    })
                    : new SurgeryChoiceView(A.S, slot, () => {
                        view = null;
                        OnPointerExit(null);
                    });
                A.S.UIQuery.ShowContextPopup.Ask(view, true);
                The.SysSig.HideTooltip.Send();
            }
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            if (view != null) {
                A.S.UIQuery.ShowContextPopup.Ask(view, false);
                view = null;
            }
            headerIdleImgTheme = null;
            OnPointerExit(null);
            slot = null;
            line = null;
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }
    }
}
