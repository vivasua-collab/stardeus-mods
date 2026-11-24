
#if !PRODUCTION_BUILD
#define DEBUG_REGION
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceRegion : ISpaceEntity {
        /// <summary>
        /// Not the same as SO.Id!
        /// </summary>
        public int Id { get; set; }
        public int Seed;
        public string Name { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 UIPosition {
            get {
                if (SO.UI == null) {
                    D.Err("Trying to get UIPosition for SpaceRegion with null UI! {0}", this);
                    return Consts.V2Zero;
                }
                return SO.UI.Rekt.localPosition;
            }
        }
        public Color LinkColor => Colors.WithA(Biome.IconColor, 0.33f);
        public SpaceBiome Biome { get; set; }
        public SpaceMap Map;
        public SpaceObject SO { get; set; }
        /// <summary>
        /// Sectors inside this region
        /// </summary>
        public List<SpaceSector> Sectors;
        /// <summary>
        /// Other regions that can be reached from this one
        /// </summary>
        public List<SpaceLink> Links { get; private set; } = new();
        private SpaceSector inboundSector;
        public SpaceSector InboundSector {
            get {
                if (inboundSector != null) {
                    return inboundSector;
                }
                for (int i = Sectors.Count - 1; i >= 0; i--) {
                    SpaceSector sec = Sectors[i];
                    if (sec.Biome.CantBeInbound) {
                        continue;
                    }
                    bool isNearRelay = false;
                    foreach (var link in sec.Links) {
                        var n = (SpaceSector) link.OtherSideOf(sec);
                        if (n.IsHyperjumpRelay) {
                            isNearRelay = true;
                            break;
                        }
                    }
                    if (isNearRelay) {
                        continue;
                    }
                    inboundSector = sec;
                    return sec;
                }
                D.Err("Failed to find an inbound sector for {0}", this);
                inboundSector = Sectors[0];
                return inboundSector;
            }
        }

        public void AddLink(SpaceLink link) {
            if (!Links.AddUniqueInefficient(link)) {
                D.Err("Trying to add duplicate link to a region! {0} -> {1}", this, link);
            }
        }
        public void RemoveLink(SpaceLink link) {
            if (!Links.Remove(link)) {
                D.Err("Trying to remove non-existent link from a region! {0} -> {1}", this, link);
            }
        }

        public override string ToString() {
            return $"SpaceRegion[{Name}|Sec:{Sectors?.Count}|{Position}|Diff:{Biome?.Difficulty}|Links:{Links?.Count}]";
        }

        public override int GetHashCode() {
            return Hashes.Quick(Primes.Prime024, Id);
        }

        public override bool Equals(object obj) {
            if (obj is SpaceRegion reg) {
                return reg.Id == Id;
            }
            return false;
        }

        public string Tooltip() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(T.SpaceObjectRegion);
            sb.Append(": ");
            sb.Append(Texts.Accented(Name));
            sb.Append(GlyphMap.BRHR);
            sb.Append(T.GameDifficulty);
            sb.Append(": ");
            sb.Append(Biome.DifficultyText);
            #if DEBUG_REGION
            sb.AppendLine();
            Biome.DebugTooltip(sb);
            sb.AppendFormat("\nSectors: {0}", Texts.Green(Units.XNum(Sectors.Count)));
            var sectorCounts = Sectors.ToLookup(s => s.Biome.Template.Id);
            foreach (var sec in sectorCounts) {
                sb.AppendFormat("\n - {0} x{1}", Texts.Accented(sec.Key), sec.Count());
            }
            sb.AppendLine();
            // if (Biome.UnavailableDeposits.Count > 0) {
            //     sb.AppendLine();
            //     sb.Append(Texts.Red("Unavailable Deposits: "));
            //     sb.AppendJoin(", ", Biome.UnavailableDeposits);
            // }
            // sb.Append(D.DumpInstance(this));
            #endif
            return sb.ToString();
        }
    }
}
