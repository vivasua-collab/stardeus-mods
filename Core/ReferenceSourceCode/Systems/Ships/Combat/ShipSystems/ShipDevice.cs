using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Utils;
using Game.Visuals;
using KL.Randomness;
using KL.Text;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipDevice : IShipWeaponTarget {
        public uint Id { get; private set; }
        /// <summary>
        /// For showing number in UI
        /// </summary>
        public int SequenceNum {
            get => sequenceNum;
            set {
                if (sequenceNum == value) { return; }
                sequenceNum = value;
                nameWithSeq = null;
            }
        }
        public string TargetName => NameWithSeq;
        private int sequenceNum;
        private string nameWithSeq;
        public string NameWithSeq {
            get {
                if (nameWithSeq != null) {
                    return nameWithSeq;
                }
                if (sequenceNum < 1) {
                    nameWithSeq = Def.NameT;
                } else {
                    nameWithSeq = $"{sequenceNum}: {Def.NameT}";
                }
                return nameWithSeq;
            }
        }
        public readonly ShipDeviceDef DeviceDef;
        public ShipWeaponTargetTypeDef TargetType => DeviceDef.TargetType;
        public Def Def { get; private set; }
        public NPCShip Ship { get; private set; }
        public Tile Tile { get; private set; }
        public int HP => (int) Health;
        public int MaxHP => (int) HealthMax;
        public float Health;
        public float HealthMax;
        public long CanRepairAfter;
        public ITurretTarget TurretTarget => ZoneView;
        public bool IsDestroyed {
            get {
                if (Tile != null) {
                    return !Tile.IsActive;
                }
                return Health <= 0f;
            }
        }
        public bool IsOperational {
            get {
                if (Tile != null) {
                    if (!Tile.ENode.IsPowerable) { return false; }
                    if (Tile.ENode.HasDeficit) { return false; }
                    if (!Tile.Health.IsOperational) { return false; }
                    return true;
                }
                return Health > 0f;
            }
        }

        public Vector2 Position {
            get {
                if (Tile != null) {
                    return Tile.Position;
                }
                if (ZoneView != null) {
                    return ZoneView.Position;
                }
                D.Err("ShipDevice has neither a Tile nor ZoneView. Cannot get position! {0}", this);
                return Consts.V2Zero;
            }
        }
        public ShipZoneView ZoneView { get; private set; }
        public float HealthPercent => Tile != null ? Tile.Health.HealthPercent : Health / HealthMax;
        public bool ShouldBeActive => Ship.IsInCombat;

        public bool IsMagneticStormAffected(GameState s) {
            if (Ship.IsPlayer) { return false; }
            // if (!Ship.IsInCombat) { return false; }
            var mod = s.BB.Get(BBKeyH.MagneticStormAffectedMod, 0);
            if (mod == 0) { return false; }
            var id = s.BB.Get(BBKeyH.MagneticStormAffectedId, 0);
            return (int)(Id % (uint)mod) == id;
        }

        public Vector2 TargetSize => ZoneView.Size;

        public ShipDevice(uint id, NPCShip ship, ShipDeviceDef def, Tile tile = null) {
            Id = id;
            Ship = ship;
            DeviceDef = def;
            Def = def.Def;
            Health = def.HP;
            HealthMax = def.HP;
            SetTile(tile);
        }

        public bool SetTile(Tile tile) {
            bool isValid = true;
            if (tile != null && !tile.IsActive) {
                isValid = false;
                D.Err("Setting inactive tile on a device! {0} | {1}",
                    this, tile);
            }
            Tile = tile;
            if (tile != null && tile.Health.MaxHealth != HealthMax) {
                D.Err("Max health / health mismatch for device / ship weapon: {0}: {1}/{2} | {3}",
                    this, HealthMax, tile.Health.MaxHealth, tile.Health.Health);
                if (tile.Health is DamageableComp dmg) {
                    var hpc = dmg.HealthPercent;
                    dmg.ChangeMaxHealth(HealthMax);
                    dmg.SetHealth(HealthMax * hpc);
                }
            }
            return isValid;
        }

        public void OnHit(GameState s, ShipWeaponShot shot) {
            Ship.CombatState.LastAttacker = shot.SourceShip;
            if (Tile != null) {
                // D.Warn("Hitting player ship device: {0}: {1}", Tile, shot);
                shot.HitPlayerShip(s, Tile.Position, Tile);
            } else if (Health > 0f) {
                CanRepairAfter = G.Ticks + Rng.URange(Consts.TicksPer60Minutes, Consts.TicksPer2Hours);
                float dmg = shot.Weapon.Damage;
                DamageText.ShowShipHit(Ship, shot.TargetPos, (int) dmg);
                Health -= dmg;
                if (Health <= 0f) {
                    Health = 0f;
                    ZoneView?.SetDestroyed(true);
                }
                ZoneView?.UpdateHealthBar();
                string msg;
                var hit = $"{Texts.RedMinus}{Texts.Red(Units.Num((int) dmg))} ({Units.XOutOfY((int) Health, (int) HealthMax)})";
                if (shot.Weapon.Device.Tile != null) {
                    msg = $"{Texts.LinkToTile(shot.Weapon.Device.Tile)} {GlyphMap.ArrowRight} {Def.NameT} ({Ship.CombatSideLabel}): {hit}";
                } else {
                    // NPC shooting an NPC: name the shooter first, and label both by the
                    // side they are actually on rather than assuming everyone is an enemy
                    msg = $"{shot.Weapon.Def.NameT} ({shot.SourceShip.CombatSideLabel}) {GlyphMap.ArrowRight} {Def.NameT} ({Ship.CombatSideLabel}): {hit}";
                }
                s.Sys.Log.AddLine(msg);
                Explosions.CreateExplosionFX(s, shot.TargetPos,
                    shot.Weapon.WeaponDef.ExplosionRadius,
                    dmg, false);
            } else if (Ship.IsPlayer) {
                D.Err("Hitting a player ship device that has no tile! {0}", this);
                shot.HitPlayerShip(s, shot.TargetPos, null);
            }
        }

        public void SetView(ShipZoneView view, Facing.Type dir, float rotAngle) {
            ZoneView = view;
            #if UNITY_EDITOR
            view.name = $"ZoneView_{DeviceDef.Id}_{Ship.Class.NameT}";
            #endif
            view.Load(this, dir, rotAngle);
        }

        public override string ToString() {
            return $"ShipDevice[{Id}|SId:{Ship.Id}|{DeviceDef.Id}|HP:{Health}]";
        }
    }
}
