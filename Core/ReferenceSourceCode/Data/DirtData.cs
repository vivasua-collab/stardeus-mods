using System;
using Game.Constants;
using Game.VFXR;
using KL.Grid;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using MessagePack;
using UnityEngine;
using Game.Systems.Rendering;

namespace Game.Data {
    [MessagePackObject]
    public class DirtData {
        private static Shader dirtShader;
        [Key("PosIdx")]
        public int PosIdx;
        // Because dynamic
        [Key("Position")]
        public Vector2 Position;
        [Key("Sprite")]
        public string Sprite;
        [Key("Size")]
        public float Size;
        // Firefoam: drawn by its own shader instead of a sprite (DirtSys owns its bookkeeping),
        // and nothing under it can catch fire (FirexSys.IsFoamProtected)
        [Key("Foam")]
        public bool IsFoam;
        // The tick it wears off at by itself, 0 never
        [Key("Until")]
        public long ExpiresAt;
        //private AnimatedSpriteSet graphic;
        private GPUSpriteRenderer renderer;
        private GPURenderSlot renderSlot;
        public static DirtData Create(Vector2 pos, string sprite, float size = 1) {
            var dd = new DirtData();
            dd.PosIdx = EntityUtils.ToPos(pos);
            dd.Position = pos;
            dd.Sprite = sprite;
            dd.Size = size;
            return dd;
        }
        public static DirtData Create(Entity entity, string sprite, float size = 1) {
            var dd = new DirtData();
            dd.PosIdx = entity.PosIdx;
            dd.Position = EntityUtils.CenterOf(entity.PosIdx) + Rng.UInsideUnitCircle() * 0.25f;
            dd.Sprite = sprite;
            dd.Size = size;
            return dd;
        }
        public static DirtData Create(int idx, string sprite, float size = 1, float radius = 0.5f) {
            var dd = new DirtData();
            dd.PosIdx = idx;
            dd.Position = EntityUtils.CenterOf(idx) + Rng.UInsideUnitCircle() * radius;
            dd.Sprite = sprite;
            dd.Size = size;
            return dd;
        }
        public static DirtData CreateFoam(int idx, long expiresAt) {
            var dd = new DirtData();
            dd.PosIdx = idx;
            dd.Position = EntityUtils.CenterOf(idx);
            dd.Size = 1f;
            dd.IsFoam = true;
            dd.ExpiresAt = expiresAt;
            return dd;
        }
        public DirtData() { }
        public void CreateGraphic(GameState S) {
            if (IsFoam) { return; }
            /*
            if (!Ready.Rendering) {
                Ready.WhenRendering(this, "Dirt After Load",
                    () => CreateGraphic(S));
                return;
            }
            */
            if (renderSlot != null) {
                if (Ready.Gen) {
                    D.Warn("Graphic already created for dirt! {0}. It's ok if it's moved by winch", Sprite);
                }
                return;
            }
            //graphic = new AnimatedSpriteSet(this, 1, WorldLayer.Dirt, Position);
            //Sprite = IconId.CMissing;
            if (!RenderingService.Sprites.TryFind(Sprite, out var sd)) {
                D.Err("Not showing dirt with missing sprite {0}", Sprite);
                return;
            }
            if (renderer == null) {
                renderer = S.Sys.Rend.GetSpriteRenderer(sd.AtlasHash, null, Renderers.Layer_Dirt,
                    SpriteOrder.Dirt, false);
                if (renderer == null) {
                    D.Err("Couldn't get dirt renderer!");
                    return;
                }
            }
            if (renderSlot == null) {
                if (!renderer.TryGetSlotFor(sd, out var slot)) {
                    D.Err("Out of slots for dirt");
                    return;
                }
                renderSlot = slot;
                var seed = Hashes.Quick(Primes.Prime016,
                    Mathf.RoundToInt(Position.x * 100));
                seed = Hashes.Quick(seed, Mathf.RoundToInt(Position.y * 100));
                var rng = new Rng(seed);
                renderSlot.SetPos(Position);
                renderSlot.SetRot(rng.Range(0, 360f));
                renderSlot.SetScale(Size);
                renderSlot.SetColor(Consts.ColorWhiteF4);
            } else {
                D.Err("Out of slots for dirt");
            }
        }

        public void RemoveGraphic(GameState S) {
            if (renderSlot != null) {
                renderer.ReleaseSlot(renderSlot);
                renderSlot = null;
            }
        }

        // After moving this has to be readded again to show the dirt
        public void MoveTo(int newPos) {
            if (IsFoam) {
                Position += EntityUtils.CenterOf(newPos) - EntityUtils.CenterOf(PosIdx);
                PosIdx = newPos;
                return;
            }
            if (renderSlot == null) {
                D.Err("Trying to move dirt without render slot! {0}", this);
                return;
            }
            var oldPos = PosIdx;
            PosIdx = newPos;
            var v1 = EntityUtils.CenterOf(oldPos);
            var v2 = EntityUtils.CenterOf(newPos);
            var diff = v2 - v1;
            // var oldPosition = Position;
            Position += diff;
            // D.Log("Moving dirt {0} -> {1} ({2} -> {3})", oldPosition, Position,
            //    Pos.String(oldPos), Pos.String(newPos));
            if (Vectors.SqrDistance(Position, v2) > 2) {
                D.Err("Moved dirt to unexpected position. Should be {0}, is {1}", v2, Position);
            }
            renderSlot.SetPos(Position);
        }

        public override string ToString() {
            if (IsFoam) {
                return $"Dirt: [{PosIdx},{Position}, Firefoam until {ExpiresAt}]";
            }
            return $"Dirt: [{PosIdx},{Position}, {Sprite}]";
        }
    }
}
