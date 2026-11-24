using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using System;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Systems.Ships;
using Game.Systems.Story;
using Game.Systems.Notify;
using Game.Systems.Voice;

namespace Game.Components {
    public sealed class AsteroidComp : BaseComponent<AsteroidComp>,
            ITickProgressListener, IForceFieldCollider, IWithTrajectory, IThreat,
            IShipCombatTurretTarget {
        // public for tests
        public Vector2 Velocity;
        private float radius;
        private float strength;
        private Mat[] contents;
        public bool WasAligned { get; set; }
        private bool hadImpact;
        public bool CanChangeTrajectory => !hadImpact;
        public float TrajectoryChangeEnergyCost => 25 * radius;
        private float startScale;
        private float endScale;
        public Vector2 Position => Obj.Position;
        private ShipWeaponTargetTypeDef targetType;
        public ShipWeaponTargetTypeDef TargetType => targetType
                ??= ShipWeaponTargetTypeDef.FastGet(ShipWeaponTargetTypeIdH.Asteroid);
        public float Size => radius;
        public float Altitude => (Obj.Graphics.Position - hitPosition).y;

        public int ThreatType => ThreatIdH.Asteroid;

        public bool IsThreatActive => !hadImpact;

        public bool IsMidFlight => true;
        public bool AffectsSoundtrack => true;
        public float TurretEvadePercent => 0.1f;
        public float PointDefenseEvadePercent => 0.05f;
        private bool isPathCalculated;
        private Vector2 hitPosition;
        private Vector2 startPosition;
        private bool isAddedToSec;
        private bool isFragment;
        public bool IsFragment => isFragment;
        public bool IgnoresForceField { get; set; }
        // Empty for the smallest rocks, those end the split chain
        private string fragmentDef;
        public string FragmentDef => fragmentDef;
        public bool CanSplit => !hadImpact && !string.IsNullOrEmpty(fragmentDef);
        private Vector2 fragmentCount;
        public Vector2 FragmentCount => fragmentCount;
        public Vector2 HitPosition => hitPosition;
        public Mat[] Contents => contents;
        // How far along the descent the asteroid is, 1 at the start, 0 over the hit position
        public float DescentFraction {
            get {
                var full = Vectors.Distance(startPosition, hitPosition);
                if (full <= 0f) { return 1f; }
                return Mathf.Clamp(Vectors.Distance(Obj.Position, hitPosition) / full,
                    0.05f, 1f);
            }
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AsteroidComp());
        }
        protected override void OnConfig() {
            var radiusRange = Config.GetVector2(PropertyIdH.RadiusRange,
                new Vector2(4f, 10f));
            var strengthRange = Config.GetVector2(PropertyIdH.StrengthRange,
                new Vector2(1f, 3f));
            fragmentDef = Config.GetString(PropertyIdH.FragmentDef, null);
            fragmentCount = Config.GetVector2(PropertyIdH.FragmentCount,
                new Vector2(2f, 4f));
            radius = S.Rng.Range(radiusRange);
            strength = S.Rng.Range(strengthRange);
            SetRadius(radius);
            int contentCount = S.Rng.Range(2, 5);
            contents = new Mat[contentCount];
            for (int i = 0; i < contentCount; i++) {
                var mType = S.Rng.WeightedFrom(MatType.RawByRarity);
                var m = new Mat {
                    Type = mType,
                    StackSize = Mathf.CeilToInt(
                        S.Rng.Range(10, 30) * mType.Rarity * 0.1f),
                };
                if (m.StackSize > 50) {
                    m.StackSize = 50;
                }
                contents[i] = m;
            }
        }

        private void SetRadius(float newRadius) {
            radius = newRadius;
            startScale = radius * 2;
            endScale = startScale * 0.2f;
        }

