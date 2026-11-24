using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Backstories;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Policies;
using Game.Systems.Stats;
using Game.Systems.Traits;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public class MindData {
        [Key("Id")] public uint Id;
        [Key("PreviousId")] public uint PreviousId;
        [Key("OwnerId")] public uint OwnerId;
        [Key("FormerOwnerId")] public uint FormerOwnerId;
        [Key("Faction")] public string FactionId;
        [Key("OriginalFaction")] public string OriginalFactionId;
        [Key("ReturnFaction")] public string ReturnFactionId;
        [Key("Role")] public ColonyRole Role;
        [Key("Sex")] public char Sex;
        [Key("AttractedToSex")] public char[] AttractedToSex;
        [Key("Traits")] public BeingTraitsData Traits;
        [Key("Jobs")] public JobsData Jobs;
        [Key("Name")] public BeingName Name;
        [Key("CombatConfig")] public CombatUnitConfig CombatConfig;
        [Key("FavColorId")] public string FavColorId;
        [Key("Age")] public float Age;
        [Key("BirthTick")] public long BirthTick;
        [Key("Experience")] public ExperienceData Experience;
        [Key("Health")] public MentalHealthData Health;
        [Key("SpeciesId")] public string SpeciesId;
        [Key("MentalStats")] public Dictionary<int, float> MentalStats;
        [Key("ObjId")] public uint ObjId;
        [Key("LastCauseOfDeath")] public string LastCauseOfDeath;
        [Key("LastTimeOfDeath")] public long LastTimeOfDeath;
        [Key("DesertedTick")] public long DesertedTick;
        [Key("StasisSentTick")] public long StasisSentTick;
        [Key("StasisArrayId")] public int StasisArrayId;
        // byte, not the enum: no generated formatter needed for AOT builds
        [Key("ManifestStatus")] public byte ManifestStatus;
        [Key("EqPolicy")] public EquipmentPolicy EquipmentPolicy;
        [Key("Trust")] public List<TrustEffect> Trust;

        public static MindData Serialize(Mind mind) {
            if (mind == null) {
                return null;
            }
            var d = new MindData();
            d.Id = mind.Id;
            d.PreviousId = mind.PreviousId;
            d.ObjId = mind.ObjId;
            d.FormerOwnerId = mind.FormerOwnerId;
            d.FactionId = mind.Faction?.Id;
            d.OriginalFactionId = mind.OriginalFaction?.Id;
            d.ReturnFactionId = mind.ReturnFaction?.Id;
            d.Role = mind.Role;
            d.Traits = BeingTraitsData.Serialize(mind.Traits);
            d.Jobs = JobsData.Serialize(mind.Jobs);
            d.Name = mind.Name;
            d.Sex = mind.Sex?.TypeChar ?? default;
            d.Age = mind.Age;
            d.MentalStats = mind.MentalStats;
            d.CombatConfig = mind.CombatConfig;
            d.SpeciesId = mind.Species?.Id;
            d.FavColorId = mind.FavColorId;
            d.BirthTick = mind.BirthTick;
            d.LastCauseOfDeath = mind.LastCauseOfDeath;
            d.LastTimeOfDeath = mind.LastTimeOfDeath;
            d.DesertedTick = mind.DesertedTick;
            d.StasisSentTick = mind.StasisSentTick;
            d.StasisArrayId = mind.StasisArrayId;
            d.ManifestStatus = (byte) mind.ManifestStatus;
            d.EquipmentPolicy = mind.EquipmentPolicy;
            d.Experience = ExperienceData.Serialize(mind.Experience);
            d.AttractedToSex = BeingSex.Serialize(mind.AttractedToSex);
            d.Health = MentalHealthData.Serialize(mind.Health);
            if (mind.Trust.Effects.Count > 0) {
                d.Trust = mind.Trust.Effects;
            }
            return d;
        }

        /// <returns>false if loading was not OK</returns>
        public static Mind Deserialize(MindData data) {
            if (data == null) {
                return null;
            }
            var mind = new Mind();
            FactionDef fac;
            if (data.FactionId == null) {
                fac = FactionDef.Player;
            } else {
                fac = FactionDef.Get(data.FactionId);
                if (fac == null) {
                    fac = FactionDef.Player;
                }
            }
            // mind.Owner = owner;
            // if (owner != null) {
            //     owner.Mind = mind;
            // }
            mind.Name = data.Name;
            if (data.SpeciesId == null) {
                // mind.Species = owner?.Species;
            } else {
                mind.Species = Species.Get(data.SpeciesId);
            }
            if (mind.Name == null) {
                D.Err("Mind name is null during load for owner with id: {0} (could be body part container)",
                    data.OwnerId);
                var name = BeingName.BlankFor(mind.Species);
                mind.Name = name;
            }
            mind.Id = data.Id;
            mind.ObjId = data.ObjId;
            mind.FormerOwnerId = data.FormerOwnerId;
            mind.PreviousId = data.PreviousId;
            mind.Age = data.Age;
            mind.MentalStats = data.MentalStats;
            mind.FavColorId = data.FavColorId;
            mind.BirthTick = data.BirthTick;
            mind.CombatConfig = data.CombatConfig;
            mind.LastCauseOfDeath = data.LastCauseOfDeath;
            mind.LastTimeOfDeath = data.LastTimeOfDeath;
            mind.DesertedTick = data.DesertedTick;
            mind.StasisSentTick = data.StasisSentTick;
            mind.StasisArrayId = data.StasisArrayId;
            mind.ManifestStatus = (ManifestStatus) data.ManifestStatus;
            mind.EquipmentPolicy = data.EquipmentPolicy;
            mind.Experience = ExperienceData.Deserialize(mind, data.Experience);
            // Wild creatures have no backstory (GenBeingExperienceDataStep); crawlers saved
            // before that rule drop the one they rolled
            if (mind.Species?.IsWild == true) {
                mind.Experience?.Roles?.Clear();
            }
            if (mind.Species != null) {
                if (data.Sex != default) {
                    mind.Sex = mind.Species.GetSex(data.Sex);
                }
                mind.AttractedToSex = BeingSex.Deserialize(data.AttractedToSex, mind.Species);
            }
            mind.Jobs = JobsData.Deserialize(mind, data.Jobs);
            mind.Health = MentalHealthData.Deserialize(mind, data.Health);
            if (data.Trust != null) {
                mind.Trust.Effects.AddRange(data.Trust);
            }
            mind.Bonds = new BeingBonds();
            mind.Bonds.Owner = mind;
            mind.Bonds.Opinions.Clear();

            BeingTraitsData.TryDeserialize(mind, data.Traits);
            mind.SetFaction(fac, true);
            if (data.OriginalFactionId != null) {
                mind.OriginalFaction = FactionDef.Get(data.OriginalFactionId);
            }
            if (data.ReturnFactionId != null) {
                mind.ReturnFaction = FactionDef.Get(data.ReturnFactionId);
            }
            mind.Role = data.Role;
            return mind;
        }

        public static List<MindData> Serialize(Dictionary<uint, Mind> minds) {
            var res = new List<MindData>();
            foreach (var mind in minds.Values) {
                var md = Serialize(mind);
                if (md != null) {
                    res.Add(md);
                }
            }
            return res;
        }

        public static void Deserialize(BeingsSysData sysData, Dictionary<uint, Mind> minds) {
            if (minds.Count > 0) {
                D.Err("Minds are not empty in restore data! {0}", minds.Count);
                minds.Clear();
            }
            var mindData = sysData.Minds;
            if (mindData == null) {
                D.Err("Migration did not run before deserializing minds!");
                return;
            }
            foreach (var md in mindData) {
                var mind = Deserialize(md);
                if (!minds.TryAdd(mind.Id, mind)) {
                    D.Err("Duplicate mind when deserializing. Id: {0} | new: {1} | existing: {2}",
                        mind.Id, mind, minds[mind.Id]);
                }
            }

            foreach (var bd in sysData.Bonds) {
                BeingBondsData.TryDeserialize(minds, bd);
            }
        }
    }
}
