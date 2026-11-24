using System;
using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Components;
using Game.Procgen.Names;
using Game.Story.Events;
using System.Text;
using System.Linq;
using Game.Systems.Creatures;
using Game.Systems.Story;

namespace Game.Constants {
    [Serializable]
    public class ColonistFate {
        public static readonly Dictionary<string, ColonistFate> ById = new();
        public static readonly List<ColonistFate> All = new();
        private static bool hasLoaded;
        public string Id;
        private string descT;
        public bool RemoveColonist;
        public bool IsDead;
        public string IsForSpecies;
        public bool ExcludeRobots;
        public bool ExcludeBiologicals;
        public bool ExcludePets;
        public bool ExcludeNonPets;
        public bool ExcludeNotInStasis;
        public bool IncludeInStasis;
        public float Chance;
        public int Max;
        public bool GenerateName;
        public string DescKey => $"fate.{Id}";
        public int NameMaxWords;
        public string CustomNames;
        public float[] NameChanceWeights;
        public NamePartDef[] NameParts;

        public const string CarriedAsAshesId = "CarriedAsAshes";

        public List<BeingData> AffectedBeings;
        public int AffectedInStasis;
        // People who are part of this fate without a BeingData to stand for them: an urn
        // holds a mind, the body it belonged to may be long gone from the roster
        public List<string> AffectedNames;

        public void ClearAffected() {
            AffectedBeings = null;
            AffectedInStasis = 0;
            AffectedNames = null;
        }

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            ById.Clear();
            All.Clear();
            hasLoaded = false;
        }

        public static void LoadAll() {
            if (hasLoaded) { return; }
            hasLoaded = true;

            var types = The.ModLoader.LoadObjects<ColonistFate>("Config/Fates");
            foreach (var s in types) {
                AddType(s);
            }
        }

        private static void AddType(ColonistFate s) {
            ById[s.Id] = s;
            All.AddUniqueInefficient(s);
        }

        public static ColonistFate Get(string type) {
            if (type == null) {
                D.Err("Getting terraform type by id null");
                return null;
            }
            LoadAll();
            var nType = ById.Get(type, null);
            if (nType == null) {
                D.Err("Terraform Type {0} is not defined! See Config/Terraform folder", type);
            }
            return nType;
        }

        public override string ToString() {
            return $"ColonistFate [{Id}][{D.Dump(AffectedBeings)}][S:{AffectedInStasis}]";
        }

        private bool IsApplicableFor(BeingData b) {
            return !ExcludeNotInStasis && IsAllowedFor(b);
        }

        public int AffectedCount => Arrays.Count(AffectedBeings) + AffectedInStasis
            + Arrays.Count(AffectedNames);

        // The same filters without the "is this someone awake" gate: sleepers are real beings
        // now, and a cat does not get elected president
        private bool IsApplicableInStasis(BeingData b) {
            return IncludeInStasis && IsAllowedFor(b);
        }

        // Robot or biological is the frame, which is what the worker fates are about. A pet is
        // a pet by its mind: a person in a cat's body can still found a city
        private bool IsAllowedFor(BeingData b) {
            if (ExcludeBiologicals && b.Species.IsBiological) { return false; }
            if (ExcludeRobots && !b.Species.IsBiological) { return false; }
            var isPet = b.Mind.Species.IsAnimal;
            if (ExcludePets && isPet) { return false; }
            if (ExcludeNonPets && !isPet) { return false; }
            if (!string.IsNullOrEmpty(IsForSpecies) && b.Species.Id != IsForSpecies) {
                return false;
            }
            return Max == 0 || AffectedCount < Max;
        }

        private float ChanceFor(BeingData b) {
            return Chance;
        }

        private static Dictionary<ColonistFate, float> WeightedOptsFor(BeingData b) {
            var opts = new Dictionary<ColonistFate, float>();
            foreach (var opt in All) {
                // Chance 0 marks a fate that is assigned, never rolled (CarriedAsAshes)
                if (opt.Chance > 0f && opt.IsApplicableFor(b)) {
                    opts.Add(opt, opt.ChanceFor(b));
                }
            }
            return opts;
        }

        private bool ApplyFor(BeingData b) {
            AffectedBeings ??= new();
            AffectedBeings.Add(b);
            return !RemoveColonist;
        }

        public static bool ForBeing(BeingData b) {
            var opts = WeightedOptsFor(b);
            if (The.R.State.Rng.TryWeightedFrom(opts, out var fate)) {
                return fate.ApplyFor(b);
            }
            return false;
        }

