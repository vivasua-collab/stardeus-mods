#if !PRODUCTION_BUILD
// #define DEBUG_TENSION
#endif
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [Serializable]
    [MessagePackObject]
    public class Situation {
        public void RestoreFrom(Situation saved) {
            var fields = GetType().GetFields();
            foreach (var field in fields) {
                //var name = field.Name;
                var savedVal = field.GetValue(saved);
                field.SetValue(this, savedVal);
                //D.Warn("Restoring saved situation state: {0} -> {1}", name, savedVal);
            }
        }

        [Key("Friendlies")] public int Friendlies;
        [Key("Workers")] public int Workers;
        // Biological friendlies
        [Key("Colonists")] public int Colonists;
        // Non-hostile visitors of another faction (see Docs/Architecture/Systems/Guests.md).
        // Deliberately outside Friendlies / Colonists / Workers, which stay the player's own
        // faction: a rescued stranger must not read as crew.
        [Key("Guests")] public int Guests;
        // Friendlies capable of firefighting
        [Key("Firefighters")] public int Firefighters;
        [Key("ColonySizeDiff")] public int ColonySizeDiff;
        // How big the colony is for the storygen: colonists + bots, pets at 0.5. Bots do a
        // colonist's work and the same storygen grows them, so they are population, not
        // infrastructure. Drives ColonyGrowth (population intent); ColonySizeDiff is derived
        // from it. Unlike ThreatWeight this is not injury or availability scaled.
        [Key("Population")] public float Population;
        [Key("HumanDeaths")] public int HumanDeaths;
        [Key("Enemies")] public int Enemies;
        [Key("ActiveThreats")] public int ActiveThreats;
        [Key("Areas")] public int Areas;
        [Key("Islands")] public int Islands;
        [Key("ShipSize")] public int ShipSize;
        [Key("ResearchedTech")] public int ResearchedTech;
        [Key("GridMaxOutput")] public float GridMaxOutput; // KW
        [Key("GridConsumption")] public float GridConsumption; // KW
        [Key("GridDeficit")] public float GridDeficit; // KW
        [Key("GridBatCharge")] public float GridBatCharge; // KW
        [Key("GridBatOutput")] public float GridBatOutput; // KW
        [Key("GridRenewOutput")] public float GridRenewableOutput; // KW
        [Key("GridSize")] public int GridSize;
        [Key("Grids")] public int Grids;
        [Key("SectorDifficulty")] public float SectorDifficulty;
        [Key("ShipComputers")] public int ShipComputers;
        [Key("TotalCPU")] public int TotalCPU;
        [Key("TotalGPU")] public int TotalGPU;
        [Key("TotalMemory")] public int TotalMemory;
        [Key("TotalDisk")] public int TotalDisk;
        [Key("BlackHoleJumps")] public int BlackHoleJumps;
        [Key("DerelictsApproached")] public int DerelictsApproached;
        [Key("ExpeditionsCompleted")] public int ExpeditionsCompleted;
        [Key("ExpeditionsOngoing")] public int ExpeditionsOngoing;
        [Key("StoryEvents")] public int StoryEvents;
        [Key("StoryPointsPositiveMax")] public int StoryPointsPositiveMax;
        [Key("StoryPointsPositive")] public int StoryPointsPositive;
        [Key("StoryPointsNegativeMax")] public int StoryPointsNegativeMax;
        [Key("StoryPointsNegative")] public int StoryPointsNegative;
        [Key("StoryPointsNeutralMax")] public int StoryPointsNeutralMax;
        [Key("StoryPointsNeutral")] public int StoryPointsNeutral;
        [Key("StoryPointsRaidMax")] public int StoryPointsRaidMax;
        [Key("StoryPointsRaid")] public int StoryPointsRaid;
        // In RestoreFrom()
        [Key("SpaceObjectsDiscovered")] public int SpaceObjectsDiscovered;
        [Key("Days")] public int Days;
        [Key("Weeks")] public int Weeks;
        [Key("Months")] public int Months;
        [Key("Years")] public int Years;
        /// <summary>
        /// Millions
        /// </summary>
        [Key("MoneyM")] public float Money;
        /// <summary>
        /// Millions, after stock market price effect
        /// </summary>
        [Key("NetWorthM")] public float NetWorth;
        /// <summary>
        /// Millions, before stock market price effect
        /// </summary>
        [Key("BaseNetWorthM")] public float BaseNetWorth;
        [Key("ShipClassPoints")] public int ShipClassPoints;
        [Key("Mats")] public int Mats;
        [Key("OnFire")] public int TimesOnFire;
        // Renamed from NumCombats; key preserved for save compatibility.
        // Counts only actual hostile raids (incremented in CombatRaid.CreateArmy).
        // Friendly fights and animal aggressions do NOT count.
        [Key("NumCombats")] public int NumRaids;
        // Cumulative mental breakdowns this run, counted in MentalBreakdownStoryEvent.
        [Key("NumBreakdowns")] public int NumBreakdowns;
        /// <summary>
        /// 1 = human without weapons
        /// </summary>
        [Key("CombatStrength")] public float CombatStrength;
        /// <summary>
        /// Sum of RaidThreatWeight.Of() over player beings - the raid-points headcount term.
        /// Unlike Friendlies (raw count), this discounts injured/downed/stored/expedition
        /// beings and animals, so a mauled colony isn't priced like a healthy one. Updated
        /// alongside CombatStrength in CombatSys.UpdateCombatStrength. 0 on a save from before
        /// this field existed; CombatRaid.ComputePlan falls back to Friendlies in that case.
        /// </summary>
        [Key("ThreatWeight")] public float ThreatWeight;
        [Key("NumShipCombats")] public int NumShipCombats;
        [Key("OngoingBuildTasks")] public int OngoingBuildTasks;
        // Can't use this anywhere except for Match checks!
        [IgnoreMember] public float Difficulty;

        public Situation Copy() {
            return (Situation) MemberwiseClone();
        }

        // d = difficulty
        public bool Matches(Situation min, float d) {
            if (min == null) { return true; }
            // if min days is 10, days passed is 15
            // 10 > 15 -> false
            if (min.Difficulty > 0 && min.Difficulty > d) { return false; }
            if (min.SectorDifficulty > 0 && min.SectorDifficulty > SectorDifficulty) { return false; }
            if (min.Days > 0 && min.Days > Days) { return false; }
            if (min.Friendlies > 0 && min.Friendlies > Friendlies) { return false; }
            if (min.Workers > 0 && min.Workers > Workers) { return false; }
            if (min.Colonists > 0 && min.Colonists > Colonists) { return false; }
            if (min.Guests > 0 && min.Guests > Guests) { return false; }
            if (min.ColonySizeDiff > 0 && min.ColonySizeDiff > ColonySizeDiff) { return false; }
            if (min.HumanDeaths > 0 && min.HumanDeaths > HumanDeaths) { return false; }
            if (min.Areas > 0 && min.Areas > Areas) { return false; }
            if (min.Islands > 0 && min.Islands > Islands) { return false; }
            if (min.ShipSize > 0 && min.ShipSize > ShipSize) { return false; }
            if (min.GridSize > 0 && min.GridSize > GridSize) { return false; }
            if (min.Grids > 0 && min.Grids > Grids) { return false; }
            if (min.ShipComputers > 0 && min.ShipComputers > ShipComputers) { return false; }
            if (min.ExpeditionsCompleted > 0 && min.ExpeditionsCompleted > ExpeditionsCompleted) { return false; }
            if (min.DerelictsApproached > 0 && min.DerelictsApproached > DerelictsApproached) { return false; }
            if (min.SpaceObjectsDiscovered > 0 && min.SpaceObjectsDiscovered > SpaceObjectsDiscovered) { return false; }
            if (min.StoryEvents > 0 && min.StoryEvents > StoryEvents) { return false; }
            if (min.Money > 0 && min.Money > Money) { return false; }
            if (min.NumRaids > 0 && min.NumRaids > NumRaids) { return false; }
            if (min.NumShipCombats > 0 && min.NumShipCombats > NumShipCombats) { return false; }
            if (min.ShipClassPoints > 0 && min.ShipClassPoints > ShipClassPoints) { return false; }
            return true;
        }

        public string MatchesDump(bool skipZero, float d) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            AppendMatchDump(skipZero, "Difficulty", d, sb);
            AppendMatchDump(skipZero, "Days", Days, sb);
            AppendMatchDump(skipZero, "Friendlies", Friendlies, sb);
            AppendMatchDump(skipZero, "Workers", Workers, sb);
            AppendMatchDump(skipZero, "Colonists", Colonists, sb);
            AppendMatchDump(skipZero, "Guests", Guests, sb);
            AppendMatchDump(skipZero, "ColonySizeDiff", ColonySizeDiff, sb);
            AppendMatchDump(skipZero, "HumanDeaths", HumanDeaths, sb);
            AppendMatchDump(skipZero, "Areas", Areas, sb);
            AppendMatchDump(skipZero, "SectorDifficulty", SectorDifficulty, sb);
            AppendMatchDump(skipZero, "Islands", Islands, sb);
            AppendMatchDump(skipZero, "GridSize", GridSize, sb);
            AppendMatchDump(skipZero, "Grids", Grids, sb);
            AppendMatchDump(skipZero, "ShipComputers", ShipComputers, sb);
            AppendMatchDump(skipZero, "ExpeditionsCompleted", ExpeditionsCompleted, sb);
            AppendMatchDump(skipZero, "DerelictsApproached", DerelictsApproached, sb);
            AppendMatchDump(skipZero, "SpaceObjectsDiscovered", SpaceObjectsDiscovered, sb);
            AppendMatchDump(skipZero, "StoryEvents", StoryEvents, sb);
            AppendMatchDump(skipZero, "StoryPointsPositive", StoryPointsPositive, sb);
            AppendMatchDump(skipZero, "StoryPointsNegative", StoryPointsNegative, sb);
            AppendMatchDump(skipZero, "StoryPointsNeutral", StoryPointsNeutral, sb);
            AppendMatchDump(skipZero, "StoryPointsRaid", StoryPointsRaid, sb);
            AppendMatchDump(skipZero, "StoryPointsPositiveMax", StoryPointsPositiveMax, sb);
            AppendMatchDump(skipZero, "StoryPointsNegativeMax", StoryPointsNegativeMax, sb);
            AppendMatchDump(skipZero, "StoryPointsNeutralMax", StoryPointsNeutralMax, sb);
            AppendMatchDump(skipZero, "StoryPointsRaidMax", StoryPointsRaidMax, sb);
            AppendMatchDump(skipZero, "MoneyM", Money, sb);
            AppendMatchDump(skipZero, "NumRaids", NumRaids, sb);
            AppendMatchDump(skipZero, "NumShipCombats", NumShipCombats, sb);
            AppendMatchDump(skipZero, "ShipClassPoints", ShipClassPoints, sb);

            return sb.ToString();
        }

        public void UpdateTime(long ticks) {
            Days = (int) (ticks / (float) Consts.TicksPerDay);
            Weeks = (int) (ticks / (float) Consts.TicksPerWeek);
            Months = (int) (ticks / (float) Consts.TicksPerMonth);
            Years = (int) (ticks / (float) Consts.TicksPerYear);
        }

        private void AppendMatchDump(bool skipZero, string key, float value, StringBuilder sb) {
            if (!skipZero || value != 0f) {
                sb.AppendFormat("\n - {0}: <b>{1}</b>", key, value);
            }
        }
        private void AppendMatchDump(bool skipZero, string key, double value, StringBuilder sb) {
            if (!skipZero || value != 0d) {
                sb.AppendFormat("\n - {0}: <b>{1}</b>", key, Math.Round(value));
            }
        }
        private void AppendMatchDump(bool skipZero, string key, int value, StringBuilder sb) {
            if (!skipZero || value != 0) {
                sb.AppendFormat("\n - {0}: <b>{1}</b>", key, value);
            }
        }

        // For Random storygen
        public bool MatchesLight(Situation min, float d) {
            if (min == null) { return true; }
            // if min days is 10, days passed is 15
            // 10 > 15 -> false
            if (min.Difficulty > 0 && min.Difficulty > d) { return false; }
            if (min.SectorDifficulty > 0 && min.SectorDifficulty > SectorDifficulty) { return false; }
            if (min.Days > 0 && min.Days > Days) { return false; }
            if (min.Friendlies > 0 && min.Friendlies > Friendlies) { return false; }
            if (min.Workers > 0 && min.Workers > Workers) { return false; }
            if (min.Colonists > 0 && min.Colonists > Colonists) { return false; }
            if (min.Guests > 0 && min.Guests > Guests) { return false; }
            if (min.ColonySizeDiff > 0 && min.ColonySizeDiff > ColonySizeDiff) { return false; }
            return true;
        }

        public string Dump(string sep = ";") {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            if (Difficulty > 0) { sb.AppendFormat("Diff: {0}{1}", Difficulty, sep); }
            if (SectorDifficulty > 0) { sb.AppendFormat("Sec Diff: {0}{1}", SectorDifficulty, sep); }
            if (Days > 0) { sb.AppendFormat("Days: {0}{1}", Days, sep); }
            if (Weeks > 0) { sb.AppendFormat("Weeks: {0}{1}", Weeks, sep); }
            if (Friendlies > 0) { sb.AppendFormat("Friendlies: {0}{1}", Friendlies, sep); }
            if (Workers > 0) { sb.AppendFormat("Workers: {0}{1}", Workers, sep); }
            if (Colonists > 0) { sb.AppendFormat("Colonists: {0}{1}", Colonists, sep); }
            if (Guests > 0) { sb.AppendFormat("Guests: {0}{1}", Guests, sep); }
            if (ColonySizeDiff > 0) { sb.AppendFormat("ColonySizeDiff: {0}{1}", ColonySizeDiff, sep); }
            if (HumanDeaths > 0) { sb.AppendFormat("Human Deaths: {0}{1}", HumanDeaths, sep); }
            if (Areas > 0) { sb.AppendFormat("Areas: {0}{1}", Areas, sep); }
            if (Islands > 0) { sb.AppendFormat("Islands: {0}{1}", Islands, sep); }
            if (ShipSize > 0) { sb.AppendFormat("Ship Size: {0}{1}", ShipSize, sep); }
            if (GridSize > 0) { sb.AppendFormat("Grid Size: {0}{1}", GridSize, sep); }
            if (Grids > 0) { sb.AppendFormat("Grids: {0}{1}", Grids, sep); }
            if (ShipComputers > 0) { sb.AppendFormat("Ship Computers: {0}{1}", ShipComputers, sep); }
            if (ExpeditionsCompleted > 0) { sb.AppendFormat("Expeditions Completed: {0}{1}", ExpeditionsCompleted, sep); }
            if (DerelictsApproached > 0) { sb.AppendFormat("Derelicts Approached: {0}{1}", DerelictsApproached, sep); }
            if (SpaceObjectsDiscovered > 0) { sb.AppendFormat("Space Objects Discovered: {0}{1}", SpaceObjectsDiscovered, sep); }
            if (StoryEvents > 0) { sb.AppendFormat("Story Events: {0}{1}", StoryEvents, sep); }
            if (StoryPointsPositiveMax > 0) { sb.AppendFormat("Story Points (Positive Max): {0}{1}", StoryPointsPositiveMax, sep); }
            if (StoryPointsNegativeMax > 0) { sb.AppendFormat("Story Points (Negative Max): {0}{1}", StoryPointsNegativeMax, sep); }
            if (StoryPointsNeutralMax > 0) { sb.AppendFormat("Story Points (Neutral Max): {0}{1}", StoryPointsNeutralMax, sep); }
            if (StoryPointsRaidMax > 0) { sb.AppendFormat("Story Points (Raid Max): {0}{1}", StoryPointsRaidMax, sep); }
            if (StoryPointsPositive > 0) { sb.AppendFormat("Story Points (Positive): {0}{1}", StoryPointsPositive, sep); }
            if (StoryPointsNegative > 0) { sb.AppendFormat("Story Points (Negative): {0}{1}", StoryPointsNegative, sep); }
            if (StoryPointsNeutral > 0) { sb.AppendFormat("Story Points (Neutral): {0}{1}", StoryPointsNeutral, sep); }
            if (StoryPointsRaid > 0) { sb.AppendFormat("Story Points (Raid): {0}{1}", StoryPointsRaid, sep); }
            if (Money > 0) { sb.AppendFormat("Money: {0}{1}", Money, sep); }
            if (NumRaids > 0) { sb.AppendFormat("Num Raids: {0}{1}", NumRaids, sep); }
            if (NumShipCombats > 0) { sb.AppendFormat("Num Ship Combats: {0}{1}", NumShipCombats, sep); }
            if (ShipClassPoints > 0) { sb.AppendFormat("Ship Class Points: {0}{1}", ShipClassPoints, sep); }
            return sb.ToString();
        }

    }
}
