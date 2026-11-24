using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using Game.Systems.Creatures;

namespace Game.Systems.Traits {
    public sealed class TraitSlot : IStatSource, IAbilitySourceSlot, INeedSourceSlot {
        public readonly TraitDef Trait;
        public readonly BeingTraits Traits;
        public string SlotName => Trait.NameT;
        public string SlotType => T.NounTrait;
        public string SlotIcon => Trait.IconId ?? Trait.Type.IconId;
        public string StatSourceNameT => Trait.NameT;
        public IAbilitySourceItem AbilitySourceItem => Trait;
        public bool IsUsableWithParent => true;

        public INeedSourceItem NeedSourceItem => Trait;

        public bool IsEssential;
        private readonly List<Stat> AffectedStats = new();
        private TraitSlot(TraitDef trait, BeingTraits traits) {
            Trait = trait;
            Traits = traits;
        }

        public static TraitSlot NewFor(TraitDef trait, BeingTraits traits) {
            var s = new TraitSlot(trait, traits);
            return s;
        }

        public void ApplyTrait(GameState s) {
            isInitialized = true;
            if (AddAbilitySources()) {
                Traits.Owner.Owner.AfterAbilitiesChanged();
            }
            if (AddNeedSources()) {
                Traits.Owner.Owner.AfterNeedsChanged(s);
            }
            AddStatSources();
            AddStatModifiers();
        }

        private bool AddNeedSources() {
            if (Trait.AddsNeeds == null) { return false; }
            bool added = false;
            foreach (var needId in Trait.AddsNeeds) {
                var src = NeedSource.CreateFor(needId, this);
                if (src.Need != null) {
                    Traits.Owner.Owner.AddNeedSource(src);
                    added = true;
                }
            }
            return added;
        }

        private void AddStatSources() {
            if (Trait.StatSources == null) { return; }
            var stats = Traits.Owner.Owner.StatsBeing;
            foreach (var ss in Trait.StatSources) {
                var stat = stats.AddStatSource(ss.StatId, this);
                if (stat != null) {
                    AffectedStats.Add(stat);
                    stat.SourcesChanged();
                    stat.Apply();
                }
            }
        }

        private bool AddAbilitySources() {
            if (Trait.AddsAbilities == null) { return false; }
            bool added = false;
            foreach (var ab in Trait.AddsAbilities) {
                var src = AbilitySource.CreateFor(ab, this);
                if (src.Ability != null) {
                    Traits.Owner.Owner.AbilitySources.Add(src);
                    added = true;
                }
            }
            return added;
        }

        // Use BeingTraits.Uninstall, which also forgets the trait id
        public void RemoveTrait() {
            // Sources are only registered by AfterLoad (spawn) or ApplyTrait; unspawned data
            // has nothing to tear down, and removing a modifier that was never added logs errors
            if (isInitialized) {
                RemoveSources();
            }
            Traits.Slots.Remove(this);
        }

        private void RemoveSources() {
            var data = Traits.Owner.Owner;
            RemoveStatModifiers();
            bool removedAb = false;
            var srcs = data.AbilitySources;
            for (int i = srcs.Count - 1; i >= 0; i--) {
                AbilitySource src = data.AbilitySources[i];
                if (src.Slot == this) {
                    srcs.RemoveAt(i);
                    removedAb = true;
                }
            }
            for (int i = AffectedStats.Count - 1; i >= 0; i--) {
                Stat st = AffectedStats[i];
                st.Sources.Remove(this);
                AffectedStats.RemoveAt(i);
                st.SourcesChanged();
            }
            bool removedNeeds = false;
            for (int i = data.NeedSources.Count - 1; i >= 0; i--) {
                if (data.NeedSources[i].Slot == this) {
                    data.NeedSources.RemoveAt(i);
                    removedNeeds = true;
                }
            }

            if (removedAb) {
                data.AfterAbilitiesChanged();
            }
            if (removedNeeds) {
                data.AfterNeedsChanged(A.S);
            }
            isInitialized = false;
        }

        private bool isInitialized;

        public void AfterLoad() {
            if (isInitialized) {
                return;
            } else {
                isInitialized = true;
            }
            AddAbilitySources();
            AddStatSources();
            AddStatModifiers();
            AddNeedSources();
        }
        private void AddStatModifiers() {
            if (Trait.StatModifiers == null) { return; }
            var stats = Traits.Owner.Owner.StatsBeing;
            foreach (var ss in Trait.StatModifiers) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.AddModifier(ss.Item2);
                }
            }
        }
        private void RemoveStatModifiers() {
            if (Trait.StatModifiers == null) { return; }
            var stats = Traits.Owner.Owner.StatsBeing;
            foreach (var ss in Trait.StatModifiers) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.RemoveModifier(ss.Item2);
                }
            }
        }

        public void UpdateStat(Stat stat) {
            if (Trait.StatSources == null) {
                D.Err("Trying to update stat when trait has no stat sources: {0}. Stat: {1}",
                    Trait.Id, stat);
                return;
            }
            if (Trait.GetStatBaseFor(stat, out float toAdd, out float limit)) {
                stat.CompoundBase(toAdd, limit);
            }
        }

        public bool GetEffectFor(Stat stat, out float toAdd, out float limit) {
            return Trait.GetStatBaseFor(stat, out toAdd, out limit);
        }

        public override string ToString() {
            return $"TraitSlot[{Trait.Id}]";
        }

        public void ApplyModifiersAfterGenBeing(GameState s, BeingData data,
                Rng rng, GenBeingVars vars) {
            if (Trait.BeingModifiers == null) { return; }
            foreach (var mod in Trait.BeingModifiers) {
                mod.AfterGenerateBeing(s, data, rng, vars);
            }
        }
        public void ApplyModifiersAfterLoadBeing(GameState s, BeingData data,
                bool isLoading) {
            if (Trait.BeingModifiers == null) { return; }
            foreach (var mod in Trait.BeingModifiers) {
                mod.AfterLoadBeing(s, data, isLoading);
            }
        }
    }
}