        // Everyone still asleep when the ship arrives gets a fate of their own, by name.
        // They were people the whole way; the ending does not turn them back into a count.
        public static void ForStasisArray(StasisArrayComp array) {
            if (array.BeingCount == 0) { return; }
            var s = array.S;
            var manifest = ManifestSys.For(s);
            manifest.EnsurePopulated(array);
            var sleepers = new List<BeingData>();
            manifest.FindSleepers(array, sleepers);
            var rng = s.Rng.Fork();
            var opts = new Dictionary<ColonistFate, float>();
            foreach (var b in sleepers) {
                opts.Clear();
                foreach (var opt in All) {
                    if (opt.Chance > 0f && opt.IsApplicableInStasis(b)) {
                        opts.Add(opt, opt.Chance);
                    }
                }
                if (rng.TryWeightedFrom(opts, out var fate)) {
                    fate.ApplyFor(b);
                }
                // Off the ship either way: the array goes down to the planet with them
                b.Mind.StasisArrayId = 0;
                b.Mind.Role = ColonyRole.Unassigned;
                b.StateFlags |= BeingState.InColony;
            }
        }

        // The dead who made the journey anyway. Deterministic, not rolled: an urn aboard
        // is a decision the player made about that person.
        // counted receives the urns whose ashes the fate names, so the caller can take
        // exactly those ashes off the ship
        public static void ForUrns(GameState s, List<UrnNameComp> counted = null) {
            var fate = ById.Get(CarriedAsAshesId, null);
            if (fate == null) { return; }
            var urns = s.Components.AllByType<UrnNameComp>();
            if (urns == null) { return; }
            foreach (UrnNameComp urn in urns) {
                var mind = urn.Mind;
                if (mind == null || mind.Faction?.IsPlayer != true) { continue; }
                if (!urn.Tile.IsConstructed) { continue; }
                fate.AffectedNames ??= new();
                fate.AffectedNames.Add(mind.Name.FullNameOr(mind.Species?.Name));
                counted?.Add(urn);
            }
        }

        // How many of the people who were put on a stasis manifest reached the planet
        // alive. Null when this game never had a manifest (an old save).
        public static string DeliveredLine(GameState s, string destination) {
            var total = ManifestSys.For(s).Total;
            if (total < 1) { return null; }
            int delivered = 0;
            foreach (var fate in All) {
                if (!fate.RemoveColonist || fate.IsDead || fate.AffectedBeings == null) { continue; }
                foreach (var b in fate.AffectedBeings) {
                    if (b.Mind.ManifestStatus != ManifestStatus.None) {
                        delivered++;
                    }
                }
            }
            return T.FateDelivered_V3.F(delivered, total, destination);
        }

        public string StoryLine(GameState s) {
            var sb = new StringBuilder();
            try {
                if (GenerateName) {
                    var name = GenNamesLegacy.For(this).Generate(s.Rng, false);
                    sb.Append(Texts.Accented(DescKey.T(AffectedCount, name)));
                } else {
                    sb.Append(Texts.Accented(DescKey.T(AffectedCount)));
                }
                var knownBeingCount = Arrays.Count(AffectedBeings);
                if (knownBeingCount > 0) {
                    var beingNames = AffectedBeings.Select(
                        b => b.Mind.Name.FullNameOr(b.Species.Name));
                    sb.Append("\n");
                    sb.AppendJoin("\n", beingNames);
                }
                if (AffectedInStasis > 0) {
                    sb.Append("\n");
                    sb.Append(T.ColonistsFromStasisArray_V1.F(AffectedInStasis));
                }
                if (!Arrays.IsEmpty(AffectedNames)) {
                    sb.Append("\n");
                    sb.AppendJoin("\n", AffectedNames);
                }
            } catch (Exception ex) {
                sb.AppendFormat("Failed generating story line for {0}: {1}",
                    Id, ex.Message);
                D.LogEx(ex, "Failed generating story line for {0}", Id);
            }
            return sb.ToString();
        }

        public static string Story(GameState s, string destination = null) {
            var stories = new List<string>();
            var delivered = DeliveredLine(s, destination);
            if (delivered != null) {
                stories.Add(Texts.Accented(delivered));
            }
            foreach (var evt in All) {
                if (evt.AffectedCount == 0) { continue; }
                var story = evt.StoryLine(s);
                stories.Add(story);
                var data = StoryEventData.CreateCustom(
                    IconId.CTerraform, T.ColonyEvent, story);
                s.Sig.AddCustomStoryEvent.Send(data, false);
            }
            return string.Join("\n\n", stories);
        }
    }
}
