using System;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.AI.Combat;
using Game.Input;
using System.Linq;
using KL.Randomness;

namespace Game.Data.Factions {
    public class FactionDef {
        public static readonly Dictionary<string, FactionDef> Known = new();
        private static readonly Dictionary<int, FactionDef> byIdH = new();
        private static readonly List<FactionDef> bodyVarsFactions = new();
        public static IOrderedEnumerable<FactionDef> AllOrdered =>
            Known.Values.Where(f => !f.IsInternal).OrderBy(f => f.NameT);
        private static bool hasLoaded;
        public string CombatObjective;
        /// <summary>
        /// Chance for trader of this faction to be trading with a ship that
        /// violates class limits. Or with pirates / virus faction
        /// </summary>
        public float ChanceTradeWithOutlaw;
        /// <summary>
        /// Chance of this ship attacking illegal ships (if they're weaker)
        /// Multiplied by ShipTagDef.ChancePolice
        /// </summary>
        public float ChancePolice;
        /// <summary>
        /// How likely ships of this faction will appear randomly in the universe
        /// </summary>
        public float ChanceRandomShip;
        public CombatObjective ArmyObjective;
        public GenBeingBodyVars GenBodyVars;

        /// <summary>
        /// Multiplier on raid points for raids by this faction (1 = no effect). Folded into
        /// CombatRaid.ComputePlan's multiplier chain alongside difficulty/tension/ramp, so a
        /// faction's combat culture scales proportionally at every stage instead of landing as
        /// a flat addition that dwarfs an early budget and is noise against a late one -
        /// replaces the old additive ExtraRaidStrength.
        /// </summary>
        public float RaidStrengthMul = 1f;
        public uint HyperjumpRelayBaseToll;

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            Known.Clear();
            byIdH.Clear();
            bodyVarsFactions.Clear();
            hasLoaded = false;
            Player = null;
            Blank = null;
        }
        public static GenBeingBodyVars RandomBodyVars(Rng rng) {
            var fac = rng.From(bodyVarsFactions);
            return fac.GenBodyVars;
        }

        public static FactionDef FastGet(int idH) {
            if (idH == 0) { return null; }
            if (byIdH.TryGetValue(idH, out var fac)) {
                return fac;
            }
            return null;
        }
        public static void LoadKnown() {
            if (hasLoaded) { return; }
            hasLoaded = true;

            var types = The.ModLoader.LoadObjects<FactionDef>(
                "Config/Diplomacy/Factions");
            foreach (var f in types) {
                if (f.IsPlayer) {
                    if (Player != null) {
                        D.Err("Player faction already assigned to {0}! Trying to assign another! {1}", Player, f);
                    }
                    Player = f;
                }
                if (f.ShipDensityMul <= 0f) {
                    f.ShipDensityMul = 1f;
                }
                if (f.IsBlank) {
                    if (Blank != null) {
                        D.Err("Blank faction already assigned to {0}! Trying to assign another! {1}", Blank, f);
                    }
                    Blank = f;
                } else {
                    f.ArmyObjective = Registries.CombatObjectives.Get(f.CombatObjective);
                }
                if (f.ArmyObjective == null) {
                    f.ArmyObjective = Registries.CombatObjectives.Get(CombatObjectiveId.Raid);
                }
                if (f.GenBodyVars.ChanceModded > 0f) {
                    bodyVarsFactions.Add(f);
                }
                // D.Warn("Raider body vars for faction: {0} -> {1}", f.Id, D.DumpInstance(f.RaiderBodyVars));
                // Cannot call this here because LoadKnown runs off the main thread!, .T() can't work
                // D.Log("Registering faction: {0} -> {1}", s.NameT, s.AboutT);
                AddFaction(f);
            }
        }
        private static void AddFaction(FactionDef f) {
            Known[f.Id] = f;
            f.IdH = Hashes.S(f.Id);
            byIdH[f.IdH] = f;
        }

        public static FactionDef Get(string id) {
            if (id == null) {
                D.Err("Trying to get faction with id null!");
                return null;
            }
            LoadKnown();
            var fac = Known.Get(id, null);
            if (fac == null) {
                D.Err("Faction {0} is not defined! See Config/Generation/Factions folder", id);
                WPopup.Create(true, Constants.IconId.CWarning,
                    "GAME BREAKING BUG", $"Faction \"{id}\" is no longer available. Perhaps a mod was removed, or this is a very old save. You can try to save a new file now and load it to see if it fixes the issue.").Spawn();
            }
            return fac;
        }
        public float[] RegionDifficultyChance;
        private Curve regionDifficultyCurve;
        public Curve RegionDifficultyCurve => regionDifficultyCurve ??= RegionDifficultyChance;
        /// <summary>
        /// How likely the ships of this faction will refuse to give in to being
        /// threatened given difference in strength.
        /// Curve key will go from -1 to 1, where -1 means that their ship is
        /// super weak compared to their enemy, 1 means that their ship is
        /// OP
        /// </summary>
        public Curve BraveryCurve => braveryCurve ??= BraveryChance;
        public float[] BraveryChance;
        private Curve braveryCurve;

