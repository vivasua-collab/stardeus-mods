using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.BodyParts {
    [MessagePackObject]
    public struct BodyPartSlotData {
        [Key("Id")] public string Id;
        [Key("PartId")] public string InstalledId;
        [Key("PartState")] public BodyPartState State;
        [Key("IsRemoved")] public bool IsPartRemoved;
        [Key("IsDestroyed")] public bool IsDestroyed;
        // Missing in saves made before body achievements started tracking provenance, which
        // deserializes to false - the safe direction, an achievement that does not fire.
        [Key("SurgInst")] public bool IsPartSurgicallyInstalled;


        public static BodyPartSlotData Serialize(BodyPartSlot slot) {
            var d = new BodyPartSlotData();
            d.Id = slot.Id;
            d.InstalledId = slot.InstalledDef?.Id;
            d.State = slot.State;
            d.IsPartRemoved = slot.IsPartRemoved;
            d.IsDestroyed = slot.IsDestroyed;
            d.IsPartSurgicallyInstalled = slot.IsPartSurgicallyInstalled;
            return d;
        }

        public static bool TryDeserialize(BodyPartSlotData d, BodyPartSlot slot) {
            if (d.Id == null) {
                return false;
            }
            if (slot.Id != d.Id) {
                D.Err("ID mismatch in BodyPartSlotData deserialize! {0} != {1}", slot.Id, d.Id);
                return false;
            };
            if (d.InstalledId != null) {
                var def = BodyPartDef.Get(d.InstalledId);
                if (def == null) {
                    D.Err("BodyPartSlot with id {0} has missing body part with part def: {1}",
                        d.Id, d.InstalledId);
                }
                slot.InstalledDef = def;
                if (d.State == null) {
                    D.Err("BodyPartSlot was saved without a state! Generating blank state");
                    d.State = BodyPartState.BrandNew();
                }
            }
            slot.SetState(d.State);
            if (slot.State != null) {
                if (slot.State.Quality == 0f) {
                    var q = Rng.URangeSmooth(0.7f, 1f);
                    slot.State.SetQuality(q);
                } else if (slot.State.Quality < 0f) {
                    // fix for a bug where quality was generated as <0% for old people / bots
                    var q = Rng.URange(0.05f, 0.1f);
                    slot.State.SetQuality(q);
                }
            }
            slot.IsPartRemoved = d.IsPartRemoved;
            slot.IsDestroyed = d.IsDestroyed;
            slot.IsPartSurgicallyInstalled = d.IsPartSurgicallyInstalled;
            return true;
        }
    }
}
