using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI.Combat;

namespace Game.Systems.Creatures {
    /// <summary>
    /// Species weighting for non-hostile derelict survivors (guests). Managing bot/robot guests
    /// is boring, so rescues are biased towards people: any faction that fields humans at all
    /// (FactionDef.AI below 1) yields only humans. All-synthetic factions (Sentinel, Virus) keep
    /// their bot mix at the current raid tier. Hostile derelict crews are unaffected by this
    /// helper - they draw from RaiderSpecies directly and keep the faction's human/bot share.
    /// </summary>
    public static class SurvivorSpecies {
        public static Dictionary<Species, float> WeightedFor(GameState S, FactionDef fac) {
            // Built fresh each call rather than cached statically - Species instances are
            // reset via [ExecuteOnReload] on domain reload.
            var res = new Dictionary<Species, float>();
            if (fac == null || fac.AI < 1f) {
                res[Species.Get(SpeciesIdH.Human)] = 1f;
                return res;
            }
            RaiderSpecies.BotsOnlyFor(RaiderSpecies.CurrentGearTier(S, fac), false, res);
            return res;
        }
    }
}
