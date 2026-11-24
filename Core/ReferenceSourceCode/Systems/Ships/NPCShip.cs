#if !PRODUCTION_BUILD
// #define DEBUG_SHIPS
// #define DEBUG_NOTICE
// #define DEBUG_BEHAVIOR
// #define SHIP_TO_STRING_LONG
// #define DEBUG_COMBAT_STR
#endif
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Input;
using Game.Rendering;
using Game.Systems.Diplomacy;
using Game.Systems.Quests;
using Game.Systems.Space;
using Game.Systems.Trade;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class NPCShip : IQuestProvider {
        public uint Id;
        public string Name;
        private string nameWithTag;
        public string NameWithTag {
            get {
                if (nameWithTag != null) { return nameWithTag; }
                if (IsPolice) {
                    nameWithTag = $"{GlyphMap.SpritePoliceBadge} {Texts.Accented(Tag.TagT)} {Name}";
                } else if (IsOutlaw) {
                    nameWithTag = $"{GlyphMap.SpriteWarning} {Texts.Accented(Tag.TagT)} {Name}";
                } else {
                    nameWithTag = $"{Texts.Accented(Tag.TagT)} {Name}";
                }
                return nameWithTag;
            }
        }
        private string nameFull;
        private string nameLinked;
        [SkipDumpInstance]
        public string NameFull {
            get {
                if (nameFull != null) { return nameFull; }
                nameFull = $"{Faction.Glyph} {Class.NameT} {NameWithTag}";
                return nameFull;
            }
        }
        [SkipDumpInstance]
        public string NameLinked {
            get {
                if (nameLinked != null) { return nameLinked; }
                nameLinked = Texts.LinkToShip(this);
                return nameLinked;
            }
        }
        [SkipDumpInstance]
        public string StarmapLink => NameLinked;
        private ShipClassDef _class;
        public ShipClassDef Class {
            get => _class;
            set {
                _class = value;
                CombatState?.Flux?.UpdateCapacity(CombatState);
                ResetNameCache();
            }
        }

        private ShipTagDef tag;
        public ShipTagDef Tag {
            get => tag;
            set {
                tag = value;
                ResetNameCache();
            }
        }
        /// <summary>
        /// Energy the ship produces in KW, for class
        /// </summary>
        public int EnergyProduction;
        public Vector2 SectorLocation => CurLocalPosition;
        public FactionDef Faction { get; set; }
        public NPCShipMemory Memory;
        public ShipStateFlags State;
        public ShipCombatState CombatState;
        public ShipHailActionUI HailUI;
        public QuestProviderType ProviderType => QuestProviderType.Ship;
        public uint ProviderId => Id;
        public bool IsInCombat => (State & ShipStateFlags.InCombat) == ShipStateFlags.InCombat;
        public bool IsDerelict => (State & ShipStateFlags.Derelict) == ShipStateFlags.Derelict;
        /// <summary>
        /// Inactive ships have to be derelicts
        /// </summary>
        public bool IsActive => (State & ShipStateFlags.Active) == ShipStateFlags.Active;

        /// <param name="npc">observer</param>
        /// <param name="target">Can be null if we are checking if NPC is able to notice anyone at all</param>
        public static bool CanNPCNotice(NPCShip npc, NPCShip target) {
            if (npc.IsPlayer) {
                D.Err("Player's ship trying to notice someone! {0}", target);
                return false;
            }

            if (npc == target) { return false; }
            if (npc.IsInHyperspace) { return false; }
            if (target?.IsInHyperspace == true) { return false; }
            if (npc.SO == null || target?.SO == null) { return false; }

            if (npc.BehaviorSector?.CanInterruptForNotice(npc, target) == false) {
                #if DEBUG_NOTICE
                if (target?.IsPlayer == true) {
                    D.Warn("NPC cannot notice player because behavior: {0} -> {1}",
                        npc, npc.BehaviorSector);
                }
                #endif
                return false;
            }

            if ((npc.State & (ShipStateFlags.Active | ShipStateFlags.Derelict
                    | ShipStateFlags.InCombat)) != ShipStateFlags.Active) {
                #if DEBUG_NOTICE
                if (target?.IsPlayer == true) {
                    D.Warn("NPC cannot notice player due to npc state: {0} -> {1}",
                        npc, npc.State);
                }
                #endif
                return false;
            }
            if (target != null &&
                    (target.State
                        & (ShipStateFlags.Active | ShipStateFlags.Derelict))
                        != ShipStateFlags.Active) {
                #if DEBUG_NOTICE
                if (target?.IsPlayer == true) {
                    D.Warn("NPC cannot notice player due to player state: {0} -> {1}",
                        npc, target.State);
                }
                #endif
                return false;
            }

            // space stations won't notice anyone
            if (npc.Class.IsSpaceStation) { return false; }
            if (npc.Faction == null || target.Faction == null) {
                D.Err("Either NPC or Target ship has no faction in CanNPCNotice: {0} | {1}", npc, target);
                return false;
            }
            return true;
        }
        /// <summary>
        /// The sector where the ship is currently at
        /// </summary>
        public SpaceSector Sector;
        public SpaceObject ParentSector => Sector?.SO;
        public SpaceObject ProviderSO => SO;
        public long NextRegionTurnTick;
        public long NextNoticeNearbyTick;
        public NPCShipBehavior BehaviorSector;
        public NPCShipBehavior BehaviorRegion;
        /// <summary>
        /// The SpaceObject that represents this ship
        /// </summary>
        public SpaceObject SO;
        public Vector2 NextLocalPosition;
        public Vector2 CurLocalPosition;
        // Whether SO.Location is a real position inside the current sector. A ship
        // spawned into an unloaded sector has none yet, and (0,0) is both the default
        // and a valid slot (a trade zone center station), so position alone can't tell
        public bool IsPlacedInSector;
        public Vector2 LastKnownPosition {
            get {
                if (CurLocalPosition.x != 0f && CurLocalPosition.y != 0f) {
                    return CurLocalPosition;
                }
                if (SO != null) {
                    return SO.Location;
                }
                return CurLocalPosition;
            }
        }
        public Vector2 LocalTarget;
        public ShipView View;
        public float AttitudeTowardsPlayer {
            get {
                if (Faction.IsPlayer) {
                    D.Err("Asking attitude towards ourselves!");
                    return 0f;
                }
                return SO.S.Query.FactionAttitude.Ask(Faction, FactionDef.Player);
            }
        }
        public bool WillTrade;
        public bool HasLocalTarget;
        public float LocalVelocity;
        public float WantedVelocity;
        public string LooksTypeId => Class.LooksFor(this);
        public bool WantsHyperspace { get; private set; }
        public bool IsInHyperspace;
        public int Size { get; private set; }
        // Combat view width: sqrt(Size) times the class ViewScale. Visual only, it is not
        // sqrt(Size), so gameplay code should read Size instead.
        public float Width;
        /// <summary>
        /// If this NPC ship is providing a quest to the player, the quest id
        /// will be set here
        /// </summary>
        public uint QuestId { get; set; }
        public bool IsQuestGiver;
        /// <summary>
        /// Spawned by a story event or a quest (attacker, duelist, kill target). Such ships
        /// must never offer quests of their own, no matter how long they survive.
        /// </summary>
        public bool NoQuests;
        public long NextQuestAvailableAt;
        public int NumShortRangeDrivesCharged;
        public int NumShortRangeDrives;
        public int NumLongRangeDrivesCharged;
        public int NumLongRangeDrives;
        public int NumThrusters;
        public int NumThrustersActual {
            get {
                if (CombatState != null) {
                    return CombatState.NumThrustersOutsideCombat;
                } else {
                    return NumThrusters;
                }
            }
        }
        public int NumHyperdrivesActual {
            get {
                if (CombatState != null) {
                    return CombatState.NumHyperdrivesOutsideCombat;
                } else {
                    return NumLongRangeDrives + NumShortRangeDrives;
                }
            }
        }
        public int NumFusionCells;
        public int NumHeavyFusionCells;
        public bool IsPlayer => Id == 1;
        private bool isMoving;
        public bool IsPolice;

        // template variables
        public int WantedWeaponClassPoints;
        public int WantedClassPoints;
        /// <summary>
        /// Target weapon combat strength for player-scaled spawns (0 = use WantedWeaponClassPoints / CP budget).
        /// </summary>
        public float WantedWeaponCombatStrength;
        /// <summary>
        /// Maximum weapon tier this ship may equip (0 = unconstrained by template).
        /// </summary>
        public int WantedWeaponsTier;

        public const string Key_Long_WeaponsHackedUntil = "weapons.hacked.until";
        /// <summary>
        /// -1 means "not resolved from Memory.Custom yet", which is what makes the
        /// lockout survive a save/load without NPCShip needing its own save hook.
        /// </summary>
        private long weaponsHackedUntil = -1L;
        /// <summary>
        /// Weapons knocked offline by a player Intrusion Suite hack. Read by
        /// <see cref="ShipWeapon.Tick"/>, so it must stay allocation free.
        /// </summary>
        public bool AreWeaponsHacked {
            get {
                if (weaponsHackedUntil < 0L) {
                    if (Memory == null) {
                        weaponsHackedUntil = 0L;
                        return false;
                    }
                    weaponsHackedUntil = Memory.TryCustom(Key_Long_WeaponsHackedUntil,
                        out long until) ? until : 0L;
                }
                return weaponsHackedUntil > G.Ticks;
            }
        }

        public void SetWeaponsHackedUntil(long tick) {
            weaponsHackedUntil = tick;
            Memory.Custom.SetLong(Key_Long_WeaponsHackedUntil, tick);
        }

        public bool IsMoving {
            set {
                if (isMoving != value) {
                    // D.Warn("Moving ship; {0}->{1} | {2}", isMoving, value, this);
                    isMoving = value;
                }
            }
            get => isMoving;
        }

        public float CombatStrength {
            get {
                float str = 0f;
                // AddToStrength(ref str, Class.WeaponsTier, 10f, "Weapons tier");
                // if (IsPlayer) {
                //     AddToStrength(ref str, CombatState.Ship.Size, 0.1f, "MaxHP");
                // } else {
                //     AddToStrength(ref str, CombatState.MaxHP, 0.1f, "MaxHP");
                // }
                // AddToStrength(ref str, CombatState.WeaponPower, 3f, "WeaponPower");
                // var sc = CombatState.Shields.Count;
                // if (sc < 1) {
                //     AddToStrength(ref str, -10f, 1f, "No Shield");
                // } else {
                //     AddToStrength(ref str, sc, 1f, "NumShields");
                // }
                foreach (var w in CombatState.Weapons) {
                    AddToStrength(ref str, w.WeaponDef.CombatStrength, 1f, w.WeaponDef.Def.NameT);
                }
                // AddToStrength(ref str, CombatState.EvasionPercent, 3f, "Evade%");
                if (str < 1f) {
                    str = 1f;
                }
                D.Warn("Final combat strength: {0} => {1}", NameFull, str);
                return str;
            }
        }

        private void AddToStrength(ref float str, float amt, float mul, string note) {
            #if DEBUG_COMBAT_STR
            D.Warn("CombatStrength: +{0}*{1}={2}: {3} ({4}) | {5}",
                amt, mul, amt * mul, note, str + amt * mul, Id);
            #endif
            str += amt * mul;
        }

        public int PlayerHailTimeoutTicks {
            get {
                var attitude = AttitudeTowardsPlayer;
                Curve attCurve = Tunables.NPCShipHailTimeoutHours_AttitudeChance;
                var hours = attCurve.Evaluate(attitude);
                return Mathf.RoundToInt(Rng.URange(0.9f, 1.1f)
                    * hours * Consts.TicksPerHour);
            }
        }

        public void SetWantsHyperspace(bool yes) {
            if (yes && IsQuestGiver) {
                if (A.S.Query.IsActiveQuestProvider.Ask(this)) {
                    D.Err("NPCShip with active quest wants to hyperjump! {0}", this);
                    return;
                }
            }
            WantsHyperspace = yes;
        }

        public void LoadWantsHyperspace(bool yes) {
            WantsHyperspace = yes;
        }

        public float FearOf(NPCShip ship) {
            var strDiff = StrengthDifferenceAgainst(ship);
            Curve curve = Tunables.NPCShipPowerDifferenceFearFactor;
            return curve.Evaluate(strDiff);
        }

        public float StrengthDifferenceAgainst(NPCShip other) {
            var ourStr = CombatStrength;
            var theirStr = other.CombatStrength;
            if (ourStr > theirStr) {
                return 1f - theirStr / ourStr;
            } else {
                return ourStr / theirStr - 1f;
            }
        }

        public string FullTooltip(bool compareWithPlayer) {
            if (IsDerelict) { return NameWithTag; }
            var sb = Caches.StringBuilder2.Clear();
            sb.Append(Texts.SubTitle(NameWithTag));
            // sb.AppendFormat(GlyphMap.BRHR);
            if (IsPolice) {
                sb.AppendFormat("{0} {1}\n", GlyphMap.SpritePoliceBadge, Texts.Blue(T.NounLawEnforcementOfficer));
            } else if (IsOutlaw) {
                sb.AppendFormat("{0} {1}", GlyphMap.SpriteWarning, Texts.Red(T.NounOutlaw));
                if (!IsClassValid) {
                    sb.AppendFormat(" ({0})\n", Texts.Grey(T.ShipViolatesClassLimit));
                } else {
                    sb.AppendLine();
                }
            }
            if (IsInCombat) {
                sb.AppendFormat("{0} {1}\n", GlyphMap.SpriteCombat, Texts.Red(T.StateInCombat));
            }
            if (compareWithPlayer && !IsPlayer) {
                var att = SO.S.Query.FactionAttitude.Ask(Faction, FactionDef.Player);
                sb.AppendFormat("{0}: {1} ({2})\n", T.NounFaction, Texts.Accented(Faction.NameT),
                    Units.PercentageSigned(att, 1, true));
            } else {
                sb.AppendFormat("{0}: {1}\n", T.NounFaction, Texts.Accented(Faction.NameT));
            }
            sb.AppendFormat("{0}: {1}\n", T.NounShipClass, Texts.Accented(Class.NameT));
            sb.AppendFormat("{0}: {1} ({2})\n", T.NounShipTag, Texts.Accented(Tag.TagT), Tag.DescT);
            if (!IsPlayer) {
                sb.AppendFormat("{0}: {1}\n", T.NounHullPoints, Texts.Accented(
                    Units.XOutOfY((int) CombatState.HP, CombatState.MaxHP)));
            }
            var cp = CombatState.CurrentClassPoints;
            var theirStr = CombatStrength;
            if (compareWithPlayer && !IsPlayer)  {
                var ps = A.Player.Ship;
                var ourCP = ps.CombatState.CurrentClassPoints;
                string ourCPText;
                if (ourCP < cp * 0.9f) {
                    ourCPText = Texts.Red(ourCP);
                } else if (ps.CombatState.CurrentClassPoints > cp * 1.2f) {
                    ourCPText = Texts.Green(ourCP);
                } else {
                    ourCPText = Texts.Yellow(ourCP);
                }
                sb.AppendFormat("{0}: {1} ({2}: {3})\n", T.NounClassPoints,
                    Texts.Accented(Units.XOutOfY(cp, Class.MaxClassPoints)),
                    T.OurShip, ourCPText);
                var ourStr = ps.CombatStrength;
                string ourStrText;
                if (ourStr < theirStr * 0.9f) {
                    ourStrText = Texts.Red(Mathf.RoundToInt(ourStr));
                } else if (ourStr > theirStr * 1.2f) {
                    ourStrText = Texts.Green(Mathf.RoundToInt(ourStr));
                } else {
                    ourStrText = Texts.Yellow(Mathf.RoundToInt(ourStr));
                }
                sb.AppendFormat("{0}: {1} ({2}: {3})\n", T.NounStrength,
                    Texts.Accented(theirStr), T.OurShip, ourStrText);
            } else {
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints,
                    Texts.Accented(Units.XOutOfY(cp, Class.MaxClassPoints)));
                sb.AppendFormat("{0}: {1}\n", T.NounStrength,
                    Texts.Accented(theirStr));
            }
            sb.AppendFormat("{0}: {1}\n", T.Size, Texts.Accented(Size));
            sb.AppendFormat("{0}: {1}\n", T.EnergyProduction, Texts.Accented(Units.Electricity(EnergyProduction)));
            bool showDebug = A.InstantBuild;
            #if DEBUG_SHIPS
            showDebug = true;
            #endif
            if (showDebug) {
                sb.Append('\n');
                sb.Append(Texts.SubTitle("Debug"));
                sb.AppendFormat("Ship ID: {0}\n", Id);
                if (!IsPlayer && Memory != null) {
                    sb.AppendFormat("Sector Behavior: {0}\n", BehaviorSector?.Id ?? "None");
                    sb.AppendFormat("Region Behavior: {0}\n", BehaviorRegion?.Id ?? "None");
                    sb.AppendFormat("Weapon Strategy: {0}\n", CombatState.StrategyWeapon?.Id);
                    sb.AppendFormat("Flux Strategy: {0}\n", CombatState.StrategyFlux?.Id);
                    sb.AppendFormat("Target Strategy: {0}\n", CombatState.StrategyTarget?.Id);
                    sb.AppendFormat("Attacking: {0}\n", CombatState.FocusedEnemy?.NameWithTag);
                    if (CombatState.NextTargetSwitchTick > 0) {
                        sb.AppendFormat("Next target switch at: {0}\n",
                            Units.TicksToTime(CombatState.NextTargetSwitchTick - G.Ticks));
                    }
                    if (CombatState.NextNPCFluxBlunderAt > 0) {
                        sb.AppendFormat("Next blunder at: {0}\n", Units.TicksToTime(CombatState.NextNPCFluxBlunderAt - G.Ticks));
                    }
                    if (IsQuestGiver) {
                        sb.AppendFormat("Quest Giver: Next quest available at: {0}\n",
                            Units.TicksToTime(NextQuestAvailableAt));
                    } else {
                        sb.AppendLine("Not a quest giver");
                    }
                    sb.AppendFormat("Num Drives: {0}|{1}|{2}\n", NumThrusters,
                        NumShortRangeDrives, NumLongRangeDrives);
                    sb.AppendFormat("Target: {0}\n", Memory.FollowTarget);
                    if (BehaviorSector is NPCShipBehavior_Sector_ExchangeGift) {
                        sb.AppendFormat("Gift Mode: {0}\n", (GiftMode) Memory.Custom.GetInt(
                            NPCShipBehavior_Sector_ExchangeGift.Key_Int_GiftMode));
                        sb.AppendFormat("Gift Dir: {0}\n", (GiftDirection) Memory.Custom.GetInt(
                            NPCShipBehavior_Sector_ExchangeGift.Key_Int_GiftDirection));
                    }
                }
                // sb.AppendFormat("Devices: {0}\n", D.DumpCollection(CombatState.Devices));
            }
            // sb.AppendFormat("Combat State: {0}\n", D.DumpInstance(CombatState));
            return sb.ToString();
        }

        /// <summary>
        /// Whether this ship is currently fighting on the player's side
        /// </summary>
        public bool IsCombatAlly => SO.S.Query.GetAllyShips.Ask().Contains(this);

        /// <summary>
        /// Colored Ally / Enemy tag for log lines about ships in the player's combat
        /// </summary>
        public string CombatSideLabel => IsCombatAlly
            ? Texts.Green(T.NounAlly) : Texts.Red(T.NounEnemy);

        public bool IsClassValid {
            get {
                if (CombatState == null) { return true; }
                return CombatState.CurrentClassPoints <= Class.MaxClassPoints;
            }
        }
        public bool IsOutlaw => Faction.IsOutlaw || !IsClassValid;


        // Copy of Trader.WillTradeWith
        public bool WillTradeWith(NPCShip ship, out TradeRefuseReason reason) {
            reason = TradeRefuseReason.None;
            var isShipLegal = ship.IsClassValid;
            if (isShipLegal) { return true; }
            var chance = Faction.ChanceTradeWithOutlaw;
            if (chance == 1f) { return true; }
            reason = TradeRefuseReason.Outlaw;
            if (chance == 0f) { return false; }
            return ship.SeededRng(Primes.Prime028).Chance(chance);
        }

        public int Seed => (int) Id;

        public Rng SeededRng(int prime) {
            return new Rng(Hashes.Quick(Seed, prime));
        }

        public float AttitudeTowards(FactionDef faction) {
            return SO.S.Query.FactionAttitude.Ask(Faction, faction);
        }
        public float ChangeAttitudeTowards(FactionDef faction, float amt) {
            return SO.S.Query.ChangeFactionAttitude.Ask(Faction, faction, amt);
        }

        public bool AppendTooltip(StringBuilder sb, bool forList) {
            if (forList) {
                sb.Append(" - ");
            }
            if (Id == 1) {
                // player's ship
                sb.AppendFormat("{0}: {1} {2} ({3})", Class.NameT, Texts.Accented(Tag.TagT, false), Name,
                    Texts.Grey(T.OurShip));
            } else {
                if (IsPolice) {
                    sb.AppendFormat("[{0}] {1}: {2} {3} {4}", Texts.Grey(Faction.NameT),
                        Class.NameT, GlyphMap.SpritePoliceBadge, Texts.Accented(Tag.TagT, false), Name);
                } else if (IsOutlaw) {
                    sb.AppendFormat("[{0}] {1}: {2} {3} {4}", Texts.Grey(Faction.NameT),
                        Class.NameT, GlyphMap.SpriteWarning, Texts.Accented(Tag.TagT, false), Name);
                } else {
                    sb.AppendFormat("[{0}] {1}: {2} {3}", Texts.Grey(Faction.NameT),
                        Class.NameT, Texts.Accented(Tag.TagT, false), Name);
                }
                if (IsDerelict) {
                    sb.AppendFormat(" ({0})", Texts.Grey(T.NounDerelict));
                }
                if (IsInHyperspace) {
                    sb.AppendFormat(" ({0})", Texts.Accented2(T.InHyperspace));
                } else if (WantsHyperspace) {
                    // we will jump next turn
                    sb.AppendFormat(" ({0})", Texts.Accented2(T.PreparingForHyperjump));
                }
                if (IsInHyperspace || WantsHyperspace) {
                    if (Memory.SectorPath?.TryPeek(out var next) == true) {
                        sb.AppendFormat(" {0} {1}", Texts.Accented(GlyphMap.ArrowRight),
                            next.SO.ParentPath());
                    }
                }
            }
            if (forList) { return true; }
            if (IsPolice) {
                sb.AppendFormat("\n{0} {1}", GlyphMap.SpritePoliceBadge,
                    Texts.Blue(T.NounLawEnforcementOfficer));
            } else if (IsOutlaw) {
                sb.AppendFormat("\n{0} {1}", GlyphMap.SpriteWarning, Texts.Red(T.NounOutlaw));
                if (!IsClassValid) {
                    sb.AppendFormat(" ({0})\n", Texts.Grey(T.ShipViolatesClassLimit));
                }
            }
            if (IsInCombat) {
                sb.AppendFormat("\n{0} {1}", GlyphMap.SpriteCombat, Texts.Red(T.StateInCombat));
            }
            if (IsQuestGiver && !IsDerelict && G.Ticks > NextQuestAvailableAt && !A.S.Query.IsActiveQuestProvider.Ask(this)) {
                    sb.AppendFormat("\n{0} {1}", GlyphMap.SpriteQuestSide,
                        Texts.Accented2(T.MayHaveAQuest, bold: false));
            }
            return true;
        }

        public void EnterHyperspace() {
            if (Id == 1) {
                D.Err("Calling EnterHyperspace on player's NPCShip!");
                return;
            }
            if (IsDerelict) {
                D.Err("Derelicts wants to enter hyperspace! {0}", this);
                return;
            }
            if (IsInHyperspace) {
                D.Err("Already in hyperspace: {0}", this);
                return;
            }
            if (IsQuestGiver) {
                if (A.S.Query.IsActiveQuestProvider.Ask(this)) {
                    D.Err("NPCShip with active quest is trying to hyperjump! {0}",
                        this);
                    return;
                }
            }

            if (SO.Parent == A.Player.Parent) {
                var msg = $"{T.ShipLeftSector}: {NameLinked}";
                SO.S.Sig.ShowFanfare.Send(msg);
                SO.S.Sys.Log.AddLine(msg);
                // D.Warn("Entering Hyperspace: {0}", this);
                PlayOutboundSound();
                HailUI?.EndCall();
            }

            IsInHyperspace = true;
            HasLocalTarget = false;
            SO.S.Sig.NPCShipEnteredSector.Send(this, Sector, false);
            LocalVelocity = 0f;

        }

        private const float HyperjumpSoundVolume = 0.2f;

        private void PlayOutboundSound() {
            var sound = "FX/HyperjumpNPCOutFX";
            if (UIShowing.Starmap) {
                PlaySoundInStarmap(sound);
            } else if (SO.Visual != null) {
                PlayShipSound(sound);
            }
        }

        private void PlayShipSound(string sound) {
            var starfieldPos = SO.Visual.transform.position;
            var pos = RenderUtils.CameraToCameraPosition(
                starfieldPos,
                RenderUtils.StarfieldCamera, RenderUtils.Camera);
            // audio listener
            pos = Vectors.SetZ(pos, RenderUtils.AudioListenerZ);
            // D.Warn("Sound: {0} @ {1}", sound, pos);
            Sounds.PlayLoud(pos, sound, false, volume: HyperjumpSoundVolume);
        }

        public void ExitHyperspace() {
            ExitHyperspace(null);
        }
        public void ExitHyperspace(Vector2? localPos) {
            if (Id == 1) {
                D.Err("Calling ExitHyperspace on player's NPCShip!");
                return;
            }
            if (!IsInHyperspace) {
                D.Err("Already not in hyperspace: {0}", this);
                return;
            }
            IsInHyperspace = false;
            if (Sector.SO.Children == null) {
                return;
            }
            NextNoticeNearbyTick = 0;
            // D.Warn("Exiting Hyperspace: {0}", this);
            if (Sector == A.Player.Parent.Sector) {
                var msg = $"{T.ShipEnteredSector}: {NameLinked}";
                SO.S.Sig.ShowFanfare.Send(msg);
                SO.S.Sys.Log.AddLine(msg);
            }

            SO.S.Sig.NPCShipEnteredSector.Send(this, Sector, true);
            PlaceInSector(localPos.HasValue ? localPos.Value :
                    SOUtils.RandomLocationOnExitRing(Sector.SO, Rng.Unseeded));

            if (Sector == A.Player.Parent.Sector) {
                SO.UI?.UpdatePosition();
                PlayInboundSound();
            }
        }

        private void PlayInboundSound() {
            // if (SO.Parent != A.Player.Parent) { return; }
            var sound = "FX/HyperjumpNPCInFX";
            if (UIShowing.Starmap) {
                PlaySoundInStarmap(sound);
            } else {
                SO.S.Clock.OnNextTick(() => {
                    if (SO.Visual != null) {
                        PlayShipSound(sound);
                    } else {
                        if (SO.Parent == A.Player.Parent) {
                            D.ErrSoft("No visual! {0}. Dist to player: {1}", this,
                                Vectors.Distance(SO.Location, A.Player.Location));
                            return;
                        }
                    }
                });
            }
        }

        public void PlaySoundInStarmap(string sound) {
            if (SO.UI == null) {
                // Can happen if player just entered and NPC just left
                // D.Err("No starmap UI object for ship: {0}", this);
                return;
            }
            var screenPos = SO.UI.Rekt.position;
            var pos = RenderUtils.Camera.ScreenToWorldPoint(screenPos);
            pos = Vectors.SetZ(pos, RenderUtils.AudioListenerZ);
            // D.Warn("UI sound playing at world pos: {0} | {1}", pos, screenPos);
            // we'll see the ship immediately, so let's play a UI sound
            Sounds.PlayLoud(pos, sound, false, volume: HyperjumpSoundVolume);
        }

        public void FlyInLocalSpaceTo(Vector2 target) {
            if (Id == 1) {
                D.Err("Trying to fly in local space for player's NPC Ship {0}", target);
                return;
            }
            LocalTarget = target;
            LocalVelocity = 0f;
            CurLocalPosition = NextLocalPosition = SO.Location;
            HasLocalTarget = true;
        }

        public void HyperjumpTo(SpaceSector sector) {
            if (IsQuestGiver) {
                if (A.S.Query.IsActiveQuestProvider.Ask(this)) {
                    D.Err("NPCShip with active quest is hyperjumping! {0}",
                        this);
                    return;
                }
            }
            sector.SO.S.Query.NPCShipJumpTo.Ask(this, sector);
        }

        public void TurnIntoDerelict(NPCShip attacker) {
            TurnIntoDerelict(attacker, false);
        }

        // crewAbandoned: the crew walked off instead of being killed, so there is no
        // "destroyed" fanfare and no crew remnants aboard
        public void TurnIntoDerelict(NPCShip attacker, bool crewAbandoned) {
            if (IsInHyperspace) {
                D.ErrSoft("Turning ship into derelict while in hyperspace! {0}. Attacker: {1}",
                    this, attacker);
                ExitHyperspace();
            }
            if (IsDerelict) {
                D.Err("Ship is already derelict: {0}", this);
            } else if (attacker != null && !crewAbandoned && SO.Parent == A.Player.Parent) {
                var msg = $"{T.ShipDestroyed}: {NameLinked}";
                SO.S.Sig.ShowFanfare.Send(msg);
                SO.S.Sys.Log.AddLine(msg);
            }
            HailUI?.EndCall();
            StopMoving(true);
            State &= ~ShipStateFlags.Active;
            State |= ShipStateFlags.Derelict;
            if (crewAbandoned) {
                State |= ShipStateFlags.CrewAbandoned;
            }
            SO.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.ShipDerelict));
            SO.ResetLooks();
            SO.UI?.UpdateLooks();
            SO.Visual?.UpdateLooks();
            SO.UpdateName(null);
            SetBehavior(null, NPCShipBehaviorScope.Sector, true);
            SetBehavior(null, NPCShipBehaviorScope.Region, true);
            SO.S.Sig.ShipTurnedDerelict.Send(this);
            if (attacker != null) {
                State |= ShipStateFlags.SpawnDevices;
                if (attacker.IsPlayer) {
                    State |= ShipStateFlags.DefeatedByPlayer;
                }
                SO.S.Sig.ShipDefeatedInCombat.Send(attacker, this);
                // Only avenge a kill from a fight the player actually picked - a player
                // that merely defended against an unprovoked NPC attack and finished it
                // off must not get ganged up on for that.
                if (attacker.IsPlayer && SO.S.Query.IsShipCombatPlayerInitiated.Ask()) {
                    ShipHailAction_Insult.SectorShipsTryRetaliateFor(this, true);
                }
            }
        }

        private void UpdateVelocity() {
            if (!HasLocalTarget) { return; }
            if (NumThrusters > 0) {
                // Same units as the player's ship: push times its per-tick speed multiplier
                WantedVelocity = Equations.LocalSpacePush(
                    NumThrusters * Tunables.NPCShipThrusterPower, Size)
                    * Tunables.SpaceTravelBaseSpeed * Tunables.SpaceTravelFlightSpeed;
                // D.Warn("Velocity: {0}", LocalVelocity);
            } else {
                WantedVelocity = 0f;
                if (HasLocalTarget) {
                    D.Err("Cannot go to local target, no thrusters! {0}", this);
                    HasLocalTarget = false;
                    IsMoving = false;
                }
            }

        }

        private void UpdateLocalPosition() {
            if (!HasLocalTarget) { return; }
            // D.Warn("Moving ship: {0} -> {1}", SO.Location, this);
            LocalVelocity = Mathf.MoveTowards(LocalVelocity, WantedVelocity, Tunables.SpaceTravelAcceleration);
            var nextBefore = NextLocalPosition;
            var nextAfter = Vector2.MoveTowards(nextBefore, LocalTarget, LocalVelocity);
            NextLocalPosition = nextAfter;
            CurLocalPosition = nextBefore;
            IsMoving = nextAfter == nextBefore;
            // D.Warn("Tick: {0}. {1} -> {2} (vel: {3}. target: {4})", G.Ticks, CurLocalPosition, NextLocalPosition, LocalVelocity, LocalTarget);
        }

        public Vector2 GetFuturePosition(int afterTicks) {
            if (!HasLocalTarget) {
                return CurLocalPosition;
            }
            return Vector2.MoveTowards(
                CurLocalPosition, LocalTarget, LocalVelocity * afterTicks);
        }

        public void OnTickProgress(float progress) {
            if (!HasLocalTarget || IsDerelict) { return; }
            // D.Warn("OnTP: {0}->{1}: {2}", CurLocalPosition, NextLocalPosition, Units.PercentageWithFraction4(progress));
            var nextPos = Vector2.Lerp(CurLocalPosition, NextLocalPosition, progress);
            #if !PRODUCTION_BUILD
            if (float.IsNaN(nextPos.x)) {
                D.Err("NPCShip moving to invalid pos. CurLocalPos: {0}, NextLocalPos: {1}, Prog: {2}",
                    CurLocalPosition, NextLocalPosition, progress);
            }
            #endif
            SO.Location = nextPos;
            if (SO.Location == LocalTarget) {
                HasLocalTarget = false;
                IsMoving = false;
            }
        }

        public void OnTick(long ticks) {
            if (Id == 1) { return; }
            if (IsDerelict && IsInHyperspace) {
                D.Err("Derelict is in hyperspace, removing: {0}", this);
                ExitHyperspace();
                return;
            }
            if (!IsInCombat) {
                CombatState.RepairHull();
                CombatState.RepairDevices(ticks);
            }
            TickTurnRegion(ticks);
            if (IsInHyperspace) { return; }
            // not spawned in local space
            if (SO.Parent == null) { return; }
            var cullSimulation = SO.Parent != A.Player.Parent
                    && SO.Parent != A.HyperspaceLocation;
            if (cullSimulation) {
                if (BehaviorSector != null) {
                    SetBehavior(null, NPCShipBehaviorScope.Sector, allowNull: true);
                }
                return;
            } else {
                NoticeNearby(ticks);
            }
            View?.Tick();
            BehaviorSector?.DoTurn(this);
            // Move in local space
            UpdateVelocity();
            UpdateLocalPosition();
        }

        private void NoticeNearby(long ticks) {
            if (NextNoticeNearbyTick < ticks) {
                if (NextNoticeNearbyTick > 0) {
                    DoNoticeNearby();
                }
                NextNoticeNearbyTick = ticks + Mathf.RoundToInt(Rng.Unseeded.Range(
                    // don't remove cast!
                    (float) Tunables.ShipNoticeDelayHoursFrom,
                    (float) Tunables.ShipNoticeDelayHoursTo) * Consts.TicksPerHour);
            }
        }
        private void DoNoticeNearby() {
            if (!CanNPCNotice(this, null)) { return; }

            if (A.S.Sys.NPCShip.SectorShips.TryGetValue(Sector, out var ships)) {
                if (Arrays.Count(ships) < 2) { return; }
                var cache = Caches.NPCShips;
                cache.Clear();
                foreach (var ship in ships) {
                    if (CanNPCNotice(this, ship)) {
                        cache.Add(ship);
                    }
                }
                if (cache.Count == 0) { return; }
                var target = Rng.UFrom(cache);
                if (Notice(target, 1f)) {
                    #if DEBUG_NOTICE
                    D.Warn("Ship noticing another ship casually: {0}->{1}",
                        this, target);
                    #endif
                }
            }
        }

        public void TickTurnRegion(long ticks) {
            if (ticks < NextRegionTurnTick) {
                return;
            }
            if (Id == 1) { return; }
            if (IsDerelict) { return; }
            // D.Warn("Will do turn: {0} >= {1} for ship {2}", ticks, NextRegionTurnTick, this);
            if (NextRegionTurnTick == 0L) {
                NextRegionTurnTick = ticks + Mathf.RoundToInt(Rng.Unseeded.Range(0f,
                    Tunables.ShipTrafficTurnHours * Consts.TicksPerHour));
            } else {
                NextRegionTurnTick = ticks
                    + (int)(Tunables.ShipTrafficTurnHours * Rng.Unseeded.Range(0.7f, 1.3f))
                        * Consts.TicksPerHour;
                DoTurnRegion();
            }
        }

        public void DoTurnRegion() {
            if (Id == 1 || IsInCombat) { return; }
            if (IsDerelict) { return; }
            if (BehaviorSector?.CanHyperjump == false) { return; }
            // D.Warn("Ship doing region turn (next in {1}): {0}", this,
            //     Units.TicksToTime(NextRegionTurnTick - G.Ticks));
            if (BehaviorRegion != null) {
                BehaviorRegion.DoTurn(this);
            } else {
                D.Err("No BehaviorRegion on ship: {0}", this);
                NPCShipBehavior_Region_Idle.AssignTo(this);
            }
        }

        public NPCShipBehavior ChangeBehavior(NPCShipBehaviorScope scope, bool canRepeat) {
            return SO.S.Query.ChangeNPCShipBehavior.Ask(this, scope, canRepeat);
        }

        public void SetBehavior(NPCShipBehavior beh, NPCShipBehaviorScope scope, bool allowNull = false) {
            if (Id == 1) {
                D.Err("Trying to set a NPC ship behavior on player's ship! {0}", beh);
                return;
            }
            #if DEBUG_BEHAVIOR
            if (Sector.SO == A.PlayerSector) {
                if (scope == NPCShipBehaviorScope.Sector) {
                    D.Warn("Setting sector behavior {0}->{1} for {2}", BehaviorSector, beh, this);
                } else if (scope == NPCShipBehaviorScope.Region) {
                    D.Warn("Setting region behavior {0}->{1} for {2}", BehaviorRegion, beh, this);
                } else {
                    D.Err("Undefined behavior scope: {0} | {1} for {2}", scope, beh, this);
                }
            }
            #endif
            if (IsDerelict && beh != null) {
                D.Err("Trying to set a non null behavior on a derelict: {0} -> {1}. Derelict loc: {2}. Player loc: {3}",
                    this, beh, Sector.SO.ParentPath(false), A.PlayerSector.ParentPath(false));
                return;
            }
            // if (Class.IsSpaceStation && !(
            //         beh == null
            //         || beh is NPCShipBehavior_Region_Idle
            //         || beh is NPCShipBehavior_Sector_Idle
            //         || beh is NPCShipBehavior_Sector_Combat)) {
            //     D.Err("Trying to set a non idle/combat behavior on a station: {0} -> {1}", this, beh);
            //     return;
            // }
            if (beh == null && !allowNull) {
                D.Err("Trying to set a null behavior for {0}", scope);
                return;
            }
            // D.Warn("Assigning behavior {0} to ship {1}", beh, this);
            if (scope == NPCShipBehaviorScope.Sector) {
                BehaviorSector?.OnEnd(this);
                BehaviorSector = beh;
                beh?.OnStart(this);
            } else if (scope == NPCShipBehaviorScope.Region) {
                BehaviorRegion?.OnEnd(this);
                BehaviorRegion = beh;
                beh?.OnStart(this);
            } else {
                D.Err("Undefined behavior scope: {0}", beh);
            }
        }

        /// <returns>true if observer is going to interact with the ship</returns>
        public bool Notice(NPCShip ship, float ignoreChanceMul) {
            if (IsPlayer) { return false; }
            if (G.IsDemoMode) { return false; }
            if (ship == this || ship == null) { return false; }
            if (IsInHyperspace || ship.IsInHyperspace) {
                D.Err("Noticing when either ship is in hyperspace: {0} | {1}", this, ship);
                return false;
            }
            if (!CanNPCNotice(this, ship)) {
                return false;
            }
            // D.Warn("Ship noticed ship: {0}->{1}", this, ship);
            // D.Warn("ship noticed another ship: {0} -> {1}", this, ship);
            var s = SO.S;
            var attitude = s.Query.FactionAttitude.Ask(Faction, ship.Faction);

            var ignore = NPCShipReactionAssignmentDef.Ignore;
            if (ignore.HasChanceFor(this, ship, attitude, out var ignoreChance)) {
                if (s.Rng.Chance(ignoreChance * ignoreChanceMul)) {
                    #if DEBUG_NOTICE
                    D.Warn("Ship chose to ignore: {0} -> {1}", this, ship);
                    #endif
                    return false;
                }
            }

            var wr = Caches.WeightedNPCShipReactions;
            wr.Clear();

            var positiveReactionChance = Faction.AttitudePositiveReactionCurve
                .Evaluate(attitude);

            ShipReactionType type;
            if (s.Rng.Chance(positiveReactionChance)) {
                type = ShipReactionType.Positive;
            } else {
                type = ShipReactionType.Negative;
            }

            #if DEBUG_NOTICE
            D.Warn("Chosen reaction type {0} (ign ch: {4}*{5}) (standing: {1}) for {2} => {3}",
                type, attitude, this, ship, ignoreChance, ignoreChanceMul);
            #endif
            var shipIsPlayer = ship.IsPlayer;

            // build wr
            foreach (var reactAss in NPCShipReactionAssignmentDef.All.Values) {
                if (reactAss.Reaction.Type != type) { continue; }
                if (shipIsPlayer) {
                    var cooldown = reactAss.Reaction.N2PCooldownTicks;
                    if (cooldown > 0 && Memory.LastN2PInteractionTick + cooldown > G.Ticks) {
                        // D.Warn("Cooling down N2P interaction: {0}", reactAss.ReactionId);
                        continue;
                    }
                    var globalCooldown = reactAss.Reaction.GlobalN2PCooldownTicks;
                    if (globalCooldown > 0 && s.Sys.NPCShip
                            .IsReactionGlobalCoolingDown(reactAss.Reaction.IdH)) {
                        // D.Warn("Cooling down global N2P interaction: {0}", reactAss.ReactionId);
                        continue;
                    }
                }
                if (reactAss.HasChanceFor(this, ship, attitude, out var chance)) {
                    if (wr.TryGetValue(reactAss.Reaction, out var oldChance)) {
                        wr[reactAss.Reaction] = Mathf.Max(oldChance, chance);
                    } else {
                        wr[reactAss.Reaction] = chance;
                    }
                }
            }

            if (Rng.Unseeded.TryWeightedFrom(wr, out var reaction)) {
                #if DEBUG_NOTICE
                D.Warn("Chosen reaction: {0}: {1} -> {1}", reaction, this, ship);
                #endif
                var willInteract = reaction.ReactTo(this, ship);
                if (willInteract && shipIsPlayer) {
                    var globalCooldown = reaction.GlobalN2PCooldownTicks;
                    if (globalCooldown > 0) {
                        s.Sys.NPCShip.StampReactionGlobalCooldown(reaction.IdH, globalCooldown);
                    }
                }
                return willInteract;
            }
            // it's possible that no reaction was chosen from the list, then
            // ignore
            #if DEBUG_NOTICE
            D.Warn("Couldn't pick a {0} reaction for ship: {1} -> {2}", type,
                this, ship);
            #endif
            return false;
        }

        public void NoticeMany(List<NPCShip> ships) {
            if (IsPlayer) { return; }
            if (ships == null || ships.Count < 2) {
                #if !PRODUCTION_BUILD
                if (Arrays.Count(ships) == 1) {
                    if (ships[0] != this) {
                        D.Err("Assertion failed! ships list does not contain the ship: {0} | {1}",
                            ships[0], this);
                    }
                }
                #endif
                return;
            }
            if (!CanNPCNotice(this, null)) { return; }

            if (ships.Count < 2) {
                foreach (var ship in ships) {
                    if (ship == this) { continue; }
                    if (Notice(ship, 1f)) {
                        #if DEBUG_NOTICE
                        D.Warn("Ship noticed the only ship: {0} -> {1}", this, ship);
                        #endif
                        return;
                    }
                }
                return;
            }

            if (SO.Parent != A.Player.Parent) {
                // cull simulation - just notice a random ship from the list
                var ship = Rng.UFrom(ships);
                int attempts = 10;
                while (!CanNPCNotice(this, ship) && attempts-- > 0) {
                    ship = Rng.UFrom(ships);
                }
                if (Notice(ship, 1f)) {
                    #if DEBUG_NOTICE
                    D.Warn("Ship noticed with culled simulation ship: {0} -> {1}",
                        this, ship);
                    #endif
                }
                return;
            }

            // pick most threatening ship
            // if we are the most threatening ship, pick the closest one
            NPCShip target = null;
            float maxFear = 0f;
            for (int i = ships.Count - 1; i >= 0; i--) {
                NPCShip ship = ships[i];
                if (!CanNPCNotice(this, ship)) { continue; }
                var fear = FearOf(ship);
                if (fear > maxFear) {
                    target = ship;
                    maxFear = fear;
                }
            }
            if (target != null) {
                if (Notice(target, 1f)) {
                    #if DEBUG_NOTICE
                    D.Warn("Ship noticed most feared ship: {0} -> {1}", this, target);
                    #endif
                    return;
                }
            }
            float minDistSqr = float.MaxValue;
            for (int i = ships.Count - 1; i >= 0; i--) {
                NPCShip ship = ships[i];
                if (!CanNPCNotice(this, ship)) { continue; }
                var distSqr = Vector2.SqrMagnitude(ship.SO.Location - SO.Location);
                if (distSqr < minDistSqr) {
                    target = ship;
                    minDistSqr = distSqr;
                }
            }
            if (Notice(target, 1f)) {
                #if DEBUG_NOTICE
                D.Warn("Ship noticed closest ship: {0} -> {1}", this, target);
                #endif
            }
        }

        public void StopMoving(bool dropFollowTarget) {
            LocalTarget = CurLocalPosition = NextLocalPosition;
            if (dropFollowTarget) {
                Memory?.SetFollowTarget(null);
            }
        }

        public void UpdateSize(int size) {
            if (Size != size) {
                // D.Warn("Updating ship size: {0}->{1}: {2}", this, Size, size);
                Size = size;
                UpdateWidth();
                CombatState?.UpdateSize(this);
            }
        }
        public void LoadSize(int size) {
            Size = size;
            UpdateWidth();
        }

        private void UpdateWidth() {
            if (Size < 1) {
                Width = 1f;
                return;
            }
            Width = Mathf.Sqrt(Size) * (Class?.ViewScale ?? 1f);
        }

        public void TeleportToSectorLocation(Vector2 location) {
            D.Warn("Teleporting ship: {1} -> {2}: {0}", this, SO.Location, location);
            PlaceInSector(location);
        }

        // Same as TeleportToSectorLocation, without the log line
        public void PlaceInSector(Vector2 location) {
            CurLocalPosition = NextLocalPosition = location;
            SO.Location = location;
            IsPlacedInSector = true;
        }

        public void SetShootHesitation() {
            CombatState.TicksTillShoot = SO.S.Difficulty.ShootHesitationFor(this);
        }

        public bool CanBeHostileTowards(NPCShip ship) {
            if (ship == this) { return false; }
            var theirFear = ship.FearOf(this);
            var ourFear = FearOf(ship);
            // we're more afraid than them, so we won't be hostile
            if (ourFear >= theirFear) { return false; }
            if (IsPolice && ship.IsOutlaw) {
                return true;
            }
            return IsOutlaw;
        }

        public override string ToString() {
            #if SHIP_TO_STRING_LONG
            return $"NPCShip[{Id},{Tag?.Id},{Name},{Class?.Id},{Faction?.Id},{Size},S:{Sector?.Name},R:{Sector?.Region.Name},{State}|HS:{IsInHyperspace}|Quest:{IsQuestGiver}]";
            #else
            return $"NPCShip[{Id},{Tag?.Id},{Name},{Class?.Id}|{Faction?.Id},{State}|HS:{IsInHyperspace}|Cop:{IsPolice}|Quest:{IsQuestGiver}]";
            #endif
        }

        public Vector2 NearbyPosFrom(Vector2 pos, out bool isNear, float extraDist = 0f) {
            var size = SO.Looks(true)?.StarmapSizeMul ?? 1f;
            if (size < 1f) {
                size = 1f;
            }
            var res = Vectors.Towards(pos, SO.Location, Tunables.NPCShipFollowDist * size);
            isNear = Vectors.SqrDistance(res, pos) - extraDist < 0.1f;
            return res;
        }
        public void ResetNameCache() {
            nameFull = null;
            nameWithTag = null;
            nameLinked = null;
            SO?.ResetNameCache();
        }
        public string DebugInfo {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.AppendLine(NameFull);
                sb.AppendFormat("Hyperdrives: {0}", NumHyperdrivesActual);
                return sb.ToString();
            }
        }
    }
}
