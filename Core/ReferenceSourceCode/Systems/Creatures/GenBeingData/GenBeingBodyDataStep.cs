using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.BodyParts;
using Game.Systems.Naming;
using Game.Systems.Stats;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEditor;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingBodyDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingBodyDataStep());
        }

        public GenBeingBodyDataStep() {
            customFuncs = new TryCustomFunc[] {
                TryBionic,
                TryNonStandard,
                TryFreaky
            };
            tryCustomFuncs = new(customFuncs.Length);
        }
        public const string StepId = "Body";
        public override string Id => StepId;
        public override int Order => 0;
        private delegate bool TryCustomFunc(BeingData data, Rng rng,
            GenBeingBodyVars vars, BodyPartSlot slot,
            BodyPartState state, BodyPartDef standard, BodyPartDef keepAbilitiesOf);
        private readonly TryCustomFunc[] customFuncs;
        private readonly List<TryCustomFunc> tryCustomFuncs;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            var slotTypes = data.Species.BodyPartSlotTypes;
            if (Arrays.IsEmpty(slotTypes)) {
                var msg = $"BodyPartSlotTypes are not defined for species: {data.Species}";
                D.Err(msg);
                ModRegistry.WarnAboutBrokenMod(data, msg);
                return false;
            }
            GenerateFor(S, data, rng, vars);
            return true;
        }
        public static void GenerateMissingFor(Body body, BodyPartSlot slot) {
            if (body.Age == 0) {
                D.ErrSoft("Generating missing body parts for body with age 0! {0} | {1} | {2} | {3}",
                    body.Owner, body.Owner.StateFlags, body, slot);
            }
            // D.Warn("Generating missing base body part for {0}: {1}", body.Owner, slot.Id);
            var cfg = BodyConfiguration.ForSpecies(body.Owner.Species);
            var lifePct = body.Owner.Species.LifeCycleStageFor(body.Age);
            var state = BodyPartState.Used(lifePct);
            if (cfg.SlotAssignments.TryGetValue(slot.Id, out var ass)) {
                var part = cfg.SlotAssignments[slot.Id];
                if (part != null) {
                    InstallPart(slot, part, state);
                } else {
                    slot.IsPartRemoved = true;
                }
            } else {
                slot.IsPartRemoved = true;
            }
        }

        private void GenerateFor(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (data.Body == null) {
                data.Body = Body.BlankFor(data);
            }
            data.Body.Sex = vars.Sex;
            data.Body.MaxLibido = vars.MaxLibido;

            // we determine this in mind gen step, which comes before this
            var varsBody = vars.UseBodyVars ? vars.Body : default;
            varsBody.ChanceScarred = vars.Body.ChanceScarred;
            if (vars.BeingType == BeingType.StartingCrew && data.Species.IsAnimal) {
                // the starting pet is shown in the crew picker and is the first thing the
                // player sees; a pet with a stranger's paws there reads as a bug, while the
                // same roll on a capsule survivor or a gift reads as a story
                varsBody.ChanceFreak = 0f;
            }
            // The starting crew's jobs are planned around stock bodies, and StartingCrewJobs
            // can only reroll a persona, never a body. A robot is its parts: a swapped hand or
            // mobilizer is lost function (a Construction Robot that cannot craft), not
            // character, so starting robots are always stock. Biological crew may still look
            // odd (a robotic eye), but a replacement part keeps the abilities of the standard
            // one, and mobilizers are never swapped.
            bool isStartingCrew = vars.BeingType == BeingType.StartingCrew;
            if (isStartingCrew && !data.Species.IsBiological) {
                varsBody.ChanceBionic = 0f;
                varsBody.ChanceNonStandard = 0f;
                varsBody.ChanceFreak = 0f;
                varsBody.ChanceImplant = 0f;
            }
            var isScarred = data.Species.IsBiological &&
                varsBody.ChanceScarred > 0f && rng.Chance(varsBody.ChanceScarred);

            var cfg = BodyConfiguration.ForSpecies(data.Species);
            // this is where we generate the missing parts
            foreach (var slot in data.Body.Slots) {
                if (vars.BodyWithoutParts && slot.Type.IdH != BodyPartSlotIdH.Body) {
                    slot.IsPartRemoved = true;
                    continue;
                }
                if (slot.IsPartRemoved || slot.InstalledDef != null) {
                    continue;
                }
                var isSwappedMind = slot.HoldsMind && data.Species != data.Mind.Species;
                var lifePct = data.Species.LifeCycleStageFor(vars.AgeBody);
                var isHQParts = isSwappedMind || (varsBody.ChanceHighQualityParts > 0f
                    && rng.Chance(varsBody.ChanceHighQualityParts));

                if (isHQParts) {
                    // almost brand new part
                    lifePct = rng.Range(0f, 0.15f);
                } else if (varsBody.ChanceLowQualityParts > 0f
                        && rng.Chance(varsBody.ChanceLowQualityParts)) {
                    // used low quality part
                    lifePct = rng.Range(0.85f, 0.95f);
                }
                var state = BodyPartState.Used(lifePct, rng);
                if (cfg != null && cfg.SlotAssignments.TryGetValue(
                        slot.Id, out var standardPart)) {
                    if (isSwappedMind) {
                        // special case, we are going to try to install
                        // the most appropriate body part ignoring any other
                        // settings
                        if (!data.Species.IsBiological) {
                            // we will use the default core unless there's chance for non-stardard parts
                        } else {
                            if (data.Mind.Species.IsBiological) {
                                // biological to biological case - try best fitting brain first
                                if (TryFillBodyPart(rng, slot, data, state)) {
                                    continue;
                                }
                            }
                        }
                    }
                    if (standardPart != null) {
                        if (slot.Type.IdH == BodyPartSlotIdH.Mobilizer
                                && (isStartingCrew
                                    || data.Species.StatFlags.HasFlag(StatFlags.Flying))) {
                            // D.Warn("Not touching mobilizer for flying being: {0}", data);
                        } else {
                            // Always shuffled from the same starting order: shuffling the
                            // previous order in place made each body depend on every being
                            // generated before it, so a seed (trade offers) did not regenerate
                            // the same body
                            tryCustomFuncs.Clear();
                            tryCustomFuncs.AddRange(customFuncs);
                            Arrays.Shuffle(rng, tryCustomFuncs);
                            bool installedCustom = false;
                            // A swapped brain must keep what the standard one gives: a
                            // human with a crawler brain had no Intelligence and no labor,
                            // and could not even board a shuttle (STA-1876)
                            var keepAbilitiesOf = isStartingCrew || slot.HoldsMind
                                ? standardPart : null;
                            foreach (var func in tryCustomFuncs) {
                                if (func(data, rng, varsBody, slot, state, standardPart,
                                        keepAbilitiesOf)) {
                                    installedCustom = true;
                                    break;
                                }
                            }
                            if (installedCustom) {
                                continue;
                            }
                        }
                        // vanilla part
                        InstallPart(slot, standardPart, state);
                    } else {
                        // likely implant
                        if (varsBody.ChanceImplant > 0f) {
                            if (rng.Chance(varsBody.ChanceImplant)) {
                                //do a random implant
                                if (TryFillBodyPart(rng, slot, data, state)) {
                                    // D.Warn("Generated body with implant: {0}->{1}",
                                    //     data, slot.InstalledDef);
                                    continue;
                                }
                            }
                        }
                        slot.IsPartRemoved = true;
                    }
                } else {
                    // no assignment, fill with random part
                    if (!TryFillBodyPart(rng, slot, data, state)) {
                        slot.IsPartRemoved = true;
                        D.Err("Failed to fill body part (no standard): {0} -> {1}",
                            slot, data.Species.Id);
                    }
                }
                // generate scars
                if (isScarred && slot.InstalledDef?.IsBiological == true
                        && rng.Chance(varsBody.ChanceScarred)) {

                    if (!slot.IsInternal || rng.Chance(0.1f)) {
                        for (int i = 0; i < 3; i++) {
                            state.AddScar(Scar.RandomHistorical(
                                    data.Species, vars.AgeBody, rng));
                            if (rng.Chance(0.9f)) {
                                break;
                            }
                        }
                    }
                }
            }
            data.Body.SetBodyState(BodyState.Alive, true);
            data.Body.Age = GenBeingIntialVarsDataStep.GetAgeForBirthTick(
                vars.BirthTickBody, G.Ticks);
            data.Body.DeathAge = vars.BodyDeathAge;
            data.Body.BirthTick = vars.BirthTickBody;

            var species = data.Species;
            if (species.HasSkinColor) {
                GenSkinColor(data, rng, species);
            }

            if (species.HasHairColor) {
                GenHairColor(data, rng, species);
            }

            if (species.HasEyeColor) {
                GenEyeColor(data, rng, species);
            }
        }

        public static void ValidateBodyColors(BeingData data) {
            var sp = data.Species;
            bool rebuild = false;
            if (sp.HasHairColor && !Palette.HasColor(data.Body.HairColorId)) {
                GenHairColor(data, Rng.Unseeded, sp);
                var hairPart = data.Looks.Get(LookPartTypeDef.Hair);
                hairPart.Color = Palette.Get(data.Body.HairColorId).Color;
                rebuild = true;
            }
            if (sp.HasSkinColor && !Palette.HasColor(data.Body.SkinColorId)) {
                GenSkinColor(data, Rng.Unseeded, sp);
                var bodyPart = data.Looks.Get(LookPartTypeDef.Body);
                bodyPart.Color = Palette.Get(data.Body.SkinColorId).Color;
                var headPart = data.Looks.Get(LookPartTypeDef.Head);
                headPart.Color = bodyPart.Color;
                rebuild = true;
            }
            if (sp.HasEyeColor && !Palette.HasColor(data.Body.EyeColorLId)) {
                GenEyeColor(data, Rng.Unseeded, sp);
                var leftPart = data.Looks.Get(LookPartTypeDef.Eye_L);
                leftPart.Color = Palette.Get(data.Body.EyeColorLId).Color;
                var rightPart = data.Looks.Get(LookPartTypeDef.Eye_R);
                rightPart.Color = Palette.Get(data.Body.EyeColorRId).Color;
                rebuild = true;
            }
            if (rebuild) {
                // Ready.WhenGameLate("Rebuild", "Rebuild looks", () => {
                    data.Being?.Looks.Rebuild(force: true, clearCache: true);
                // });
            }
        }

        private static void GenEyeColor(BeingData data, Rng rng, Species species) {
            data.Body.EyeColorLId = Palette.RandomForGroup(
                species.EyeColorPalette, rng)?.Id;
            if (rng.Chance(0.98f)) {
                data.Body.EyeColorRId = data.Body.EyeColorLId;
            } else {
                data.Body.EyeColorRId = Palette.RandomForGroup(
                    species.EyeColorPalette, rng)?.Id;
            }
        }

        private static void GenHairColor(BeingData data, Rng rng, Species species) {
            data.Body.HairColorId = Palette.RandomForGroup(
                species.HairColorPalette, rng)?.Id;
        }

        private static void GenSkinColor(BeingData data, Rng rng, Species species) {
            data.Body.SkinColorId = Palette.RandomForGroup(
                species.SkinColorPalette, rng)?.Id;
        }

        private bool TryNonStandard(BeingData data, Rng rng, GenBeingBodyVars vars,
                BodyPartSlot slot, BodyPartState state, BodyPartDef standard,
                BodyPartDef keepAbilitiesOf) {
            if (vars.ChanceNonStandard <= 0f) { return false; }
            if (rng.Chance(vars.ChanceNonStandard)) {
                if (TryFillBodyPart(rng, slot, data, state, except: standard,
                        keepAbilitiesOf: keepAbilitiesOf)) {
                    return true;
                }
            }
            return false;
        }
        private bool TryFreaky(BeingData data, Rng rng, GenBeingBodyVars vars, BodyPartSlot slot, BodyPartState state, BodyPartDef standard, BodyPartDef keepAbilitiesOf) {
            if (vars.ChanceFreak <= 0f) { return false; }
            if (rng.Chance(vars.ChanceFreak)) {
                if (TryFillBodyPart(rng, slot, data, state, freak: true,
                        keepAbilitiesOf: keepAbilitiesOf)) {
                    return true;
                }
            }
            return false;
        }


        private bool TryBionic(BeingData data, Rng rng, GenBeingBodyVars vars, BodyPartSlot slot, BodyPartState state, BodyPartDef standard, BodyPartDef keepAbilitiesOf) {
            if (vars.ChanceBionic <= 0f) { return false; }
            if (rng.Chance(vars.ChanceBionic)) {
                if (TryFillBodyPart(rng, slot, data, state, bionic: true,
                        keepAbilitiesOf: keepAbilitiesOf)) {
                    return true;
                }
            }
            return false;
        }

        // keepAbilitiesOf: only parts that add every ability of this one are candidates
        private bool TryFillBodyPart(Rng rng, BodyPartSlot slot, BeingData data,
                BodyPartState state,
                bool bionic = false,
                bool freak = false,
                BodyPartDef except = null,
                BodyPartDef keepAbilitiesOf = null) {
            int maxCandidates = 128;
            var pool = ArrayPool<BodyPartDef>.Shared;
            var candidates = pool.Rent(maxCandidates);
            int numCandidates = 0;
            bool? wantsRoboticPart = null;

            bool isSwappedMind = false;
            Species overrideSpecies = null;
            if (slot.HoldsMind) {
                isSwappedMind = data.Species != data.Mind.Species;
                if (isSwappedMind) {
                    overrideSpecies = data.Mind.Species;
                } else {
                    // use the standard issue mind for non-swapped ones
                    return false;
                }
            }

            if (bionic) {
                wantsRoboticPart = data.Species.IsBiological;
            }

            if (freak && slot.HoldsMind) {
                // let's do a normal brain
                freak = false;
            }
            // A humanoid must keep at least one hand that can both work (Manipulate) and
            // hold a weapon (UseItem), or a freak / bionic roll could leave a colonist unable
            // to do skilled jobs and a raider unable to shoot.
            bool mustKeepWorkHand = slot.Type.IdH == BodyPartSlotIdH.Hand
                && data.Species.IsHumanoid
                && !OtherHandKeepsWorkAbilities(data.Body, slot);

            foreach (var part in BodyPartDef.All.Values) {
                if (part.NeverGenerate) { continue; }
                if (mustKeepWorkHand && !AddsWorkAbilities(part)) { continue; }
                if (keepAbilitiesOf != null && !AddsAllAbilitiesOf(part, keepAbilitiesOf)) {
                    continue;
                }
                if (wantsRoboticPart.HasValue) {
                    // skip parts that do not match bionic flag
                    if (part.IsBiological == wantsRoboticPart.Value) {
                        continue;
                    }
                }
                // standard part?
                if (part == except) {
                    continue;
                }
                if (part.IsCompatibleWith(slot, out var isVanilla)) {
                    if (!bionic && part.IsBiological != data.Species.IsBiological) {
                        // would be bionic
                        continue;
                    }
                    // must go first
                    if (isSwappedMind && Arrays.ContainsSafe(part.TargetSpecies, overrideSpecies.Id)) {
                        // this is the dedicated mind for that species! bingo;
                        pool.Return(candidates);
                        InstallPart(slot, part, state);
                        return true;
                    }
                    // we're going for a non-standard part
                    if (except != null) {
                        if (part.TargetSpecies != null) {
                            if (!Arrays.Contains(part.TargetSpecies, data.Species.Id)) {
                                // would be a freak, not a non-standard
                                continue;
                            }
                        }
                    }
                    if (freak && part.TargetSpecies != null) {
                        if (!Arrays.Contains(part.TargetSpecies, data.Species.Id)) {
                            candidates[numCandidates++] = part;
                            if (numCandidates == maxCandidates) {
                                break;
                            }
                        }
                        // disallow non-freaky parts
                        continue;
                    }
                    candidates[numCandidates++] = part;
                    if (numCandidates == maxCandidates) {
                        break;
                    }
                }
            }
            if (numCandidates == 0) {
                pool.Return(candidates);
                return false;
            }

            var idx = rng.Range(0, numCandidates);
            var chosenPart = candidates[idx];
            pool.Return(candidates);
            InstallPart(slot, chosenPart, state);
            return true;
        }

        private static void InstallPart(BodyPartSlot slot,
                BodyPartDef part, BodyPartState state) {
            slot.InstalledDef = part;
            slot.SetState(state);
            slot.IsPartRemoved = false;
        }

        private static bool AddsWorkAbilities(BodyPartDef part) {
            return Arrays.ContainsSafe(part.AddsAbilities, AbilityId.Manipulate)
                && Arrays.ContainsSafe(part.AddsAbilities, AbilityId.UseItem);
        }

        private static bool AddsAllAbilitiesOf(BodyPartDef part, BodyPartDef other) {
            if (other.AddsAbilities == null) { return true; }
            foreach (var ability in other.AddsAbilities) {
                if (!Arrays.ContainsSafe(part.AddsAbilities, ability)) {
                    return false;
                }
            }
            return true;
        }

        private static bool OtherHandKeepsWorkAbilities(Body body, BodyPartSlot slot) {
            foreach (var other in body.Slots) {
                if (other == slot || other.Type.IdH != BodyPartSlotIdH.Hand) { continue; }
                if (other.InstalledDef != null) {
                    if (AddsWorkAbilities(other.InstalledDef)) {
                        return true;
                    }
                    continue;
                }
                if (!other.IsPartRemoved) {
                    // not generated yet: slots are filled in species order, so when its turn
                    // comes this same check sees the non-working hand here and forces a
                    // working one there
                    return true;
                }
            }
            return false;
        }

    }
}
