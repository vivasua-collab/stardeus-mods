using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class StoryEventDef { // Changed class name from StoryAspectDef to StoryEventDef
        public static readonly Dictionary<int, StoryEventDef> All = new();
        public static readonly List<StoryEventDef> Ordered = new();
        /// <summary>
        /// All events by outcome excluding OnDemand ones
        /// </summary>
        public static List<StoryEventDef>[] Considerable;
        /// <summary>
        /// All events by outcome
        /// </summary>
        public static List<StoryEventDef>[] ByOutcome;
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
            Ordered.Clear();
            Considerable = null;
            ByOutcome = null;
            MaxPoints = 0;
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static int MaxPoints;
        public string Id;
        public string EventId; // Renamed property from AspectId to EventId
        public int Points;
        public int IdH;
        public bool IsDisabled;
        [JsonEnum(typeof(Game.Systems.Story.StoryEventFlags))]
        public string[] EventFlags;
        [NonSerialized]
        public StoryEventFlags Flags;
        [JsonEnum(typeof(Game.Systems.Story.StoryEventOutcome))]
        public string OutcomeType;
        public StoryEventOutcome Outcome;
        public StoryEvent Event;
        public Situation Require;
        /// <summary>
        /// Use Vars instead of this in C#
        /// </summary>
        public SerializableProperty[] Properties;
        private CachedProperties vars;
        public CachedProperties Vars => vars ??= new CachedProperties(Properties);
        /// <summary>
        /// Can't be 0, 1f - normal, 2f - common, 0.5f - rare
        /// </summary>
        public float Rarity;
        public float MinRepeatDelayDays;
        /// <summary>
        /// If it's 0, it will default to Tunable value
        /// </summary>
        public float MaxOngoingDays;
        public int MaxOngoingTicks;
        public static StoryEventFlags[] AllFlags;
        public static StoryEventOutcome[] AllOutcomes;
        public static StoryEventFlags[] CoolableFlags;
        public float[] SectorDifficultyChance;
        private Curve sectorDifficultyCurve;
        public Curve SectorDifficultyCurve => sectorDifficultyCurve ??= SectorDifficultyChance;
        public bool RunASAPWhenPrioritized;
        public static StoryEventDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("StoryEventDef not found by id: {0}", id);
            return null;
        }
        public static StoryEventDef FastGet(int idH) {
            if (All.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("StoryEventDef not found by id hash: {0}", idH);
            return null;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            AllFlags = (StoryEventFlags[]) Enum.GetValues(typeof(StoryEventFlags));
            AllOutcomes = (StoryEventOutcome[]) Enum.GetValues(typeof(StoryEventOutcome));
            var flags = Enum.GetValues(typeof(StoryEventFlags));
            var coolable = new List<StoryEventFlags>();
            foreach (StoryEventFlags flag in flags) {
                if (flag == StoryEventFlags.None) { continue; }
                if ((StoryEventFlags.Coolable & flag) == flag) {
                    coolable.Add(flag);
                }
            }
            CoolableFlags = coolable.ToArray();
            var types = The.ModLoader.LoadObjects<StoryEventDef>("Config/Story/Events");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                type.Event = Registries.StoryEvents.Get(type.EventId);
                type.Flags = Enums.FlagsFromStringArray_ULong<StoryEventFlags>(type.EventFlags, type);
                type.Outcome = Enums.FromString<StoryEventOutcome>(type.OutcomeType);
                float maxDays = type.MaxOngoingDays;
                if (maxDays > 0) {
                    type.MaxOngoingTicks = (int) (maxDays * Consts.TicksPerDay);
                }
                if (type.Rarity <= 0f) {
                    type.Rarity = 1f;
                }
                if (type.Event == null) {
                    D.Err("Couldn't find StoryEvent for StoryEventDef with id: {0} -> {1}",
                        type.Id, type.EventId);
                    continue;
                }
                if (type.Points > MaxPoints) {
                    MaxPoints = type.Points;
                }
                if (type.Points < 1 && !type.Flags.HasFlag(StoryEventFlags.OnDemand)) {
                    D.Err("Cannot allow StoryEventDef with < 1 Points without OnDemand flag! {0}", type);
                    type.Points = 1;
                }
                if (type.Flags.HasFlag(StoryEventFlags.Fallback)
                        && type.Flags.HasFlag(StoryEventFlags.OncePerGame)) {
                    D.Err("Fallback event cannot be OncePerGame, it would never be pickable! {0}",
                        type);
                }
                type.RunASAPWhenPrioritized = (type.Flags & StoryEventFlags.RunASAPWhenPrioritized) != StoryEventFlags.None;
                type.Event.LinkedDefs.Add(type);
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("StoryEventDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }

            Ordered.AddRange(All.Values.OrderBy(e => e.Id));

            var outcomes = Enum.GetValues(typeof(StoryEventOutcome));
            Considerable = new List<StoryEventDef>[outcomes.Length];
            ByOutcome = new List<StoryEventDef>[outcomes.Length];
            for (int i = 0; i < outcomes.Length; i++) {
                Considerable[i] = new List<StoryEventDef>();
                ByOutcome[i] = new List<StoryEventDef>();
            }
            foreach (var evt in All.Values) {
                ByOutcome[(int) evt.Outcome].Add(evt);
            }
            foreach (var evt in All.Values.Where(
                    e => !e.Flags.HasFlag(StoryEventFlags.OnDemand))) {
                Considerable[(int) evt.Outcome].Add(evt);
            }
        }
        public static StoryEventDef Find(string id, bool exactMatch) {
            id = Strings.ToSearchString(id);
            StoryEventDef ev = null;
            var ordered = All.Values.OrderBy(e => e.Id);
            foreach (var evt in ordered) {
                if (Strings.ToSearchString(evt.Id) == id) {
                    ev = evt;
                    break;
                }
            }
            if (!exactMatch && ev == null) {
                foreach (var evt in ordered) {
                    if (Strings.ToSearchString(evt.Id).StartsWith(id)) {
                        ev = evt;
                        break;
                    }
                }
            }
            if (!exactMatch && ev == null) {
                foreach (var evt in ordered) {
                    if (Strings.ToSearchString(evt.Id).Contains(id)) {
                        ev = evt;
                        break;
                    }
                }
            }

            return ev;
        }
        public string Tooltip {
            get {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                sb.Append(Texts.Accented(Id));
                sb.Append(GlyphMap.BRHR);
                sb.AppendFormat("Implementation: <b>{0}</b>\n", EventId);
                if (EventFlags != null) {
                    sb.AppendFormat("Flags: <b>{0}</b>\n", string.Join(", ", EventFlags));
                }
                sb.AppendFormat(
                    "Min Repeat Delay Days: <b>{0:0.#}</b>\n",
                    MinRepeatDelayDays);
                sb.AppendFormat("Rarity: <b>{0:0.#}</b>\n", Rarity);
                sb.AppendFormat(
                    "Outcome: <b>{0}</b>", Outcome);
                if (Require != null) {
                    sb.Append(GlyphMap.BRHR);
                    sb.Append("<b>Requirements:</b>");
                    if (Require.Difficulty > 1.01f) {
                        sb.AppendFormat(
                            " - Min Difficulty: <b>{0:0.#}</b> (Challenging+)\n",
                                Require.Difficulty);
                    } else if (Require.Difficulty > 0.51f) {
                        sb.AppendFormat(
                            " - Min Difficulty: <b>{0:0.#}</b> (Balanced+)\n",
                                Require.Difficulty);
                    }
                    sb.Append(Require.MatchesDump(true, Require.Difficulty));
                }
                return sb.ToString();
            }
        }

        public bool IsColonyGrowthBio =>
            (Flags & StoryEventFlags.ColonyGrowthBio) == StoryEventFlags.ColonyGrowthBio;

        public bool IsColonyGrowthRobo =>
            (Flags & StoryEventFlags.ColonyGrowthRobo) == StoryEventFlags.ColonyGrowthRobo;

        public bool IsColonyGrowth => IsColonyGrowthBio || IsColonyGrowthRobo;

        // Raids and heavy disasters: what Difficulty.AllowMajorThreats turns off
        public bool IsMajorThreat => Outcome == StoryEventOutcome.Raid
            || (Flags & StoryEventFlags.HeavyDisaster) == StoryEventFlags.HeavyDisaster;

        public bool CheckPoints(StoryContext ctx, StoryPickIgnore ignore) {
            return Event.CheckPoints(ctx, ignore, this);
        }

        public override string ToString() {
            return $"StoryEventDef[{Id}|Pts:{Points}]";
        }
    }
}
