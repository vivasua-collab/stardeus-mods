using System;
using System.Collections.Generic;
using Game.Data.Factions;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceBiomeData {
        [Key("Type")] public SpaceBiomeType Type;
        [Key("Diff")] public float Difficulty;
        [Key("TplId")] public string TemplateId;
        [Key("Deposits")] public Dictionary<string, int> Deposits;
        [Key("POIs")] public Dictionary<string, int> POIs;
        [Key("Effects")] public Dictionary<string, int> Effects;
        [Key("Factions")] public Dictionary<string, float> Factions;

        public static SpaceBiomeData Serialize(SpaceBiome biome) {
            var d = new SpaceBiomeData();
            d.Difficulty = biome.Difficulty;
            d.Type = biome.Type;
            d.TemplateId = biome.Template?.Id;
            d.Deposits = new Dictionary<string, int>();
            if (biome.FactionsByDominance != null) {
                d.Factions = new();
                foreach (var fac in biome.FactionsByDominance) {
                    d.Factions.Add(fac.Key.Id, fac.Value);
                }
            }
            foreach (var dep in biome.Deposits) {
                d.Deposits.Add(dep.Key.Id, dep.Value);
            }
            d.POIs = new Dictionary<string, int>();
            foreach (var dep in biome.POIs) {
                d.POIs.Add(dep.Key.Id, dep.Value);
            }
            d.Effects = new Dictionary<string, int>();
            foreach (var eff in biome.Effects) {
                d.Effects.Add(eff.Key.Id, eff.Value);
            }
            return d;
        }

        public static SpaceBiome Deserialize(SpaceBiomeData data) {
            var b = new SpaceBiome();
            b.Difficulty = data.Difficulty;
            b.Type = data.Type;
            switch (b.Type) {
                case SpaceBiomeType.Region: {
                    b.SetTemplate(RegionTemplate.Get(data.TemplateId));
                    break;
                }
                case SpaceBiomeType.Sector: {
                    b.SetTemplate(SectorTemplate.Get(data.TemplateId));
                    break;
                }
                default: {
                    D.Err("SpaceBiomeData saved without correct SpaceBiomeType! {0}",
                        D.DumpInstance(data));
                    break;
                }
            }
            if (data.Deposits?.Count > 0) {
                foreach (var dep in data.Deposits) {
                    var cfg = DepositConfig.Get(dep.Key);
                    if (cfg != null) {
                        b.Deposits.Add(cfg, dep.Value);
                    }
                }
            }
            if (data.POIs?.Count > 0) {
                foreach (var poi in data.POIs) {
                    var cfg = POIConfig.Get(poi.Key);
                    if (cfg != null) {
                        b.POIs.Add(cfg, poi.Value);
                    }
                }
            }
            if (data.Effects?.Count > 0) {
                foreach (var eff in data.Effects) {
                    var cfg = SpaceEffectConfig.Get(eff.Key);
                    if (cfg != null) {
                        b.Effects.Add(cfg, eff.Value);
                    }
                }
            }
            if (!Arrays.IsEmpty(data.Factions)) {
                foreach (var fac in data.Factions) {
                    var f = FactionDef.Get(fac.Key);
                    if (f == null) {
                        D.Err("Failed loading faction for biome by id: {0} | {1}",
                            fac.Key, b);
                        continue;
                    }
                    b.AddFaction(f, fac.Value);
                }
            }
            return b;
        }
    }
}
