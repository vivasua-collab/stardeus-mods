using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Systems.Space;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Procgen {
    public class ShipFactoryReport {
        [SelfInit]
        private static void Reset() {
            WantsFullReport = false;
        }
        public static bool WantsFullReport;
        public int Seed;
        public int ShipWidth;
        public int ShipHeight;
        public float ShipSize;
        public int Attempts;
        public bool IsSuccess;
        public double Duration;
        public SpaceMap SpaceMap;
        public struct ReportEntry {
            public string Stage;
            public int SeedBefore;
            public int SeedAfter;
            public int Attempts;
            public double Duration;
            public Def[] Defs;

            public override bool Equals(object obj) {
                if (obj is ReportEntry re) {
                    return GetHashCode() == re.GetHashCode();
                }
                return false;
            }

            public void CalculateDefs() {
                Defs = new Def[Pos.GridSize];
                for (int i = 0; i < Pos.GridSize; i++) {
                    var def = EntityUtils.GetTopmostParentTile(i);
                    if (def != null) {
                        Defs[i] = def.Definition;
                    }
                }
            }

            public override int GetHashCode() {
                var hash = 7;
                if (Stage != null) {
                    hash = Hashes.Quick(hash, Stage.GetHashCode());
                }
                hash = Hashes.Quick(hash, SeedBefore);
                hash = Hashes.Quick(hash, SeedAfter);
                hash = Hashes.Quick(hash, Attempts);
                if (Defs != null) {
                    for (int i = Defs.Length - 1; i >= 0; i--) {
                        Def d = Defs[i];
                        if (d != null) {
                            hash = Hashes.Quick(hash, i + d.GetHashCode());
                        }
                    }
                }

                return hash;
            }

            public override string ToString() {
                return $"[{Stage}] {SeedBefore} -> {SeedAfter}]";
            }
        }
        public readonly List<ReportEntry> Stages = new();


        public void Add(ReportEntry entry) {
            Stages.Add(entry);
        }

        public override bool Equals(object obj) {
            if (obj == null) { return false; }
            if (!(obj is ShipFactoryReport r)) { return false; }
            return GetHashCode() == r.GetHashCode();
        }

        public override int GetHashCode() {
            var seed = 17;
            var hash = Hashes.Quick(seed, Seed);
            hash = Hashes.Quick(hash, ShipWidth);
            hash = Hashes.Quick(hash, ShipHeight);
            hash = Hashes.Quick(hash, Attempts);
            hash = Hashes.Quick(hash, IsSuccess ? 1 : 2);
            foreach (var stage in Stages) {
                hash = Hashes.Quick(hash, stage.GetHashCode());
            }
            return hash;
        }

        public bool CompareSpaceMap(ShipFactoryReport other) {
            var sm1 = SpaceMap;
            var sm2 = other.SpaceMap;
            bool match = true;

            if (Arrays.Count(sm1.Regions) != Arrays.Count(sm2.Regions)) {
                D.Err("Region count mismatch! {0} vs {1}",
                    sm1.Regions.Count, sm2.Regions.Count);
                match = false;
            }
            for (int i = sm1.Regions.Count - 1; i >= 0; i--) {
                SpaceRegion reg1 = sm1.Regions[i];
                SpaceRegion reg2 = sm2.Regions[i];
                if (!CompareRegions(reg1, reg2)) {
                    match = false;
                }
            }
            return match;
        }

        private bool CompareRegions(SpaceRegion reg1, SpaceRegion reg2) {
            bool match = true;
            if (Arrays.Count(reg1.Sectors) != Arrays.Count(reg2.Sectors)) {
                D.Err("Region count mismatch! {0} vs {1}",
                    reg1.Sectors.Count, reg2.Sectors.Count);
                return false;
            }
            if (!CompareSpaceEntities(reg1, reg2)) {
                match = false;
            }
            for (int i = reg1.Sectors.Count - 1; i >= 0; i--) {
                SpaceSector sec1 = reg1.Sectors[i];
                SpaceSector sec2 = reg2.Sectors[i];
                if (!CompareSpaceEntities(sec1, sec2)) {
                    match = false;
                }
            }
            return match;
        }

        private List<DepositConfig> depositConfigs;

        private bool CompareSpaceEntities(ISpaceEntity en1, ISpaceEntity en2) {
            // D.Warn("Comparing {0} and {1}", en1, en2);
            bool match = true;
            if (en1.Biome.Difficulty != en2.Biome.Difficulty) {
                D.Err("Difficulty mismatch! {0} != {1} for {2} and {3}",
                    en1.Biome.Difficulty, en2.Biome.Difficulty, en1, en2);
                match = false;
            }
            if (en1.Links.Count != en2.Links.Count) {
                D.Err("Link count mismatch! {0} != {1} for {2} and {3}",
                    en1.Links.Count, en2.Links.Count, en1, en2);
                match = false;
            }
            if (en1.Biome.Template.Id != en2.Biome.Template.Id) {
                D.Err("Biome template mismatch! {0} != {1} for {2} and {3}",
                    en1.Biome.Template.Id, en2.Biome.Template.Id, en1, en2);
                match = false;
            } else {
                if (Arrays.Count(en1.Biome.Deposits) != Arrays.Count(en2.Biome.Deposits)) {
                    D.Err("Biome deposits count mismatch! {0} != {1} for {2} and {3}",
                        en1.Biome.Deposits?.Count, en2.Biome.Deposits?.Count, en1, en2);
                    match = false;
                } else {
                    depositConfigs ??= new();
                    depositConfigs.Clear();
                    foreach (var dep in en1.Biome.Deposits) {
                        depositConfigs.Add(dep.Key);
                    }
                    foreach (var dep in depositConfigs) {
                        if (en2.Biome.Deposits.TryGetValue(dep, out var cnt)) {
                            if (en1.Biome.Deposits[dep] != cnt) {
                                D.Err("Biome deposit count mismatch: {0} ({1} != {2}). {3} and {4}",
                                    dep.Id, en1.Biome.Deposits[dep], cnt, en1, en2);
                                match = false;
                            }
                        } else {
                            D.Err("Biome deposit not found in both: {0}. {1} and {2}",
                                dep.Id, en1, en2);
                            match = false;
                        }
                    }
                }
            }
            return match;
        }

        public bool HasDifferences(ShipFactoryReport other,
                out ReportEntry firstDifference, out int diffIdx) {
            diffIdx = -1;
            if (other == null) {
                D.Err("Other report was null");
                diffIdx = 0;
                firstDifference = Stages[0];
                return true;
            }
            for (int i = 0; i < Mathf.Min(other.Stages.Count, Stages.Count); i++) {
                var s1 = Stages[i];
                var s2 = other.Stages[i];
                if (!s1.Equals(s2)) {
                    if (s1.Defs != null && s2.Defs != null) {
                        var diffPercent = DifferenceBetween(s1, s2, out var pos, out var d1, out var d2);
                        D.Err("Stages do not match: {0} != {1}. Difference: {2}%. First mismatch at: {3}, expected: {4}. got: {5}",
                            s1, s2, diffPercent, Pos.String(pos), d1, d2);
                        EntityUtils.CameraFocusOn(pos);
                    }
                    diffIdx = i;
                    firstDifference = s1;
                    return true;
                }
            }
            firstDifference = default;
            return false;
        }

        private static float DifferenceBetween(ReportEntry r1, ReportEntry r2, out int pos, out Def d1, out Def d2) {
            int mismatch = 0;
            bool firstFound = false;
            pos = Pos.Invalid;
            d1 = null;
            d2 = null;
            for (int i = 0; i < Pos.GridSize; i++) {
                if (r1.Defs[i] != r2.Defs[i]) {
                    mismatch++;
                    if (!firstFound) {
                        firstFound = true;
                        pos = i;
                        d1 = r1.Defs[i];
                        d2 = r2.Defs[i];
                    }
                }
            }
            return mismatch / (float) Pos.GridSize;
        }
    }
}
