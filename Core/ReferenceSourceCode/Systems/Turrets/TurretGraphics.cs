using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Rendering;
using Game.Utils;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Turrets {
    public sealed class TurretGraphics {
        private const int numHeadSprites = 128;
        private readonly GameState S;
        /// <summary>
        /// int key = definition id hash
        /// </summary>
        private readonly Dictionary<int, SpriteData[]> headGraphics = new();
        private readonly Dictionary<RotatingTurret, AnimatedSpriteSet> headSprites = new();
        private readonly Dictionary<RotatingTurret, AnimatedSpriteSet> topSprites = new();
        public TurretGraphics(GameState s) {
            S = s;
        }

        public void OnTickProgress(float prog) {
            // rotate idle heads, aim at targets
            SpriteData[] sprites = null;
            int lastDefIdH = 0;
            foreach (var ts in headSprites) {
                var turret = ts.Key;
                var idH = turret.DefIdH;
                if (lastDefIdH != idH) {
                    sprites = HeadSpritesFor(turret);
                    lastDefIdH = idH;
                }
                // TODO resolve actual rotation
                var sprite = ts.Value;
                var spriteIdx = ResolveSpriteForAngle(turret.HeadAngle, out var headRot);
                if (turret.SymmetryFrame > 0) {
                    spriteIdx = spriteIdx % turret.SymmetryFrame;
                }
                var spr = sprites[spriteIdx];
                if (spr != null) {
                    sprite.SetSprite(0, spr, S);
                    sprite.Rotation = headRot;
                } else {
                    D.Err("Got null sprite for turret: {0}. sprite idx: {1}. sprites length: {2}",
                        turret.TurretTile.Definition.Id, spriteIdx, sprites.Length);
                }
            }
        }

        /// <param name="angle">input angle (0f-360 degrees). 0f = pure down, we go clockwise, so 90f = left</param>
        /// <param name="spriteRot">wanted rotation of a sprite (-22.5f to 22.5f degrees)</param>
        /// <returns>index of SpriteData[]</returns>
        public static int ResolveSpriteForAngle(float angle, out float spriteRot) {
            Maths.CalculateDirectionAndRotation(angle, numHeadSprites, out int dir, out var rot);
            // interpolation with -rot sucks
            // spriteRot = -rot;
            spriteRot = 0;
            return dir;
        }

        private SpriteData TopSpriteFor(RotatingTurret turret) {
            var tile = turret.TurretTile;
            SpriteData sd;
            if (tile.Graphics.IsRotatable) {
                sd = MapRenderer.Resolver.ResolveRotatableTile(tile, tile.Transform.Rotation);
            } else {
                sd = RenderingService.Sprites.Find($"{turret.GraphicsId}_Top");
            }
            sd.Scale = turret.TurretTile.Graphics.OriginalGraphicSize;
            return sd;
        }
        private SpriteData[] HeadSpritesFor(RotatingTurret turret) {
            var def = turret.TurretTile.Definition;
            if (headGraphics.TryGetValue(def.IdH, out var sprites)) {
                return sprites;
            }
            var gfxId = turret.GraphicsId.Replace("/Devices/", "/TurretHeads/");
            /// Array of 8 directions, starting at down and going clockwise
            /// D -> DDiag -> L -> LDiag -> U -> UDiag -> R -> RDiag
            int idx = 0;
            var headSprites = numHeadSprites;
            if (turret.SymmetryFrame > 0) {
                headSprites = turret.SymmetryFrame;
            }
            sprites = new SpriteData[headSprites];
            for (int i = 0; i < headSprites; i++) {
                sprites[idx++] = RenderingService.Sprites.Find($"{gfxId}_Head_R{i}");
            }
            for (int i = 0; i < headSprites; i++) {
                sprites[i].Scale = turret.TurretTile.Graphics.OriginalGraphicSize;
            }
            headGraphics[def.IdH] = sprites;
            return sprites;
        }
        public void PrepareFor(RotatingTurret turret) {
            var sprites = HeadSpritesFor(turret);
            if (turret.GraphicsId == null) {
                D.Err("Turret graphics id not set yet! {0}", turret);
                turret.GraphicsId = turret.TurretTile.Graphics.Config
                    .GetString(PropertyIdH.Graphic);
            }
            turret.TurretTile.Graphics.SetGraphicId($"{turret.GraphicsId}_Base");

            var headSprite = new AnimatedSpriteSet(this, 1, WorldLayer.ObjectsAbove,
                turret.TurretTile.Position + turret.HeadOffset);
            headSprite.Owner = turret.TurretTile;
            headSprite.SetSprite(0, Rng.UFrom(sprites), S);
            headSprite.SetRenderer(S, 0, Renderers.DeviceParts, turret.SpriteOrder);
            if (headSprites.TryAdd(turret, headSprite)) {
                headSprite.AddToRenderer(S);
            }
            if (turret.HasTopSprite) {
                var topSpriteId = TopSpriteFor(turret);
                var topSprite = new AnimatedSpriteSet(this, 1, WorldLayer.ObjectsAbove,
                    turret.TurretTile.Position + turret.TopOffset);
                topSprite.Owner = turret.TurretTile;
                topSprite.SetSprite(0, topSpriteId, S);
                topSprite.SetRenderer(S, 0, Renderers.DeviceParts, SpriteOrder.TurretTop);
                if (topSprites.TryAdd(turret, topSprite)) {
                    topSprite.AddToRenderer(S);
                }
            }
        }
        public void UnloadFor(RotatingTurret turret) {
            if (headSprites.TryGetValue(turret, out var sprite)) {
                sprite.RemoveFromRenderer(S);
                headSprites.Remove(turret);
            }
            if (topSprites.TryGetValue(turret, out sprite)) {
                sprite.RemoveFromRenderer(S);
                topSprites.Remove(turret);
            }
        }
        public void ShowTurretShotFX(GameState S, RotatingTurret turret) {
            if (turret.Target == null) { return; }
            ShowTurretShotFX(S, turret, turret.Target.Position);
        }

        // The target position is read before the shot: the shot itself can clear the target
        public void ShowTurretShotFX(GameState S, RotatingTurret turret, Vector2 targetPos) {
            if (turret.ShotFXPrefab == null) {
                D.Err("Turret shot prefab not set! {0}", turret.DefIdH);
                return;
            }
            // D.Warn("Playing sound: {0} at {1}", turret.ShotSound, turret.TurretTile.Position);
            var turretPos = turret.TurretTile.Position;
            // D.Warn("ShowingFX: {0}->{1}", turret.ShotFXPrefab, this);

            var prefab = turret.ShotFXPrefab;
            int budgetIdx = turret.TurretTile.Definition.IdH;
            int maxParticles = Tunables.TurretShotMaxParticles;
            float lifeTime = turret.ShotFXLifetime;
            string shotSound = turret.ShotSound;

            CreateShotFX(S, turretPos, targetPos, prefab,
                budgetIdx, maxParticles, lifeTime, shotSound, isLoud: turret.IsLoud,
                    loudVolume: turret.ShotSoundVol);
        }

        public static ParticlesVFX CreateShotFX(GameState S, Vector2 turretPos,
                Vector2 targetPos, string prefab,
                int budgetIdx, int maxParticles,
                float lifeTime, string shotSound,
                float projectileSpeed = 0f,
                bool isLoud = true,
                float loudVolume = 1f) {
            var tfx = ParticlesVFX.CreateWithBudget(S, prefab,
                turretPos,
                budgetIdx,
                maxParticles, isCosmetic: false,
                orderInLayer: 3);
            S.Sys.Light?.Flashes?.AddShot(turretPos, Light.LightFlashes.TurretShotColor);
            if (isLoud) {
                Sounds.PlayLoud(turretPos, shotSound, true, volume: loudVolume);
            } else {
                Sounds.Play(turretPos, shotSound, true);
            }
            if (tfx == null) { return null; }
            tfx.SetClock(S.Clock);
            if (tfx.TryGetFX(out var fx)) {
                var dist = Vectors.Distance(turretPos, targetPos);
                tfx.WithoutOwnership();
                fx.SetVector3(ShaderVars.Target, targetPos - turretPos);
                fx.SetFloat(ShaderVars.Distance, dist);
                if (projectileSpeed > 0f) {
                    fx.SetFloat(ShaderVars.Speed, projectileSpeed);
                }
                tfx.Play(true);
                tfx.DestroyAfterGameTime(lifeTime);
            } else {
                #if UNITY_EDITOR
                D.Warn("could not get fx with budget {0}: {1}",
                    maxParticles, tfx);
                #endif
            }
            return tfx;
        }

        public void ResetHeads() {
            foreach (var hs in headSprites) {
                var turret = hs.Key;
                var head = hs.Value;
                head.Teleport(turret.TurretTile.Position + turret.HeadOffset);
                head.UpdateRenderer(G.Ticks, 0f, Time.frameCount - 2);
            }
            foreach (var ts in topSprites) {
                var turret = ts.Key;
                var head = ts.Value;
                head.Teleport(turret.TurretTile.Position + turret.HeadOffset);
                head.UpdateRenderer(G.Ticks, 0f, Time.frameCount - 2);
            }
        }
    }
}
