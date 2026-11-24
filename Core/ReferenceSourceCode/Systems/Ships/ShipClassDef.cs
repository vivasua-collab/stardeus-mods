using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipClassDef {
        public static Dictionary<int, ShipClassDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            Unclassified = null;
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipClassDef Unclassified;
        public static ShipClassDef Fighter;
        /// <summary>
        /// To unlock this ship class, player will have to unlock the class
        /// defined in this variable first.
        /// </summary>
        public string RequiredClassId;
        public ShipClassDef RequiredClass;
        /// <summary>
        /// This is the price in starcreds player will have to pay to unlock
        /// this ship class.
        /// </summary>
        public int UnlockPrice;

        public static IOrderedEnumerable<ShipClassDef> AllOrdered =>
            All.Values.OrderBy(f => f.MaxClassPoints).ThenBy(f => f.MinSize);

        /// <summary>
        /// Chance to get this ship spawned in a sector with given difficulty
        /// </summary>
        public float[] OriginDifficultyChance;
        private Curve originDifficultyCurve;
        public Curve OriginDifficultyCurve => originDifficultyCurve ??= OriginDifficultyChance;
        /// <summary>
        /// How large this ship will typically be when it's an NPC ship.
        /// We can't use min-max size here, because it will end up in odd
        /// results for smallest and largest ships (Fighter and Titan)
        /// </summary>
        public float[] NPCSizeRange;
        private Curve npcSizeCurve;
        public Curve NPCSizeCurve => npcSizeCurve ??= NPCSizeRange;

        /// <summary>
        /// How wide is the ship visual from nose to tail
        /// </summary>
        public float[] ViewZoneWidth;
        private Curve viewZoneWidthCurve;
        public Curve ViewZoneWidthCurve => viewZoneWidthCurve ??= ViewZoneWidth;

        /// <summary>
        /// How much evasion this ship will have when there are 0 engines
        /// </summary>
        public float BaseEvasion;

        /// <summary>
        /// Distributed among all devices, multiplied by ship health
        /// </summary>
        public float NPCDeviceRepairHealthPerHour;

        /// <summary>
        /// What kinds of weapons this ship may have
        /// </summary>
        public int WeaponsTier;

        public bool IsSpaceStation;
        public bool IsUnclassified;
        // 0f to 1f, where 0f is Fighter, 1 is Titan.
        // The length of the derelict will be determined by this value.
        public float DerelictSizeMin;
        public float DerelictSizeMax;

        // Ship has length and width.
        // Width here is percentage of length.
        public float DerelictWidthMin;
        public float DerelictWidthMax;

        /// <summary>
        /// Fixed soft flux vent rate per in-game hour. Hard vent is a multiple of this.
        /// Larger ships vent slightly faster in absolute terms but still take longer
        /// because they accumulate more flux.
        /// </summary>
        public float FluxSoftVentPerHour;

        /// <summary>
        /// How common is for this ship type to spawn compared to other ships
        /// given all other things are equal.
        /// Should to zero to one.
        /// 0.0 = never, 1.0 = very common
        /// </summary>
        public float Rarity;
        public string Id;
        public int IdH;
        private string nameT;
        [SkipDumpInstance]
        public string NameT {
            get {
                if (nameT != null) { return nameT; }
                var key = $"ship.class.{Id.ToLowerInvariant()}";
                if (key.HasT()) {
                    nameT = key.T();
                } else {
                    // for some reason this happens in some tests, but it's ok
                    // D.Err("No Translation for key: {0}", key);
                    return key;
                }
                return nameT;
            }
        }
        /// <param name="ship">Ship we compare to this class(always our ship)</param>
        public string Tooltip(NPCShip ship) {
            var sb = Caches.StringBuilder2;
            sb.Clear();

            sb.AppendFormat("{0}: {1}\n", T.NounShipClass, Texts.Accented(NameT));

            sb.Append(GlyphMap.BRHR);
            var unlocked = A.S.Query.GetUnlockedShipClasses.Ask();
            if (unlocked.Contains(this)) {
                sb.AppendLine(Texts.Green(T.ResearchUnlocked));
            } else {
                sb.AppendLine(Texts.Red(T.ResearchLocked));
                if (RequiredClass != null) {
                    var mustOwnName = RequiredClass.NameT;
                    if (unlocked.Contains(RequiredClass)) {
                        mustOwnName = Texts.Green(mustOwnName);
                    } else {
                        mustOwnName = Texts.Red(mustOwnName);
                    }
                    var mustOwn = $"({T.MustOwn}: {mustOwnName})";
                    sb.AppendFormat("{0}: {1} {2}\n", T.NounLicenseFee,
                        Texts.Accented(Wallet.ToValueString(UnlockPrice)),
                        Texts.Grey(mustOwn));
                } else {
                    sb.AppendFormat("{0}: {1}\n", T.NounLicenseFee,
                        Texts.Accented(Wallet.ToValueString(UnlockPrice)));
                }
                var hasEnough = A.S.Wallet.Amount >= UnlockPrice;
                var walletStr = A.S.Wallet.ValueString;
                if (hasEnough) {
                    walletStr = Texts.Green(walletStr);
                } else {
                    walletStr = Texts.Red(walletStr);
                }
                sb.AppendFormat(Texts.Grey(" {0} {1}: {2}"), GlyphMap.ArrowRight, T.OurWallet,
                    walletStr);
            }

            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat("{0}:\n", Texts.Accented2(T.NounRequirements, false));

            if (IsSpaceStation) {
                string engineText;
                if (ship != null) {
                    if (ship.CombatState.Engines.Count > 0) {
                        engineText = Texts.Red(ship.CombatState.Engines.Count);
                    } else {
                        engineText = Texts.Green("0");
                    }
                } else {
                    engineText = Texts.Accented("0");
                }
                var title = T.NounEngines;
                if (ship == null) {
                    title = $"{T.NounEngines} ({Texts.Grey(T.Max)})";
                }
                sb.AppendFormat(" {2} {0}: {1}\n", title, engineText, GlyphMap.ArrowRight);
            }

            string text = string.Empty;
            if (MinSize > 0) {
                text = Texts.Accented(MinSize);
                text += Texts.Accented("+");
                if (ship != null) {
                    var size = ship.Size;
                    string ourSize;
                    if (MinSize > 0 && size < MinSize) {
                        ourSize = Texts.Red(size);
                    } else {
                        ourSize = Texts.Green(size);
                    }
                    ourSize = $"({T.OurShip}: {ourSize})";
                    ourSize = Texts.Grey(ourSize);
                    text = $"{text} {ourSize}";
                }
            }
            if (text.Length == 0) {
                text = Texts.Accented(T.PronounAny);
            }
            sb.AppendFormat(" {2} {0}: {1}\n", T.Size, text, GlyphMap.ArrowRight);

            string textEn;
            if (MinEnergy > 0) {
                textEn = Texts.Accented(Units.Electricity(MinEnergy));
            } else {
                textEn = Texts.Accented(T.PronounAny);
            }
            sb.AppendFormat(" {2} {0}: {1}", $"{T.EnergyProduction}",
                textEn, GlyphMap.ArrowRight);

            if (ship != null) {
                The.R.Query.UpdatePlayerShipEnergy.Ask();
                var nrg = ship.EnergyProduction;
                string ourEnTxt;
                if (nrg < MinEnergy) {
                    ourEnTxt = Texts.Red(Units.Electricity(nrg));
                } else {
                    ourEnTxt = Texts.Green(Units.Electricity(nrg));
                }
                ourEnTxt = $" ({T.OurShip}: {ourEnTxt})";
                sb.AppendLine(Texts.Grey(ourEnTxt));
            } else {
                sb.AppendLine();
            }

            sb.AppendFormat("{0}:\n", Texts.Accented2(T.NounStats, false));

            sb.AppendFormat(" {2} {0}: {1}", T.NounClassPoints,
                Texts.Accented(MaxClassPoints),
                GlyphMap.ArrowRight);
            if (ship != null) {
                var cp = ship.CombatState.CurrentClassPoints;
                var maxCP = MaxClassPoints;
                string textCP;
                if (cp < maxCP) {
                    textCP = Texts.Red(cp);
                } else {
                    textCP = Texts.Green(cp);
                }
                textCP = $" ({T.OurShip}: {textCP})";
                sb.AppendLine(Texts.Grey(textCP));
            } else {
                sb.AppendLine();
            }

            sb.AppendFormat(" {2} {0}: {1}", T.ShipClassBaseEvade,
                Texts.Accented(Units.Percentage(BaseEvasion)),
                GlyphMap.ArrowRight);
            if (ship != null) {
                var ourEvade = Units.Percentage(ship.Class.BaseEvasion);
                if (ship.Class.BaseEvasion < BaseEvasion) {
                    ourEvade = Texts.Red(ourEvade);
                } else {
                    ourEvade = Texts.Green(ourEvade);
                }
                ourEvade = $" ({T.OurShip}: {ourEvade})";
                sb.AppendLine(Texts.Grey(ourEvade));
            } else {
                sb.AppendLine();
            }

            sb.AppendFormat(" {2} {0}: {1}", T.NounFluxBonus,
                Texts.Accented(FluxBonus),
                GlyphMap.ArrowRight);
            if (ship != null) {
                var ourBonus = Units.Num(ship.Class.FluxBonus);
                if (ship.Class.FluxBonus < FluxBonus) {
                    ourBonus = Texts.Red(ourBonus);
                } else {
                    ourBonus = Texts.Green(ourBonus);
                }
                ourBonus = $" ({T.OurShip}: {ourBonus})";
                sb.AppendLine(Texts.Grey(ourBonus));
            } else {
                sb.AppendLine();
            }

            return sb.ToString();
        }
        private string[] graphicRot;
        private string[] graphicDerelictRot;
        private string LooksTypeId;
        private string LooksTypeDerelictId;
        public string Preview;
        /// <summary>
        /// Used for limiting the device loadouts. Each device costs certain
        /// amount of class points. Exceeding this limit for a ship will cause
        /// the police to come over and f you up for being a cheater.
        /// </summary>
        public int MaxClassPoints;
        public int MinShields;
        public int MaxShields;
        public int MaxPointDefense;
        /// <summary>
        /// -1 - engines curve inwards, +1 - outwards
        /// </summary>
        public int Visual_EngineCurvatureSign;
        public int Visual_EnginesPerRow;
        public float Visual_EngineDensity;
        public float Visual_DeviceMaxScale;
        public string Graphic(Facing.Type facing, bool derelict) {
            if (IsSpaceStation) {
                return GraphicsId.SpaceObjectsStationsSpaceStation01;
            }
            if (derelict) {
                if (graphicDerelictRot == null) {
                    graphicDerelictRot = new string[] {
                        $"SpaceObjects/Derelicts/Ship{Id}01_Derelict_D",
                        $"SpaceObjects/Derelicts/Ship{Id}01_Derelict_L",
                        $"SpaceObjects/Derelicts/Ship{Id}01_Derelict_U",
                        $"SpaceObjects/Derelicts/Ship{Id}01_Derelict_R",
                    };
                }
                return graphicDerelictRot[(int) facing];
            }
            if (graphicRot == null) {
                graphicRot = new string[] {
                    $"SpaceObjects/Ships/Ship{Id}01_D",
                    $"SpaceObjects/Ships/Ship{Id}01_L",
                    $"SpaceObjects/Ships/Ship{Id}01_U",
                    $"SpaceObjects/Ships/Ship{Id}01_R",
                };
            }
            return graphicRot[(int) facing];
        }
        /// <summary>
        /// Min size of player's ship in tiles to qualify for this class
        /// </summary>
        public int MinSize;

        /// <summary>
        /// Max size of the derelict of ships of this class.
        /// NOTE: Don't use this to validate player's ship size, because
        /// player can attach derelicts!
        /// </summary>
        public int MaxSize;

        /// <summary>
        /// Minimum energy production in KW to qualify for this ship class.
        /// Doesn't include batteries.
        /// </summary>
        public int MinEnergy;
        public int FluxBonus;
        /// <summary>
        /// Hard cap on total flux capacity. Extra flux capacitors beyond this
        /// limit will not add any benefit. 0 means no limit.
        /// </summary>
        public int MaxFluxCapacity;

        /// <summary>
        /// Max number of Flux Vent devices this class can benefit from. Vents beyond
        /// this count still take up class points but add no vent rate bonus.
        /// </summary>
        public int MaxFluxVents;

        /// <summary>
        /// Curve for number thrusters generated for this ship class
        /// </summary>
        public float[] NumThrustersChance;
        private Curve numThrustersCurve;
        public Curve NumThrustersCurve => numThrustersCurve ??= NumThrustersChance;
        /// <summary>
        /// Curve for number of short-range drives generated for this ship class
        /// </summary>
        public float[] NumShortRangeDrivesChance;
        private Curve numShortRangeDrivesCurve;
        public Curve NumShortRangeDrivesCurve => numShortRangeDrivesCurve ??= NumShortRangeDrivesChance;

        /// <summary>
        /// Curve for number of long-range drives generated for this ship class
        /// </summary>
        public float[] NumLongRangeDrivesChance;
        private Curve numLongRangeDrivesCurve;
        public Curve NumLongRangeDrivesCurve => numLongRangeDrivesCurve ??= NumLongRangeDrivesChance;

        /// <summary>
        /// How much the ship will hover in the game view mid combat.
        /// Small ships show have value close to 1, large ships should have it closer to 5
        /// </summary>
        public float CombatMotionHover;
        /// <summary>
        /// How much the ship will rotate in the game view mid combat.
        /// Small ships show have value close to 3, large ships should have it closer to 1
        /// </summary>
        public float CombatMotionRotation;
        /// <summary>
        /// How much the ship will hover and rotate in the game view mid combat.
        /// Small ships show have value close to 1, large ships should have it closer to 0.1
        /// </summary>
        public float CombatMotionSpeed;
        /// <summary>
        /// Once in a while the ship will consider sending a breach capsule mid combat.
        /// This chance tells you how likely this will happen.
        /// There is still timed delay, so this value can be up to 1.0
        /// </summary>
        public float CombatBreachCapsuleChance;
        /// <summary>
        /// How likely an NPC ship will have no shields at all
        /// </summary>
        public float NoShieldsChance;
        /// <summary>
        /// Bonus hull points for every ship of this class, the player's included
        /// </summary>
        public int HPBonus;
        /// <summary>
        /// Extra hull points for NPC ships only. NPC ships are generated close to player ship
        /// sizes, so this puts back the hull points they used to get from their bulk, plus
        /// some for the flux capacity ship size no longer gives them.
        /// </summary>
        public int NPCHPBonus;
        /// <summary>
        /// Multiplier on sqrt(size) for the ship's width in the combat view. Lets the on-screen
        /// size be tuned apart from the gameplay size. 0 means 1.
        /// </summary>
        public float ViewScale;
        /// <summary>
        /// Largest size an NPC ship of this class is generated with (end of NPCSizeRange)
        /// </summary>
        public int NPCSizeMax => Arrays.IsEmpty(NPCSizeRange)
            ? 0 : (int) NPCSizeRange[NPCSizeRange.Length - 1];

        public static ShipClassDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var classDef)) {
                D.Err("ShipClassDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ", All.Values.Select(v => v.Id)));
                classDef = All[Hashes.S("Corvette")];
            }
            return classDef;
        }
        public static ShipClassDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var classDef)) {
                D.Err("ShipClassDef not found by id hash: {0}", idH);
                classDef = All[Hashes.S("Corvette")];
            }
            return classDef;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipClassDef>("Config/Ships/Classes");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                type.LooksTypeId = $"Ships/{type.Id}";
                type.LooksTypeDerelictId = $"Derelicts/{type.Id}";
                if (type.IdH == ShipClassIdH.SpaceStation) {
                    type.IsSpaceStation = true;
                    type.Preview = GraphicsId.SpaceObjectsStationsSpaceStation01;
                } else {
                    if (type.IdH == ShipClassIdH.Unclassified) {
                        type.IsUnclassified = true;
                        Unclassified = type;
                    } else if (type.IdH == ShipClassIdH.Fighter) {
                        Fighter = type;
                    }
                    type.Preview = $"SpaceObjects/Ships/Ship{type.Id}01_R";
                }

                if (type.Rarity == 0f) {
                    D.Err("Rarity not set for ship class: {0}!", type.Id);
                    type.Rarity = 0.5f;
                }
                if (type.MaxFluxCapacity < 1) {
                    D.Err("MaxFluxCapacity not set for ship class: {0}!", type.Id);
                    type.MaxFluxCapacity = type.FluxBonus + 1000;
                }
                if (type.MaxFluxVents < 1) {
                    D.Err("MaxFluxVents not set for ship class: {0}!", type.Id);
                    type.MaxFluxVents = 2;
                }
                if (type.ViewScale <= 0f) {
                    type.ViewScale = 1f;
                }
                if (type.FluxSoftVentPerHour <= 0f) {
                    D.Err("FluxSoftVentPerHour not set for ship class: {0}!", type.Id);
                    type.FluxSoftVentPerHour = 100f;
                }
                if (type.WeaponsTier < 1) {
                    D.Err("WeaponsTier not set for ship class: {0}!", type.Id);
                    type.WeaponsTier = 1;
                }
                if (type.Visual_EngineCurvatureSign == 0) {
                    D.Err("Visual_EngineCurvatureSign not set for ship class: {0}!", type.Id);
                    type.Visual_EngineCurvatureSign = 1;
                }
                if (type.Visual_EnginesPerRow == 0) {
                    D.Err("Visual_EnginesPerRow not set for ship class: {0}!", type.Id);
                    type.Visual_EnginesPerRow = 8;
                }
                if (type.Visual_EngineDensity == 0f) {
                    D.Err("Visual_EnginesDensity not set for ship class: {0}!", type.Id);
                    type.Visual_EngineDensity = 0.15f;
                }
                if (type.Visual_DeviceMaxScale == 0f) {
                    D.Err("Visual_DeviceMaxScale not set for ship class: {0}!", type.Id);
                    type.Visual_DeviceMaxScale = 1f;
                }
                if (Arrays.IsEmpty(type.OriginDifficultyChance)) {
                    D.Err("OriginDifficultyChance not set for ship class: {0}!", type.Id);
                }
                if (Arrays.IsEmpty(type.NPCSizeRange)) {
                    D.Err("NPCSizeRange not set for ship class: {0}!", type.Id);
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipClass with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
            foreach (var def in All.Values) {
                if (def.RequiredClassId != null) {
                    def.RequiredClass = Get(def.RequiredClassId);
                    if (def.RequiredClass == null) {
                        D.Err("RequiredClassId ({0}) is invalid for ShipClassDef: {1}",
                            def.RequiredClassId, def.Id);
                    }
                }
            }
        }
        public string LooksFor(NPCShip ship) {
            if ((ship.State & ShipStateFlags.Derelict) == ShipStateFlags.Derelict) {
                return LooksTypeDerelictId;
            }
            return LooksTypeId;
        }

        public override string ToString() {
            return $"ShipClass[{Id}]";
        }

        public float DerelictSizeFor(float shipSize) {
            float minSize = MinSize;
            if (minSize == 0) { minSize = 300; }
            float maxSize = MaxSize;
            if (maxSize == 0) { maxSize = Pos.GridSize * 0.5f; }
            shipSize = Mathf.Clamp(shipSize, minSize, maxSize);
            var t = Mathf.InverseLerp(minSize, maxSize, shipSize);
            return Mathf.Lerp(DerelictSizeMin, DerelictSizeMax, t);
        }
        public float DerelictWidth(Rng rng) {
            var w = rng.Range(DerelictWidthMin, DerelictWidthMax);
            if (w < 0.3f) {
                D.Err("Refusing to set derelict width less than 0.3, will produce bad result otherwise! {0}", this);
                return 0.3f;
            }
            return w;
        }

        /// <param name="forLocked">
        /// If true, also validates the requirements that gate an un-owned class:
        /// energy, size, and the required predecessor class.
        /// </param>
        public bool IsCompatibleWithPlayer(GameState s, bool forLocked) {
            if (IsSpaceStation && A.Player.Ship.CombatState.Engines.Count > 0) {
                return false;
            }
            if (!forLocked) { return true; }
            s.Query.UpdatePlayerShipEnergy.Ask();

            if (A.Player.Ship.EnergyProduction < MinEnergy) {
                return false;
            }

            var shipSize = s.Sys.Areas.ShipSize;
            if (MinSize > 0 && shipSize < MinSize) { return false; }
            // Don't check max size!
            // if (MaxSize > 0 && shipSize > MaxSize) { return false; }

            if (RequiredClass != null) {
                var unlocked = s.Query.GetUnlockedShipClasses.Ask();
                if (!unlocked.Contains(RequiredClass)) {
                    return false;
                }
            }

            return true;
        }

        public bool IsCompatibleWith(NPCShip template) {
            if (template == null) { return true; }
            if (template.EnergyProduction > 0) {
                if (template.EnergyProduction < MinEnergy) { return false; }
            }
            var shipSize = template.Size;
            if (shipSize > 0) {
                if (MinSize > 0 && shipSize < MinSize) { return false; }
                if (MaxSize > 0 && shipSize > MaxSize) { return false; }
            }
            return true;
        }

        public string CodexLink => Texts.LinkToCodex(NameT, "ShipClasses", Id);

        public static ShipClassDef SelectFor(NPCShip ship) {
            foreach (var classDef in All.Values) {
                if (classDef.IsUnclassified) { continue; }
                if (classDef.IsSpaceStation) { continue; }
                if (classDef.IsCompatibleWith(ship)) {
                    return classDef;
                }
            }
            // D.Err("No compatible ship class found for ship: {0}", ship);
            return Unclassified;
        }

        /// <summary>
        /// Eases opponent class-point budgets down as the player's own class points grow, so a
        /// fixed StrengthMul/difficulty ratio doesn't turn into an ever-widening absolute
        /// weapon-count gap late-game. Returns 1 (no compression) if the curve is missing.
        /// </summary>
        public static float OpponentStrengthCompression(int playerClassPoints) {
            if (CurveDef.TryGet(CurveId.ShipCombat_OpponentPointsCompression, out var curve)) {
                return curve.Evaluate(playerClassPoints);
            }
            return 1f;
        }

        public static ShipClassDef StrongestClassForPoints(float cp, bool isPolice) {
            ShipClassDef strongest = Fighter;
            foreach (var cl in AllOrdered) {
                if (cl.IsSpaceStation) { continue; }
                if (isPolice && cl == Unclassified) { continue; }
                if (cl.MaxClassPoints <= cp &&
                        strongest.MaxClassPoints < cl.MaxClassPoints) {
                    strongest = cl;
                }
            }
            return strongest;
        }

        /// <summary>
        /// Extra margin over the bare class requirements before we suggest an upgrade, so we
        /// don't nag about a class the ship only barely scrapes into or can't comfortably afford.
        /// </summary>
        private const float SuggestUpgradeHeadroom = 1.05f;

        public static bool IsBetterClassAvailableForPlayer(GameState s,
                HashSet<string> ignoredIds = null) {
            return FindBetterClassesForPlayer(s, ignoredIds, null);
        }

        public static void CollectBetterClassesForPlayer(GameState s, List<ShipClassDef> result) {
            FindBetterClassesForPlayer(s, null, result);
        }

        // Returns on the first match when result is null, collects all matches otherwise
        private static bool FindBetterClassesForPlayer(GameState s, HashSet<string> ignoredIds,
                List<ShipClassDef> result) {
            var ship = s.Universe.Player.Ship;
            var cl = ship.Class;
            if (cl.IsSpaceStation || ship.IsDerelict || cl.IdH == ShipClassIdH.Titan) {
                return false;
            }
            var shipSize = s.Sys.Areas.ShipSize;
            var unlocked = s.Query.GetUnlockedShipClasses.Ask();
            bool found = false;
            foreach (var c in All.Values) {
                if (unlocked.Contains(c)) { continue; }
                if (c.IsSpaceStation) { continue; }
                if (c.MaxClassPoints <= cl.MaxClassPoints) { continue; }
                if (ignoredIds != null && ignoredIds.Contains(c.Id)) { continue; }
                // Same eligibility rules the ship class UI shows the player.
                // Also refreshes ship.EnergyProduction.
                if (!c.IsCompatibleWithPlayer(s, true)) { continue; }
                // if (c.MaxSize > 0 && shipSize > c.MaxSize) { continue; }
                if (c.MinSize > 0 && shipSize < c.MinSize * SuggestUpgradeHeadroom) { continue; }
                if (ship.EnergyProduction < c.MinEnergy * SuggestUpgradeHeadroom) { continue; }
                if (c.UnlockPrice * SuggestUpgradeHeadroom > s.Wallet.Amount) { continue; }
                if (result == null) {
                    return true;
                }
                result.Add(c);
                found = true;
            }
            return found;
        }

    }
}
