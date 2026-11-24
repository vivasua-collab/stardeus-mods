using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using Game.Rendering;
using KL.Utils;
using KL.Randomness;
using Game.Components;
using Game.Data.Factions;
using Game.Visuals;
using System;
using Game.Systems.Creatures;
using Game.Systems.AI.Combat;
using Game.Systems.Combat;
using KL.Grid;

namespace Game.Systems.Turrets {
    public sealed class RotatingTurretComp : BaseComponent<RotatingTurretComp>,
            IAttackable, IAttackableTurret, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new RotatingTurretComp());
        }
        private const float DefaultEnergyCost = 0f;
        private RotatingTurret turret;
        public RotatingTurret Turret => turret;
        private float energyCost;
        public Tile TurretTile => Tile;
        public string Name => Tile.NameLink;
        public Vector2 AttackablePosition => Tile.Position;
        public int AttackablePosIdx => Tile.PosIdx;
        public string[] TakeDamageSounds => null;
        public bool IsScanning => turret.IsScanning;
        public bool ShootsBeings =>
            (turret.Type & TurretType.AntiPersonnel) == TurretType.AntiPersonnel;
        public string EntityName => Tile.EntityName;

        protected override void OnConfig() {
            turret = new RotatingTurret();
            turret.TurretTile = Tile;
            turret.HeadOffset = Config.GetVector2(PropertyIdH.HeadOffset);
            turret.ShotFXPrefab = Config.GetString(PropertyIdH.ShotFX, null);
            turret.HasTopSprite = Config.GetBool(PropertyIdH.HasTopSprite, false);
            turret.IsLoud = Config.GetBool(PropertyIdH.IsLoud, true);
            turret.AttackableTurret = this;
            if (turret.HasTopSprite) {
                turret.TopOffset = Config.GetVector2(PropertyIdH.TopOffset);
            } else {
                Config.MarkUsed(PropertyIdH.TopOffset);
            }
            turret.ShotFXLifetime = Config.GetFloat(PropertyIdH.ShotFXDuration);
            turret.HeadRotDir = Rng.UOneOf(-1f, 1f);
            turret.DefIdH = Tile.Definition.IdH;
            turret.ShotSound = Config.GetString(PropertyIdH.ShotSound, null);
            turret.ShotSoundVol = Config.GetFloat(PropertyIdH.ShotSoundVol, 1f);
            turret.SymmetryFrame = Config.GetInt(PropertyIdH.SymmetryFrame, 0);
            var order = Config.GetString(PropertyIdH.SpriteOrder, null);
            if (order == null) {
                turret.SpriteOrder = SpriteOrder.TurretHead;
            } else {
                turret.SpriteOrder = SpriteOrder.ForName(order, entity.Definition);
            }

            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override void OnLateReady(bool wasLoaded) {
            turret.GraphicsId = Tile.Graphics.Config.GetString(PropertyIdH.Graphic);
        }

        public override void PostLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !turret.IsRegistered) {
                if (wasLoaded) {
                    S.Clock.OnNextUpdate(() => RegisterTurret(true));
                } else {
                    RegisterTurret(true);
                }
            }
        }

        private void RegisterTurret(bool add) {
            // D.Warn("Reg turret: {0} | {1}", add, Tile);
            if (add && !Tile.IsActive) {
                if (Ready.Gen) {
                    D.Err("Trying to add inactive turret! {0}", Tile);
                }
                add = false;
            }
            // The margin only blocks adding: a ship move can carry a registered turret out of
            // it, and its head would keep spinning after removal
            if (add && !EntityUtils.IsWithinMargin(Tile.PosIdx, false, false)) {
                D.Warn("Skipping turret outside margin: {0} | {1}", Tile,
                    Pos.String(Tile.PosIdx));
                add = false;
            }
            if (turret.IsRegistered == add) { return; }
            turret.IsRegistered = add;
            turret.HeadAngle = Rng.URange(0f, 360f);
            if (!S.Query.RegisterTurret.Ask(turret, add)) {
                D.Err("Failed to register turret! {0}", Tile);
            }
            if (add) {
                // weapons will set the cost before this
                if (energyCost > 0f) {
                    Tile.ENode.SetConsumerWantedInput(energyCost);
                }
            } else {
                turret.IsScanning = false;
                // TurretsSys stops retargeting an unregistered turret, so its target would keep
                // it in TurretsTargetingThisUnit, which switches off that unit's search goals
                if (turret.Target != null
                        && (turret.Type & TurretType.AntiPersonnel) == TurretType.AntiPersonnel) {
                    turret.SetTarget(null);
                }
            }
        }

        public override void OnRemove() {
            RegisterTurret(false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.ActQ.Enqueue(() => {
                    if (Tile.IsActive) {
                        RegisterTurret(true);
                    }
                });
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
                turret.IsScanning = false;
                turret.SetTarget(null);
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    S.ActQ.Enqueue(() => {
                        RegisterTurret(false);
                    });
                } else if (Tile.IsConstructed) {
                    S.ActQ.Enqueue(() => {
                        RegisterTurret(true);
                    });
                }
            }
        }
    }
}
