using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Procgen;
using Game.Procgen.Names;
using Game.Systems.AI;
using Game.Systems.Mental;
using Game.Systems.Naming;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingMindDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingMindDataStep());
        }
        private const string StepId = "Mind";
        public override string Id => StepId;
        // Generate this before anything else
        public override int Order => -20;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (data.Mind != null) {
                return true;
            }
            Mind mind;
            if (vars.BodyWithoutParts) {
                mind = Mind.Blank(BeingName.BlankFor(data.Species), data.Species);
                mind.SetOwner(data);
                return true;
            }
            mind = new Mind();
            mind.BirthTick = vars.BirthTickMind;
            mind.Age = GenBeingIntialVarsDataStep.GetAgeForBirthTick(
                mind.BirthTick, G.Ticks);
            mind.Species = data.Species;
            mind.Sex = vars.MindSex ?? vars.Sex;
            // Wild creatures are unmodified organisms: no bionic, transplanted or oversized parts
            // and no borrowed minds, whatever body vars the caller passed. Plenty of callers pass
            // a faction default without thinking about species - the player faction rolls a
            // modded body 15% of the time - and since the body rework a crawler Leg or Jaw slot
            // accepts cat, dog and pig parts, so a freak crawler is one roll away. Deciding it
            // here covers every spawn path, because the body step only honours body vars when
            // UseBodyVars is set. Scars are copied regardless and still apply.
            if (vars.Body.ChanceModded > 0f && !data.Species.IsWild) {
                vars.UseBodyVars = rng.Chance(vars.Body.ChanceModded);
            }
            bool needsRandomizeMind = false;
            if (vars.MindSpecies != null) {
                mind.Species = vars.MindSpecies;
            } else if (vars.UseBodyVars && vars.Body.ChanceCustomMind > 0f) {
                if (rng.Chance(vars.Body.ChanceCustomMind)) {
                    mind.Species = Species.RandomExcept(rng, data.Species);
                    needsRandomizeMind = true;
                }
            }
            if (mind.Species.IdH == SpeciesIdH.Mindjar) {
                mind.Species = Species.Get(SpeciesIdH.Human);
                needsRandomizeMind = true;
            }
            if (needsRandomizeMind) {
                mind.Sex = mind.Species.RandomSex(rng);
                mind.Age = rng.IntCurve((Curve) mind.Species.SpawnAgeCurve);
                vars.AgeMind = (uint) mind.Age;
                vars.BirthTickMind = GenBeingIntialVarsDataStep.GetBirthTickForAge(
                    rng, vars.AgeMind, G.Ticks);
                mind.BirthTick = vars.BirthTickMind;
                mind.Age = GenBeingIntialVarsDataStep.GetAgeForBirthTick(
                    mind.BirthTick, G.Ticks);
            }
            if (!Arrays.ContainsSafe(mind.Species.Sexes, mind.Sex)) {
                // We generated a sex that is not supported for this mind species
                mind.Sex = mind.Species.RandomSex(rng);
            }

            char sexChar = GenerateAttraction(rng, mind);
            mind.CombatConfig = data.Species.DefaultCombatConfig;
            mind.SetOwner(data);
            // Role must be set before any SetFaction (GenerateFaction does one of its own):
            // SetFaction -> AfterSetFaction -> Name.CheckState() colors the name by IsGuest
            // (Mind.Role), and Role has no change notification (same ordering rule as
            // SpawnPassengerQuestStep).
            mind.Role = vars.Role;
            GenerateFaction(data, mind);
            if (vars.Faction != null) {
                mind.SetFaction(vars.Faction);
            }
            GenerateName(S, mind.Species, vars.BeingType, rng, mind, sexChar,
                vars.ExceptNames);
            mind.Name.UpdateName();
            GenerateMentalHealth(mind.Species, rng, mind);

            return true;
        }

        private static void GenerateFaction(BeingData data, Mind mind) {
            FactionDef faction = FactionDef.Player;
            if (data.Species.ForcedFaction != null) {
                var fac = FactionDef.Get(data.Species.ForcedFaction);
                if (fac == null) {
                    D.Err("ForcedFaction id is incorrect for species: {0}: {1}",
                        data.Species.ForcedFaction, data.Species);
                } else {
                    faction = fac;
                }
            }
            mind.SetFaction(faction);
        }

        public static void GenerateName(GameState S, Species species,
                BeingType beingType, Rng rng,
                Mind mind, char sexChar, HashSet<int> exceptNames) {

            if (beingType == BeingType.None) {
                // we will set the name later somehow
                mind.Name = BeingName.BlankFor(species);
                return;
            }

            var req = NameRequest.ForSpecies(species, beingType, sexChar, rng);
            var nameRes = S.Query.GenerateNameExcept.Ask(req, exceptNames);
            if (nameRes == null || nameRes.BeingName == null) {
                D.Err("Failed getting name result for being: {0} -> {1}", req, nameRes);
                mind.Name = BeingName.BlankFor(species);
            } else {
                mind.Name = nameRes.BeingName;
            }
        }

        public static void GenerateMentalHealth(Species species,
                Rng rng, Mind mind) {
            if (species.GrantsMentalHealth) {
                mind.Health = MentalHealth.TryNewFor(mind);
                mind.FavColorId = rng.From(Palette.Generic).Id;
                mind.GenerateMentalStats(rng);
                if (mind.Owner != null) {
                    mind.LoadMentalStats();
                }
            }
        }

        public static char GenerateAttraction(Rng rng, Mind mind) {
            var sex = mind.Sex;
            char sexChar = sex == null ? 'N' : sex.TypeChar;
            if (!Arrays.IsEmpty(mind.Species.SexualOrientations)) {
                var sexOr = mind.Species.RandomSexualOrientation(rng);
                mind.AttractedToSex = sexOr.Attractions(mind.Species, sex);
            }

            return sexChar;
        }
    }
}
