using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Traits {
    [MessagePackObject]
    public class BeingTraitsData {
        [Key("Traits")] public string[] Traits;

        public static BeingTraitsData Serialize(BeingTraits traits) {
            if (Arrays.IsEmpty(traits.Slots)) { return null; }
            var d = new BeingTraitsData();
            d.Traits = new string[traits.Slots.Count];
            for (int i = traits.Slots.Count - 1; i >= 0; i--) {
                TraitSlot s = traits.Slots[i];
                d.Traits[i] = s.Trait.Id;
            }
            return d;
        }

        /// <returns>false if loading was not OK</returns>
        public static bool TryDeserialize(Mind owner, BeingTraitsData d) {
            var traits = BeingTraits.BlankFor(owner);
            owner.Traits = traits;
            if (d == null) {
                return true;
            }
            for (int i = 0; i < d.Traits.Length; i++) {
                string id = d.Traits[i];
                var trait = TraitDef.Get(id);
                if (trait != null) {
                    traits.AddTrait(trait);
                }
            }

            return true;
        }
    }
}
