using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
using Game.Tools;
using Game.Systems.Turrets;
using Game.Systems.Ships;
using Game.Systems.Notify;
using Game.Systems.Voice;

namespace Game.Components {
    public sealed class MeteorComp : BaseComponent<MeteorComp>,
            ITickProgressListener, IThreat, IForceFieldCollider,
            IWithTrajectory, ITurretTarget, IShipCombatTurretTarget {
        public Vector2 Velocity;
        public bool CanChangeTrajectory => Entity.IsActive;
        public float TrajectoryChangeEnergyCost => 25 * radius;
        private float radius;
        private float strength;
        private Mat[] contents;
        private int lastPos;
        public Vector2 Position => Obj.Position;
        public float Size => radius;
        public float Altitude => 0;
        public bool IsMidFlight => true;
        public bool IsThreatActive => true;
        public bool AffectsSoundtrack => true;
        public float TurretEvadePercent => 0.33f;
        public float PointDefenseEvadePercent => 0.05f;
        private ShipWeaponTargetTypeDef targetType;
        public ShipWeaponTargetTypeDef TargetType => targetType
                ??= ShipWeaponTargetTypeDef.FastGet(ShipWeaponTargetTypeIdH.Meteoroid);

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MeteorComp());
        }
        protected override void OnConfig() {
            var radiusRange = Config.GetVector2(PropertyIdH.RadiusRange,
                new Vector2(1.5f, 2.5f));
            var strengthRange = Config.GetVector2(PropertyIdH.StrengthRange,
                new Vector2(1.5f, 5f));
            var velocityRange = Config.GetVector2(PropertyIdH.VelocityRange,
                new Vector2(0.8f, 1.5f)) * 5f;
            var rng = S.Rng;
            radius = rng.Range(radiusRange);
            strength = rng.Range(strengthRange);
            var vx = rng.Range(velocityRange);
            Velocity = S.BB.Get(BBKeyH.MeteorVector, new Vector2(-1, 0))
                * vx * EntityUtils.MapSizeRatio;
            // we are spawning it with the tool, let's change velocity angle
            if (The.Toolbox.ActiveTool is ToolSpawnObj) {
                // Slightly adjust the velocity angle
                Velocity = Vectors.Rotate(Velocity, Consts.V2Zero, rng.Range(-10f, 10f));
            }

            int contentCount = S.Rng.Range(1, 4);
            contents = new Mat[contentCount];
            for (int i = 0; i < contentCount; i++) {
                var mType = S.Rng.WeightedFrom(MatType.RawByRarity);
                var m = new Mat {
                    Type = mType,
                    StackSize = Mathf.CeilToInt(
                        S.Rng.Range(1, 5) * mType.Rarity * 0.05f),
                };
                contents[i] = m;
            }
        }

        public bool WasAligned { get; set; }
        public bool IgnoresForceField { get; set; }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IgnoresForceField", IgnoresForceField);
            data.SetFloat("Radius", radius);
            data.SetFloat("Strength", strength);
            data.SetBool("WasAligned", WasAligned);
            data.SetVector2("Velocity", Velocity);
            data.SetMats("Contents", contents);
            data.SetBool("HadImpact", hadImpact);
        }

        protected override void OnLoad(ComponentData data) {
            radius = data.GetFloat("Radius", 5);
            WasAligned = data.GetBool("WasAligned", false);
            IgnoresForceField = data.GetBool("IgnoresForceField", false);
            strength = data.GetFloat("Strength", 150);
            contents = data.GetMats("Contents", null);
            Velocity = data.GetVector2("Velocity", new(-1, 0));
            hadImpact = data.GetBool("HadImpact", hadImpact);
        }

        public override string ToString() {
            return " * Meteor";
        }

        public override void OnRemove() {
            if (isAddedToSec) {
                isAddedToSec = false;
                S.Sig.OnShipRotate.RemoveListener(OnShipRotate);
                S.Sys.Sec.RemoveForceFieldCollider(this);
                S.Sig.RegisterThreat.Send(this, false);
            }
        }

        private bool isAddedToSec;

        private void AddThreat() {
            if (!isAddedToSec) {
                isAddedToSec = true;
                S.Sig.OnShipRotate.AddListener(OnShipRotate);
                S.Sig.RegisterThreat.Send(this, true);
                S.Sys.Sec.AddForceFieldCollider(this);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            //Obj.Transform.SkipMoveLoose = true;
            // Obj.IsFlying = true;
            Obj.Health.IsInvincible = false;
            Obj.Graphics.SetSize(new Vector2(radius, radius));
        }
        private float lastRotation;
        private void OnShipRotate(float rotation) {
            S.Sys.Ship.RotateSpaceObj(rotation, Velocity, Obj.Position, ref lastRotation);
        }

        public void DestroyMeteor() {
            if (!Obj.IsActive) {
                // Meteor can or cannot explode and destroy itself, so it's ok
                //D.Err("Trying to destroy inactive meteor! {0}", Obj);
                return;
            }
            S.CmdQ.Enqueue(new CmdRemoveObj(Obj));
        }

        public override void Receive(IComponent sender, int message) {
            if (!hadImpact && message == MsgIdH.HealthWentToZero) {
                ApplyImpact(false, Obj.Position);
            }
        }

        public int ThreatType => ThreatIdH.Meteor;

        private void NotifyImpact() {
            Notification.Build(IconId.CWarning,
                T.MeteorImpact, Consts.TicksPer2Hours, NotificationType.Alert)
                .Group("met_imp")
                .Emergency(true)
                .WithArea(Obj.Position, Obj.Size)
                .WithOnClick(() => EntityUtils.CameraFocusOn(Obj.Position, Obj.Size))
                .WithVoice(VoiceLine.MeteorImpact)
                .Show();
        }

        private bool hadImpact;

        public void ApplyImpact(bool notify, Vector2 impactPos) {
            if (S.Sys.Sec.CheckCollision(this)) {
                return;
            }
            hadImpact = true;
            // We don't want to damage the meter with explosion, we will remove it anyway
            // if (Obj.Health is ObjHealthComp health) {
            //     health.IsInvincible = true;
            // }
            Explosion.Create(S, (int) radius + 1,
                position: impactPos,
                customRadius: radius,
                customStr: radius * strength,
                delay: 1,
                flags: ExplosionFlags.SkipEvent | ExplosionFlags.BlowHoleInFloor);
            // S.Clock.OnNextTick(() => new CmdCreateExplosion(Obj.Position, radius,
            //     radius * strength, false, true).Execute(S));
            if (notify) {
                NotifyImpact();
            }
            Obj.Health.TakeDamage(
                Obj.Health.MaxHealth * Rng.URangeSmooth(0.3f, 1.1f),
                    DamageType.Explosive);
            Velocity *= 0.5f;

            if (Obj.Health.IsDestroyed && !Arrays.IsEmpty(contents)) {
                for (int i = contents.Length - 1; i >= 0; i--) {
                    Mat mat = contents[i];
                    var pile = EntityUtils.SpawnRawMaterial(mat,
                        Pos.FromVector(impactPos), radius * 2);
                    if (pile != null && Tunables.AutoForbidSpaceBodyLoot) {
                        pile.IsForbidden = true;
                    }
                }
            }
        }

        public bool HadImpactWithTheShip;
        private float rotationAngle;
        private bool hasTickedProgress;

        public void TickProgress(float tickProgress) {
            if (!hasTickedProgress) {
                hasTickedProgress = true;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                DestroyMeteor();
                return;
            }
            if (!Obj.IsActive) { return; }
            // S.Clock.PreventMultiTick();
            var tpf = S.Clock.TicksPerUpdate;
            var curPos = Obj.Transform.Position;
            if (lastPos == 0) {
                lastPos = Obj.Transform.PosIdx;
            }
            var newPos = curPos + Velocity * tpf - shipShift;
            var newPosIdx = Pos.FromVector(newPos);
            shipShift = Consts.V2Zero;
            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => {
                    if (Obj.IsActive) {
                        S.Objs.Destroy(Obj, true);
                    }
                });
            } else {
                AddThreat();
                rotationAngle += strength * 2f * tpf;
                Obj.Graphics.SetRotation(rotationAngle % 360);
                if (lastPos != newPosIdx) {
                    if (!Pos.AreAdjacent(lastPos, newPosIdx)) {
                        // velocity is too hight
                        var start = lastPos;
                        while (start != newPosIdx) {
                            start = Pos.MoveTowards(start, newPosIdx);
                            if (CheckImpactAt(newPos, start)) {
                                newPos = Pos.ToVector(start);
                                break;
                            }
                        }
                        lastPos = start;
                    } else {
                        lastPos = newPosIdx;
                        CheckImpactAt(newPos, newPosIdx);
                    }
                }
                Obj.Transform.TeleportTo(newPos);
                Obj.Graphics.TeleportTo(newPos);
            }
        }
        private bool CheckImpactAt(Vector2 position, int pos) {
            // Check if we should hit
            var objHit = EntityUtils.GetWallOrObjAt(pos, skipImmutable: true);
            if (objHit != null) {
                HadImpactWithTheShip = true;
                ApplyImpact(true, position);
                return true;
            }
            return false;
        }
        public bool HitForceField(Vector3 hitPos, out float softFlux, out float hardFlux) {
            softFlux = 0f;
            hardFlux = 50f;
            Explosions.CreateExplosionFX(S, hitPos, Rng.URange(5f, 8f), Rng.URange(300f, 700f), false);
            DestroyMeteor();
            return true;
        }
        private Vector2 shipShift;
        public void OnShipMove(Vector2 dir) {
            shipShift = dir * Tunables.IncomingSpaceBodiesShipMoveMul;
        }

        public bool InfluenceTrajectory(Vector2 target, float strength) {
            var hitPosition = Obj.Transform.Position + Velocity;
            // 1. Set new hit position
            hitPosition = Vector2.MoveTowards(hitPosition, target, strength);
            var mag = Velocity.magnitude;
            Velocity = (hitPosition - Obj.Graphics.Position).normalized * mag;
            return true;
        }

        public bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity) {
            velocity = this.Velocity;
            hitPoint = Obj.Graphics.Position + (velocity * 25f);
            return true;
        }

        public static Obj Spawn(GameState S, Rng rng, Vector2 strikeV, Vector2 startPos) {
            S.BB.Set(BBKeyH.MeteorVector, strikeV);

            //counterbalance the edge reduction in the for loop later + add variety
            // // S.Situation.AddTension(0.2f, $"Meteor Shower: One Meteor at {pos}");
            var cmd = new CmdSpawnObj(startPos,
                rng.From(The.Defs.Matching(
                    DefIds.ObjAsteroidsMeteor01)), true);
            cmd.Execute(S);
            return cmd.Obj;
        }

        public Vector2 PredictPosition(float afterSeconds) {
            var newPos = Position + Velocity * afterSeconds * 2 - shipShift;
            // D.Warn($"Position: {Position}, Velocity: {Velocity}, AfterSeconds: {afterSeconds}, ShipShift: {shipShift}, NewPos: {newPos}");
            return newPos;
        }
    }
}
