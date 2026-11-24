using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Traits {
    public sealed class BeingTraits {
        public Mind Owner;
        public readonly List<TraitSlot> Slots = new();
        private readonly HashSet<int> traitIds = new();
        public bool IsPromiscuous => traitIds.Contains(TraitIdH.Gigolo) || traitIds.Contains(TraitIdH.Hooker);
        public override string ToString() {
            return $"Traits [{Owner}]";
        }

        public bool HasTrait(int id) {
            return traitIds.Contains(id);
        }
        public bool HasAnyTrait(int id1, int id2) {
            return traitIds.Contains(id1) || traitIds.Contains(id2);
        }
        public bool HasAnyTrait(int id1, int id2, int id3) {
            return traitIds.Contains(id1) || traitIds.Contains(id2)
                || traitIds.Contains(id3);
        }

        public void AfterLoad() {
            foreach (var slot in Slots) {
                slot.AfterLoad();
            }
        }

        public void AddTrait(TraitDef trait) {
            if (!traitIds.Add(trait.IdH)) {
                D.Err("Trying to add a trait twice: {0} for {1}", trait, Owner);
                return;
            }
            Slots.Add(TraitSlot.NewFor(trait, this));
        }

        public void RemoveTrait(TraitDef trait) {
            if (!traitIds.Remove(trait.IdH)) {
                D.Err("Failed to remove trait from TraitIds: {0} for {1}",
                    trait, Owner);
            }
            for (int i = Slots.Count - 1; i >= 0; i--) {
                TraitSlot slot = Slots[i];
                if (slot.Trait == trait) {
                    Slots.RemoveAt(i);
                    return;
                }
            }
            D.Err("Did not find trait slot for trait {0} for {1}", trait, Owner);
        }

        // Removes the trait together with everything it registered on a spawned being (stats,
        // abilities, needs). RemoveTrait(TraitDef) only drops the slot, which is enough before
        // spawn and for a mind transfer, where the sources get rebuilt anyway.
        public void Uninstall(TraitSlot slot) {
            traitIds.Remove(slot.Trait.IdH);
            slot.RemoveTrait();
        }

        public static BeingTraits BlankFor(Mind mind) {
            var traits = new BeingTraits();
            traits.SetOwner(mind);
            return traits;
        }

        private void SetOwner(Mind owner) {
            Owner = owner;
        }

        public static void TransferBodyBoundTraits(Mind from, Mind to) {
            for (int i = from.Traits.Slots.Count - 1; i >= 0; i--) {
                var trait = from.Traits.Slots[i];
                if (trait.Trait.Flags.HasFlag(TraitFlags.BodyBound)) {
                    // D.Warn("Transferring body bound trait from {0} to {1}", from,
                    //     to, trait.Trait);
                    from.Traits.RemoveTrait(trait.Trait);
                    to.Traits.AddTrait(trait.Trait);
                }
            }
        }

        public void ApplyModifiersAfterLoad(GameState s, bool isLoading) {
            if (Owner == null || Owner.Owner == null) {
                return;
            }
            foreach (var tr in Slots) {
                tr.ApplyModifiersAfterLoadBeing(s, Owner.Owner, isLoading);
            }
        }
    }
}