        public override void OnRemove() {
            if (isAddedToSec) {
                isAddedToSec = false;
                S.Sys.Sec.RemoveForceFieldCollider(this);
                S.Sig.RegisterThreat.Send(this, false);
                S.Sig.OnShipRotate.RemoveListener(OnShipRotate);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsFragment", isFragment);
            data.SetBool("IgnoresForceField", IgnoresForceField);
            data.SetBool("WasAligned", WasAligned);
            data.SetBool("HadImpact", hadImpact);
            data.SetBool("IsPathCalculated", isPathCalculated);
            data.SetFloat("Radius", radius);
            data.SetFloat("Strength", strength);
            data.SetVector2("Velocity", Velocity);
            data.SetFloat("StartScale", startScale);
            data.SetFloat("EndScale", endScale);
            data.SetMats("Contents", contents);
            data.SetVector2("HitPosition", hitPosition);
            data.SetVector2("StartPosition", startPosition);
        }

        protected override void OnLoad(ComponentData data) {
            hadImpact = data.GetBool("HadImpact", false);
            isFragment = data.GetBool("IsFragment", false);
            IgnoresForceField = data.GetBool("IgnoresForceField", false);
            WasAligned = data.GetBool("WasAligned", false);
            isPathCalculated = data.GetBool("IsPathCalculated", false);
            radius = data.GetFloat("Radius", 5);
            strength = data.GetFloat("Strength", 3);
            startScale = data.GetFloat("StartScale", 5);
            endScale = data.GetFloat("EndScale", 1);
            hitPosition = data.GetVector2("HitPosition");
            startPosition = data.GetVector2("StartPosition");
            contents = data.GetMats("Contents", null);
            Velocity = data.GetVector2("Velocity", new(-1, -1));
        }

        public override string ToString() {
            return " * Asteroid";
        }

        public override void OnLateReady(bool wasLoaded) {
            //Obj.Transform.SkipMoveLoose = true;
            // Obj.IsFlying = true;
            if (isPathCalculated) {
                //Ready.WhenRendering(this, "Restore asteroid looks after load", () => {
                    if (hadImpact) {
                        SwitchImpactShader();
                    }
                    Obj.Graphics.SetSize(new Vector2(radius, radius));
                //});
            } else {
                Obj.Graphics.Show(false);
            }
        }

        private void AddThreat() {
            if (!isAddedToSec) {
                isAddedToSec = true;
                S.Sig.OnShipRotate.AddListener(OnShipRotate);
                if (!IgnoresForceField) {
                    S.Sys.Sec.AddForceFieldCollider(this);
                }
                S.Sig.RegisterThreat.Send(this, true);
            }
        }

        private void CalculatePath() {
            // 1. Transform.Position is now at the point of impact
            // we want to move the obj to the edge of the map
            // and then calculate velocity vector, and speed, so the asteroid
            // would land in target spot

            // figure out on which edge we will spawn the roid
            var gw = S.GridWidth;
            var gh = S.GridHeight;
            hitPosition = Obj.Transform.Position;
            var distX = gw - hitPosition.x;
            var distY = gh - hitPosition.y;

            var count = Pos.GridW / 32;
            var directionFrom = Rng.UFrom(Facing.Types);
            var posF = EntityUtils.MeteorVector(
                directionFrom, 35f, count, Rng.Unseeded,
                    out var strikeV);

            var start = posF(Mathf.RoundToInt(Rng.URange(count * 0.1f, count * 0.9f)));
            if (!EntityUtils.IsWithinBounds(start)) {
                D.Err("Asteroid start position is OOB: {0}", start);
                start = EntityUtils.ToSafe(start);
            }
            // D.Warn("Dropping asteroid from {0} to {1}", start, hitPosition);
            if (start.y < gh - 0.5f) {
                start.y = gh - 0.5f;
            }

            // var start = new Vector2(
            //     Rng.URange(gw - distX * 0.1f, gw * 0.95f),
            //     Rng.URange(gh - distY * 0.5f, gh * 0.95f));
            // if (distX >= distY) {
            //     // Spawn on top edge
            //     start.y = gh - 0.1f;
            // } else {
            //     // Spawn on right edge
            //     start.x = gw - 0.1f;
            // }

            // Here we have both start and end positions, we can not get
            // a normalized velocity vector
            var velVecNorm = (hitPosition - start).normalized;

            Velocity = velVecNorm * (Rng.URange(0.8f, 1.2f) * S.Difficulty.SoftLevel);
            Velocity = EntityUtils.MapSizeAdjustedVelocity(Velocity);
            isPathCalculated = true;

            Obj.Transform.TeleportTo(start);
            Obj.Graphics.TeleportTo(start);
            startPosition = start;
            Obj.Graphics.Show(true);
        }

        public void InitFromIncoming(Vector2 velocity, Vector2 hitPos, Vector2 startPos) {
            Velocity = velocity;
            hitPosition = hitPos;
            startPosition = startPos;
            isPathCalculated = true;
            Obj.Graphics.Show(true);
        }

        public void InitAsFragment(float fragmentRadius, Mat[] fragmentContents,
                bool ignoresForceField, Vector2 velocity, Vector2 hitPos, Vector2 startPos) {
            isFragment = true;
            IgnoresForceField = ignoresForceField;
            contents = fragmentContents;
            SetRadius(fragmentRadius);
            InitFromIncoming(velocity, hitPos, startPos);
            // Not on first movement like a fresh asteroid. The parent's threat goes away
            // within a tick, and a moment without any space threat drops the shield
            // right when the fragments arrive.
            AddThreat();
        }

        private void SwitchImpactShader() {
            Obj.Graphics.RemoveShadow();
            Obj.Graphics.OverrideSortOrder(SpriteOrder.ObjBelow);
            if (Entity.TryGetComponent<ParticleTrailComp>(out var pt)) {
                pt.TrySwitchTo(null, "_Below");
            }
        }

        public void DestroyAsteroid() {
            S.Objs.Destroy(Obj, false);
        }

        private NotificationHandle incomingEvent;

        private void NotifyIncoming() {
            if (incomingEvent.IsValid || hadImpact) { return; }
            AddThreat();
            // The parent already announced itself, fragments would spam
            if (isFragment) { return; }
            incomingEvent = Notification.BuildFor(Obj, null, T.AsteroidIncoming,
                Consts.TicksPer8Hours,
                NotificationType.Critical)
                    .Group("ast_inc")
                    .WithVoice(VoiceLine.Asteroid)
                    .Show();
            var text = Texts.LinkToObj(Obj, Texts.Red(T.AsteroidIncoming));
            S.Sys.Log.AddLine(text);
            S.Sig.ShowFanfare.Send(text);
        }

        private void NotifyImpact() {
            Notification.Build(Obj.Definition.Preview,
                T.AsteroidImpact, Consts.TicksPer8Hours, NotificationType.Critical)
                .Group("ast_impact")
                .WithArea(Obj.Position, Obj.Size)
                .WithOnClick(() => EntityUtils.CameraFocusOn(Obj.Position, Obj.Size))
                .Show();

            // One story entry per asteroid, not one per fragment
            if (isFragment) { return; }
            S.Sys.Log.AddLine(T.AsteroidImpact, Texts.TMPColorRed);

            var ev = StoryEventData.CreateCustom(Obj.Definition.Preview,
                T.EvtAsteroidImpactTitle, T.EvtAsteroidImpactDesc);
            S.Sig.AddCustomStoryEvent.Send(ev, false);
        }

        private bool RollPunchThrough() {
            // Once per asteroid, not worth caching a curve that mods can reload
            var curve = CurveDef.FastGet(CurveIdH.StoryEvents_AsteroidPunchThroughChance);
            var chance = curve.Evaluate(radius * S.Difficulty.SoftLevel);
            return S.Rng.Chance(chance);
        }

        public void ApplyImpact() {
            if (S.Sys.Sec.CheckCollision(this)) {
                return;
            }
            var punchesThrough = RollPunchThrough();
            var impactStr = Mathf.PI * radius * radius * 10f;
            if (punchesThrough) {
                Explosion.Create(S,
                    level: (int) radius + 1,
                    position: Obj.Position,
                    customRadius: radius,
                    customStr: impactStr,
                    flags: ExplosionFlags.SkipEvent | ExplosionFlags.BlowHoleInFloor
                        | ExplosionFlags.IgnoreObstacles);
            } else {
                // Too small to go through the ship, it bursts on the surface like a meteoroid
                Explosion.Create(S,
                    level: (int) radius + 1,
                    position: Obj.Position,
                    customRadius: radius,
                    customStr: impactStr * Tunables.AsteroidSurfaceImpactStrengthMul,
                    flags: ExplosionFlags.SkipEvent | ExplosionFlags.BlowHoleInFloor);
            }
            // make it rotate faster after impact
            strength *= Rng.URange(2f, 5f);
            if (!Arrays.IsEmpty(contents)) {
                for (int i = contents.Length - 1; i >= 0; i--) {
                    Mat mat = contents[i];
                    var pile = EntityUtils.SpawnRawMaterial(mat, Obj.PosIdx, radius * 2);
                    if (pile != null && Tunables.AutoForbidSpaceBodyLoot) {
                        pile.IsForbidden = true;
                    }
                }
            }
            NotifyImpact();
            if (!punchesThrough && Obj.IsActive) {
                DestroyAsteroid();
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero && CanSplit) {
                Explosions.CreateExplosionFX(S, Obj.Position, Rng.URange(5f, 8f),
                    Rng.URange(300f, 500f), true);
                AsteroidSplitter.Split(S, this, false);
            }
        }
        private float lastRotation;

        private void OnShipRotate(float rotation) {
            S.Sys.Ship.RotateSpaceObj(rotation, Velocity, Obj.Position, ref lastRotation);
        }

        private int lastPos;
        private float rotationAngle;
        private bool hasTickedProgress;

        public void TickProgress(float tickProgress) {
            if (!hasTickedProgress) {
                hasTickedProgress = true;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                DestroyAsteroid();
                return;
            }
            if (!Obj.IsActive) { return; }
            if (!isPathCalculated) {
                CalculatePath();
            }
            // S.Clock.PreventMultiTick();
            var tpf = S.Clock.TicksPerUpdate;
            var newPos = Obj.Transform.Position + Velocity * tpf - shipShift;
            shipShift = Consts.V2Zero;
            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => {
                    if (Obj.IsActive) {
                        S.Objs.Destroy(Obj, true);
                    }
                });
            } else {
                NotifyIncoming();
                Obj.Transform.TeleportTo(newPos);
                var newPosIdx = Obj.Transform.PosIdx;
                Obj.Graphics.TeleportTo(newPos);
                if (!hadImpact && lastPos != newPosIdx) {
                    lastPos = newPosIdx;
                    // Check if we should hit
                    if (Vectors.SqrDistance(hitPosition, newPos) < 0.5f) {
                        bool hasHit = false;
                        // Detect a hit if we are flying near something
                        Pos.ForEachInRadius(newPosIdx, radius, true, (p) => {
                            // TODO detect collision with drones?
                            if (EntityUtils.IsConstructedTileAt(p)) {
                                hasHit = true;
                                return false;
                            }
                            return true;
                        });
                        if (hasHit) {
                            Pos.ForEachInRadius(newPosIdx, radius, true, (p) => {
                                if (EntityUtils.IsConstructedTileAt(p)) {
                                    S.Clock.OnNextUpdate(ApplyImpact);
                                    return false;
                                }
                                return true;
                            });
                        }
                        Notification.Remove(ref incomingEvent);
                        hadImpact = true;
                        SwitchImpactShader();
                    }
                }
            }
            if (!hadImpact) {
                var shadowHeight = Altitude;
                Obj.Graphics.SetShadowHeight(shadowHeight);
            }
            var distCur = Vectors.Distance(Obj.Graphics.Position, hitPosition);
            if (distFull == 0) {
                distFull = Vectors.Distance(startPosition, hitPosition);
            }
            var f = (distCur / distFull) * 0.5f;
            float lerpVal = hadImpact ? 0.5f - f : 0.5f + f;
            var scale = Mathf.LerpUnclamped(endScale, startScale, lerpVal);
            if (scale < 0.01f) { scale = 0.01f; }
            Obj.Graphics.SetSize(new Vector2(scale, scale));
            rotationAngle += strength * 2f * tpf;
            Obj.Graphics.SetRotation(rotationAngle % 360);
        }

        private float distFull;

        public bool HitForceField(Vector3 hitPos, out float fluxSoft, out float fluxHard) {
            fluxSoft = 0f;
            fluxHard = Size * 5f;
            Explosions.CreateExplosionFX(S, hitPos, Rng.URange(7f, 10f),
                Rng.URange(500f, 700f), true);
            if (CanSplit) {
                AsteroidSplitter.Split(S, this, true);
            }
            DestroyAsteroid();
            return true;
        }

        private Vector2 shipShift;

        public void OnShipMove(Vector2 dir) {
            shipShift = dir * Tunables.IncomingSpaceBodiesShipMoveMul;
            if (!hadImpact) {
                hitPosition -= shipShift;
            }
        }

        public bool InfluenceTrajectory(Vector2 target, float strength) {
            if (hadImpact) { return false; }
            var prevHitPosition = hitPosition;
            // 1. Set new hit position
            //D.Log("Asteroid size: {0}", radius);
            hitPosition = Vector2.MoveTowards(hitPosition, target,
                strength / Math.Max(1, radius * 0.2f));
            //D.Log("Moved position: {0} -> {1}", prevHitPosition, hitPosition);
            if (hitPosition != prevHitPosition) {
                // 2. Make velocity vector point to new hit position
                var mag = Velocity.magnitude;
                Velocity = (hitPosition - Obj.Graphics.Position).normalized * mag;
                return true;
            }
            // we are already on this trajectory
            return false;
        }

        public bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity) {
            velocity = this.Velocity;
            if (hadImpact) {
                hitPoint = Consts.V2Zero;
                return false;
            }
            hitPoint = hitPosition;
            return true;
        }

        public Vector2 PredictPosition(float afterSeconds) {
            var newPos = Position + Velocity * afterSeconds - shipShift;
            // D.Warn($"Asteroid Position: {Position}, Velocity: {velocity}, AfterSeconds: {afterSeconds}, ShipShift: {shipShift}, NewPos: {newPos}");
            return newPos;
        }
    }
}
