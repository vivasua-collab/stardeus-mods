using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Naming;
using Game.Systems.Ships;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class NPCShipGen {
        private readonly GameState S;
        public NPCShipGen(GameState s)  {
            S = s;
        }
        private readonly Dictionary<ShipClassDef, float> weightedClasses = new();
        private readonly Dictionary<ShipTagDef, float> weightedTags = new();
        private readonly Dictionary<NPCShipBehavior, float> weightedSectorBehaviors = new();
        private readonly Dictionary<NPCShipBehavior, float> weightedRegionBehaviors = new();
        private readonly Dictionary<ShipWeaponDef, float> weightedWeapons = new();
        private readonly Dictionary<ShipShieldDef, float> weightedShields = new();
        private readonly Dictionary<ShipEngineDef, float> weightedEngines = new();
        private readonly Dictionary<ShipCapacitorDef, float> weightedCapacitors = new();
        private readonly Dictionary<ShipVentDef, float> weightedVents = new();

        public NPCShip Generate(SpaceObject origin, Rng rng,
                NPCShip template = null,
                Dictionary<SpaceSector, List<NPCShip>> sectorShips = null) {
            var diff = SOUtils.SectorDifficultyOf(origin, null);

            FactionDef faction = template?.Faction ?? GenerateFaction(origin, rng, template);
            ShipClassDef cl = template?.Class ?? GenerateClass(rng, diff, template);
            ShipTagDef tag = template?.Tag ?? GenerateTag(rng, faction, cl, template);
            string shipName = template?.Name ?? GenerateName(rng);
            int numThrusters = template?.NumThrusters ?? 0;
            if (numThrusters == 0) {
                numThrusters = GenerateNumThrusters(cl, rng);
            }
            int numShortRangeDrives = template?.NumShortRangeDrives ?? 0;
            if (numShortRangeDrives == 0) {
                numShortRangeDrives = GenerateNumShortRangeDrives(cl, rng);
            }
            int numLongRangeDrives = template?.NumLongRangeDrives ?? 0;
            if (numLongRangeDrives == 0) {
                numLongRangeDrives = GenerateNumLongRangeDrives(cl, rng);
            }
            int energy = template?.EnergyProduction ?? 0;
            if (energy == 0) {
                energy = GenerateEnergyProduction(cl, rng);
            }
            int size = template?.Size ?? 0;
            if (size == 0) {
                size = GenerateSize(cl, rng);
            }
            ShipStateFlags state = ShipStateFlags.Active;
            if (template != null && template.State != ShipStateFlags.None) {
                state = template.State;
            }

            var ship = new NPCShip();
            ship.State = state;
            ship.Name = shipName;
            ship.Class = cl;
            ship.Tag = tag;
            ship.EnergyProduction = energy;
            ship.WillTrade = rng.Chance(tag.ChanceWillTrade);
            ship.Sector = SOUtils.SectorFor(origin);
            ship.Faction = faction;
            ship.Memory = new NPCShipMemory(ship);
            ship.Memory.OriginSector = ship.Sector;
            ship.NumThrusters = numThrusters;
            ship.NumShortRangeDrives = numShortRangeDrives;
            ship.NumLongRangeDrives = numLongRangeDrives;
            ship.WantedWeaponClassPoints = template?.WantedWeaponClassPoints ?? 0;
            ship.WantedClassPoints = template?.WantedClassPoints ?? 0;
            ship.WantedWeaponCombatStrength = template?.WantedWeaponCombatStrength ?? 0f;
            ship.WantedWeaponsTier = template?.WantedWeaponsTier ?? 0;
            ship.NoQuests = template?.NoQuests ?? false;
            ship.UpdateSize(size);
            GenCombatStateFor(ship, rng);

            var so = template?.SO ?? S.Universe.CreateObject();
            var type = ship.Class.IsSpaceStation ?
                SpaceObjectTypeIdH.SpaceStation : SpaceObjectTypeIdH.Ship;
            so.SetType(SpaceObjectType.Find(type));

            so.SetFaction(ship.Faction);
            so.UpdateName(ship.Name);
            ship.SO = so;
            so.Ship = ship;

            if (template?.IsPolice != true) {
                GenPoliceFlag(ship, rng, sectorShips);
            } else {
                ship.IsPolice = true;
            }

            // Rolled after GenPoliceFlag so ship.IsPolice reflects its final value -
            // otherwise the police branch below is unreachable.
            GenQuestGiverFlag(ship, rng);

            AssignBehaviors(ship, rng);

            // if (template != null) {
            //     D.LogInstance("Generated ship from template: {0} from {1}",
            //         ship, template);
            // }

            return ship;
        }

        public void RegenerateLoadoutFor(NPCShip ship, Rng rng) {
            ship.NumThrusters = GenerateNumThrusters(ship.Class, rng);
            ship.NumShortRangeDrives = GenerateNumShortRangeDrives(ship.Class, rng);
            ship.NumShortRangeDrivesCharged = 0;
            ship.NumLongRangeDrives = GenerateNumLongRangeDrives(ship.Class, rng);
            ship.NumLongRangeDrivesCharged = 0;
            ship.EnergyProduction = GenerateEnergyProduction(ship.Class, rng);
            ship.NumFusionCells = 0;
            ship.NumHeavyFusionCells = 0;
            ship.LoadSize(GenerateSize(ship.Class, rng));
            GenCombatStateFor(ship, rng, false);
            ValidateShipEngineCounts(ship, false);
        }

        private static int GenerateEnergyProduction(ShipClassDef cl, Rng rng) {
            return (int) (cl.MinEnergy * rng.Range(1.05f, 1.5f));
        }

        private static int GenerateSize(ShipClassDef cl, Rng rng) {
            return rng.IntCurve(cl.NPCSizeCurve);
        }

        private static int GenerateNumThrusters(ShipClassDef cl, Rng rng) {
            var numThrusters = rng.IntCurve(cl.NumThrustersCurve);
            if (numThrusters == 0 && !cl.IsSpaceStation) {
                D.Err("Refusing to spawn a ship with 0 thrusters: {0}", cl);
                numThrusters = 1;
            }
            return numThrusters;
        }

        private static int GenerateNumShortRangeDrives(ShipClassDef cl, Rng rng) {
            var numShortRangeDrives = rng.IntCurve(cl.NumShortRangeDrivesCurve);
            if (numShortRangeDrives == 0 && !cl.IsSpaceStation) {
                D.Err("Refusing to spawn a ship with 0 short range drives: {0}", cl);
                numShortRangeDrives = 1;
            }
            return numShortRangeDrives;
        }

        private static int GenerateNumLongRangeDrives(ShipClassDef cl, Rng rng) {
            return rng.IntCurve(cl.NumLongRangeDrivesCurve);
        }

        private static int shieldCP;
        private static int capCP;
        private static int ventCP;
        private static int thrusterCP;

        private void GenCombatStateFor(NPCShip ship, Rng rng, bool logEngineErrors = true) {
            ShipCombatState.CreateFor(ship);
            if (shieldCP == 0) {
                shieldCP = ShipDeviceDef.FastGet(
                    ShipDeviceIdH.Shield_ShieldEmitter).ClassPoints;
                thrusterCP = ShipDeviceDef.FastGet(
                    ShipDeviceIdH.Engine_Thruster).ClassPoints;
                capCP = ShipDeviceDef.FastGet(
                    ShipDeviceIdH.Capacitor_FluxCapacitor).ClassPoints;
                ventCP = ShipDeviceDef.FastGet(
                    ShipDeviceIdH.Vent_FluxVent).ClassPoints;
            }
            var offensiveness = ship.Tag.OffensivenessFor(ship);
            offensiveness = Mathf.Clamp01(offensiveness * S.Difficulty.ShipCombatDifficulty);
            var wantedShields = ship.Tag.WantedShieldsFor(ship);
            var wantThrusters = ship.NumThrusters;
            var mcp = ship.Class.MaxClassPoints;
            if (ship.WantedClassPoints > 0) {
                mcp = ship.WantedClassPoints;
            }

            var maxCP = mcp - wantedShields * shieldCP - wantThrusters * thrusterCP;
            if (ship.WantedClassPoints == 0) {
                if (ship.Class.IdH == ShipClassIdH.Unclassified) {
                    // we will generate outlaws with unclassified ships
                    maxCP = rng.Range(30, 100);
                } else if (ship.IsPolice) {
                    // have a chance to make OP police
                    if (rng.Chance(0.5f)) {
                        maxCP += (int)(mcp * rng.Range(0.25f, 0.85f));
                    }
                } else if (ship.Faction.IsOutlaw) {
                    maxCP += (int)(mcp * rng.Range(0.15f, 0.75f));
                }
            }

            if (maxCP < mcp * 0.3f) {
                D.Err("Not enough class points for weapons after shields / thrusters: cp: {0} / max: {1} | want shields: {2}, want thrusters: {3} | {4}",
                    maxCP, mcp, wantedShields, wantThrusters, ship.Class);
                if (ship.WantedWeaponCombatStrength > 0f || ship.WantedClassPoints > 0) {
                    // Player-scaled spawn: clamp shields down to preserve the intended
                    // budget rather than silently inflating CP to rng.Range(30,100).
                    while (wantedShields > 0 && maxCP < 0) {
                        wantedShields--;
                        maxCP = mcp - wantedShields * shieldCP - wantThrusters * thrusterCP;
                    }
                    if (maxCP < 0) { maxCP = 0; }
                } else {
                    wantedShields /= 2;
                    maxCP = mcp - wantedShields * shieldCP - wantThrusters * thrusterCP;
                    if (maxCP < 0) {
                        D.Err("Not enough class points for shields after fallback! {0}",
                            ship.Class);
                        maxCP = rng.Range(30, 100);
                    }
                }
            }
            int attempts = maxCP;
            var weaponBudget = Mathf.RoundToInt(maxCP * offensiveness);
            if (ship.WantedWeaponClassPoints > 0) {
                weaponBudget = ship.WantedWeaponClassPoints;
            } else if (ship.WantedWeaponCombatStrength <= 0f && offensiveness > 0.7f) {
                // Highly offensive ships would otherwise reserve almost nothing for flux.
                // Divert up to 0-30% of the weapon budget to flux management, scaled by how
                // far offensiveness exceeds 0.7 (a 1.0-offensiveness ship can give up to 30%).
                var maxDivert = 0.3f * Mathf.InverseLerp(0.7f, 1.0f, offensiveness);
                weaponBudget -= Mathf.RoundToInt(weaponBudget * rng.Range(0f, maxDivert));
            }
            var cpLeft = weaponBudget;
            float csTarget = ship.WantedWeaponCombatStrength;
            float accumulatedCS = 0f;
            int numPD = 0;
            while (attempts-- > 0 && cpLeft > 0) {
                if (csTarget > 0f && accumulatedCS >= csTarget) { break; }
                weightedWeapons.Clear();
                foreach (var weap in ShipWeaponDef.All.Values) {
                    if (weap.WeaponType == ShipWeaponType.PointDefense
                            && numPD >= ship.Class.MaxPointDefense) {
                        continue;
                    }
                    if (weap.CanAddFor(ship, cpLeft, out float w)) {
                        weightedWeapons.Add(weap, w);
                    }
                }
                if (rng.TryWeightedFrom(weightedWeapons, out var weapon)) {
                    if (weapon.WeaponType == ShipWeaponType.PointDefense) {
                        numPD++;
                    }
                    ship.CombatState.AddWeapon(weapon);
                    cpLeft -= weapon.Device.ClassPoints;
                    accumulatedCS += weapon.CombatStrength;
                }
            }

            weightedShields.Clear();
            foreach (var shield in ShipShieldDef.All.Values) {
                if (shield.CanAddFor(ship, out float w)) {
                    weightedShields.Add(shield, w);
                }
            }
            var extraCapPoints = 0;

            if (wantedShields > 1) {
                var shields = wantedShields - 1;
                var reducedShields = Mathf.CeilToInt(shields * rng.Value);
                if (reducedShields > 0) {
                    wantedShields -= reducedShields;
                    extraCapPoints = reducedShields * shieldCP;
                }
            }
            for (int i = 0; i < wantedShields; i++) {
                if (rng.TryWeightedFrom(weightedShields, out var sh)) {
                    ship.CombatState.AddShield(sh);
                }
            }

            AddShipEngines(ship, rng, ShipEngineType.Thruster, wantThrusters);
            AddShipEngines(ship, rng, ShipEngineType.HyperdriveShort, ship.NumShortRangeDrives);
            AddShipEngines(ship, rng, ShipEngineType.HyperdriveLong, ship.NumLongRangeDrives);
            ValidateShipEngineCounts(ship, logEngineErrors);

            if (wantedShields > 0) {
                var reserve = 0f;
                if (ship.WantedWeaponClassPoints == 0 && ship.WantedWeaponCombatStrength <= 0f) {
                    // Class points held back from weapons (the (1 - offensiveness) share,
                    // plus any diverted above) fund the flux systems, instead of being
                    // discarded.
                    reserve = Mathf.Max(0, maxCP - weaponBudget);
                }
                // Split the CP pool that flux systems get overall randomly between
                // capacitors and vents, so both can appear (some ships may still end up
                // with only one or the other).
                var fluxCP = extraCapPoints + reserve + cpLeft * rng.Range(0.5f, 1.0f);
                var ventShare = rng.Range(0f, 1f);
                var wantedCapacitors = Mathf.FloorToInt(fluxCP * (1f - ventShare) / capCP);
                if (wantedCapacitors > 0) {
                    weightedCapacitors.Clear();
                    foreach (var capacitor in ShipCapacitorDef.All.Values) {
                        if (capacitor.CanAddFor(ship, out float w)) {
                            weightedCapacitors.Add(capacitor, w);
                        }
                    }
                    int addedFluxBonus = 0;
                    int maxFluxBonus = 0;
                    if (ship.Class.MaxFluxCapacity > 0) {
                        var baseFlux = Tunables.ShipBaseFlux + ship.Class.FluxBonus;
                        var maxAllowed = Mathf.RoundToInt(
                            ship.Class.MaxFluxCapacity * rng.Range(1.1f, 1.2f));
                        maxFluxBonus = Mathf.Max(0, maxAllowed - baseFlux);
                    }
                    for (int i = 0; i < wantedCapacitors; i++) {
                        if (rng.TryWeightedFrom(weightedCapacitors, out var cap)) {
                            if (maxFluxBonus > 0 && addedFluxBonus + cap.Capacity > maxFluxBonus) {
                                break;
                            }
                            ship.CombatState.AddCapacitor(cap);
                            addedFluxBonus += cap.Capacity;
                        }
                    }
                }

                var wantedVents = Mathf.FloorToInt(fluxCP * ventShare / ventCP);
                var maxVents = ship.Class.MaxFluxVents;
                if (maxVents > 0 && wantedVents > maxVents) {
                    wantedVents = maxVents;
                }
                if (wantedVents > 0) {
                    weightedVents.Clear();
                    foreach (var vent in ShipVentDef.All.Values) {
                        if (vent.CanAddFor(ship, out float w)) {
                            weightedVents.Add(vent, w);
                        }
                    }
                    for (int i = 0; i < wantedVents; i++) {
                        if (rng.TryWeightedFrom(weightedVents, out var vent)) {
                            ship.CombatState.AddVent(vent);
                        }
                    }
                }
            }
        }

        public static void ValidateShipEngineCounts(NPCShip ship, bool logErrors) {
            if (ship.IsPlayer) { return; }
            var actThrusters = GetShipEngineCount(ship, ShipEngineType.Thruster);
            if (actThrusters != ship.NumThrusters) {
                if (logErrors) {
                    D.ErrSoft("Ship thruster count mismatch: {0}: actual: {1}, declared: {2}",
                        ship, actThrusters, ship.NumThrusters);
                }
                ship.NumThrusters = actThrusters;
            }
            var actShort = GetShipEngineCount(ship, ShipEngineType.HyperdriveShort);
            if (actShort != ship.NumShortRangeDrives) {
                if (logErrors) {
                    D.ErrSoft("Ship short drive count mismatch: {0}: actual: {1}, declared: {2}",
                        ship, actShort, ship.NumShortRangeDrives);
                }
                ship.NumShortRangeDrives = actShort;
            }
            var actLong = GetShipEngineCount(ship, ShipEngineType.HyperdriveLong);
            if (actLong != ship.NumLongRangeDrives) {
                if (logErrors) {
                    D.ErrSoft("Ship long drive count mismatch: {0}: actual: {1}, declared: {2}",
                        ship, actLong, ship.NumLongRangeDrives);
                }
                ship.NumLongRangeDrives = actLong;
            }
        }

        private void AddShipEngines(NPCShip ship, Rng rng, ShipEngineType wantedType, int wantedNum) {
            if (wantedNum < 1) { return; }
            weightedEngines.Clear();
            foreach (var engine in ShipEngineDef.All.Values) {
                if (engine.EngineType == wantedType &&
                        engine.CanAddFor(ship, out float w)) {
                    weightedEngines.Add(engine, w);
                }
            }
            for (int i = 0; i < wantedNum; i++) {
                if (rng.TryWeightedFrom(weightedEngines, out var en)) {
                    ship.CombatState.AddEngine(en);
                }
            }
        }

        private static int GetShipEngineCount(NPCShip ship, ShipEngineType type) {
            int cnt = 0;
            foreach (var engine in ship.CombatState.Engines) {
                if (engine.EngineDef.EngineType == type) {
                    cnt++;
                }
            }
            return cnt;
        }

        private string GenerateName(Rng rng) {
            var name = S.Query.GenerateName.Ask(NameRequest.ForShip(rng));
            name.Consume();
            var shipName = name.Name;
            return shipName;
        }

        private ShipTagDef GenerateTag(Rng rng, FactionDef faction, ShipClassDef cl,
                NPCShip template) {
            weightedTags.Clear();
            foreach (var shipTag in ShipTagDef.All.Values) {
                if (!shipTag.IsCompatibleWith(template)) {
                    continue;
                }

                if (!Arrays.IsEmpty(shipTag.CompatibleFactions)) {
                    if (!Arrays.Contains(shipTag.CompatibleFactions, faction.Id)) {
                        continue;
                    }
                }
                if (!Arrays.IsEmpty(shipTag.CompatibleClasses)) {
                    if (!Arrays.Contains(shipTag.CompatibleClasses, cl.Id)) {
                        continue;
                    }
                }
                weightedTags.Add(shipTag, shipTag.Rarity);
            }


            if (!rng.TryWeightedFrom(weightedTags, out var tag)) {
                D.ErrSoft("Faction / Ship class tag not available: {0}/{1}", faction, cl);
                tag = ShipTagDef.Get("MSV");
            }

            return tag;
        }

        private ShipClassDef GenerateClass(Rng rng, float diff, NPCShip template) {
            weightedClasses.Clear();
            foreach (var shipClass in ShipClassDef.All.Values) {
                if (!shipClass.IsCompatibleWith(template)) {
                    continue;
                }
                var chance = shipClass.OriginDifficultyCurve.Evaluate(diff);
                if (!rng.Chance(chance)) { continue; }
                weightedClasses.Add(shipClass, shipClass.Rarity);
            }

            var cl = rng.WeightedFrom(weightedClasses);
            return cl;
        }

        private FactionDef GenerateFaction(SpaceObject origin, Rng rng,
                NPCShip template) {
            var w = Caches.WeightedFactions;
            w.Clear();
            S.Sys.Universe.FactionsIn(origin, w, FactionIdH.Adventurer);
            foreach (var f in FactionDef.Known.Values) {
                if (f.IsInternal || f.IsBlank) { continue; }
                if (RogueAIUtils.IsFactionEliminated(f)) {
                    // Also drops it if a stale biome dominance entry seeded it above.
                    w.Remove(f);
                    continue;
                }
                if (f.ChanceRandomShip <= 0f) { continue; }
                if (template?.IsPolice == true && f.ChancePolice <= 0f) {
                    continue;
                }
                if (w.TryGetValue(f, out var chance)) {
                    w[f] = chance + f.ChanceRandomShip;
                } else {
                    w.Add(f, f.ChanceRandomShip);
                }
            }
            var faction = rng.WeightedFrom(w);
            return faction;
        }

        public void GenPoliceFlag(NPCShip ship, Rng rng,
                Dictionary<SpaceSector, List<NPCShip>> sectorShips) {
            var chance = ship.Tag.ChancePolice * ship.Faction.ChancePolice;
            if (chance == 0f ) { return; }
            List<NPCShip> shipsAtSec;
            if (sectorShips != null) {
                sectorShips.TryGetValue(ship.Sector, out shipsAtSec);
            } else {
                shipsAtSec = S.Query.NPCShipsAt.Ask(ship.Sector);
            }
            var policeCount = 0;
            if (shipsAtSec != null) {
                for (int i = shipsAtSec.Count - 1; i >= 0; i--) {
                    NPCShip s = shipsAtSec[i];
                    if (s.IsPolice) { policeCount++; }
                }
            }

            if (policeCount == 0 && ship.Id % 15 == 0) {
                chance = 1f;
                // D.Warn("Forcing police = {0} * {1} = {2} | {3}",
                //     ship.Tag.ChancePolice, ship.Faction.ChancePolice,
                //     chance, this);
            // } else {
            //     D.Warn("Chance police = {0} * {1} = {2} | {3}",
            //         ship.Tag.ChancePolice, ship.Faction.ChancePolice,
            //         chance, this);
            }

            // reduce chance of spawning more police ships
            if (policeCount == 1) {
                chance *= 0.5f;
            } else if (policeCount == 2) {
                chance *= 0.25f;
            } else if (policeCount > 2) {
                chance *= 0.1f;
            }
            ship.IsPolice = rng.Chance(chance);
        }

        private static void GenQuestGiverFlag(NPCShip ship, Rng rng) {
            if (ship.NoQuests) { return; }
            if (ship.Class.IsSpaceStation) {
                ship.IsQuestGiver = rng.Chance(Tunables.QuestGiverChance_SpaceStation);
            } else if (ship.IsPolice) {
                ship.IsQuestGiver = rng.Chance(Tunables.QuestGiverChance_ShipPolice);
            } else if (ship.IsOutlaw) {
                ship.IsQuestGiver = rng.Chance(Tunables.QuestGiverChance_ShipOutlaw);
            } else {
                ship.IsQuestGiver = rng.Chance(Tunables.QuestGiverChance_ShipNPC);
            }
        }

        // Both lists are prepared per call: they are shared by all ships, so a list left
        // over from another ship would hand out behaviors that do not fit this one.
        public void AssignSectorBehavior(NPCShip ship, bool canRepeat, Rng rng) {
            if (ship.IsPlayer) { return; }
            PrepareBehaviorsFor(ship, canRepeat);
            if (weightedSectorBehaviors.Count == 0 && !canRepeat) {
                // The current behavior is the only one that fits, keep it
                PrepareBehaviorsFor(ship, true);
            }
            if (rng.TryWeightedFrom(weightedSectorBehaviors, out var beh)) {
                ship.SetBehavior(beh, NPCShipBehaviorScope.Sector);
            } else {
                D.Err("Could not get sector behavior for {0}", ship);
            }
            ClearBehaviors();
        }
        public void AssignRegionBehavior(NPCShip ship, bool canRepeat, Rng rng) {
            PrepareBehaviorsFor(ship, canRepeat);
            if (weightedRegionBehaviors.Count == 0 && !canRepeat) {
                // The current behavior is the only one that fits, keep it
                PrepareBehaviorsFor(ship, true);
            }
            if (rng.TryWeightedFrom(weightedRegionBehaviors, out var beh)) {
                ship.SetBehavior(beh, NPCShipBehaviorScope.Region);
            } else {
                D.Err("Could not get region behavior for {0}", ship);
            }
            ClearBehaviors();
        }

        private void AssignBehaviors(NPCShip ship, Rng rng) {
            AssignSectorBehavior(ship, true, rng);
            AssignRegionBehavior(ship, true, rng);
        }

        private void ClearBehaviors() {
            weightedSectorBehaviors.Clear();
            weightedRegionBehaviors.Clear();
        }

        private void PrepareBehaviorsFor(NPCShip ship, bool canRepeat) {
            weightedSectorBehaviors.Clear();
            weightedRegionBehaviors.Clear();

            foreach (var assDef in NPCShipBehaviorAssignmentDef.All.Values) {
                if (!assDef.HasChanceFor(ship, out float chance)) { continue; }
                var b = assDef.Behavior;
                if (!b.IsCompatibleWith(ship, ship.Sector)) { continue; }
                if (b.Scope == NPCShipBehaviorScope.Sector) {
                    if (!canRepeat) {
                        if (ship.BehaviorSector == b) { continue; }
                    }
                    if (weightedSectorBehaviors.TryGetValue(b, out var ch)) {
                        weightedSectorBehaviors[b] = Math.Max(ch, chance);
                    } else {
                        weightedSectorBehaviors.Add(b, chance);
                    }
                } else if (b.Scope == NPCShipBehaviorScope.Region) {
                    if (!canRepeat) {
                        if (ship.BehaviorRegion == b) { continue; }
                    }
                    if (weightedRegionBehaviors.TryGetValue(b, out var ch)) {
                        weightedRegionBehaviors[b] = Math.Max(ch, chance);
                    } else {
                        weightedRegionBehaviors.Add(b, chance);
                    }
                } else {
                    D.Err("Unexpected scope in NPC Ship Behavior: {0}", b);
                }
            }
        }
    }
}
