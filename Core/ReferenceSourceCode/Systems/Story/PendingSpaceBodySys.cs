using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class PendingSpaceBodySys : GameSystem, ISaveableSpecial {
        public const string SysId = "PendingSpaceBodySys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PendingSpaceBodySys());
        }

        private readonly List<PendingSpaceBody> pending = new();
        private Vector2 shipShift;
        private float lastRotation;
        private float rotationAngle;

        protected override void OnInitialize() {
            S.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Sig.OnShipRotate.AddListener(OnShipRotate);
        }

        public override void Unload() { }

        public void Enqueue(PendingSpaceBody p) {
            CreateFauxSprite(p);
            pending.Add(p);
        }

        // Called by ShipSys.MoveLooseObjects so pending bodies drift with the ship.
        public void OnShipMove(Vector2 dir) {
            shipShift += dir;
        }

        private void OnShipRotate(float rotation) {
            if (pending.Count == 0) { return; }
            if (lastRotation == 0f) { lastRotation = rotation; return; }
            if (lastRotation == rotation) { return; }
            var delta = (lastRotation - rotation) * 0.01f;
            lastRotation = rotation;
            var center = new Vector2(Pos.GridW * 0.5f, Pos.GridH * 0.5f);
            for (int i = 0; i < pending.Count; i++) {
                var p = pending[i];
                p.Pos = Vectors.Rotate(p.Pos, center, delta);
                p.Velocity = Vectors.Rotate(p.Velocity, Consts.V2Zero, delta);
            }
        }

        private void OnTickProgress(float progress) {
            if (pending.Count == 0) {
                shipShift = Consts.V2Zero;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                ClearAllFauxSprites();
                pending.Clear();
                shipShift = Consts.V2Zero;
                return;
            }
            var tpf = S.Clock.TicksPerUpdate;
            var shift = shipShift;
            shipShift = Consts.V2Zero;
            var ticks = S.Clock.Ticks;
            rotationAngle = (rotationAngle + 1.5f * tpf) % 360f;
            for (int i = pending.Count - 1; i >= 0; i--) {
                var p = pending[i];
                p.Pos += p.Velocity * tpf - shift;
                UpdateFauxSprite(p);
                if (EntityUtils.IsWithinBounds(p.Pos)) {
                    DestroyFauxSprite(p);
                    Materialize(p);
                    pending.RemoveAt(i);
                } else if (ticks > p.ExpireTick) {
                    D.Warn("PendingSpaceBody expired without reaching bounds: {0}", p.DefId);
                    DestroyFauxSprite(p);
                    pending.RemoveAt(i);
                }
            }
        }

        private void CreateFauxSprite(PendingSpaceBody p) {
            var def = The.Defs.Get(p.DefId);
            if (def == null) { return; }
            var spriteId = def.Preview;
            if (string.IsNullOrEmpty(spriteId)) { return; }
            if (!RenderingService.Sprites.TryFind(spriteId, out var sd)) { return; }
            sd = sd.Copy();
            // Use a size that approximates typical meteor/asteroid visual scale
            var size = p.IsAsteroid ? 4f : 2f;
            sd.Scale = new float2(size, size);
            var rot = VelocityToRotation(p.Velocity);
            var sprite = new AnimatedSpriteSet(p, 1, WorldLayer.FreeEntities, p.Pos, rot);
            sprite.IsRotatable = true;
            sprite.SetSprite(0, sd, S);
            sprite.AddToRenderer(S);
            p.FauxSprite = sprite;
        }

        private void UpdateFauxSprite(PendingSpaceBody p) {
            if (p.FauxSprite == null) { return; }
            p.FauxSprite.Teleport(p.Pos, rotationAngle);
        }

        private void DestroyFauxSprite(PendingSpaceBody p) {
            if (p.FauxSprite == null) { return; }
            p.FauxSprite.RemoveFromRenderer(S);
            p.FauxSprite = null;
        }

        private void ClearAllFauxSprites() {
            for (int i = 0; i < pending.Count; i++) {
                DestroyFauxSprite(pending[i]);
            }
        }

        private static float VelocityToRotation(Vector2 velocity) {
            if (velocity.sqrMagnitude < 0.0001f) { return 0f; }
            return Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        }

        private void RestoreFauxSprites() {
            for (int i = 0; i < pending.Count; i++) {
                CreateFauxSprite(pending[i]);
            }
        }

        private void Materialize(PendingSpaceBody p) {
            if (p.IsAsteroid) {
                S.BB.Set(BBKeyH.MeteorVector, p.StrikeV);
                var def = The.Defs.Get(p.DefId);
                if (def == null) {
                    D.Err("PendingSpaceBodySys: asteroid def not found: {0}", p.DefId);
                    return;
                }
                var cmd = new CmdSpawnObj(p.Pos, def, true);
                cmd.Execute(S);
                if (cmd.Obj != null
                        && cmd.Obj.TryGetComponent<AsteroidComp>(out var asteroid)) {
                    asteroid.InitFromIncoming(p.Velocity, p.HitPosition, p.Pos);
                }
            } else {
                var obj = MeteorComp.Spawn(S, S.Rng, p.StrikeV, p.Pos);
                if (obj != null && obj.TryGetComponent<MeteorComp>(out var meteor)) {
                    meteor.Velocity *= p.VelMul;
                }
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.PendingBodies ??= new PendingSpaceBodySysData();
            sd.PendingBodies.Bodies = pending.Count > 0 ? pending.ToArray() : null;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            ClearAllFauxSprites();
            pending.Clear();
            var data = sd?.PendingBodies;
            if (data?.Bodies != null) {
                pending.AddRange(data.Bodies);
                D.Warn("PendingSpaceBodySys restored {0} pending bodies", pending.Count);
                // Faux sprites can't be created here — renderer not ready yet.
                // They're created lazily on the first tick via OnTickProgress calling
                // RestoreFauxSprites, which we trigger by leaving FauxSprite null.
                Ready.When(WhenH.AllSystemsLoaded, RestoreFauxSprites);
            }
        }
    }
}