        /// <summary>
        /// How likely the reaction will be positive (vs negative) given the faction standing
        /// key => standing, value => chance of positive reaction
        /// </summary>
        public Curve AttitudePositiveReactionCurve => positiveReactionCurve ??= AttitudePositiveReactionChance;
        public float[] AttitudePositiveReactionChance;
        private Curve positiveReactionCurve;
        /// <summary>
        /// NPC ship density multiplier for locations with this faction presence
        /// </summary>
        public float ShipDensityMul;
        public static FactionDef Player;
        public static FactionDef Blank;
        public void OverrideName(string name) {
            nameT = name;
        }
        private string nameT;
        public string NameT => nameT ??= (NameKey ?? $"faction.{Id}").T();
        public string NameKey;
        private string nameWithGlyph;
        public string NameWithGlyph {
            get {
                if (nameWithGlyph != null) { return nameWithGlyph; }
                if (IsInternal) {
                    nameWithGlyph = NameT;
                } else {
                    nameWithGlyph = $"{Glyph} {NameT}";
                }
                return nameWithGlyph;
            }
        }
        private string aboutT;
        public string AboutT => aboutT ??= $"faction.{Id}.about".TOrNull();
        public Entity Entity => null;
        public string Tooltip {
            get {
                if (IsPlayer) {
                    return AboutT;
                }
                return $"{NameT}: {AboutT}";
            }
        }
        private string glyph;
        public string Glyph {
            get {
                if (glyph != null) { return glyph; }
                if (IsInternal) {
                    D.Err("Trying to get glyph for internal faction: {0}", Id);
                    glyph = string.Empty;
                } else {
                    glyph = GlyphMap.Sprite("IconsColor", $"Faction_{Id}");
                }
                return glyph;
            }
        }
        public string Id;
        public int IdH;
        public bool IsInternal;
        public bool IsPlayer;
        public bool IsAntiPlayer;
        public bool IsBlank;
        /// <summary>
        /// This faction can never be part of an alliance. The only exception is the
        /// Virus faction when the player has installed the Asimov Override.
        /// </summary>
        public bool NeverAllies;
        // Outside the robot weapon law (RobotWeaponLaw): its robots may wield any weapon
        public bool IgnoresAsimovLaws;
        /// <summary>
        /// Faction ids this faction is permanently hostile towards. Treated
        /// symmetrically, so declaring it on one side is enough. These pairs are
        /// locked to FactionRelation.Enemy and never change.
        /// </summary>
        public string[] AlwaysEnemies;
        private int[] alwaysEnemyIdHs;
        /// <summary>
        /// Resolved lazily: LoadKnown may run off the main thread and before all
        /// factions have been registered.
        /// </summary>
        public bool IsAlwaysEnemyOf(FactionDef other) {
            if (Arrays.IsEmpty(AlwaysEnemies)) { return false; }
            if (alwaysEnemyIdHs == null) {
                alwaysEnemyIdHs = new int[AlwaysEnemies.Length];
                for (int i = 0; i < AlwaysEnemies.Length; i++) {
                    var fac = Get(AlwaysEnemies[i]);
                    alwaysEnemyIdHs[i] = fac?.IdH ?? 0;
                }
            }
            for (int i = 0; i < alwaysEnemyIdHs.Length; i++) {
                if (alwaysEnemyIdHs[i] == other.IdH) { return true; }
            }
            return false;
        }
        public string FlagIcon;
        /// <summary>
        /// How likely a trader of this faction will include black market items
        /// </summary>
        public float ChanceBlackMarket;
        /// <summary>
        /// Traders of this faction buy and sell people (once Black Market is researched).
        /// Kept to outlaw factions so that human trade stays a deliberate, shady choice rather
        /// than something every station offers.
        /// </summary>
        public bool TradesPeople;
        /// <summary>
        /// Will the police attack those on sight
        /// </summary>
        public bool IsOutlaw;
        /// <summary>
        /// When randomly choosing factions for something lie a raid,
        /// this will be used as weight
        /// </summary>
        public float Rarity;
        public float Friendliness;
        /// <summary>
        /// Share of this faction's members that are synthetic (0-1). This alone decides how
        /// many of its raiders, derelict crews and gifted beings are bots vs humans; *which*
        /// bots is a function of raid strength, not of the faction (see RaiderSpecies). The
        /// old per-faction RaidBeings/RaidBeingChances lists were removed for that reason.
        /// </summary>
        public float AI;
        /// <summary>
        /// Chance that a bot raider of this faction comes as a human body run by that bot's
        /// mind instead, from raid gear tier 1 up. The human body carries the heavy gear bots
        /// can't (InventoryItemDef.RequiresBodyFlags), the bot mind still fights without
        /// tactics (CombatUnit.IsTactical). Gives all-bot factions a way to field it.
        /// </summary>
        public float PilotedHumanShare;
        /// <summary>
        /// Whether this faction can be rolled as a raider at all: an actual, non-player faction
        /// whose combat objective is to raid (Player defends, AntiPlayer/Crawler survive).
        /// </summary>
        public bool CanRaid => !IsInternal && !IsPlayer
            && CombatObjective == CombatObjectiveId.Raid;

        public override string ToString() {
            return $"Faction[{Id}]";
        }

        /// <summary>
        /// Chance to raid the player's ship after defeating it in combat
        /// </summary>
        public float VictoryRaidChance;
    }
}
