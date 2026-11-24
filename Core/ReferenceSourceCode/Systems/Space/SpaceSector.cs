using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Space;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceSector : ISpaceEntity {
        /// <summary>
        /// Not the same as SO.Id!
        /// </summary>
        public int Id { get; set; }
        public int Seed { get; set; }
        public string Name { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 UIPosition {
            get {
                if (SO.UI == null) {
                    D.Err("Trying to get UIPosition for SpaceSector with null UI! {0}", this);
                    return Consts.V2Zero;
                }
                return SO.UI.Rekt.localPosition;
            }
        }
        public SpaceRegion Region;
        public List<SpaceLink> Links { get; private set; } = new();
        public SpaceObject SO { get; set; }
        public Color LinkColor => Colors.WithA(Biome.IconColor, 0.33f);
        public SpaceBiome Biome { get; set; }
        public bool IsHyperjumpRelay => Biome.IsTemplate(
            SpaceEntityTemplateIdH.HyperjumpRelay);

        public string Tooltip() {
            return ToString();
        }
        public void AddLink(SpaceLink link) {
            if (!Links.AddUniqueInefficient(link)) {
                D.Err("Trying to add duplicate link to a sector! {0} -> {1}", this, link);
            }
        }
        public void RemoveLink(SpaceLink link) {
            if (!Links.Remove(link)) {
                D.Err("Trying to remove non-existent link from a sector! {0} -> {1}", this, link);
            }
        }
        public override int GetHashCode() {
            return Hashes.Quick(Primes.Prime031, Id);
        }

        public override bool Equals(object obj) {
            if (obj is SpaceSector sec) {
                return sec.Id == Id;
            }
            return false;
        }

        public override string ToString() {
            return $"SpaceSector[{Name}|{Position}|{Links?.Count}|Tpl:{Biome?.Template?.Id}]";
        }
    }
}
