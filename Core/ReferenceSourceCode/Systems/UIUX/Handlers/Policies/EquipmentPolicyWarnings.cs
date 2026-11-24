using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Factions;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Tells the player when a weapon policy can never be satisfied, because the being
    // is incapable of the job the chosen weapon kind needs. This reads the same
    // Jobs checks that InventoryItemDef.IsCompatibleWith enforces via RequiredJobId,
    // so the warning cannot drift away from the filter that causes it.
    public static class EquipmentPolicyWarnings {
        private const int MaxNamesInTooltip = 10;

        public static int Spawn(PolicyTabContext ctx, Lifetime life, RectTransform container,
                InventorySlotType type, EquipmentSlotPolicy policy, int idx) {
            if (policy.Mode == AutoEquipMode.Manual) { return idx; }
            idx = SpawnUnfitPinnedItem(ctx, life, container, policy, idx);
            if (type.IdH != InventorySlotIdH.Weapon) { return idx; }
            return ctx.Scope == PolicyScope.Being
                ? SpawnForBeing(ctx, life, container, policy, idx)
                : SpawnForGroup(ctx, life, container, policy, idx);
        }

        // A pinned item the scope's body may not use (an old "Robot: Energy Rifle" policy
        // from before the rifle became illegal for robots) can never be satisfied.
        private static int SpawnUnfitPinnedItem(PolicyTabContext ctx, Lifetime life,
                RectTransform container, EquipmentSlotPolicy policy, int idx) {
            if (policy.ItemDefId == null) { return idx; }
            var body = EquipmentPolicyPickers.BodyOf(ctx);
            if (body == null) { return idx; }
            var item = InventoryItemDef.FastGet(policy.ItemDefIdH);
            if (item == null || item.IsAllowedFor(body, FactionDef.Player)) { return idx; }
            if (item.IsIllegalFor(body)) {
                return SpawnRow(life, container, $"{T.AdjIllegal}: {item.ItemName}",
                    T.WeaponIllegalForRobots, idx);
            }
            return SpawnRow(life, container, $"{T.AdjIncompatible}: {item.ItemName}", null,
                idx);
        }

        private static int SpawnForBeing(PolicyTabContext ctx, Lifetime life,
                RectTransform container, EquipmentSlotPolicy policy, int idx) {
            var jobs = ctx.Being?.Mind?.Jobs;
            if (jobs == null) { return idx; }
            if (!jobs.CanHaveWeapon()) {
                return SpawnRow(life, container, T.PolicyWarnNoWeapon, null, idx);
            }
            int requiredJob = RequiredJobFor(policy.WeaponKind);
            if (requiredJob != 0 && !jobs.CanDo(requiredJob)) {
                var kind = EquipmentPolicyTexts.WeaponKindName(policy.WeaponKind);
                return SpawnRow(life, container, T.PolicyWarnKindIncapable_V1.F(kind),
                    null, idx);
            }
            return idx;
        }

        private static int SpawnForGroup(PolicyTabContext ctx, Lifetime life,
                RectTransform container, EquipmentSlotPolicy policy, int idx) {
            int requiredJob = RequiredJobFor(policy.WeaponKind);
            int total = 0;
            int affected = 0;
            List<string> names = null;
            foreach (var b in ctx.ListedBeings) {
                if (ctx.Scope == PolicyScope.Species && b.Species != ctx.Species) { continue; }
                if (b.Inventory == null
                        || !b.Inventory.Equipment.ContainsKey(InventorySlotIdH.Weapon)) {
                    continue;
                }
                total++;
                var jobs = b.Mind?.Jobs;
                if (jobs == null) { continue; }
                if (jobs.CanHaveWeapon() && (requiredJob == 0 || jobs.CanDo(requiredJob))) {
                    continue;
                }
                affected++;
                if (names == null) { names = new List<string>(); }
                if (names.Count < MaxNamesInTooltip) {
                    names.Add(b.Mind.Name.FullNameOrNameless);
                }
            }
            if (affected == 0) { return idx; }
            var text = T.PolicyWarnCount_V2.F(Units.Num(affected), Units.Num(total));
            return SpawnRow(life, container, text,
                names == null ? null : string.Join("\n", names), idx);
        }

        private static int SpawnRow(Lifetime life, RectTransform container, string text,
                string tip, int idx) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text,
                container, idx++);
            w.Load(IconId.CWarning, Texts.Red(text));
            if (tip != null) {
                UIUtils.AddTip(life, w, tip);
            }
            return idx;
        }

        private static int RequiredJobFor(WeaponKind kind) {
            return kind switch {
                WeaponKind.Melee => JobTypeIdH.Melee,
                WeaponKind.Ranged => JobTypeIdH.Shooting,
                _ => 0,
            };
        }
    }
}
