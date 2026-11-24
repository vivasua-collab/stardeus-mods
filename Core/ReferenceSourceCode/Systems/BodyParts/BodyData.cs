using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using KL.Utils;
using MessagePack;

namespace Game.Systems.BodyParts {
    [MessagePackObject]
    public class BodyData {
        [Key("Slots")] public BodyPartSlotData[] Slots;
        [Key("State")] public BodyState State;
        [Key("Sex")] public char Sex;
        [Key("MaxLibido")] public float MaxLibido;
        [Key("BirthTick")] public long BirthTick;
        [Key("DeathTick")] public long DeathTick;
        [Key("DeathCause")] public string DeathCause;
        [Key("DeathAge")] public float DeathAge;
        [Key("SkinColorId")] public string SkinColorId;
        [Key("HairColorId")] public string HairColorId;
        [Key("EyeColorLId")] public string EyeColorLId;
        [Key("EyeColorRd")] public string EyeColorRId;
        [Key("Flags")] public BodyFlags Flags;
        [Key("Age")] public float Age;

        private static readonly HashSet<Species> warnedMismatchSpecies = new();
        private static readonly HashSet<Species> warnedPartialSpecies = new();

        public static BodyData Serialize(Body body) {
            var d = new BodyData();
            if (Arrays.IsEmpty(body.Slots)) {
                D.Err("Trying to serialize body with null slots! {0}",
                    D.DumpInstance(body));
            } else {
                d.Slots = new BodyPartSlotData[body.Slots.Length];
                for (int i = body.Slots.Length - 1; i >= 0; i--) {
                    BodyPartSlot s = body.Slots[i];
                    d.Slots[i] = BodyPartSlotData.Serialize(s);
                }
            }
            d.State = body.StateB.Value;
            d.Sex = body.Sex?.TypeChar ?? 'N';
            d.Age = body.Age;
            d.Flags = body.Flags;
            d.BirthTick = body.BirthTick;
            d.DeathTick = body.DeathTick;
            d.DeathCause = body.DeathCause;
            d.DeathAge = body.DeathAge;
            d.SkinColorId = body.SkinColorId;
            d.HairColorId = body.HairColorId;
            d.EyeColorLId = body.EyeColorLId;
            d.EyeColorRId = body.EyeColorRId;
            d.MaxLibido = body.MaxLibido;
            return d;
        }

        /// <returns>false if loading was not OK, in that case being body must be generated fresh or appended to fill missing parts</returns>
        public static bool TryDeserialize(BeingData owner, BodyData d, out Body body) {
            body = Body.BlankFor(owner);
            if (d == null) {
                return false;
            }
            if (Arrays.IsEmpty(d.Slots)) {
                D.Err("Body slots are empty! {0}", D.DumpInstance(d));
                return false;
            }
            bool isOK = true;
            // Once per species: a species body rework makes every being of it in an older save
            // mismatch, and repeating that per being per load buries everything else in the log
            if (d.Slots.Length != body.Slots.Length
                    && warnedMismatchSpecies.Add(owner.Species)) {
                D.Warn("There is a mismatch of saved vs native body part count for species: {0}. Trying to load what we can. Species slot types: {1}. Saved slot types: {2}. Further mismatches for this species will not be logged",
                    owner.Species, D.Dump(owner.Species.BodyPartSlotTypes),
                    D.Dump(d.Slots.Select(s => s.Id)));
                // isOK = false;
            }

            body.Age = d.Age;

            int deserialized = 0;
            for (int i1 = body.Slots.Length - 1; i1 >= 0; i1--) {
                BodyPartSlot bs = body.Slots[i1];
                bool found = false;
                for (int i = d.Slots.Length - 1; i >= 0; i--) {
                    BodyPartSlotData ds = d.Slots[i];
                    if (bs.Id == ds.Id) {
                        if (!BodyPartSlotData.TryDeserialize(ds, bs)) {
                            D.ErrSoft("Failed to deserialize slot: {0}", D.DumpInstance(ds));
                        } else {
                            found = true;
                            deserialized++;
                        }
                        break;
                    }
                }
                if (!found) {
                    GenBeingBodyDataStep.GenerateMissingFor(body, bs);
                }
            }

            if (deserialized < body.Slots.Length
                    && warnedPartialSpecies.Add(owner.Species)) {
                D.Warn("Failed to fully deserialize all body parts for species {0}! Expected: {1}. Deserialized: {2}. Further failures for this species will not be logged",
                    owner.Species, body.Slots.Length, deserialized);
                // isOK = false;
            }

            // A slot is either empty (IsPartRemoved) or has an InstalledDef. Saves break that
            // invariant when a saved part def id no longer resolves (renamed / removed def,
            // disabled mod): the slot keeps its state with IsPartRemoved false, and re-saving
            // persists it as InstalledId == null, so it survives every later load silently.
            for (int i1 = body.Slots.Length - 1; i1 >= 0; i1--) {
                BodyPartSlot bs = body.Slots[i1];
                if (bs.IsPartRemoved || bs.InstalledDef != null) { continue; }
                D.Warn("Body part slot {0} of {1} has no installed part def, regenerating",
                    bs.Id, owner);
                bs.SetState(null);
                GenBeingBodyDataStep.GenerateMissingFor(body, bs);
            }

            body.SetBodyState(d.State, true);
            body.DeathAge = d.DeathAge;
            body.DeathCause = d.DeathCause;
            body.BirthTick = d.BirthTick;
            body.Flags = d.Flags;
            if (d.DeathTick != 0) {
                body.SetDeathTick(d.DeathTick);
            }
            body.HairColorId = d.HairColorId;
            body.SkinColorId = d.SkinColorId;
            body.EyeColorLId = d.EyeColorLId;
            body.EyeColorRId = d.EyeColorRId;
            body.Sex = owner.Species.GetSex(d.Sex);
            body.MaxLibido = d.MaxLibido;
            return isOK;
        }
    }
}
