using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class CombatControlsRow {
        private static readonly List<Being> draftList = new(1);

        private readonly BeingData data;
        private readonly CombatUnit unit;
        private readonly DirectControlComp control;
        private readonly WBeingButton2 widget;

        private bool hasShownState;
        private CombatUnitConfig shownConfig;
        private bool shownControllable;
        private AttackMethod shownAttack;
        private float shownDps;

        public BeingData Data => data;

        public CombatControlsRow(Lifetime life, RectTransform parent, int idx, BeingData data) {
            this.data = data;
            unit = data.Being.AIAgent.CombatUnit;
            control = data.Being.AIAgent.DirectControl;
            widget = UISpawn.Widget<WBeingButton2>(life,
                UIPrefabId.WContent2Button_Being_Button2, parent, idx);
            widget.Item.Load(data);
            UIUtils.AddTipFunc(life, widget.Item, CombatTooltip);

            widget.IconButton1.Load(DraftIcon, ToggleDraft);
            UIUtils.AddTipFunc(life, widget.IconButton1, DraftTooltip);
            control.DirectControlChangedB.Bind(life, OnDirectControlChanged, invokeImmediately: false);

            widget.IconButton2.Load(CombatUnitComp.IconFor(unit.Config), ShowReactionChoice);
            UIUtils.AddTipFunc(life, widget.IconButton2, ReactionTooltip);

            Update();
        }

        public void Update() {
            var cfg = unit.Config;
            var controllable = control.IsControllable;
            if (!hasShownState || cfg != shownConfig || controllable != shownControllable) {
                shownConfig = cfg;
                shownControllable = controllable;
                widget.IconButton2.SetImage(CombatUnitComp.IconFor(cfg));
                SetEnabled(widget.IconButton1, controllable);
                SetEnabled(widget.IconButton2, controllable);
            }

            var attack = unit.PreviewBestAttack();
            var dps = attack?.Stats.DPS.Value ?? 0f;
            if (!hasShownState || attack != shownAttack || dps != shownDps) {
                shownAttack = attack;
                shownDps = dps;
                widget.TextSide.Load(AttackText(attack));
                widget.UICollisionResolver.AutoResize();
            }
            hasShownState = true;
        }

        private void SetEnabled(WButtonIcon button, bool enabled) {
            button.SetInteractable(enabled);
            button.SetTint(enabled ? Consts.ColorWhite : The.UITheme.GetColor("Grey", this));
        }

        private static string AttackText(AttackMethod attack) {
            if (attack == null) {
                return Texts.RedMinus;
            }
            return $"{WeaponGlyph.For(attack)}{attack.Stats.DPS.Display()}";
        }

        private string DraftIcon => control.IsUnderDirectControl ? IconId.CUndraft : IconId.CDraft;

        private void OnDirectControlChanged() {
            widget.IconButton1.SetImage(DraftIcon);
        }

        private void ToggleDraft() {
            if (data.Being == null) {
                return;
            }
            draftList.Clear();
            draftList.Add(data.Being);
            bool ok;
            if (control.IsUnderDirectControl) {
                ok = UndraftAction.UndraftAll(draftList, false, out _);
            } else {
                ok = DraftAction.DraftAll(draftList, false, out _);
            }
            draftList.Clear();
            if (!ok && !control.CheckCanToggleDirectControl(out var tip)) {
                A.S.Sig.ShowErrorMessage.Send(tip);
            }
        }

        private string DraftTooltip() {
            var action = control.IsUnderDirectControl ? T.VerbUndraft : T.VerbDraft;
            if (control.CheckCanToggleDirectControl(out var tip)) {
                return action;
            }
            return $"{action}\n{tip}";
        }

        private void ShowReactionChoice() {
            if (data.Being == null) {
                return;
            }
            CombatReactionUI.ShowChoice(widget.IconButton2.Rect, unit, Update);
        }

        private string ReactionTooltip() {
            var reaction = $"{T.CombatReaction}: {Texts.Accented(CombatUnitComp.NameFor(unit.Config))}";
            if (control.IsControllable) {
                return reaction;
            }
            return $"{reaction}\n{Texts.Red(T.AdjNotControllable)}";
        }

        private string CombatTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var attack = unit.PreviewBestAttack();
            sb.Append(T.StatDPS);
            sb.Append(": <b>");
            sb.Append(attack == null ? Texts.RedMinus : attack.Stats.DPS.Display());
            sb.Append("</b>");
            if (attack != null) {
                sb.Append(' ');
                sb.Append(Texts.Grey($"({attack.NameT})"));
            }
            sb.Append('\n');
            AppendWeapon(sb);
            sb.Append(GlyphMap.BRHR);
            AppendArmor(sb, data.StatsBeing.ArmorBlunt, true);
            AppendArmor(sb, data.StatsBeing.ArmorSharp, true);
            AppendArmor(sb, data.StatsBeing.ArmorEnergy, true);
            AppendArmor(sb, data.StatsBeing.ArmorPiercing, false);
            sb.Append(GlyphMap.BRHR);
            sb.Append(ReactionTooltip());
            return sb.ToString();
        }

        private void AppendWeapon(StringBuilder sb) {
            string weapon = null;
            if (data.Inventory?.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var slot) == true) {
                weapon = slot.Item?.ItemName;
            }
            sb.Append(T.EquipmentSlotWeapon);
            sb.Append(": <b>");
            sb.Append(weapon ?? Texts.RedMinus);
            sb.Append("</b>");
        }

        private static void AppendArmor(StringBuilder sb, Stat stat, bool newLine) {
            sb.Append(Stat.NameFor(stat));
            sb.Append(": <b>");
            sb.Append(stat.Display());
            sb.Append("</b>");
            if (newLine) {
                sb.Append('\n');
            }
        }
    }
}
