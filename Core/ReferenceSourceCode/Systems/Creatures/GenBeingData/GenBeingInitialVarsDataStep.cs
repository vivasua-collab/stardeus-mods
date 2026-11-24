using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Procgen;
using Game.Procgen.Names;
using Game.Systems.AI;
using Game.Systems.Backstories;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures {
    public sealed class GenBeingIntialVarsDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingIntialVarsDataStep());
        }
        private const string StepId = "InitialVars";
        public override string Id => StepId;
        // we will run this after mind but before anything else, so we can have a body type
        // and some traits determined by this
        public override int Order => int.MinValue;
        private static Curve maxLibidoByAgeCurve;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            BeingSex sex = vars.Sex;
            if (sex == null) {
                sex = data.Species.RandomSex(rng);
                vars.Sex = sex;
            }
            if (sex != null) {
                vars.BodyType = rng.CharFromStr(sex.BodyTypes, 'N', sex.BodyTypeChances);
                vars.HeadType = rng.CharFromStr(sex.HeadTypes, 'N', null);
                vars.HairType = rng.CharFromStr(sex.HairTypes, 'N', null);
            } else {
                vars.BodyType = 'N';
                vars.HeadType = 'N';
                vars.HairType = 'N';
            }
            // D.Warn("Generated body type: {0} (options: {1}) for {2}",
            //     vars.BodyType, sex?.BodyTypes, data.Species);
            GenerateAgeVars(G.Ticks, data.Species, rng, vars);
            if (sex != null && vars.MaxLibido <= 0f) {
                vars.MaxLibido = GenerateLibido(rng, vars.AgeBody, vars.Species);
                if (vars.MaxLibido <= 0f) {
                    vars.MaxLibido = 0.01f;
                }
            }
            return true;
        }

        public static float GenerateLibido(Rng rng, float ageBody, Species species) {
            maxLibidoByAgeCurve ??= CurveDef.FastGet(CurveIdH.Drama_MaxLibidoByAge);
            var agePct = ageBody / species.LifeExpectancyYears;
            return maxLibidoByAgeCurve.Evaluate(agePct) + rng.RangeSmooth(-0.15f, 0.15f);
        }

        public static void GenerateAgeVars(long ticks, Species species,
                Rng rng, GenBeingVars vars) {
            if (vars.AgeBody == 0) {
                if (!vars.IsClone || vars.Species?.IdH == SpeciesIdH.Mindjar) {
                    vars.AgeBody = (uint) rng.Curve(species.SpawnAgeCurve);
                }
            }

            // TODO generate age outliers
            var lifeExpectancyDiff = species.LifeExpectancyYears * 0.05f;
            if (lifeExpectancyDiff < 3f) {
                lifeExpectancyDiff = 3f;
            }

            int maxAge = (int) (species.LifeExpectancyYears +
                rng.Range(-lifeExpectancyDiff, lifeExpectancyDiff));
            if (maxAge < vars.AgeBody) {
                maxAge = (int) (vars.AgeBody + lifeExpectancyDiff);
            }

            if (vars.IsClone) {
                vars.BirthTickBody = ticks;
            } else {
                vars.BirthTickBody = GetBirthTickForAge(rng, vars.AgeBody, ticks);
            }
            vars.BodyDeathAge = maxAge + rng.Value;
            if (vars.AgeMind == 0) {
                vars.AgeMind = vars.AgeBody;
            }
            if (vars.AgeMind == vars.AgeBody) {
                vars.BirthTickMind = vars.BirthTickBody;
            } else {
                vars.BirthTickMind = GetBirthTickForAge(rng, vars.AgeMind, ticks);
            }
        }

        public static long GetBirthTickForAge(Rng rng, uint age, long ticksNow) {
            var birthTick = ticksNow - (age * Consts.TicksPerYear
                + rng.Range(0, 350) * Consts.TicksPerDay);
            return birthTick;
        }

        public static float GetAgeForBirthTick(long birthTick, long ticksNow) {
            if (ticksNow <= birthTick) {
                return 0f;
            }
            return (ticksNow - birthTick) / (float) Consts.TicksPerYear;
        }

    }
}
