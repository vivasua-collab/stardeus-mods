using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Procgen;
using Game.Procgen.Names;
using Game.Systems.AI;
using Game.Systems.Backstories;
using Game.Systems.Mental;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingExperienceDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingExperienceDataStep());
        }
        private const string StepId = "Experience";
        public override string Id => StepId;
        // we will run this after mind but before anything else, so we can have a body type
        // and some traits determined by this
        public override int Order => -10;
        private readonly Dictionary<BeingRole, float> availableRoles = new();
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (vars.BodyWithoutParts) {
                return true;
            }
            if (data.Mind == null) {
                D.Err("Backstory generation cannot run before Mind is created!");
                return false;
            }
            var bs = Experience.BlankFor(data.Mind);
            data.Mind.Experience = bs;
            bs.Roles = new Dictionary<AgePhase, BeingRole>();
            availableRoles.Clear();
            AgePhase phases = AgePhase.None;

            if (vars.IsClone) {
                return true;
            }

            // A backstory is the life the mind lived, so the mind's species decides it, as it
            // decides the mental needs: a bot mind piloting a human body or a cat mind in a
            // borrowed body rolled human childhoods. A wild creature has lived no life to
            // describe: crawlers hatched with Seaman or Beloved Pet behind them.
            var mindSpecies = data.Mind.Species ?? data.Species;
            if (mindSpecies.IsWild) {
                return true;
            }

            // 1. pick age phases that we are generating
            if (!Arrays.IsEmpty(mindSpecies.AgePhases)) {
                var age = vars.AgeMind;
                foreach (var phaseRange in mindSpecies.AgePhases) {
                    if (age > phaseRange.AgeFrom) {
                        phases |= phaseRange.AgePhase;
                        bs.Roles.Add(phaseRange.AgePhase, null);
                    }
                }
                // D.Warn("Age phases for being: {0}({2}) -> {1}",
                //     data.Name, age, phases);
            } else {
                bs.Roles.Add(AgePhase.None, null);
            }

            BuildAvailableRoles(mindSpecies, rng, vars, phases);
            BeingRole lastAdded = null;
            foreach (var p in bs.Roles.Keys.ToArray()) {
                if (availableRoles.Count == 0) {
                    D.Err("Not enough available roles for {0}: {1}",
                        mindSpecies, p);
                    bs.Roles.Remove(p);
                    break;
                }
                if (p == AgePhase.None) {
                    D.Err("Being roles has AgePhase:None! {0}", mindSpecies);
                    bs.Roles.Remove(p);
                    continue;
                } else {
                    int attempts = 1000;
                    while (availableRoles.Count > 0 && attempts-- > 0) {
                        var role = rng.WeightedFrom(availableRoles);
                        if (role.AgePhases != AgePhase.None) {
                            if ((role.AgePhases & p) == AgePhase.None) {
                                // this role won't work for this phase
                                continue;
                            }
                        }
                        if (HasConflictWith(bs.Roles, role)) {
                            continue;
                        }
                        if (!CanFollow(lastAdded, role, rng)) {
                            // cross-role restrictions failed
                            continue;
                        }
                        bs.Roles[p] = role;
                        lastAdded = role;
                        if (role.Flags.HasFlag(RoleFlags.Once)) {
                            availableRoles.Remove(role);
                        }
                        break; // while
                    }
                }
                if (bs.Roles[p] == null) {
                    // we failed to generate anything
                    bs.Roles.Remove(p);
                }
            }
            return true;
        }

        internal static bool HasConflictWith(Dictionary<AgePhase, BeingRole> roles, BeingRole role) {
            if (role.IncapableJobs == null && role.LovesJobs == null) { return false; }
            Caches.IntSet.Clear();
            Caches.IntSet2.Clear();
            var incapable = Caches.IntSet;
            var loves = Caches.IntSet2;
            // D.Warn("Checking role: {0}", D.DumpInstance(role));
            foreach (var r in roles.Values) {
                if (r == null) { continue; }
                if (r.IncapableJobs != null) {
                    foreach (var j in r.IncapableJobs) {
                        incapable.Add(j.IdHash);
                    }
                }
                if (r.LovesJobs != null) {
                    foreach (var j in r.LovesJobs) {
                        loves.Add(j.IdHash);
                    }
                }
            }
            if (incapable.Count == 0 && loves.Count == 0) { return false; }
            if (role.IncapableJobs != null) {
                foreach (var j in role.IncapableJobs) {
                    if (loves.Contains(j.IdHash)) {
                        // D.Warn("Skipping conflicting incapable job {2} in role: {0} after {1}",
                        //     role.Id, D.Dump(roles.Values), j.Id);
                        return true;
                    }
                }
            }
            if (role.LovesJobs != null) {
                foreach (var j in role.LovesJobs) {
                    if (incapable.Contains(j.IdHash)) {
                        // D.Warn("Skipping conflicting loves job {2} in role: {0} after {1}",
                        //     role.Id, D.Dump(roles.Values), j.Id);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CanFollow(BeingRole prevRole, BeingRole nextRole, Rng rng) {
            if (!Arrays.IsEmpty(nextRole.RequiresRoles)) {
                if (prevRole == null) {
                    return false;
                }
                return Arrays.Contains(nextRole.RequiresRoles, prevRole.Id);
            }
            if (prevRole == null) {
                return true;
            }
            if (!Arrays.IsEmpty(nextRole.NotAfterRoles)) {
                if (Arrays.Contains(nextRole.NotAfterRoles, prevRole.Id)) {
                    return false;
                }
            }
            if (prevRole.Flags != RoleFlags.None && nextRole.Flags != RoleFlags.None) {
                if (prevRole.Flags.HasFlag(RoleFlags.Dumb) && nextRole.Flags.HasFlag(RoleFlags.Smart)) {
                    if (rng.Chance(0.33f)) {
                        return false;
                    }
                }
                if (prevRole.Flags.HasFlag(RoleFlags.Smart) && nextRole.Flags.HasFlag(RoleFlags.Dumb)) {
                    if (rng.Chance(0.25f)) {
                        return false;
                    }
                }
                if (prevRole.Flags.HasFlag(RoleFlags.Poor) && nextRole.Flags.HasFlag(RoleFlags.Rich)) {
                    if (rng.Chance(0.33f)) {
                        return false;
                    }
                }
                if (prevRole.Flags.HasFlag(RoleFlags.Rich) && nextRole.Flags.HasFlag(RoleFlags.Poor)) {
                    if (rng.Chance(0.5f)) {
                        return false;
                    }
                }
                if (prevRole.Flags.HasFlag(RoleFlags.Good) && nextRole.Flags.HasFlag(RoleFlags.Evil)) {
                    if (rng.Chance(0.33f)) {
                        return false;
                    }
                }
                if (prevRole.Flags.HasFlag(RoleFlags.Evil) && nextRole.Flags.HasFlag(RoleFlags.Good)) {
                    if (rng.Chance(0.33f)) {
                        return false;
                    }
                }
            }
            return true;
        }

        private void BuildAvailableRoles(Species species, Rng rng, GenBeingVars vars, AgePhase phases) {
            // 1. pick all applicable roles for this being
            foreach (var role in BeingRole.All.Values) {
                if (role.AgePhases != AgePhase.None) {
                    if ((role.AgePhases & phases) == AgePhase.None) {
                        // no overlap, this role is for an older age phase
                        continue;
                    }
                }
                if (!Arrays.IsEmpty(role.Species)) {
                    if (!Arrays.Contains(role.Species, species.Id)) {
                        // wrong species
                        continue;
                    }
                }
                if (!Arrays.IsEmpty(role.IncompatibleSpecies)) {
                    if (Arrays.Contains(role.IncompatibleSpecies, species.Id)) {
                        // incompatible species
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(role.BodyTypes)) {
                    if (role.BodyTypes.IndexOf(vars.BodyType) == -1) {
                        // wrong body type
                        continue;
                    }
                }
                var sex = vars.Sex;
                if (sex != null && !Arrays.IsEmpty(role.SexChances)) {
                    bool sexMatched = false;
                    foreach (var s in role.SexChances) {
                        if (s.Sex == sex.Type) {
                            // we have a match!
                            sexMatched = rng.Chance(s.Chance);
                            break;
                        }
                    }
                    if (!sexMatched) {
                        // wrong sex
                        continue;
                    }
                }
                availableRoles.Add(role, role.Rarity);
            }
        }
    }
}
