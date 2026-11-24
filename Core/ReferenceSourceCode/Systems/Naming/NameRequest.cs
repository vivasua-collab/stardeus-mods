using System;
using Game.CodeGen;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Naming {
    public struct NameRequest {
        public NameType Type;
        public int SubTypeId;
        public SpaceMapObjectType SpaceType;
        public BeingType BeingType;
        public Species Species;
        public Rng Rng;
        public char Sex;
        public static NameRequest ForShipOS(Rng rng = null) {
            return new NameRequest {
                Type = NameType.ShipOS,
                Rng = rng,
            };
        }
        public static NameRequest ForSpaceMap(SpaceMapObjectType type, Rng rng = null) {
            return new NameRequest {
                Type = NameType.SpaceMapObject,
                SpaceType = type,
                Rng = rng,
            };
        }
        public static NameRequest ForSO(int soTypeId, Rng rng = null) {
            return new NameRequest {
                Type = NameType.SpaceObject,
                SubTypeId = soTypeId,
                Rng = rng,
            };
        }
        public static NameRequest Generic(NameType type, Rng rng = null) {
            return new NameRequest {
                Type = type,
                Rng = rng,
            };
        }
        public static NameRequest ForShip(Rng rng = null) {
            return new NameRequest {
                Type = NameType.Ship,
                Rng = rng,
            };
        }
        public static NameRequest ForSpecies(Species species, BeingType beingType, Rng rng = null) {
            if (beingType == BeingType.None) {
                D.Err("Generating being without BeingType! This will produce bland generic names only!");
            }
            char sexChar;
            if (species.Sexes == null) {
                sexChar = 'N';
            } else {
                sexChar = rng.From(species.Sexes).TypeChar;
            }
            return new NameRequest {
                Type = NameType.Being,
                BeingType = beingType,
                Rng = rng,
                Species = species,
                Sex = sexChar,
            };
        }
        public static NameRequest ForSpecies(Species species, BeingType beingType, char sex, Rng rng = null) {
            if (beingType == BeingType.None) {
                D.Err("Generating being without BeingType! Please provide a type!");
            }
            return new NameRequest {
                Type = NameType.Being,
                BeingType = beingType,
                Rng = rng,
                Species = species,
                Sex = sex,
            };
        }
        public override string ToString() {
            return $"NameRequest[{Type}|{Species?.Id}|{Sex}]";
        }
    }
}
