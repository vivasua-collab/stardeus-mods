using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.BodyParts;
using Game.Systems.Naming;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures {
    // Every occupied stasis chamber holds a specific, registered BeingData (a "sleeper")
    // bound to its array through Mind.StasisArrayId. This system owns that binding and the
    // one invariant everything else relies on:
    //
    //     array.BeingCount == number of InStasis beings bound to that array
    //
    // It is a system rather than more code in StasisArrayComp because the colony-wide
    // counters below have to survive on their own: "delivered X of N" at the ending must
    // not depend on dead BeingData being kept around forever.
    public sealed class ManifestSys : GameSystem, ISaveable {
        public const string SysId = "ManifestSys";
        public override string Id => SysId;

        // What an array gets when nothing more specific asked for a headcount (sandbox
        // fill, game clone) - the same people a scenario start would have.
        public const int DefaultHumans = 15;
        public const int DefaultMinPets = 3;
        public const int DefaultMaxPets = 5;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ManifestSys());
        }

        public static ManifestSys For(GameState s) {
            return s.Sys.Get<ManifestSys>(SysId);
        }

        // Everyone who was ever put on a manifest in this game
        public int Total { get; private set; }
        // People from a manifest who died inside a pod, whether or not they had been woken
        // before and sent back
        public int LostInStasis { get; private set; }
        // Wake-ups so far, returning colonists included
        public int WokenCount { get; private set; }

        #if !PRODUCTION_BUILD
        // For tests: lets them assert that generation did NOT happen (e.g. on load)
        public int PopulateCalls;
        public int GeneratedCount;
        #endif

        private readonly List<BeingData> scratch = new();
        private readonly List<Species> petSpecies = new();

        private ComponentData data;
        private ComponentData Data => data ??= new(0, GetName());
        public string GetName() => Id;

        public ComponentData Save() {
            var d = Data;
            d.SetInt("Total", Total);
            d.SetInt("Lost", LostInStasis);
            d.SetInt("Woken", WokenCount);
            return d;
        }

        public void Load(ComponentData data) {
            this.data = data;
            Total = data.GetInt("Total", 0);
            LostInStasis = data.GetInt("Lost", 0);
            WokenCount = data.GetInt("Woken", 0);
        }

        // Harm only exists below this stability. It sits under the band a powered array
        // settles in without advanced life support (0.5-0.7), so harm always means
        // something is actually wrong with the array. The curves are zero from here up.
        public const float HarmBand = 0.5f;

        private Curve harmCurve;
        private Curve deathCurve;
        private bool isBulkLoss;
        private readonly List<BeingData> harmScratch = new();

        protected override void OnInitialize() {
            harmCurve = CurveDef.FastGet(CurveIdH.Stasis_HarmChancePerHour);
            deathCurve = CurveDef.FastGet(CurveIdH.Stasis_DeathChancePerHour);
            S.Sig.BeingDied.AddListener(OnBeingDied);
            S.Clock.OnTick.AddListener(OnTick);
        }

        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != 17) { return; }
            var arrays = S.Components.AllByType<StasisArrayComp>();
            if (arrays == null) { return; }
            foreach (StasisArrayComp array in arrays) {
                HarmHour(array, ticks);
            }
        }

        // One hour of an unstable array. What it does to people happens here, while they
        // sleep and where the player can watch it coming, not as a dice roll at wake-up.
        public void HarmHour(StasisArrayComp array, long ticks) {
            if (array.IsElysium || array.BeingCount < 1) { return; }
            if (!array.Tile.IsConstructed) { return; }
            var stability = array.Stability;
            if (stability >= HarmBand) { return; }
            if (ticks < array.HarmGraceUntilTick) { return; }
            var harmChance = harmCurve.Evaluate(stability);
            var deathChance = deathCurve.Evaluate(stability);
            if (harmChance <= 0f && deathChance <= 0f) { return; }

            EnsurePopulated(array);
            FindSleepers(array, harmScratch);
            var rng = S.Rng;
            // Scaled inside the harm band, so damage starts mild right under 0.5 and only
            // becomes critical in an array that is nearly gone
            var severity = Severities.For((HarmBand - stability) / HarmBand, false);
            bool changed = false;
            for (int i = harmScratch.Count - 1; i >= 0; i--) {
                var bd = harmScratch[i];
                if (deathChance > 0f && rng.Chance(deathChance)) {
                    DieInPod(array, bd, ticks);
                    changed = true;
                    continue;
                }
                if (harmChance > 0f && rng.Chance(harmChance)) {
                    BeingUtils.DamageRandomBodyPart(S, bd, rng, DamageType.Organ, severity,
                        array.Tile, 1f, skipInternal: false);
                    // The damage itself can be the end of them; OnBeingDied has then
                    // already taken them off the roster
                    if (!bd.HasStateFlag(BeingState.Dead)) {
                        S.Sys.Log.AddLine(T.StasisHarmLog_V1.F(Texts.LinkToBeing(bd)));
                    }
                    changed = true;
                }
            }
            harmScratch.Clear();
            if (changed) {
                array.SyncBeingCount(CountSleepers(array));
                array.NotifyManifestChanged();
            }
        }

        // The pod opens and the body is on the floor in front of the array. A body rather
        // than a silent removal, because a body is what mourning, cremation and an urn
        // carried to the new world all start from.
        private void DieInPod(StasisArrayComp array, BeingData bd, long ticks) {
            var mind = bd.Mind;
            bool wasManifest = mind.ManifestStatus != ManifestStatus.None;
            bd.Body.Flags &= ~BodyFlags.Missing;
            // Never on the map before: let BeingsSys.Activate treat it as a first spawn
            bd.StateFlags = StasisUtils.IsUnrevealed(bd)
                ? BeingState.Uninitialized
                : bd.StateFlags & ~BeingState.Hidden;
            mind.Role = ColonyRole.Unassigned;
            mind.StasisArrayId = 0;
            if (wasManifest) {
                LostInStasis++;
                // The body is out in the open now. Left as Sleeping, its panel would stay
                // identity-only for good and hide everything a body is examined for.
                mind.ManifestStatus = ManifestStatus.Woken;
            }
            var cmd = new CmdSpawnBeing(bd, array.Tile.Transform.OutPort);
            cmd.Execute(S);
            if (cmd.SpawnedBeing == null) {
                BeingUtils.Eliminate(S, bd, T.StasisDeathCause, ticks);
            } else {
                BeingUtils.Kill(S, bd, DamageType.Organ, array.Tile, T.StasisDeathCause, null);
            }
            AnnounceDeath(array, bd);
        }

        private void AnnounceDeath(StasisArrayComp array, BeingData bd) {
            var msg = T.StasisDeathAlert_V1.F(bd.Mind.Name.FullNameOrNameless);
            Notification.BuildFor(array.Tile, array.Tile.Definition.Preview, msg,
                    Consts.TicksPerDay, NotificationType.Alert)
                .Group("sts_death")
                .Show();
            S.Sys.Log.AddLine(msg);
        }

        // The one place a death inside a pod is accounted for, whatever caused it: stasis
        // harm, accumulated organ damage, or the array going down with people in it.
        private void OnBeingDied(BeingData bd) {
            var mind = bd.Mind;
            if (mind == null || mind.Role != ColonyRole.InStasis) { return; }
            var arrayId = mind.StasisArrayId;
            mind.StasisArrayId = 0;
            mind.Role = ColonyRole.Unassigned;
            if (mind.ManifestStatus != ManifestStatus.None) {
                LostInStasis++;
            }
            if (arrayId == 0) { return; }
            var array = S.Components.Find<StasisArrayComp>(arrayId);
            if (array == null) { return; }
            array.SyncBeingCount(CountSleepers(array));
            array.NotifyManifestChanged();
            if (!isBulkLoss) {
                AnnounceDeath(array, bd);
            }
        }

        public override void Unload() {
            data = null;
            isBulkLoss = false;
            harmScratch.Clear();
            Total = 0;
            LostInStasis = 0;
            WokenCount = 0;
            scratch.Clear();
            petSpecies.Clear();
        }

        public void NoteWoken() {
            WokenCount++;
        }

        // Up to count sleepers of this array in a seeded random order. Uniform on purpose:
        // who wakes next stays unknown to the player, and relationship driven wake-ups are
        // meant to be their own mechanics that bypass this draw, not a hidden tilt of it.
        // maxPets caps pets while there are people to fill the slots instead: a wake-up
        // choice is how the colony grows, and one full of pets is no choice at all. Pets
        // past the cap are passed over, not removed up front, so the chance of a pet being
        // offered at all stays what the uniform draw gives.
        public int DrawCandidates(StasisArrayComp array, Rng rng, int count,
                List<BeingData> result, int maxPets = int.MaxValue) {
            EnsurePopulated(array);
            FindSleepers(array, result);
            int people = 0;
            foreach (var bd in result) {
                if (!bd.Species.IsPet) {
                    people++;
                }
            }
            if (maxPets < count - people) {
                maxPets = count - people;
            }
            int taken = 0;
            int pets = 0;
            for (int i = 0; i < result.Count && taken < count; i++) {
                var j = rng.Range(i, result.Count);
                (result[i], result[j]) = (result[j], result[i]);
                if (result[i].Species.IsPet) {
                    if (pets >= maxPets) { continue; }
                    pets++;
                }
                (result[taken], result[i]) = (result[i], result[taken]);
                taken++;
            }
            result.RemoveRange(taken, result.Count - taken);
            return taken;
        }

        // The array is gone and these people with it. No bodies: there is nothing left
        // to eject them from. One alert for all of them, a death each.
        public int LoseSleepers(StasisArrayComp array, string causeOfDeath) {
            EnsurePopulated(array);
            var lost = new List<BeingData>();
            FindSleepers(array, lost);
            if (lost.Count == 0) { return 0; }
            var ticks = S.Ticks;
            // Not the shared cache builder: every death below fans out to listeners
            // that may be using it
            var names = new StringBuilder();
            // One alert for the whole array below, not one per person
            isBulkLoss = true;
            foreach (var bd in lost) {
                if (names.Length > 0) {
                    names.Append("<br>");
                }
                names.Append(bd.Mind.Name.FullNameOrNameless);
                // Unbinds and recounts through OnBeingDied
                BeingUtils.Eliminate(S, bd, causeOfDeath, ticks);
            }
            isBulkLoss = false;
            var msg = T.StasisLostAlert_V1.F(lost.Count);
            Notification.BuildFor(array.Tile, array.Tile.Definition.Preview, msg,
                    Consts.TicksPerDay, NotificationType.Alert)
                .WithTooltip(names.ToString())
                .Group("sts_lost")
                .Show();
            S.Sys.Log.AddLine(msg);
            return lost.Count;
        }

        // For an array that is removed without anyone being harmed by it (sandbox): the
        // people it held never existed as far as the colony is concerned.
        public void DiscardSleepers(StasisArrayComp array) {
            var discarded = new List<BeingData>();
            FindSleepers(array, discarded);
            foreach (var bd in discarded) {
                bd.Mind.StasisArrayId = 0;
                bd.Mind.Role = ColonyRole.Unassigned;
                if (bd.Mind.ManifestStatus != ManifestStatus.None) {
                    Total--;
                }
                bd.StateFlags = BeingState.Uninitialized;
                S.Query.DeactivateBeingData.Ask(bd);
            }
            array.SyncBeingCount(0);
        }

        // Moves every sleeper of one array to another, for an array that is relocated
        // and comes back as a different entity.
        public void Rebind(StasisArrayComp from, StasisArrayComp to) {
            RebindById(from.EntityId, to.EntityId);
        }

        public void RebindById(int fromId, int toId) {
            foreach (var bd in S.Query.AllBeingData.Ask().Values) {
                if (IsSleeperOf(bd, fromId)) {
                    bd.Mind.StasisArrayId = toId;
                }
            }
        }

        public static bool IsSleeperOf(BeingData bd, int arrayId) {
            var mind = bd.Mind;
            return mind != null && mind.Role == ColonyRole.InStasis
                && mind.StasisArrayId == arrayId
                && !bd.HasStateFlag(BeingState.Dead);
        }

        // Ordered by id, not by dictionary order, so a seeded draw over the result is
        // reproducible across save/load
        public int FindSleepers(StasisArrayComp array, List<BeingData> result) {
            result.Clear();
            var arrayId = array.EntityId;
            foreach (var bd in S.Query.AllBeingData.Ask().Values) {
                if (IsSleeperOf(bd, arrayId)) {
                    result.Add(bd);
                }
            }
            result.Sort(CompareById);
            return result.Count;
        }

        private static int CompareById(BeingData a, BeingData b) {
            return a.Id.CompareTo(b.Id);
        }

        public int CountSleepers(StasisArrayComp array) {
            int count = 0;
            var arrayId = array.EntityId;
            foreach (var bd in S.Query.AllBeingData.Ask().Values) {
                if (IsSleeperOf(bd, arrayId)) {
                    count++;
                }
            }
            return count;
        }

        // Generation time entry point. The rng is the caller's so that a scenario seed
        // always yields the same people.
        public void Populate(StasisArrayComp array, int humans, int pets, Rng rng) {
            #if !PRODUCTION_BUILD
            PopulateCalls++;
            #endif
            if (array.EntityId == 0) {
                D.Err("Cannot populate a stasis array that has no entity id yet: {0}", array);
                return;
            }
            var free = array.Capacity - CountSleepers(array);
            if (humans > free) { humans = free; }
            if (humans < 0) { humans = 0; }
            if (pets > free - humans) { pets = free - humans; }
            if (pets < 0) { pets = 0; }

            var generated = new List<BeingData>(humans + pets);
            var exceptNames = new HashSet<int>();
            var human = Species.Get(SpeciesIdH.Human);
            for (int i = 0; i < humans; i++) {
                Generate(array, human, rng, exceptNames, generated);
            }
            int humanCount = generated.Count;
            for (int i = 0; i < pets; i++) {
                var species = RandomPetSpecies(rng);
                if (species == null) { break; }
                Generate(array, species, rng, exceptNames, generated);
            }
            GenerateHouseholds(generated, humanCount, rng);
            ManifestBonds.Apply(S, generated, rng);
            array.SyncBeingCount(CountSleepers(array));
            // An array that turns up in a running game (a derelict) is usually already
            // unstable. Scenario start arrays get no grace: Ready.Game is still false then.
            if (Ready.Game) {
                array.HarmGraceUntilTick = S.Ticks
                    + (long) (Tunables.StasisHarmGraceDays * Consts.TicksPerDay);
            }
        }

        // Generation time entry point for people chosen before the game started (the
        // Custom Start screen). Each one is copied, never taken out of the list: a failed
        // generation is retried with the same setup and must find everyone again.
        // Relationships are rolled here, as for a seeded manifest, so they always fit
        // whoever ended up on it.
        public void Populate(StasisArrayComp array, List<StartingBeingData> presets, Rng rng) {
            #if !PRODUCTION_BUILD
            PopulateCalls++;
            #endif
            if (array.EntityId == 0) {
                D.Err("Cannot populate a stasis array that has no entity id yet: {0}", array);
                return;
            }
            var free = array.Capacity - CountSleepers(array);
            if (presets.Count > free) {
                D.Err("Stasis array is too small for the chosen sleepers: {0} of {1}",
                    free, presets.Count);
            }
            var added = new List<BeingData>(presets.Count);
            // Households expect the people first and the pets after them
            AddPresets(array, presets, false, free, added);
            int humanCount = added.Count;
            AddPresets(array, presets, true, free, added);
            GenerateHouseholds(added, humanCount, rng);
            ManifestBonds.Apply(S, added, rng);
            array.SyncBeingCount(CountSleepers(array));
            if (Ready.Game) {
                array.HarmGraceUntilTick = S.Ticks
                    + (long) (Tunables.StasisHarmGraceDays * Consts.TicksPerDay);
            }
        }

        private void AddPresets(StasisArrayComp array, List<StartingBeingData> presets,
                bool pets, int free, List<BeingData> added) {
            foreach (var sb in presets) {
                if (sb.Species.IsPet != pets) { continue; }
                if (added.Count >= free) { return; }
                var bd = sb.DeepCopyFor(S);
                // The copy does not carry the name source's result, and the name has to be
                // reserved in this game (RegisterDormant consumes it), not in the menu's.
                // Left on the original too, so a retried generation reserves it again. After
                // a rename it reserves the rolled name instead, which only costs a spare name.
                bd.Mind.Name.NameResult = sb.Data.Mind.Name.NameResult;
                AddSleeper(array, bd, added);
            }
        }

        #if !PRODUCTION_BUILD
        // Test seam: Populate rolls pet species by weight and cannot be told which
        public void PopulateSpecies(StasisArrayComp array, Species species, int count, Rng rng) {
            var free = array.Capacity - CountSleepers(array);
            if (count > free) { count = free; }
            var generated = new List<BeingData>(count);
            var exceptNames = new HashSet<int>();
            for (int i = 0; i < count; i++) {
                Generate(array, species, rng, exceptNames, generated);
            }
            array.SyncBeingCount(CountSleepers(array));
        }
        #endif

        // Restores the invariant for an array whose count was set without people behind
        // it: a save from before the manifest, a sandbox fill, a cloned game. Lazy on
        // purpose - an old save only pays for generation when somebody needs the roster.
        public void EnsurePopulated(StasisArrayComp array) {
            if (array.EntityId == 0) { return; }
            var bound = CountSleepers(array);
            var wanted = array.BeingCount;
            if (bound == wanted) { return; }
            if (bound > wanted) {
                array.SyncBeingCount(bound);
                return;
            }
            bound += BindUnboundSleepers(array, wanted - bound);
            var missing = wanted - bound;
            if (missing > 0) {
                var rng = Rng.Unseeded;
                var generated = new List<BeingData>(missing);
                var exceptNames = new HashSet<int>();
                var human = Species.Get(SpeciesIdH.Human);
                for (int i = 0; i < missing; i++) {
                    Generate(array, human, rng, exceptNames, generated);
                }
                GenerateHouseholds(generated, generated.Count, rng);
                ManifestBonds.Apply(S, generated, rng);
            }
            array.SyncBeingCount(CountSleepers(array));
        }

        // Colonists sent to stasis before sleepers were bound to arrays carry no array
        // id. They took up a chamber somewhere, so they fill the gap before anyone new
        // is invented for it.
        private int BindUnboundSleepers(StasisArrayComp array, int limit) {
            int bound = 0;
            foreach (var bd in S.Query.AllBeingData.Ask().Values) {
                if (bound >= limit) { break; }
                var mind = bd.Mind;
                if (mind == null || mind.Role != ColonyRole.InStasis) { continue; }
                if (mind.StasisArrayId != 0) { continue; }
                if (bd.HasStateFlag(BeingState.Dead)) { continue; }
                mind.StasisArrayId = array.EntityId;
                bound++;
            }
            return bound;
        }

        private void Generate(StasisArrayComp array, Species species, Rng rng,
                HashSet<int> exceptNames, List<BeingData> result) {
            BeingData bd;
            try {
                var vars = new GenBeingVars(species, BeingType.StasisWakeup);
                vars.Body = FactionDef.Player.GenBodyVars;
                vars.ExceptNames = exceptNames;
                bd = S.Query.GenBeingFull.Ask(vars, rng.Fork());
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to generate a stasis sleeper. Species: {0}", species);
                return;
            }
            if (bd == null) { return; }
            if (!AddSleeper(array, bd, result)) { return; }
            #if !PRODUCTION_BUILD
            GeneratedCount++;
            #endif
        }

        // Registers someone who has never been on the map and binds them to the array
        private bool AddSleeper(StasisArrayComp array, BeingData bd, List<BeingData> result) {
            if (!S.Query.RegisterDormantBeing.Ask(bd)) { return false; }
            bd.Mind.Role = ColonyRole.InStasis;
            bd.Mind.StasisArrayId = array.EntityId;
            bd.Mind.ManifestStatus = ManifestStatus.Sleeping;
            Total++;
            result.Add(bd);
            return true;
        }

        private Species RandomPetSpecies(Rng rng) {
            return RandomPetSpecies(rng, petSpecies);
        }

        // Weighted by StasisWakeUpChance. Public so the Custom Start screen rolls pets the
        // same way generation does. The cache is the caller's, sorted by id before rolling,
        // so the pick does not depend on the order species happened to be loaded in
        public static Species RandomPetSpecies(Rng rng, List<Species> petSpecies) {
            if (petSpecies.Count == 0) {
                foreach (var s in Species.Known.Values) {
                    if (s.IsPet && s.StasisWakeUpChance > 0f) {
                        petSpecies.Add(s);
                    }
                }
                petSpecies.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }
            if (petSpecies.Count == 0) { return null; }
            float total = 0f;
            foreach (var s in petSpecies) {
                total += s.StasisWakeUpChance;
            }
            var roll = rng.Range(0f, total);
            foreach (var s in petSpecies) {
                roll -= s.StasisWakeUpChance;
                if (roll <= 0f) { return s; }
            }
            return petSpecies[petSpecies.Count - 1];
        }

        // Relationships are generated inside small households instead of across the
        // whole array: GenerateRelationshipsFor is n^2 opinions, all of them saved, and
        // a manifest where everyone knows everyone tells the player nothing. Pets are
        // dealt into the human households, which is what lets them get a master.
        private void GenerateHouseholds(List<BeingData> generated, int humanCount, Rng rng) {
            if (generated.Count < 2) { return; }
            var size = Tunables.ManifestRelationshipClusterSize;
            if (size < 2) { size = 2; }
            var seats = humanCount > 0 ? humanCount : generated.Count;
            var households = (seats + size - 1) / size;
            Shuffle(generated, 0, humanCount, rng);
            Shuffle(generated, humanCount, generated.Count, rng);
            for (int h = 0; h < households; h++) {
                scratch.Clear();
                for (int i = 0; i < humanCount; i++) {
                    if (i % households == h) {
                        scratch.Add(generated[i]);
                    }
                }
                for (int i = humanCount; i < generated.Count; i++) {
                    if ((i - humanCount) % households == h) {
                        scratch.Add(generated[i]);
                    }
                }
                if (scratch.Count > 1) {
                    S.Query.GenerateRelationshipsFor.Ask(scratch);
                }
            }
            scratch.Clear();
        }

        public static void Shuffle(List<BeingData> list, int from, int to, Rng rng) {
            for (int i = to - 1; i > from; i--) {
                var j = rng.Range(from, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
