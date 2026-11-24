using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Policies {
    public sealed class PoliciesSys : GameSystem, ISaveableSpecial {
        public const string SysId = "PoliciesSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PoliciesSys());
        }

        // null means "nothing configured", which resolves to EquipmentPolicyDefaults.
        public EquipmentPolicy GlobalEquipment;
        private readonly Dictionary<int, EquipmentPolicy> equipmentBySpecies = new();

        // Starts at 1 so a caller's "never resolved" sentinel of 0 or -1 is always stale.
        private int version = 1;
        public int Version => version;

        protected override void OnInitialize() {
            // Defaults capture Tunables.AutoForbidDroppedWeapons, which a mod or a test
            // may have changed since the last game.
            EquipmentPolicyDefaults.Reset();
            S.Query.EquipmentPolicyFor.SetResponder(EquipmentPolicyFor);
            S.Query.EquipmentPolicyVersion.SetResponder(GetVersion);
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        public override void Unload() {
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
        }

        // Editing a policy sweeps everyone immediately, but beings that were away at
        // the time (expedition, stasis, unspawned) are missed. Re-check hourly so the
        // colony converges without any per-being bookkeeping.
        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != 43) { return; }
            EnforceComplianceAll();
        }

        private int GetVersion() {
            return version;
        }

        public EquipmentSlotPolicy EquipmentPolicyFor(BeingData being, InventorySlotType type) {
            if (type == null) {
                // Callers that work on "any slot" pass null and skip policy filtering.
                return null;
            }
            return EquipmentPolicyResolver.Resolve(being?.Mind?.EquipmentPolicy,
                SpeciesEquipment(being?.Species), GlobalEquipment, type);
        }

        // Same chain minus the scopes that don't exist above a species / globally.
        // Used by the policy UI to show what a Species or Global tab resolves to.
        public EquipmentSlotPolicy EquipmentPolicyForSpecies(Species species,
                InventorySlotType type) {
            return EquipmentPolicyResolver.Resolve(null, SpeciesEquipment(species),
                GlobalEquipment, type);
        }

        public EquipmentSlotPolicy EquipmentPolicyGlobal(InventorySlotType type) {
            return EquipmentPolicyResolver.Resolve(null, null, GlobalEquipment, type);
        }

        public EquipmentPolicy SpeciesEquipment(Species species) {
            if (species == null) { return null; }
            equipmentBySpecies.TryGetValue(species.IdH, out var policy);
            return policy;
        }

        public PolicyOrigin OriginFor(BeingData being) {
            return EquipmentPolicyResolver.OriginOf(being?.Mind?.EquipmentPolicy,
                SpeciesEquipment(being?.Species), GlobalEquipment);
        }

        public PolicyOrigin OriginForSpecies(Species species) {
            return EquipmentPolicyResolver.OriginOf(null, SpeciesEquipment(species),
                GlobalEquipment);
        }

        // The policy a scope shows when it has none of its own: what it inherits.
        public EquipmentPolicy InheritedFor(BeingData being) {
            return SpeciesEquipment(being?.Species) ?? GlobalEquipment
                ?? EquipmentPolicyDefaults.CreateDefault();
        }

        public EquipmentPolicy InheritedForSpecies() {
            return GlobalEquipment ?? EquipmentPolicyDefaults.CreateDefault();
        }

        // The Set* methods only store. Publishing is left to the caller, so a UI edit
        // can promote a scope to its own policy and apply the actual change in one go
        // instead of firing two change notifications. Follow every Set* with
        // CommitChange (or MarkChanged mid-drag).
        public void SetGlobalEquipment(EquipmentPolicy policy) {
            GlobalEquipment = policy;
        }

        // null removes the override and lets the species inherit the global policy.
        public void SetSpeciesEquipment(Species species, EquipmentPolicy policy) {
            if (species == null) {
                D.Err("Trying to set an equipment policy for a null species");
                return;
            }
            if (policy == null) {
                equipmentBySpecies.Remove(species.IdH);
            } else {
                equipmentBySpecies[species.IdH] = policy;
            }
        }

        // null removes the override and lets the being inherit species/global.
        public void SetIndividualEquipment(BeingData being, EquipmentPolicy policy) {
            if (being?.Mind == null) {
                D.Err("Trying to set an equipment policy on a being without a mind: {0}", being);
                return;
            }
            being.Mind.EquipmentPolicy = policy;
        }

        // Publishes an in-place edit: the AI sees the new values from here on. Cheap
        // enough to call while a slider is still being dragged, which is why it does
        // not run the compliance sweep - stripping gear on every intermediate value
        // would drop items the player never meant to forbid.
        public void MarkChanged() {
            version++;
            S.Sig.EquipmentPolicyChanged.Send();
        }

        // A finished edit: publish it and act on it.
        public void CommitChange() {
            version++;
            EnforceComplianceAll();
            S.Sig.EquipmentPolicyChanged.Send();
        }

        public void EnforceComplianceAll() {
            foreach (var being in S.Query.AllBeingData.Ask().Values) {
                EnforceCompliance(being);
            }
        }

        // Takes off gear the policy forbids when the slot asks for it immediately
        // rather than waiting for a replacement. Never touches Manual slots, and
        // never leaves a being without life support.
        public void EnforceCompliance(BeingData being) {
            if (being == null) { return; }
            if (!being.IsPlayer || being.IsGuest) { return; }
            var inventory = being.Inventory;
            if (inventory == null) { return; }
            // Dropping needs a spawned body to drop next to.
            if (being.Being == null) { return; }
            if (being.HasStateFlag(BeingState.Dead)) { return; }
            foreach (var slot in inventory.Equipment.Values) {
                if (slot.ObjData == null) { continue; }
                var policy = EquipmentPolicyFor(being, slot.Type);
                // Cheap opt-out before the palette lookup the filter may do.
                if (policy?.WantsImmediateUnequip != true) { continue; }
                var filter = EquipmentPolicyFilter.For(policy, being);
                if (!EquipmentPolicyUtils.ShouldUnequipNow(slot.ObjData, policy, filter)) {
                    continue;
                }
                if (!slot.CanUnequipSafely(being)) { continue; }
                slot.RemoveItem();
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            if (GlobalEquipment == null && equipmentBySpecies.Count == 0) {
                return;
            }
            sd.Policies ??= new PoliciesSysData();
            sd.Policies.GlobalEquipment = GlobalEquipment;
            if (equipmentBySpecies.Count == 0) {
                sd.Policies.EquipmentBySpecies = null;
                return;
            }
            var bySpecies = new Dictionary<string, EquipmentPolicy>(equipmentBySpecies.Count);
            foreach (var kv in equipmentBySpecies) {
                if (!Species.Known.TryGetValue(kv.Key, out var species)) {
                    D.Warn("Dropping equipment policy for unknown species hash: {0}", kv.Key);
                    continue;
                }
                bySpecies[species.Id] = kv.Value;
            }
            sd.Policies.EquipmentBySpecies = bySpecies;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            GlobalEquipment = null;
            equipmentBySpecies.Clear();
            version++;
            var data = sd?.Policies;
            if (data == null) { return; }
            GlobalEquipment = data.GlobalEquipment;
            if (data.EquipmentBySpecies == null) { return; }
            foreach (var kv in data.EquipmentBySpecies) {
                if (kv.Value == null) { continue; }
                // A species can disappear when a mod is removed. Losing its policy is
                // not a reason to fail the load.
                if (!Species.Known.TryGetValue(Hashes.S(kv.Key), out var species)) {
                    D.Warn("Dropping equipment policy for unknown species: {0}", kv.Key);
                    continue;
                }
                equipmentBySpecies[species.IdH] = kv.Value;
            }
        }
    }
}
