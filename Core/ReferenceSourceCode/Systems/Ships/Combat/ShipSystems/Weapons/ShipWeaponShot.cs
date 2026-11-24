using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipWeaponShot {
        public ShipWeapon Weapon;
        public Vector2 SourcePos => Weapon.Device.Position;
        public NPCShip SourceShip => Weapon.Device.Ship;
        public IShipWeaponTarget Target;
        public Vector2 TargetPos;
        public NPCShip TargetShip;
        public DamageType DamageType;
        public ShipWeapon HitPointDefense;
        public Tile HitTile;
        public float TimeTillEndAnimate;
        public float OverrideProjectileSpeed;
        public bool IsHit;
        public Action<GameState, ShipWeaponShot> OnEndAnimate;

        public void Execute(GameState s) {
            Weapon.Stats.RecordShot(IsHit);
            var def = Weapon.WeaponDef;
            SourceShip.CombatState.Flux.AddShotFlux(s,
                def.SoftFluxPerShot, def.HardFluxPerShot);
            var dist = Vectors.Distance(SourcePos, TargetPos);
            // TimeTillEndAnimate = dist / (def.ProjectileSpeed * def.ProjectileSpeedArcAdj);
            var projSpeed = def.ProjectileSpeed;
            if (OverrideProjectileSpeed > 0f) {
                projSpeed = OverrideProjectileSpeed;
            }
            TimeTillEndAnimate = dist / projSpeed;
            TurretGraphics.CreateShotFX(s, SourcePos, TargetPos,
                def.ProjectileVFX,
                DefIdsH.ResearchShipCombatRoot,
                Tunables.TurretShotMaxParticles,
                // allow to play longer
                TimeTillEndAnimate + 5f,
                def.ShotSoundFX,
                projSpeed,
                isLoud: true,
                loudVolume: def.ShotSoundVol);
        }

        public void HitPlayerShip(GameState s, Vector2 position, Entity entity) {
            var wd = Weapon.WeaponDef;
            var dmg = Weapon.Damage;
            if (wd.CreatesExplosion) {
                Explosion.Create(s, (int) wd.ExplosionRadius + 1,
                    position: position,
                    customRadius: wd.ExplosionRadius,
                    customStr: dmg);
                return;
            }

            string msg;
            if (entity is Tile tile) {
                // the shot landed: show it, or it reads as a miss
                Explosions.CreateExplosionFX(s, position, wd.ExplosionRadius, dmg, false);
                tile.Health.TakeDamage(dmg, wd.DamageType);
                msg = $"{Weapon.Def.NameT} ({Texts.Red(T.NounEnemy)}) {GlyphMap.ArrowRight} {Texts.LinkToTile(tile)}: {Texts.RedMinus}{Texts.Red(Units.Num((int) dmg))} ({Units.XOutOfY((int) tile.Health.Health, (int) tile.Health.MaxHealth)})";
                s.Sys.Log.AddLine(msg);
            } else if (entity is Being b) {
                BeingUtils.DamageRandomBodyPart(s, b.Data, s.Rng, wd.DamageType,
                    Severity.Critical, Weapon, 2f);
            } else if (entity is Obj o) {
                o.Health.TakeDamage(dmg, wd.DamageType);
                Explosion.Create(s, 1,
                    position: position,
                    customRadius: 1f,
                    customStr: dmg);
            } else {
                Explosion.Create(s, 1,
                    position: position,
                    customRadius: 1f,
                    customStr: dmg);
            }
        }
    }
}
