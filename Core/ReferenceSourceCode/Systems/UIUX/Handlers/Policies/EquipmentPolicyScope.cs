using System;
using Game.Systems.Items;
using Game.Systems.Policies;

namespace Game.Systems.UIUX {
    // Resolves which equipment policy a policy tab scope reads from and writes to.
    // Shared by the Equipment tab shell and its slot sections.
    public static class EquipmentPolicyScope {
        // What this scope currently resolves to, for display.
        public static EquipmentSlotPolicy Effective(PolicyTabContext ctx,
                InventorySlotType type) {
            return ctx.Scope switch {
                PolicyScope.Being => ctx.Sys.EquipmentPolicyFor(ctx.Being, type),
                PolicyScope.Species => ctx.Sys.EquipmentPolicyForSpecies(ctx.Species, type),
                _ => ctx.Sys.EquipmentPolicyGlobal(type),
            };
        }

        // Whether this scope has a policy of its own rather than inheriting one.
        public static bool OwnsPolicy(PolicyTabContext ctx) {
            return ctx.Scope switch {
                PolicyScope.Being => ctx.Being?.Mind?.EquipmentPolicy != null,
                PolicyScope.Species => ctx.Sys.SpeciesEquipment(ctx.Species) != null,
                _ => ctx.Sys.GlobalEquipment != null,
            };
        }

        // The slot policy this scope owns, promoting it from the inherited values the
        // first time the player changes something. Resolved at edit time, never at
        // build time, so simply looking at a tab never creates an override.
        public static EquipmentSlotPolicy Editable(PolicyTabContext ctx,
                InventorySlotType type, Action onPromoted) {
            return EditablePolicy(ctx, onPromoted).GetOrAdd(type);
        }

        // The whole policy this scope owns, promoted the same way as for one slot.
        // onPromoted fires exactly where a scope gains its own policy, so every edit
        // path gets the notification without having to test for it at the call site.
        public static EquipmentPolicy EditablePolicy(PolicyTabContext ctx,
                Action onPromoted) {
            switch (ctx.Scope) {
                case PolicyScope.Being: {
                    var mind = ctx.Being?.Mind;
                    if (mind == null) { return new EquipmentPolicy(); }
                    if (mind.EquipmentPolicy == null) {
                        ctx.Sys.SetIndividualEquipment(ctx.Being,
                            ctx.Sys.InheritedFor(ctx.Being).Clone());
                        onPromoted?.Invoke();
                    }
                    return mind.EquipmentPolicy;
                }
                case PolicyScope.Species: {
                    var own = ctx.Sys.SpeciesEquipment(ctx.Species);
                    if (own == null) {
                        own = ctx.Sys.InheritedForSpecies().Clone();
                        ctx.Sys.SetSpeciesEquipment(ctx.Species, own);
                        onPromoted?.Invoke();
                    }
                    return own;
                }
                default: {
                    if (ctx.Sys.GlobalEquipment == null) {
                        ctx.Sys.SetGlobalEquipment(EquipmentPolicyDefaults.CreateDefault());
                        onPromoted?.Invoke();
                    }
                    return ctx.Sys.GlobalEquipment;
                }
            }
        }
    }
}
