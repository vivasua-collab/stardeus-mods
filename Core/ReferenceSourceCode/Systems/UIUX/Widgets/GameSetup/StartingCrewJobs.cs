using System.Collections.Generic;
using Game.CodeGen;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;

namespace Game.Systems.UIUX {
    // Keeps the starting crew able to do the jobs the early game cannot do without. A
    // backstory can make the only robot incapable of crafting or building, and in robot-only
    // starts nobody else can until the first stasis wake-up. The Assembler tutorial only
    // partly replaces a crafter, and nothing replaces a builder or hauler.
    //
    // Solar panels need a cleaner who can fly: they sit out in space (AdFlyingOnly), and dust
    // takes their output down within weeks - New Beginning's stasis array runs off one. A plain
    // "anyone can clean" check would pass on the robot, whose hand cleans but cannot get there.
    // A crew with no flyer at all (No Robots) gets one through a Jet Suit instead
    // (Scenario.GivesCleanerJetSuit), so there anyone who could wear it counts.
    //
    // Random rolls reroll the crew member whose persona caused the gap, so a roll never deals
    // such a crew. What is left (Custom Rules counts, modded crews) is a deliberate choice,
    // and the crew setup screen warns about it instead.
    //
    // A whole-crew roll also keeps a holder for each of the scenario's starting weapons
    // (StartingCrewWeapons), which the crew holds from the first tick: the first raid comes
    // within days, and an unarmed bot crew lost the colony to a single raider. Only whole-crew
    // rolls: the dice on one crew member is the player's pick, and no warning is shown.
    public static class StartingCrewJobs {
        // Build, deliver materials to the build site (hauling goals), produce
        private static readonly int[] essentialJobs = {
            JobTypeIdH.Construction,
            JobTypeIdH.Hauling,
            JobTypeIdH.Crafting,
        };
        // What the cleaning goal and AIActionCleanDevice require, plus Move, which
        // AIActionSetCanFly needs next to Fly to reach a flyers-only target. A carrier flies
        // but has nothing to clean with.
        private static readonly int[] panelCleanerAbilities = {
            AbilityIdH.CleanDevice,
            AbilityIdH.DumbLabor,
            AbilityIdH.Move,
        };
        // A blocking backstory is a few percent per roll, so this is never reached in practice
        private const int MaxRerolls = 20;

        // Same test as QuestUtils.CanAnyoneCraft, so setup and the intro's CrewCannotCraft
        // check cannot disagree
        private static bool CanDo(StartingBeingData sb, int jobId) {
            return sb.Data.Mind?.Jobs.CanDo(jobId, out string _) == true;
        }

        private static bool AnyoneCanDo(GameSetupData data, int jobId) {
            foreach (var list in data.StartingBeings.Values) {
                foreach (var sb in list) {
                    if (CanDo(sb, jobId)) {
                        return true;
                    }
                }
            }
            return false;
        }

        // The crew is not spawned yet, so BeingData.HasAbility would see no ability sources.
        // Where the scenario puts a cleaner in a Jet Suit, anyone who could wear it will fly.
        private static bool HasPanelCleanerBody(GameSetupData data, StartingBeingData sb) {
            var abilities = new GenBeingAbilities(sb.Data);
            foreach (var abilityId in panelCleanerAbilities) {
                if (!abilities.HasAbility(abilityId)) {
                    return false;
                }
            }
            if (data.Scenario.GivesCleanerJetSuit && JetSuits.HasSuitSlot(sb.Data)) {
                return true;
            }
            return abilities.HasAbility(AbilityIdH.Fly);
        }

