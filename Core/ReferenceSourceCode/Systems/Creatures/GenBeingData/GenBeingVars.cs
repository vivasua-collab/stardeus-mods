
using System.Collections.Generic;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Backstories;
using Game.Systems.Naming;
using KL.Randomness;

namespace Game.Systems.Creatures {
    public sealed class GenBeingVars {
        public GenBeingVars(Species species, BeingType beingType) {
            Species = species;
            BeingType = beingType;
        }
        public readonly Species Species;
        public HashSet<int> ExceptNames;
        public FactionDef Faction;
        public BeingSex Sex;
        public BeingSex MindSex;
        public float MaxLibido;
        public AgePhase AgePhase;
        public readonly BeingType BeingType;
        public uint AgeMind;
        public uint AgeBody;
        public long BirthTickBody;
        public long BirthTickMind;
        public float BodyDeathAge;
        public string SexualOrientation;
        public char BodyType;
        public char HeadType;
        public char HairType;
        public bool BodyWithoutParts;
        public bool IsClone;
        public bool UseBodyVars;
        public GenBeingBodyVars Body;
        public Species MindSpecies;
        /// <summary>
        /// Special non-hostile standing (e.g. ColonyRole.Passenger for rescued guests)
        /// to give the generated being's mind. Must be applied before SetFaction - see
        /// GenBeingMindDataStep. Default Unassigned, so existing callers are unaffected.
        /// </summary>
        public ColonyRole Role;

        /// <summary>
        /// Resolves a LookPartTypeDef.VarTag ("Head"/"Hair"/default "Body") to the matching
        /// type char, used by GenLooks to pick a sprite variant for a look part.
        /// </summary>
        public char TagFor(string varTag) {
            switch (varTag) {
                case "Head": return HeadType;
                case "Hair": return HairType;
                default: return BodyType;
            }
        }
    }
}
