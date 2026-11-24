#if !PRODUCTION_BUILD
// #define DEBUG_NON_SHIP_TARGET
#endif
using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Text;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipWeapon : IShipDevice, INamedEntity {
        public ShipWeaponDef WeaponDef;
        public ShipDevice Device { get; set; }
        public Def Def => Device.Def;
        public float Damage => WeaponDef.Damage * Rng.URange(0.9f, 1.1f);
        public ShipWeaponStats Stats;
        public ShipWeaponPhase Phase;
        public ShipWeaponComp Comp;
        public BattleStationComp BackingStation;
        public float AccuracyBonusFromBattleStation = 1.0f;
        public bool UseSettings;
        public ShipWeaponSettings Settings;
        public bool SkippedShotDueToFlux;
        public long NextReloadTick;
        public bool IsPointDefense => WeaponDef.WeaponType == ShipWeaponType.PointDefense;

        public string EntityName {
            get {
                if (Comp != null) {
                    return Comp.Tile.EntityName;
                }
                return Def.NameT;
            }
        }
        public ShipWeaponSettings EffectiveSettings {
            get {
                ShipWeaponSettings s = null;
                if (UseSettings) {
                    s = Settings;
                }
                if (s == null) {
                    s = Device.Ship.CombatState.WeaponSettings;
                }
                return s;
            }
        }
        public NPCShip TargetShip;
        public IShipWeaponTarget Target;
        #if DEBUG_NON_SHIP_TARGET
        private IShipCombatTurretTarget nonShipTarget;
        public IShipCombatTurretTarget NonShipTarget {
            get => nonShipTarget;
            set => D.SetFieldWithDebug(ref nonShipTarget, value, "NonShipTarget");
        }
        #else
        public IShipCombatTurretTarget NonShipTarget;
        #endif
        public Action<NPCShip, IShipWeaponTarget> OnSetTarget;
        public Action OnPhaseChange;
        public Action OnPhaseProgressChange;

        public int PhaseTicksLeft;
        public int PhaseTicksTotal;
        public bool hasAppliedOverloadPenalty;
        public int Ammo;
        /// <summary>
        /// TargetShipId, Target.Id and NonShipTarget.Id, so we can restore it
        /// after loading everything
        /// </summary>
        public (uint, uint, int) TargetData;

        public float CurrentEnergyCost {
            get {
                switch (Phase) {
                    case ShipWeaponPhase.OutOfAmmo: return 0f;
                    case ShipWeaponPhase.Inactive:
                    case ShipWeaponPhase.Disabled: return Device.DeviceDef.EnergyCostPassive;
                    case ShipWeaponPhase.Charging:
                    case ShipWeaponPhase.Windup:
                    case ShipWeaponPhase.Firing:
                    case ShipWeaponPhase.Ready: return Device.DeviceDef.EnergyCostActive;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 0f;
                    }
                }
            }
        }
        public float PhasePercent {
            get {
                switch (Phase) {
                    case ShipWeaponPhase.Inactive: return 0f;
                    case ShipWeaponPhase.Disabled: return 0f;
                    case ShipWeaponPhase.Ready: return 1f;
                    case ShipWeaponPhase.Charging:
                    case ShipWeaponPhase.Windup:
                    case ShipWeaponPhase.Firing: {
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                    case ShipWeaponPhase.OutOfAmmo: {
                        return Device.Ship.IsPlayer ? 0f : 1f;
                    }
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                }
            }
        }
        public bool PhaseHasProgress {
            get {
                switch (Phase) {
                    case ShipWeaponPhase.Inactive:
                    case ShipWeaponPhase.OutOfAmmo:
                    case ShipWeaponPhase.Disabled: return false;
                    case ShipWeaponPhase.Ready:
                    case ShipWeaponPhase.Windup:
                    case ShipWeaponPhase.Firing:
                    case ShipWeaponPhase.Charging: return true;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return true;
                    }
                }
            }
        }
        public string PhaseIcon {
            get {
                switch (Phase) {
                    case ShipWeaponPhase.Inactive: return IconId.CPeace;
                    case ShipWeaponPhase.Charging: return IconId.CLoading;
                    case ShipWeaponPhase.Windup: return IconId.CTime;
                    case ShipWeaponPhase.Ready: {
                        if (SkippedShotDueToFlux || IsHeldByVent) {
                            return IconId.CWarning;
                        }
                        return IconId.CAttackTarget;
                    }
                    case ShipWeaponPhase.Firing: return IconId.CAttackTarget;
                    case ShipWeaponPhase.Disabled: return IconId.CForbid;
                    case ShipWeaponPhase.OutOfAmmo:  return IconId.CNoAmmo;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return IconId.CWarning;
                    }
                }
            }
        }
        /// <summary>
        /// OutOfAmmo is deliberately not here, we don't know
        /// </summary>
        public bool IsActive => Phase != ShipWeaponPhase.Inactive && Phase != ShipWeaponPhase.Disabled;
        public bool CanShoot => IsActive && Phase != ShipWeaponPhase.OutOfAmmo;
        public bool IsReachable => Comp == null || Comp.Tile.ENode == null
            || Comp.Tile.ENode.IsReachable;
        /// <summary>
        /// Whether this weapon would fire in a fight. Phase alone cannot answer that: out of
        /// combat ShouldBeActive is false, so Tick parks every weapon in Inactive. Mirrors the
        /// Tick gates that do not depend on Ship.IsInCombat.
        /// </summary>
        public bool CanBecomeActive => !HasNoStation && Device.IsOperational;

        // Out of combat a weapon parks until a threat shows up, and the windup made incoming
        // threats nearly impossible to intercept. A battle station the player keeps manned
        // (its Keep Operator toggle) holds its weapons ready, for a colonist's time at the
        // station and the weapons' active power (STA-1910).
        public bool IsHeldReadyByStation => Device.Ship.IsPlayer
            && BackingStation?.Operatable?.Operator != null;

        /// <summary>
        /// Enemy ships always come first: space hazards are ignored while this ship is
        /// in combat. Threats launched by enemy ships (e.g. breach capsules) stay
        /// engageable.
        /// </summary>
        public bool CanEngage(IShipCombatTurretTarget target) {
            if (!target.TargetType.SkipDuringShipCombat) { return true; }
            return !Device.Ship.IsInCombat;
        }

        public bool HasEngageableNonShipTarget =>
            NonShipTarget != null && CanEngage(NonShipTarget);

        // While the vent is open, weapons hold fire at ships; space threats are still shot and
        // point defense keeps intercepting. Venting used to only discount the flux of shots, so
        // a ship vented through its own volley and had its shields back for the enemy's answer
        public bool IsHeldByVent => !IsPointDefense && Device.Ship.IsInCombat
            && !HasEngageableNonShipTarget
            && Device.Ship.CombatState.Flux.State == ShipFluxState.Venting;

        public bool SetPhase(GameState s, ShipWeaponPhase phase) {
            if (phase == Phase) { return false; }
            // D.Warn("Weapon phase: {0} -> {1}: {2}", Phase, phase, this);
            var speed = Comp != null ? Comp.UpgradeSpeed : 1f;
            switch (phase) {
                case ShipWeaponPhase.Windup:
                case ShipWeaponPhase.Inactive: {
                    PhaseTicksTotal = PhaseTicksLeft = Maths.Max(1,
                        Mathf.RoundToInt((WeaponDef.WindupTimeMinutes + Rng.URange(-3, 3)) / speed));
                    break;
                }
                case ShipWeaponPhase.Charging: {
                    PhaseTicksTotal = PhaseTicksLeft = Maths.Max(1,
                        Mathf.RoundToInt((WeaponDef.ChargeTimeMinutes + Rng.URange(-2, 2)) / speed));
                    break;
                }
                case ShipWeaponPhase.Firing: {
                    PhaseTicksTotal = PhaseTicksLeft = Maths.Max(1,
                        WeaponDef.FiringTimeMinutes + Rng.URange(-1, 1));
                    break;
                }
                case ShipWeaponPhase.Ready: {
                    Device.Ship.SetShootHesitation();
                    break;
                }
                case ShipWeaponPhase.Disabled: { break; }
                case ShipWeaponPhase.OutOfAmmo: { break; }
                default: {
                    D.Err("Unhandled phase: {0}", phase);
                    break;
                }
            }
            Phase = phase;
            OnPhaseChange?.Invoke();
            s.Sig.ShipDevicePhaseChanged.Send(this);
            return true;
        }

        /// <returns>True if UI needs to be rebuilt</returns>
        public bool Tick(GameState s, long ticks) {
            if (WeaponDef.AmmoCapacity > 0) {
                if (Comp != null) {
                    // update ammo for player's device
                    Ammo = (int) Comp.Ammo.CurrentStock;
                }
                if (Ammo < 1) {
                    if (Phase != ShipWeaponPhase.OutOfAmmo) {
                        if (!Device.Ship.IsPlayer) {
                            NextReloadTick = G.Ticks + Device.Ship.CombatState.Rng.Range(
                                Tunables.NPCShipWeaponReloadDelayMinutesFrom,
                                Tunables.NPCShipWeaponReloadDelayMinutesTo);
                            // D.Warn("Out of ammo, will reload weapon {0} at {1}",
                            //     this, NextReloadTick);
                        }
                        return SetPhase(s, ShipWeaponPhase.OutOfAmmo);
                    }
                    return false;
                } else {
                    if (Phase == ShipWeaponPhase.OutOfAmmo) {
                        return SetPhase(s, ShipWeaponPhase.Inactive);
                    }
                }
            }

            if (NonShipTarget != null &&
                    (!CanEngage(NonShipTarget)
                        || !NonShipTarget.IsThreatActive || !NonShipTarget.Entity.IsActive)) {
                if (Comp.Turret?.Target == NonShipTarget) {
                    Comp.Turret.SetTarget(null);
                }
                NonShipTarget = null;
            }

            if (!Device.ShouldBeActive && NonShipTarget == null && !IsHeldReadyByStation) {
                if (Phase != ShipWeaponPhase.Inactive) {
                    hasAppliedOverloadPenalty = false;
                    return SetPhase(s, ShipWeaponPhase.Inactive);
                }
                return false;
            }
            if (HasNoStation) {
                if (Phase != ShipWeaponPhase.Inactive) {
                    return SetPhase(s, ShipWeaponPhase.Inactive);
                }
                return false;
            }
            if (!Device.IsOperational || Device.IsMagneticStormAffected(s)
                    || Device.Ship.AreWeaponsHacked) {
                if (Phase != ShipWeaponPhase.Disabled) {
                    return SetPhase(s, ShipWeaponPhase.Disabled);
                }
                return false;
            } else if (Phase == ShipWeaponPhase.Disabled) {
                return SetPhase(s, ShipWeaponPhase.Windup);
            }
            if (Device.Ship.CombatState.Flux.IsOverloaded) {
                if (!hasAppliedOverloadPenalty) {
                    SetPhase(s, ShipWeaponPhase.Windup);
                    hasAppliedOverloadPenalty = true;
                }
            } else {
                hasAppliedOverloadPenalty = false;
            }
            var ticksLeft = PhaseTicksLeft - 1;
            // change phase
            switch (Phase) {
                case ShipWeaponPhase.Inactive: {
                    if (WeaponDef.AmmoCapacity > 0) {
                        // cannot activate
                        if (Ammo < 1) { break; }
                    }
                    SetPhase(s, ShipWeaponPhase.Windup);
                    break;
                }
                case ShipWeaponPhase.Ready: {
                    break;
                };
                case ShipWeaponPhase.Windup: {
                    if (ticksLeft < 1) {
                        SetPhase(s, ShipWeaponPhase.Ready);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                    }
                    break;
                }
                case ShipWeaponPhase.Firing: {
                    if (ticksLeft < 1) {
                        SetPhase(s, ShipWeaponPhase.Charging);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                    }
                    break;
                }
                case ShipWeaponPhase.Charging: {
                    if (ticksLeft < 1) {
                        SetPhase(s, ShipWeaponPhase.Ready);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                    }
                    break;
                }
                default: {
                    D.Err("Unhandled phase: {0}", Phase);
                    break;
                }
            }
            return false;
        }

        public void Reset() {
            Phase = ShipWeaponPhase.Inactive;
            PhaseTicksLeft = 0;
            PhaseTicksTotal = 0;
        }

        public bool Shoot(GameState S, Rng rng) {
            if (Phase != ShipWeaponPhase.Ready) {
                D.Err("Trying to shot weapon when phase != Ready: {0}|{1}",
                    Phase, this);
                return false;
            }
            if (Comp != null) {
                D.Warn("Shooting player weapon: {0}", this);
            }
            if (WeaponDef.AmmoCapacity > 0) {
                D.FIXME_LATER("Ammo might be required to display somewhere in the new ui");
                if (Comp != null) { // player's weapon
                    if (!Comp.Ammo.TryConsume(1f)) {
                        D.Err("Ship weapon shot with no ammo: {0}", this);
                        SetPhase(S, ShipWeaponPhase.OutOfAmmo);
                        Ammo = 0;
                        return false;
                    }
                    Ammo = (int) Comp.Ammo.CurrentStock;
                } else { // NPC ship weapon
                    if (Ammo < 1) {
                        D.Err("Ship weapon shot with no ammo: {0}", this);
                        SetPhase(S, ShipWeaponPhase.OutOfAmmo);
                        return false;
                    }
                    Ammo--;
                }
            }
            SetPhase(S, ShipWeaponPhase.Firing);

            if (HasEngageableNonShipTarget) {
                if (!NonShipTarget.IsThreatActive || !NonShipTarget.Entity.IsActive) {
                    NonShipTarget = null;
                    // ShipWeaponComp's SetTarget will handle turret targeting
                    Comp.SetTarget(null);
                } else {
                    ShootNonShipTarget(S, rng);
                }
                return true;
            }

            if (TargetShip == null) {
                D.Err("Trying to shoot when target ship is null");
                return false;
            }

            if (TargetShip.View == null && !TargetShip.IsPlayer) {
                D.Err("Trying to shoot a ship without a view: {0}", TargetShip);
                return false;
            }

            bool hitHull = false;
            bool hitTarget = false;
            bool hitShield = false;
            bool isPDSuccess = false;

            ShipWeapon hitPointDefense = null;
            if (WeaponDef.WeaponType == ShipWeaponType.Missile) {
                // check if opponent has an active point defense, if so, use it
                hitPointDefense = TargetShip.CombatState
                    .GetClosestAvailablePointDefense(Device.Position);
                if (hitPointDefense != null) {
                    var pdHitChance = hitPointDefense.WeaponDef.Accuracy;
                    isPDSuccess = rng.Chance(pdHitChance);
                    if (isPDSuccess) {
                        hitHull = true;
                        hitShield = true;
                    }
                }
            }
            if (!isPDSuccess) {
                float evadeChance = TargetShip.CombatState.EvasionPercent;
                hitHull = !rng.Chance(evadeChance);
                // D.Warn("Ship evade chance: {0}->{1}. Hit: {2}",
                //     evadeChance, TargetShip.Name, hitHull);

                if (hitHull && TargetShip.CombatState.IsShieldUp) {
                    // chance to penetrate shield
                    var chanceToPenetrateShield = WeaponDef.ShieldPenetrateChance;
                    if (!rng.Chance(chanceToPenetrateShield)) {
                        hitShield = true;
                    }
                }

                if (hitHull && !hitShield && Target != null) {
                    float targetHitChance = WeaponDef.Accuracy
                        * AccuracyBonusFromBattleStation;
                    hitTarget = rng.Chance(targetHitChance);
                    // if (!Device.Ship.IsPlayer) {
                        // D.Warn("Ship weapon hit target: {0}->{1}. Hit: {2}",
                        //     Target, targetHitChance, hitTarget);
                    // }
                }
            }

            var targetPos = GetTargetPos(S, hitHull, hitTarget, hitShield,
                isPDSuccess, out var hitTile);

            var shot = new ShipWeaponShot();
            shot.IsHit = (hitHull || hitTarget) && !hitShield && !isPDSuccess;
            shot.Weapon = this;
            shot.TargetPos = targetPos;
            shot.TargetShip = TargetShip;
            shot.Target = Target;
            shot.HitTile = hitTile;
            shot.HitPointDefense = hitPointDefense;
            shot.DamageType = WeaponDef.DamageType;
            shot.OnEndAnimate = (s, shot) => {
                if (!hitHull) { return; }
                if (!shot.TargetShip.IsInCombat) { return; }
                if (shot.TargetShip.CombatState.IsNegotiating) {
                    // deactivate missile
                    return;
                }
                if (isPDSuccess) {
                    // D.Warn("Point defense attack shot ended: {0}", targetPos);
                    Explosions.CreateExplosionFX(s, shot.TargetPos, 1f, 100f, false);
                    return;
                }
                if (hitShield) {
                    Sounds.PlayLoud(shot.TargetPos, "FX/ShieldHitSFX", true);
                    if (s.Clock.Speed < ClockSpeed.Unlimited) {
                        Explosions.AddRipple(shot.TargetPos, WeaponDef.ExplosionRadius, Damage);
                    }
                    if (!shot.TargetShip.IsPlayer) {
                        shot.TargetShip.View.ShakeShield(WeaponDef.ShieldShakeAmt);
                    } else {
                        S.Query.ShakePlayerShipShield.Ask(WeaponDef.ShieldShakeAmt);
                    }
                    shot.TargetShip.CombatState.Flux.AddFlux(S,
                        WeaponDef.SoftFluxPerShieldHit, WeaponDef.HardFluxPerShieldHit);
                } else {
                    if (hitTarget && shot.Target.IsDestroyed) {
                        ClearTarget_ShipDevice();
                        hitTarget = false;
                    }
                    if (hitTarget) {
                        // hit precision target
                        shot.Target.OnHit(S, shot);
                    } else {
                        // hit hull
                        shot.TargetShip.CombatState.OnHit(S, shot);
                    }
                }
            };

            S.Query.ShootShipWeapon.Ask(shot);

            if (hitPointDefense != null) {
                if (!isPDSuccess) {
                    // fake point defense success, but it will miss
                    targetPos = GetTargetPos(S, true, true, true, true, out _);
                }

                var weaponPos = Device.Position;
                var weaponDistToTarget = Vectors.Distance(targetPos, weaponPos);
                var pdDistToTarget = Vectors.Distance(targetPos, hitPointDefense.Device.Position);
                var distRatio = pdDistToTarget / weaponDistToTarget;
                var pdProjectileSpeed = WeaponDef.ProjectileSpeed * distRatio;

                var pdShot = new ShipWeaponShot();
                pdShot.IsHit = isPDSuccess;
                pdShot.Weapon = hitPointDefense;
                // a failed interception flies on instead of stopping where the missile was
                // (same speed, so it still crosses the missile's path on time)
                pdShot.TargetPos = isPDSuccess ? targetPos
                    : FlyPast(hitPointDefense.Device.Position, targetPos);
                pdShot.OverrideProjectileSpeed = pdProjectileSpeed * 1.25f;
                hitPointDefense.SetPhase(S, ShipWeaponPhase.Firing);
                // consume player's point defense ammo
                hitPointDefense.Comp?.Ammo?.TryConsume(1f);
                if (hitPointDefense.Ammo > 0) {
                    hitPointDefense.Ammo--;
                }
                // pdShot.OnEndAnimate = (s, shot) => {
                //     D.Warn("Point defense defense shot ended: {0}", targetPos);
                // };
                S.Query.ShootShipWeapon.Ask(pdShot);
            }
            return true;
        }

        private void ShootNonShipTarget(GameState s, Rng rng) {
            var hitChance = WeaponDef.Accuracy;
            var evadePercent = IsPointDefense ?
                NonShipTarget.PointDefenseEvadePercent : NonShipTarget.TurretEvadePercent;
            var adj = Mathf.Clamp(1f - evadePercent, 0.01f, 1f);
            hitChance *= adj;
            var hit = rng.Chance(hitChance);
            // D.Warn("Shooting at non ship target: chance: {0} -> {1}",
            //     hitChance, hit);
            var shot = new ShipWeaponShot();
            shot.IsHit = hit;
            shot.Weapon = this;
            // we're targeting where the target is moving, not the current position
            var dist = Vectors.Distance(Device.Position, NonShipTarget.Position);
            float projectileReachTime = dist / WeaponDef.ProjectileSpeed;
            // D.Warn("projectile will reach target in {0} seconds", projectileReachTime);
            shot.TargetPos = hit ? NonShipTarget.PredictPosition(projectileReachTime)
                : NonShipTargetMissPosition(NonShipTarget.Position, Device.Position);
            SetPhase(s, ShipWeaponPhase.Firing);
            // The turret can drop or switch its target while the shot flies: the hit belongs
            // to what was aimed at
            var target = NonShipTarget;
            shot.OnEndAnimate = (s, shot) => {
                if (!hit) { return; }
                var dmg = Damage;
                // the shot still lands where it was aimed, even when the target is gone
                Explosions.CreateExplosionFX(s, shot.TargetPos, 2f, dmg, false);
                // it could have been destroyed or have landed in the meantime
                if (!target.IsThreatActive || !target.Entity.IsActive) { return; }
                if (target.Entity is Obj obj) {
                    obj.Health.TakeDamage(dmg, WeaponDef.DamageType);
                    if (obj.Health.IsDestroyed && NonShipTarget == target) {
                        NonShipTarget = null;
                        Comp.SetTarget(null);
                    }
                } else {
                    D.Err("TODO handle non ship target: {0}", target);
                }
            };
            s.Query.ShootShipWeapon.Ask(shot);
        }

        // A miss flies on past the target and off the map: one that stopped beside the
        // target with no explosion read as a hit that did nothing
        public Vector2 NonShipTargetMissPosition(Vector2 target, Vector2 shooter) {
            float offset;
            if (IsPointDefense) {
                offset = Rng.URange(4f, 8f);
            } else if (WeaponDef.WeaponType == ShipWeaponType.Missile) {
                offset = Rng.URange(8f, 12f);
            } else {
                var dist = Vectors.Distance(target, shooter);
                offset = Rng.URange(dist * 0.1f, dist * 0.15f);
            }
            var missPos = target + Rng.UInsideUnitCircle().normalized * offset;
            return FlyPast(shooter, missPos);
        }

        private static Vector2 FlyPast(Vector2 from, Vector2 through) {
            var dir = (through - from).normalized;
            return from + dir * (Vectors.Distance(from, through) + Pos.GridW);
        }

        private Vector2 GetTargetPos(GameState S, bool hitHull, bool hitTarget,
                bool hitShield, bool hitPointDefense, out Tile obstacle) {
            obstacle = null;
            if (hitPointDefense) {
                hitShield = true;
                hitHull = true;
                hitTarget = false;
            }
            if (TargetShip.IsPlayer) {
                var b = S.Sys.Areas.ShipBounds;
                if (hitHull) {
                    if (hitShield) {
                        var shield = S.Query.GetPlayerShield.Ask();
                        Vector2 pos;
                        if (Target != null)  {
                            pos = Target.Position;
                        } else {
                            pos = shield.Item2 + Rng.UInsideUnitCircle() * shield.Item3;
                        }

                        // if we hit point defense, shield would have been penetrated
                        if (hitPointDefense) {
                            // hit above
                            return pos + new Vector2(Rng.URange(-10f, 10f), Rng.URange(20f, 30f));
                        }
                        var dir = (pos - Device.Position).normalized;
                        if (Maths.FindIntersectionPoint(Device.Position, dir,
                            shield.Item2, shield.Item3 * 0.95f, out var point)) {
                            return point;
                        } else {
                            return pos;
                        }
                    } else if (hitTarget) {
                        // hit precision target
                        return Target.Position;
                    } else {
                        // hit hull
                        var pos = b.Center() + Rng.UInsideUnitCircle() * b.Extents * 0.5f;
                        if (WeaponDef.WeaponType == ShipWeaponType.Ballistic) {
                            // hitting the closest wall
                            obstacle = EntityUtils.FirstObstacleAlongTrajectory(
                                Device.Position, pos);
                            if (obstacle != null) {
                                return obstacle.Position;
                            }
                        }
                        return pos;
                    }
                } else {
                    // miss
                    Vector2 pos;
                    if (Target != null)  {
                        pos = Target.Position + Rng.UInsideUnitCircle() * 2f;
                    } else {
                        pos = b.Center() + Rng.UInsideUnitCircle() * b.Extents * 0.5f;
                    }
                    var dist = Vectors.Distance(Device.Position, pos);
                    var dir = (pos - Device.Position).normalized;
                    return Device.Position + dir * (dist + b.MaxEdgeLength * 2f);
                }
            }

            if (hitHull) {
                if (hitShield) {
                    return TargetShip.View.AdjustedHitPos(Device.Position,
                        Target, true, true, hitPointDefense);
                } else if (hitTarget) {
                    // hit precision target
                    return TargetShip.View.AdjustedHitPos(Device.Position,
                        Target, true, false, false);
                } else {
                    // hit hull
                    return TargetShip.View.AdjustedHitPos(Device.Position,
                        null, true, false, false);
                    // targetPos = b.Center() + Rng.UInsideUnitCircle() * b.Extents * 0.5f;
                }
            } else {
                return TargetShip.View.AdjustedHitPos(Device.Position, Target,
                    false, false, false);
                // targetPos = b.Center() + Rng.UInsideUnitCircle().normalized * b.Extents * 1.25f;
            }
        }

        public override string ToString() {
            return $"ShipWeapon[{WeaponDef.Id}|{Phase}|{PhaseTicksLeft}s|T:{Target}|TShip:{TargetShip?.Id}]";
        }

        public void ClearTarget_ShipDevice() {
            Target = null;
            OnSetTarget?.Invoke(TargetShip, null);
        }
        public void ClearTarget_Ship() {
            TargetShip = null;
            Target = null;
            OnSetTarget?.Invoke(null, null);
        }

        public void SetTarget(NPCShip ship) {
            if (!ship.IsInCombat || !Device.Ship.IsInCombat) {
                D.Err("Trying to target a ship that is not in combat: {0}: {1}",
                    ship, this);
                return;
            }
            TargetShip = ship;
            Target = null;
            OnSetTarget?.Invoke(ship, null);
        }

        public void SetTarget(IShipWeaponTarget target) {
            if (IsPointDefense) {
                D.Err("Trying to set target on a point defense: {0}->{1}", this, target);
                return;
            }
            if (!Device.Ship.IsInCombat || target?.Ship.IsInCombat == false) {
                D.Err("Trying to target a ship device of a ship that is not in combat: {0}: {1}->{2}",
                    target.Ship, this, target);
                return;
            }
            TargetShip = target?.Ship;
            Target = target;
            OnSetTarget?.Invoke(TargetShip, Target);
        }

        private static string readyTooMuchFlux;
        private static string readyVentOpen;

        public string PhaseName {
            get {
                switch (Phase) {
                    case ShipWeaponPhase.Charging: return T.Charging;
                    case ShipWeaponPhase.Firing: return T.Active;
                    case ShipWeaponPhase.Ready: {
                        if (IsHeldByVent) {
                            return readyVentOpen ??= $"{T.Ready} ({Texts.Yellow(T.VentOpen)})";
                        }
                        if (SkippedShotDueToFlux) {
                            return readyTooMuchFlux ??= $"{T.Ready} ({Texts.Yellow(T.TooMuchFlux)})";
                        }
                        return T.Ready;
                    }
                    case ShipWeaponPhase.Inactive: {
                        if (Target != null) {
                            return T.Inactive;
                        } else {
                            return T.NoTarget;
                        }
                    }
                    case ShipWeaponPhase.Disabled: return T.StateDisabled;
                    case ShipWeaponPhase.Windup: return T.StateWindup;
                    case ShipWeaponPhase.OutOfAmmo: return T.StateNoAmmo;
                    default: {
                        D.Err("Unhandled phase: {0}", Phase);
                        return Phase.ToString();
                    }
                }
            }
        }

        public string FullTooltip {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(WeaponDef.Def.CodexLink));
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints,
                    Texts.Accented(WeaponDef.Device.ClassPoints));
                sb.AppendFormat("{0}: {1}\n",
                    T.NounWeaponPower, Texts.Accented((int) WeaponDef.CombatStrength));

                var phasePct = PhasePercent;
                if (phasePct > 0f) {
                    sb.AppendFormat("{0}: {1}\n", PhaseName,
                        Texts.Accented(Units.Percentage(PhasePercent)));
                } else {
                    sb.AppendLine(PhaseName);
                }

                if (Phase == ShipWeaponPhase.Inactive
                        || Phase == ShipWeaponPhase.Disabled
                        || Phase == ShipWeaponPhase.OutOfAmmo) {
                    return sb.ToString();
                }

                return sb.ToString();
            }
        }

        public bool HasNoStation {
            get {
                if (IsPointDefense) { return false; }
                if (Comp != null && (BackingStation == null || !BackingStation.Tile.IsActive)) {
                    return true;
                }
                return false;
            }
        }

        public void UpdateHealth() {
            Device.ZoneView?.UpdateHealthBar();
        }
    }
}