        private static bool AnyoneCanCleanPanels(GameSetupData data) {
            foreach (var list in data.StartingBeings.Values) {
                foreach (var sb in list) {
                    if (CanDo(sb, JobTypeIdH.Cleaning) && HasPanelCleanerBody(data, sb)) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Sleepers are not counted: they live in StasisSleepers and stay asleep for days
        public static void FindMissing(GameSetupData data, List<MissingCrewJob> missing) {
            missing.Clear();
            // Instant build and spawn tools make the warning untrue there
            if (data.Scenario.IsSandbox) { return; }
            foreach (var jobId in essentialJobs) {
                if (!AnyoneCanDo(data, jobId)) {
                    missing.Add(new MissingCrewJob(JobType.Get(jobId), false));
                }
            }
            if (!AnyoneCanCleanPanels(data)) {
                missing.Add(new MissingCrewJob(JobType.Get(JobTypeIdH.Cleaning), true));
            }
        }

        // The body can do the job but the persona rules it out, and nobody else can do it.
        // Only that is fixable by a reroll: a species without the job slot (Carrier has no
        // Construction) or a body without the abilities would reroll for nothing.
        public static bool HoldsBack(GameSetupData data, StartingBeingData sb) {
            var jobs = sb.Data.Mind?.Jobs;
            if (jobs == null) { return false; }
            foreach (var jobId in essentialJobs) {
                if (PersonaBlocks(jobs, jobId) && !AnyoneCanDo(data, jobId)) {
                    return true;
                }
            }
            return PersonaBlocks(jobs, JobTypeIdH.Cleaning) && HasPanelCleanerBody(data, sb)
                && !AnyoneCanCleanPanels(data);
        }

        private static bool PersonaBlocks(Jobs jobs, int jobId) {
            return jobs.TryJob(jobId, out var slot) && !slot.LacksAbilities && slot.Incapable;
        }

        // For a whole-crew roll. Re-evaluates the crew after every reroll, because a reroll
        // can move the gap to another job that someone else was relying on.
        public static void RerollHoldouts(GameSetupData data) {
            for (int i = 0; i < MaxRerolls; i++) {
                var holdout = FindHoldout(data);
                if (holdout == null) { return; }
                holdout.Randomize(data, false);
            }
        }

        // For the dice on one crew member: never touches anyone else
        public static void RerollWhileHoldingBack(GameSetupData data, StartingBeingData sb) {
            for (int i = 0; i < MaxRerolls && HoldsBack(data, sb); i++) {
                sb.Randomize(data, false);
            }
        }

        private static StartingBeingData FindHoldout(GameSetupData data) {
            foreach (var list in data.StartingBeings.Values) {
                foreach (var sb in list) {
                    if (HoldsBack(data, sb)) {
                        return sb;
                    }
                }
            }
            return FindUnarmedHoldout(data);
        }

        // A starting weapon nobody is left to hold, and a crew member whose body could hold it
        // but whose persona rules out its job (a Hippie and the Handgun)
        private static StartingBeingData FindUnarmedHoldout(GameSetupData data) {
            var weapons = StartingCrewWeapons.ItemsFor(data.Scenario.StartingWeapons);
            if (weapons.Count == 0) { return null; }
            var crew = new List<(BeingData Data, bool HasHands)>();
            foreach (var list in data.StartingBeings.Values) {
                foreach (var sb in list) {
                    crew.Add((sb.Data, HasHands(sb)));
                }
            }
            var holders = StartingCrewWeapons.Assign(crew, weapons);
            for (int w = 0; w < weapons.Count; w++) {
                if (holders[w] != null || weapons[w].RequiredJobId == 0) { continue; }
                foreach (var list in data.StartingBeings.Values) {
                    foreach (var sb in list) {
                        var jobs = sb.Data.Mind?.Jobs;
                        if (jobs != null && PersonaBlocks(jobs, weapons[w].RequiredJobId)
                                && StartingCrewWeapons.BodyCanHold(sb.Data, weapons[w],
                                    HasHands(sb))) {
                            return sb;
                        }
                    }
                }
            }
            return null;
        }

        // The crew is not spawned yet (see HasPanelCleanerBody)
        private static bool HasHands(StartingBeingData sb) {
            return new GenBeingAbilities(sb.Data).HasAbility(AbilityIdH.UseItem);
        }
    }
}
