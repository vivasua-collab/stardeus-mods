using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Systems.UIUX;
using Game.Visuals;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipCombatState {
        public readonly NPCShip Ship;
        public ShipFlux Flux;
        public readonly List<ShipWeapon> Weapons = new();
        public readonly List<ShipShield> Shields = new();
        public readonly List<ShipEngine> Engines = new();
        public readonly List<IShipDevice> Devices = new();
        public readonly List<ShipCapacitor> Capacitors = new();
        public readonly List<ShipVent> Vents = new();
        public readonly List<BattleStationComp> BattleStations = new();
        private BCallback devicesChangedB;
        public BCallback DevicesChangedB => devicesChangedB ??= new();
        /// <summary>
        /// The opposing ship this ship is currently attacking. For the player it is the
        /// enemy shown in ShipCombatTheirWidget, for NPCs it is the target their side
        /// picked. Not serialized - re-resolved on load by ShipCombatAI.ResolveFocus.
        /// </summary>
        public NPCShip FocusedEnemy;
        /// <summary>
        /// Deserialized FocusedEnemy ship id, resolved against the live opponents in
        /// load stage B (ShipCombatAI.ResolveFocus with FocusResolveReason.Restore).
        /// </summary>
        public uint FocusedEnemyId;
        /// <summary>
        /// When an interval based target strategy is due to pick a new target.
        /// 0 means there is no scheduled switch.
        /// </summary>
        public long NextTargetSwitchTick;
        /// <summary>
        /// The ship that last dealt damage to this ship. Drives kill attribution
        /// when this ship is destroyed. Cleared when combat is joined or cleaned up,
        /// so attribution never leaks between separate battles.
        /// </summary>
        public NPCShip LastAttacker;
        /// <summary>
        /// Deserialized LastAttacker ship id. Combat state is restored while ships
        /// are still being constructed, so the reference is resolved later, once
        /// every ship exists (see ResolveLastAttacker).
        /// </summary>
        public uint LastAttackerId;

        public void ResolveLastAttacker(GameState s) {
            if (LastAttackerId == 0) { return; }
            LastAttacker = s.Query.NPCShipById.Ask(LastAttackerId);
            LastAttackerId = 0;
        }

        public void AddBattleStation(BattleStationComp bs) {
            if (!BattleStations.Contains(bs)) BattleStations.Add(bs);
        }
        public void RemoveBattleStation(BattleStationComp bs) {
            BattleStations.Remove(bs);
        }
        public ShipCombatFluxStrategyDef StrategyFlux;
        public ShipCombatWeaponStrategyDef StrategyWeapon;
        public ShipCombatTargetStrategyDef StrategyTarget;
        public ShipWeaponSettings WeaponSettings;
        public ShipFluxSettings FluxSettings;
        public long NextNPCFluxBlunderAt;
        public int WeaponsShotThisTick = 0;
        /// <summary>
        /// Ammo that is in stock and can be used to reload weapons mid combat
        /// </summary>
        public Dictionary<ShipWeaponDef, int> AmmoStock = new();
        public float Offensiveness {
            get {
                var maxCP = Ship.Class.MaxClassPoints;
                var wp = WeaponClassPoints;
                var offensiveness = Mathf.Clamp01(wp / (maxCP * Tunables.ShipOffensivenessClassPointRatio));
                return offensiveness;
            }
        }
        public int CurrentClassPoints {
            get {
                var points = 0;
                for (int i = Devices.Count - 1; i >= 0; i--) {
                    var dev = Devices[i];
                    points += dev.Device.DeviceDef.ClassPoints;
                }
                return points;
            }
        }
        public int WeaponClassPoints {
            get {
            var points = 0;
            for (int i = Weapons.Count - 1; i >= 0; i--) {
                var weapon = Weapons[i];
                points += weapon.WeaponDef.Device.ClassPoints;
            }
            return points;
            }
        }
        public bool CanHyperjump {
            get {
                if (!Ship.IsInCombat) { return true; }
                foreach (var engine in Engines) {
                    if (engine.EngineDef.IsHyperdrive) {
                        if (engine.Phase == ShipEnginePhase.Active) {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
        public uint LastDeviceId;
        public bool IsShieldUp => NumShieldsUp > 0;
        public int NumShieldsUp;
        public float HP;
        public int MaxHP;
        public float HealthPercent => HP / MaxHP;
        private BCallback onHPChanged;
        public BCallback OnHPChanged => onHPChanged ??= new BCallback();
        public Rng Rng;
        public int TicksTillShoot;
        public int BreachCapsulesSent;
        public long NextBreachCapsuleTick;

        public float EvasionPercent {
            get {
                float baseEvasionPct = Ship.Class.BaseEvasion;
                // No one (and nothing) at the helm: only the ship class's inherent evasion
                // applies - thrusters and piloting skill both require IsPiloted.
                if (!IsPiloted) {
                    return Mathf.Clamp(baseEvasionPct, 0f, Tunables.ShipMaxEvadePercent);
                }
                var evasion = 0f;
                for (int i = Engines.Count - 1; i >= 0; i--) {
                    ShipEngine e = Engines[i];
                    if (e.IsActive) {
                        var ev = e.EngineDef.Evade;
                        // thruster upgrade bonus
                        if (e.Comp != null && e.Comp.EvadeBonus != 0f) {
                            ev *= 1f + e.Comp.EvadeBonus;
                        }
                        evasion += ev;
                    }
                }
                var pct = evasion / (Ship.Size * Tunables.ShipEvadeShipSizePenalty);
                // D.Warn("Evasion for ship: {0}/{1} = {2}. {3}",
                //     evasion, Ship.Size, pct, Ship.Class.NameT);
                return Mathf.Clamp(baseEvasionPct + pct + PilotingEvadeBonus, 0f, Tunables.ShipMaxEvadePercent);
            }
        }
        /// <summary>
        /// Evade bonus from the most skilled seated, operational Bridge Controls operator.
        /// Does not stack across multiple Bridge Controls devices. Player ship only.
        /// </summary>
        public float PilotingEvadeBonus {
            get {
                TryGetBestPilotingBonus(out _, out _, out var bonus);
                return bonus;
            }
        }
        /// <summary>
        /// Finds the best (non-stacking) piloting evade bonus across all operational
        /// Bridge Controls devices on the player's ship. Queried on demand rather than
        /// tracked via registration, since Bridge Controls devices are few and this is
        /// only evaluated during combat.
        /// </summary>
        public bool TryGetBestPilotingBonus(out Being pilot, out int level, out float bonus) {
            pilot = null;
            level = 0;
            bonus = 0f;
            if (!Ship.IsPlayer) { return false; }
            var bridges = A.S.Components.AllByType<BridgeControlsComp>();
            if (bridges == null) { return false; }
            if (!CurveDef.TryGet(CurveId.ShipCombat_PilotingSkillEvadeCurve, out var curve)) {
                D.Throw("Curve not available: {0}", CurveId.ShipCombat_PilotingSkillEvadeCurve);
            }
            foreach (BridgeControlsComp bc in bridges) {
                if (!bc.IsOperational) { continue; }
                var op = bc.Operator;
                if (op == null) { continue; }
                var lvl = op.Data.Mind.Jobs.LevelOf(JobTypeIdH.Piloting);
                var b = curve.Evaluate(lvl);
                // pilot == null check ensures a level-0 pilot (0% bonus) still registers
                // as present, rather than being indistinguishable from "no pilot at all"
                if (pilot == null || b > bonus) {
                    bonus = b;
                    pilot = op;
                    level = lvl;
                }
            }
            return pilot != null;
        }
        /// <summary>
        /// Whether the ship currently has someone (or something) at the helm. NPC ships are
        /// not simulated for operator presence and are always considered piloted. The player
        /// ship needs either a seated, operational Bridge Controls operator, or an
        /// operational Bridge Controls device running on autopilot (no piloting skill bonus
        /// in that case, but thrusters still contribute).
        /// </summary>
        public bool IsPiloted {
            get {
                if (!Ship.IsPlayer) { return true; }
                if (TryGetBestPilotingBonus(out _, out _, out _)) { return true; }
                return HasOperationalAutomatedBridge();
            }
        }
        private bool HasOperationalAutomatedBridge() {
            var bridges = A.S.Components.AllByType<BridgeControlsComp>();
            if (bridges == null) { return false; }
            foreach (BridgeControlsComp bc in bridges) {
                if (bc.IsOperational && bc.IsAutomated) { return true; }
            }
            return false;
        }
        public float GetEvasionPercentBonus(out float currentThrusterBonus, out float maxThrusterBonus) {
            float baseEvasionPct = Ship.Class.BaseEvasion;
            var evasion = 0f;
            var maxEvasion = 0f;
            for (int i = Engines.Count - 1; i >= 0; i--) {
                ShipEngine e = Engines[i];
                var ev = e.EngineDef.Evade;
                if (e.Comp != null && e.Comp.EvadeBonus != 0f) {
                    ev *= 1f + e.Comp.EvadeBonus;
                }
                if (e.IsActive) {
                    evasion += ev;
                }
                maxEvasion += ev;
            }
            // Not piloted (no seated operator, no autopilot): thrusters contribute nothing
            // right now, though maxThrusterBonus below still shows what a pilot would unlock.
            currentThrusterBonus = IsPiloted
                ? evasion / (Ship.Size * Tunables.ShipEvadeShipSizePenalty)
                : 0f;
            if (currentThrusterBonus + baseEvasionPct > Tunables.ShipMaxEvadePercent) {
                currentThrusterBonus = Tunables.ShipMaxEvadePercent - baseEvasionPct;
            }
            maxThrusterBonus = maxEvasion / (Ship.Size * Tunables.ShipEvadeShipSizePenalty);
            if (maxThrusterBonus + baseEvasionPct > Tunables.ShipMaxEvadePercent) {
                maxThrusterBonus = Tunables.ShipMaxEvadePercent - baseEvasionPct;
            }
            return baseEvasionPct + currentThrusterBonus;
        }

        public static ShipCombatState CreateFor(NPCShip ship) {
            var state = new ShipCombatState(ship);
            state.WeaponSettings = ShipWeaponSettings.Default;
            state.FluxSettings = ShipFluxSettings.Default;
            ship.CombatState = state;
            return state;
        }
        public ShipCombatState(NPCShip ship, ShipFlux flux = null) {
            Ship = ship;
            if (flux == null) {
                Flux = new ShipFlux();
                Flux.LoadShip(ship);
                Flux.LoadState(ShipFluxState.OK);
                Rng = ship.SeededRng(Primes.Prime083);
                if (ship.Size > 0) {
                    UpdateSize(ship);
                }
            } else {
                // loading saved data
                Flux = flux;
            }
            Flux.UpdateCapacity(this);
        }
        public void UpdateSize(NPCShip ship) {
            Flux.UpdateCapacity(this);
            MaxHP = CalculateMaxHP(ship);
            if (HP == 0 && !Ship.IsDerelict) {
                HP = MaxHP;
            }
        }

        internal void RecalculateMaxHP(bool capCurrentHP) {
            MaxHP = CalculateMaxHP(Ship);
            if (capCurrentHP && HP > MaxHP) {
                HP = MaxHP;
            }
        }

        private static int CalculateMaxHP(NPCShip ship) {
            var size = ship.Size;
            if (size == 0) {
                D.Err("Ship size is 0! {0}", ship);
                size = ship.Class.MinSize;
            }
            var hp = size / 10 + ship.Class.HPBonus;
            if (!ship.IsPlayer) {
                hp += ship.Class.NPCHPBonus;
            }
            return hp;
        }

        /// <summary>
        /// Usable firepower: weapons this ship could bring to a fight, based on device
        /// state (station assignment, power), not on Phase. Valid outside combat too - Phase
        /// only reflects live firing state, which requires Ship.IsInCombat.
        /// </summary>
        public int WeaponCombatStrength {
            get {
                float wp = 0;
                for (int i = Weapons.Count - 1; i >= 0; i--) {
                    var w = Weapons[i];
                    if (w.CanBecomeActive && !w.IsPointDefense) {
                        wp += w.WeaponDef.CombatStrength;
                    }
                }
                return (int) wp;
            }
        }
        /// <summary>
        /// Whether the ship can still take part in a fight: it has at least one weapon
        /// that can actually fire, or at least one shield that is actually operational.
        /// Devices that are merely present (destroyed, unpowered, unreachable) don't count.
        /// </summary>
        public bool IsCombatCapable {
            get {
                if (WeaponCombatStrength > 0) { return true; }
                for (int i = Shields.Count - 1; i >= 0; i--) {
                    if (Shields[i].Device.IsOperational) { return true; }
                }
                return false;
            }
        }
        public int FluxCapacitorBonus(bool activeOnly) {
            var cap = 0;
            for (int i = Capacitors.Count - 1; i >= 0; i--) {
                var c = Capacitors[i];
                if (!activeOnly || (c.Phase == ShipCapacitorPhase.Active && c.IsReachable)) {
                    cap += c.CapacitorDef.Capacity;
                }
            }
            return cap;
        }
        /// <summary>
        /// Sum of active Flux Vent bonuses. When <paramref name="capAtClassLimit"/> is true,
        /// the count is capped at Ship.Class.MaxFluxVents - vents beyond that count still
        /// cost class points but add no vent rate bonus.
        /// </summary>
        public float FluxVentBonus(bool capAtClassLimit) {
            var bonus = 0f;
            var counted = 0;
            var maxVents = capAtClassLimit ? Ship.Class.MaxFluxVents : 0;
            for (int i = Vents.Count - 1; i >= 0; i--) {
                var v = Vents[i];
                if (!v.IsActive) { continue; }
                if (maxVents > 0 && counted >= maxVents) { continue; }
                bonus += v.VentDef.VentBonus;
                counted++;
            }
            return bonus;
        }
        /// <summary>
        /// Capacitors active / total
        /// </summary>
        public (int, int) NumCapacitorsActiveTotal {
            get {
                var count = 0;
                var total = 0;
                for (int i = Capacitors.Count - 1; i >= 0; i--) {
                    var dev = Capacitors[i];
                    if (dev.Phase == ShipCapacitorPhase.Active && dev.IsReachable) {
                        count++;
                    }
                    total++;
                }
                return (count, total);
            }
        }
        /// <summary>
        /// Thrusters active / total
        /// </summary>
        public (int, int) NumThrustersActiveTotal {
            get {
                var thrCount = 0;
                var thrTotal = 0;
                for (int i = Engines.Count - 1; i >= 0; i--) {
                    var dev = Engines[i];
                    if (dev.EngineDef.IsHyperdrive) { continue; }
                    if (dev.IsActive) {
                        thrCount++;
                    }
                    thrTotal++;
                }
                return (thrCount, thrTotal);
            }
        }
        public int NumThrustersOutsideCombat {
            get {
                var thrCount = 0;
                for (int i = Engines.Count - 1; i >= 0; i--) {
                    var dev = Engines[i];
                    if (dev.EngineDef.IsHyperdrive) { continue; }
                    if (dev.Device.IsDestroyed) { continue; }
                    thrCount++;
                }
                return thrCount;
            }
        }
        public int NumHyperdrivesOutsideCombat {
            get {
                var count = 0;
                for (int i = Engines.Count - 1; i >= 0; i--) {
                    var dev = Engines[i];
                    if (!dev.EngineDef.IsHyperdrive) { continue; }
                    if (dev.Device.IsDestroyed) { continue; }
                    count++;
                }
                return count;
            }
        }

        public (int, int) NumWeaponsActiveTotal {
            get {
                var count = 0;
                var total = Weapons.Count;
                for (int i = total - 1; i >= 0; i--) {
                    var dev = Weapons[i];
                    if (dev.IsActive) {
                        count++;
                    }
                }
                return (count, total);
            }
        }

        public bool IsNPCDefeated {
            get {
                if (HealthPercent >= 0.5f) { return false ;}
                for (int i = Devices.Count - 1; i >= 0; i--) {
                    IShipDevice device = Devices[i];
                    if (device.Device.Health > 0f) {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Not saved / loaded, used for surrender
        /// </summary>
        public bool IsNegotiating;

        /// <summary>
        /// Same shape as NPCShip.StrengthDifferenceAgainst, but both operands are the
        /// weapons each side can actually fire right now instead of its paper loadout,
        /// so a ship whose weapons are all gone surrenders like the weakling it is.
        /// </summary>
        private float EffectiveStrengthDifferenceAgainst(List<NPCShip> opposingSide) {
            float ourStr = Mathf.Max(1, WeaponCombatStrength);
            float theirStr = 0f;
            for (int i = opposingSide.Count - 1; i >= 0; i--) {
                theirStr += opposingSide[i].CombatState.WeaponCombatStrength;
            }
            theirStr = Mathf.Max(1f, theirStr);
            if (ourStr > theirStr) {
                return 1f - theirStr / ourStr;
            } else {
                return ourStr / theirStr - 1f;
            }
        }

        public bool ConsiderSurrender(List<NPCShip> opposingSide) {
            if (IsNegotiating) {
                return false;
            }
            var rng = Ship.SeededRng(Primes.Prime019);
            if (rng.Chance(0.05f)) {
                // never surrender
                return false;
            }
            var strDiff = EffectiveStrengthDifferenceAgainst(opposingSide);
            var chance = Ship.Faction.BraveryCurve.Evaluate(strDiff);
            if (!rng.Chance(chance)) {
                // won't surrender, too brave
                return false;
            }
            float maxHealth = 0f;
            float health = 0f;
            foreach (var dev in Devices) {
                maxHealth += dev.Device.HealthMax;
                health += dev.Device.Health;
            }
            if (maxHealth <= 0f) {
                return true;
            }
            var deviceHealthPct = health / maxHealth;
            var healthPct = Mathf.Min(deviceHealthPct, Ship.CombatState.HealthPercent);
            Curve curve = Tunables.NPCShipHealthSurrenderChance;
            chance = curve.Evaluate(healthPct);
            return rng.Chance(chance);
        }

        public bool AddDevice(IShipDevice device) {
            if (device == null) {
                D.Err("Trying to add null device!");
                return false;
            }
            if (device.Device == null) {
                D.Err("Trying to add a ship device that has no ShipDevice instance! {0}: {1}",
                    device, D.DumpInstance(device, forLog: true));
                return false;
            }
            var tile = device.Device.Tile;
            if (tile != null) {
                foreach (var devs in Devices) {
                    if (devs.Device.Tile?.Id == tile.Id) {
                        D.Err("Trying to add ship device more than once! {0}", device);
                        return false;
                    }
                }
            }
            if (Devices.AddUniqueInefficient(device)) {
                if (device is ShipWeapon w) {
                    Weapons.Add(w);
                } else if (device is ShipEngine e) {
                    if (Ship.Class.IsSpaceStation) {
                        D.Err("Refusing to add engine to space station: {0}", device);
                        return false;
                    }
                    Engines.Add(e);
                } else if (device is ShipShield s) {
                    Shields.Add(s);
                } else if (device is ShipCapacitor c) {
                    Capacitors.Add(c);
                } else if (device is ShipVent v) {
                    Vents.Add(v);
                } else {
                    D.Err("Unexpected device class: {0}: {1}",
                        device, device?.GetType());
                }
                devicesChangedB?.Invoke();
                return true;
            } else {
                D.Err("Refusing to add duplicate device: {0}", device);
            }
            return false;
        }

        public bool RemoveDevice(IShipDevice device) {
            if (device == null) {
                D.Err("Trying to remove null device!");
                return false;
            }
            if (Devices.Remove(device)) {
                if (device is ShipWeapon w) {
                    Weapons.Remove(w);
                } else if (device is ShipEngine e) {
                    Engines.Remove(e);
                } else if (device is ShipShield s) {
                    Shields.Remove(s);
                } else if (device is ShipCapacitor c) {
                    Capacitors.Remove(c);
                } else if (device is ShipVent v) {
                    Vents.Remove(v);
                } else {
                    D.Err("Unexpected device class: {0}: {1}",
                        device, device?.GetType());
                }
                devicesChangedB?.Invoke();
                return true;
            }
            return false;
        }

        internal void ValidatePlayerDevices() {
            if (!Ship.IsPlayer) { return; }

            var preferredByTileId = new Dictionary<int, IShipDevice>();
            var removedAny = false;
            for (int i = 0; i < Devices.Count; i++) {
                var device = Devices[i];
                var tile = device.Device?.Tile;
                if (tile?.IsActive != true || !tile.IsConstructed) { continue; }

                if (!preferredByTileId.TryGetValue(tile.Id, out var preferred)
                        || PreferPlayerDevice(device, preferred, tile)) {
                    preferredByTileId[tile.Id] = device;
                }
            }

            for (int i = Devices.Count - 1; i >= 0; i--) {
                var device = Devices[i];
                var tile = device.Device?.Tile;
                if (tile?.IsActive != true || !tile.IsConstructed) {
                    D.Warn("Removing player ship device without an active constructed tile: {0}",
                        device);
                    RemoveDevice(device);
                    removedAny = true;
                } else if (preferredByTileId[tile.Id] != device) {
                    D.Warn("Removing duplicate player ship device: {0}", device);
                    RemoveDevice(device);
                    removedAny = true;
                }
            }
            if (removedAny) {
                Flux.UpdateCapacity(this);
            }
        }

        private static bool PreferPlayerDevice(IShipDevice candidate,
                IShipDevice preferred, Tile tile) {
            var candidateIsLinked = IsPlayerDeviceLinkedToTile(candidate, tile);
            var preferredIsLinked = IsPlayerDeviceLinkedToTile(preferred, tile);
            if (candidateIsLinked != preferredIsLinked) {
                return candidateIsLinked;
            }
            return candidate.Device.Id > preferred.Device.Id;
        }

        private static bool IsPlayerDeviceLinkedToTile(IShipDevice device, Tile tile) {
            return device switch {
                ShipWeapon weapon => weapon.Comp?.Tile == tile,
                ShipShield shield => shield.Comp?.Tile == tile,
                ShipEngine engine => engine.Comp?.Tile == tile,
                ShipCapacitor capacitor => capacitor.Comp?.Tile == tile,
                ShipVent vent => vent.Comp?.Tile == tile,
                _ => false,
            };
        }

        public ShipEngine AddEngine(ShipEngineDef engine) {
            var e = new ShipEngine();
            e.Phase = ShipEnginePhase.Inactive;
            e.Device = new ShipDevice(++LastDeviceId, Ship, engine.Device);
            e.EngineDef = engine;
            AddDevice(e);
            return e;
        }

        public ShipWeapon AddWeapon(ShipWeaponDef weapon) {
            var w = new ShipWeapon();
            w.Phase = ShipWeaponPhase.Inactive;
            w.Device = new ShipDevice(++LastDeviceId, Ship, weapon.Device);
            w.WeaponDef = weapon;
            AddDevice(w);
            return w;
        }
        public ShipShield AddShield(ShipShieldDef shield) {
            var sh = new ShipShield();
            sh.Phase = ShipShieldPhase.Inactive;
            sh.Device = new ShipDevice(++LastDeviceId, Ship, shield.Device);
            sh.ShieldDef = shield;
            AddDevice(sh);
            return sh;
        }

        public ShipCapacitor AddCapacitor(ShipCapacitorDef cap) {
            var capacitor = new ShipCapacitor();
            capacitor.Phase = ShipCapacitorPhase.Inactive;
            capacitor.Device = new ShipDevice(++LastDeviceId, Ship, cap.Device);
            capacitor.CapacitorDef = cap;
            AddDevice(capacitor);
            Flux.UpdateCapacity(this);
            return capacitor;
        }

        public ShipVent AddVent(ShipVentDef ventDef) {
            var vent = new ShipVent();
            vent.Device = new ShipDevice(++LastDeviceId, Ship, ventDef.Device);
            vent.VentDef = ventDef;
            AddDevice(vent);
            Flux.UpdateCapacity(this);
            return vent;
        }

        /// <returns>if false, we don't have to create any additional effects</returns>
        public bool OnHit(GameState s, ShipWeaponShot shot) {
            if (!Ship.IsInCombat) { return false; }
            LastAttacker = shot.SourceShip;
            if (Ship.IsPlayer) {
                shot.HitPlayerShip(s, shot.TargetPos, shot.HitTile);
                return true;
            }
            // D.Warn("Dealing damage to ship: {0}/{1} - {2}. {3}",
            //     HP, MaxHP, shot.Weapon.Damage, shot.SourceShip);
            var dmg = shot.Weapon.Damage;
            if (Flux.IsOverloaded) {
                dmg *= 1f - Tunables.ShipFluxOverloadArmorBonus;
            }
            HP -= dmg;
            DamageText.ShowShipHit(Ship, shot.TargetPos, (int) dmg);
            if (HP <= 0f) {
                HP = 0f;
                Explosions.CreateExplosionFX(s, Ship.View.Position,
                    Ship.Width * 0.5f, 10000, true);
                s.Query.EndShipCombatWith.Ask(Ship, ShipStateFlags.Derelict);
            } else {
                var er = shot.Weapon.WeaponDef.ExplosionRadius;
                Explosions.CreateExplosionFX(s, shot.TargetPos,
                    er, dmg, false);
            }
            onHPChanged?.Invoke();
            return true;
        }

        public ShipWeapon GetClosestAvailablePointDefense(Vector2 position) {
            ShipWeapon closest = null;
            float dist = float.MaxValue;
            foreach (var w in Weapons) {
                if (w.Phase != ShipWeaponPhase.Ready) { continue; }
                if (w.WeaponDef.WeaponType != ShipWeaponType.PointDefense) {
                    continue;
                }
                var d = Vectors.SqrDistance(position, w.Device.Position);
                if (d < dist) {
                    dist = d;
                    closest = w;
                }
            }
            return closest;
        }

        public void SetFluxStrategy(ShipCombatFluxStrategyDef strat) {
            // D.Warn("Setting flux strategy for ship: {0} | {1}", strat.Id, Ship);
            StrategyFlux = strat;
            WeaponSettings.ShootFluxThreshold = strat.ShootFluxThreshold;
            FluxSettings.ToleratedFluxTotal = strat.ToleratedFluxTotal;
            FluxSettings.ToleratedFluxHard = strat.ToleratedFluxHard;
            FluxSettings.VentFluxStopHard = strat.VentFluxStopHard;
            FluxSettings.VentFluxStopTotal = strat.VentFluxStopTotal;
        }
        public void SetWeaponStrategy(ShipCombatWeaponStrategyDef strat) {
            // D.Warn("Setting weapon strategy for ship: {0} | {1}", strat.Id, Ship);
            StrategyWeapon = strat;
            if (!Arrays.IsEmpty(strat.TargetPriorityKeys)) {
                for (int i = strat.TargetPriorityKeys.Length - 1; i >= 0; i--) {
                    string key = strat.TargetPriorityKeys[i];
                    int val = strat.TargetPriorityVals[i];
                    if (key.Contains('*')) {
                        foreach (var tt in ShipWeaponTargetTypeDef.All.Values) {
                            if (Strings.Matches(tt.Id, key)) {
                                WeaponSettings.SetPriorityFor(tt, val);
                            }
                        }
                    } else {
                        var target = ShipWeaponTargetTypeDef.Get(key);
                        if (target != null) {
                            WeaponSettings.SetPriorityFor(target, val);
                        }
                    }
                }
            }
        }
        public void SetTargetStrategy(ShipCombatTargetStrategyDef strat) {
            // D.Warn("Setting target strategy for ship: {0} | {1}", strat.Id, Ship);
            StrategyTarget = strat;
            NextTargetSwitchTick = 0;
        }
        /// <param name="isPlayerSide">true for the player's NPC allies</param>
        public void SetStrategies(NPCShip opponent, bool isPlayerSide) {
            if (Ship.IsPlayer) {
                D.Err("Cannot set strategies for player's ship!");
                return;
            }
            WeaponSettings = ShipWeaponSettings.Default;
            FluxSettings = ShipFluxSettings.Default;
            if (StrategyFlux == null) {
                ShipCombatFluxStrategyDef.ChooseFor(Ship, opponent, false);
                // D.Warn("NPC ship will use flux strategy: {0}:{1}",
                //     StrategyFlux.Id, this);
            }
            if (StrategyWeapon == null) {
                ShipCombatWeaponStrategyDef.ChooseFor(Ship, opponent);
                // D.Warn("NPC ship will use weapon strategy: {0}:{1}",
                //     StrategyWeapon.Id, this);
            }
            if (StrategyTarget == null) {
                ShipCombatTargetStrategyDef.ChooseFor(Ship, opponent, isPlayerSide);
            }
        }

        public void CleanupAfterCombat() {
            if (Ship.IsPlayer) {
                D.Err("Cannot clear strategies for player's ship!");
                return;
            }
            IsNegotiating = false;
            LastAttacker = null;
            FocusedEnemy = null;
            NextTargetSwitchTick = 0;
            BreachCapsulesSent = 0;
            NextBreachCapsuleTick = 0;
            WeaponsShotThisTick = 0;
            WeaponSettings = null;
            StrategyFlux = null;
            StrategyWeapon = null;
            StrategyTarget = null;
            NPCShipBehavior_Sector_Combat.CleanupMemoryFor(Ship);
        }

        public void DealRandomDamage(float dmg, Rng rng, NPCShip source) {
            if (source != null) {
                LastAttacker = source;
            }
            if (rng.Chance(0.75f)) {
                var device = rng.From(Devices);
                if (device.Device.Health > 0) {
                    device.Device.Health -= dmg;
                    if (device.Device.Health <= 0) {
                        device.Device.Health = 0;
                    }
                    return;
                }
                // Dead device wreckage still absorbs most hull bleed-through
                dmg *= Tunables.ShipHullDamageOnDeadDeviceHitMul;
            }

            if (Flux.IsOverloaded) {
                dmg *= 1f - Tunables.ShipFluxOverloadArmorBonus;
            }
            if (MaxHP * 0.25f < dmg) {
                dmg *= 0.25f;
            }
            HP -= dmg;
        }

        private readonly List<IShipDevice> damagedDevices = new();

        public void RepairDevices(long ticks) {
            damagedDevices.Clear();
            for (int i = Devices.Count - 1; i >= 0; i--) {
                IShipDevice device = Devices[i];
                if (device.Device.Health <= 0f && Ship.IsInCombat) { continue; }
                if (device.Device.Health < device.Device.HealthMax) {
                    if (device.Device.CanRepairAfter > ticks) { continue; }
                    damagedDevices.Add(device);
                }
            }
            if (damagedDevices.Count > 0) {
                Arrays.Shuffle(Rng, damagedDevices);
                // SoftLevel: 0.1/0.5/1.0/2.0/~3.0 across difficulties.
                // At NPCDeviceCombatRepairMul=0.5: Balanced repairs at half old rate;
                // Challenging matches old Balanced; Impossible gets ~1.5x.
                var repairScale = Ship.SO.S.Difficulty.SoftLevel * Tunables.NPCDeviceCombatRepairMul;
                var repairBudget = Ship.Class.NPCDeviceRepairHealthPerHour / Consts.TicksPerHour;
                repairBudget *= repairScale;
                // D.Warn("Repairing devices: {0} | {1}", repairBudget, damagedDevices.Count);
                foreach (var dev in damagedDevices) {
                    var device = dev.Device;
                    var dmg = device.HealthMax - device.Health;
                    var toHeal = Mathf.Min(dmg, repairBudget);
                    toHeal = Mathf.Min(toHeal, Tunables.NPCDamageCombatRepairMaxPerTick * repairScale);
                    // D.Warn("Repairing device: {0} | {1}", device, toHeal);
                    device.Health += toHeal;
                    repairBudget -= toHeal;
                    dev.UpdateHealth();
                    if (repairBudget <= 0f) { break; }
                }
                damagedDevices.Clear();
            }
        }

        public void ResetAmmoState() {
            if (Ship.IsPlayer) {
                D.Err("Refusing to reload player ship weapons!");
                return;
            }
            var rng = Rng.Fork();
            foreach (var weapon in Weapons) {
                var cap = weapon.WeaponDef.AmmoCapacity;
                if (cap == 0) { continue; }
                weapon.Ammo = cap;
            }
            GenerateAmmoStock(rng);
        }

        /// <summary>
        /// Fills the reserve ammunition used to reload weapons mid combat, leaving the
        /// weapons themselves alone. Takes the Rng to use, because loading a legacy save
        /// has to generate stock without advancing the restored combat Rng.
        /// </summary>
        public void GenerateAmmoStock(Rng rng) {
            var diff = A.S.Difficulty.Level;
            AmmoStock.Clear();
            foreach (var weapon in Weapons) {
                var cap = weapon.WeaponDef.AmmoCapacity;
                if (cap == 0) { continue; }
                if (!AmmoStock.TryGetValue(weapon.WeaponDef, out var stock)) {
                    stock = 0;
                }
                stock += Mathf.RoundToInt(rng.Range(cap, cap * 0.5f * diff));
                AmmoStock[weapon.WeaponDef] = stock;
                // D.Warn("Generated ammo stock for {0}: {1}: {2}", Ship, weapon, stock);
            }
        }

        public void RepairHull() {
            if (HP >= MaxHP) { return; }
            HP += MaxHP *
                Tunables.ShipPostCombatRepairPercentPerHour / Consts.TicksPerHour;
            if (HP > MaxHP) {
                HP = MaxHP;
            }
        }

        public void SetNextBlunder() {
            var ticks = Rng.Range(
                Tunables.NPCShipFluxBlunderDelayHoursFrom,
                    Tunables.NPCShipFluxBlunderDelayHoursTo) * Consts.TicksPerHour;
            if (StrategyFlux != null) {
                // weakCap = how long a Strength=0 strategy lasts relative to Strength=1.
                // At difficulty=1 a pure-dumb strategy uses ~25% of cycle time,
                // at difficulty=5 ~6% (near zero), at difficulty=0.1 ~77%.
                var d = Mathf.Max(0.01f, A.S.Difficulty.Level);
                var weakCap = 1f / (3f * d);
                ticks *= Mathf.Lerp(weakCap, 1f, StrategyFlux.Strength);
            }
            // D.Warn("Ship will do next combat blunder in {0} | {1}",
            //     Units.TicksToTime((int) ticks), Ship);
            NextNPCFluxBlunderAt = G.Ticks + (int) ticks;
        }
    }
}
