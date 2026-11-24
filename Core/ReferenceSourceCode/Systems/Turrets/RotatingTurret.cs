using System;
using Game.Components;
using Game.Data;
using Game.Systems.Ships;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Turrets {
    public class RotatingTurret {
        public TurretType Type;
        public Vector2 HeadOffset;
        public Vector2 TopOffset;
        public bool HasTopSprite;
        public bool IsScanning;
        public float HeadAngle;
        public float HeadRotDir;
        public float HeadScale;
        public string GraphicsId;
        public ITurretTarget Target;
        public Tile TurretTile;
        public bool IsRegistered;
        public string ShotFXPrefab;
        public float ShotFXLifetime;
        public string ShotSound;
        public float ShotSoundVol;
        public int DefIdH;
        public float Range;
        public float RangeSqr;
        public int SymmetryFrame;
        public int SpriteOrder;
        public Action CheckTarget;
        public Action<ITurretTarget> SetTarget;
        public Action<long> OnTick;
        public IAttackableTurret AttackableTurret;
        public ShipWeapon ShipWeapon;
        /// <summary>
        /// ticks, hasShot
        /// </summary>
        public Func<long, bool> TryShoot;
        public bool IsLoud;

        public RotatingTurret() {
            SetTarget = (t) => {
                D.Err("SetTarget not set for rotating turret: {0}. Target: {1}",
                    TurretTile, t);
            };
            TryShoot = (t) => {
                D.Err("TryShoot not set for rotating turret: {0}", TurretTile);
                return default;
            };
            CheckTarget = () => {
                D.Err("CheckTarget not set for rotating turret: {0}", TurretTile);
            };
        }

        public void Tick(long ticks) {
            if (OnTick == null) {
                IsScanning = TurretTile.ENode.IsConsuming &&
                    TurretTile.Reinstallable?.IsMidRelocation != true;
            } else {
                OnTick.Invoke(ticks);
            }
        }

        public override string ToString() {
            return $"RotatingTurret[Sc:{IsScanning},{TurretTile}]";
        }
    }
}
