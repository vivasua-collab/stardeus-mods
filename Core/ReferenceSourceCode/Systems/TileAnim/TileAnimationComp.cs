using Game.Constants;
using Game.Data;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using KL.Randomness;
using Game.Components;
using Game.Utils;
namespace Game.Systems.TileAnim {
    public sealed class TileAnimationComp : BaseComponent<TileAnimationComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TileAnimationComp());
        }
        private TileAnimation anim;
        private bool needsEnergy;
        private bool autoRegister;
        private Vector2[] offsets;
        private string[] rotations;
        private string[] baseSprites;
        private int[] spriteOrders;
        private Facing.Type[] skipRotations;
        public bool ResetAfterRotate;
        private string animSpriteId;

        protected override void OnConfig() {
            autoRegister = Config.GetBool(PropertyIdH.AutoRegister, true);
            anim = new TileAnimation();
            anim.AnimatedTile = Tile;
            offsets = Config.GetVector2Set(PropertyIdH.Offsets, null);
            if (offsets == null) {
                anim.PosOffset = Config.GetVector2(PropertyIdH.Offset);
            }
            rotations = Config.GetStringSet(PropertyIdH.Rotations, null);
            anim.NumFrames = Config.GetInt(PropertyIdH.NumFrames);
            anim.Speed = Config.GetFloat(PropertyIdH.Speed, 1f);
            anim.PingPong = Config.GetBool(PropertyIdH.PingPong, false);
            // How many frames the animation will idle at the edge of ping pong
            anim.PingPongStillFrames = Config.GetInt(PropertyIdH.PingPongStillFrames, 0);

            var numFramesActual = anim.NumFrames;
            if (anim.PingPong) {
                // add the reverse loop without 2 frames
                numFramesActual += numFramesActual - 2;
                // add still hold
                numFramesActual += anim.PingPongStillFrames * 2;
            }
            anim.NumFramesActual = numFramesActual;

            anim.FramePrefix = Config.GetString(PropertyIdH.FramePrefix, "F");
            anim.Size = Config.GetVector2(PropertyIdH.Size, Consts.V2Zero);
            anim.CopyAnimSprite = Config.GetBool(PropertyIdH.CopyAnimSprite, false);
            var baseSprite = Config.GetString(PropertyIdH.BaseSprite, null);
            if (string.IsNullOrWhiteSpace(baseSprite)) {
                baseSprite = GraphicsId.NoAtlasTransparent;
            }
            anim.BaseSpriteId = baseSprite;
            animSpriteId = anim.AnimSpriteId = Config.GetString(PropertyIdH.AnimSprite, null);
            if (anim.AnimSpriteId == null) {
                D.Err("AnimSprite cannot be null for TileAnimation component! {0}",
                    entity.Definition.Id);
            }
            if (anim.Size.x <= Maths.Epsilon) {
                anim.Size = Tile.Graphics.OriginalGraphicSize;
            }
            var dir = Config.GetInt(PropertyIdH.Direction, 0);
            if (dir == 0) {
                dir = Rng.UOneOf(-1, 1);
            }
            anim.Speed *= Mathf.Sign(dir);
            needsEnergy = Config.GetBool(PropertyIdH.NeedsEnergy, true);
            baseSprites = Config.GetStringSet(PropertyIdH.BaseSprites, null);
            var skipRotations = Config.GetStringSet(PropertyIdH.SkipRotations, null);
            if (skipRotations != null) {
                this.skipRotations = new Facing.Type[skipRotations.Length];
                for (int i = 0; i < skipRotations.Length; i++) {
                    string rot = skipRotations[i];
                    this.skipRotations[i] = Facing.ConvertRotation(rot[0]);
                }
            }
            var sprOrders = Config.GetStringSet(PropertyIdH.SpriteOrders, null);
            if (sprOrders != null) {
                spriteOrders = new int[4];
                for (int i = 0; i < 4; i++) {
                    spriteOrders[i] = SpriteOrder.ForName(sprOrders[i], entity.Definition);
                }
            }
            var order = Config.GetString(PropertyIdH.SpriteOrder, null);
            if (order == null) {
                anim.SpriteOrder = SpriteOrder.TurretHead;
            } else {
                anim.SpriteOrder = SpriteOrder.ForName(order, entity.Definition);
            }
            anim.NeedsEnergy = needsEnergy;
        }

        public override void PostLateReady(bool wasLoaded) {
            if (!autoRegister) { return; }
            if (Tile.IsConstructed && !anim.IsRegistered) {
                if (wasLoaded) {
                    RegisterOnNextUpdate();
                } else {
                    RegisterAnim(true);
                }
            }
        }

        public void RegisterOnNextUpdate() {
            S.Clock.OnNextUpdate(() => {
                if (Tile.IsActive) {
                    RegisterAnim(true);
                }
            });
        }

        public void RegisterAnim(bool add) {
            // A skipped facing only blocks adding: an overlay registered at an earlier facing
            // (before the ship turned) must still be removed, or it stays on screen for good
            if (add && skipRotations != null &&
                    Arrays.Contains(skipRotations, Tile.Transform.Rotation)) {
                ResetAfterRotate = true;
                add = false;
            }
            // D.Warn("Reg turret: {0} | {1}", add, Tile);
            if (add && !Tile.IsActive) {
                if (Ready.Gen) {
                    D.Err("Trying to add inactive anim! {0}", Tile);
                }
                add = false;
            }
            if (anim.IsRegistered == add) { return; }
            if (add) {
                var rotIdx = (int) Tile.Transform.Rotation;
                if (offsets != null) {
                    ResetAfterRotate = true;
                    anim.PosOffset = offsets[rotIdx];
                }
                if (spriteOrders != null) {
                    ResetAfterRotate = true;
                    anim.SpriteOrder = spriteOrders[rotIdx];
                }
                if (baseSprites != null) {
                    ResetAfterRotate = true;
                    anim.BaseSpriteId = baseSprites[rotIdx];
                }
                if (rotations != null) {
                    ResetAfterRotate = true;
                    anim.AnimSpriteId = $"{animSpriteId}_{rotations[rotIdx]}";
                    anim.Hash = Hashes.Quick(Tile.Definition.IdH, Hashes.S(anim.AnimSpriteId));
                } else {
                    anim.Hash = Tile.Definition.IdH;
                }
            }
            anim.IsRegistered = add;
            anim.CurrentFrame = Rng.URange(0f, anim.NumFramesActual);

            if (!S.Query.RegisterTileAnim.Ask(anim, add)) {
                D.Err("Failed to register turret! {0}", Tile);
            }
            anim.IsAnimating = add;
        }

        public override void OnRemove() {
            RegisterAnim(false);
        }

        // Rebuilds the overlay for the new facing. A device turned out of a skipped facing has
        // no overlay yet, so it registers again if it would have on its own
        public void AfterShipRotate() {
            if (!ResetAfterRotate) { return; }
            var wasRegistered = anim.IsRegistered;
            RegisterAnim(false);
            if (wasRegistered || (autoRegister && Tile.IsConstructed
                    && Tile.Reinstallable?.IsMidRelocation != true)) {
                RegisterAnim(true);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (autoRegister && message == MsgIdH.ConstructionFinished) {
                S.ActQ.Enqueue(() => {
                    if (Tile.IsActive) {
                        RegisterAnim(true);
                    }
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    S.ActQ.Enqueue(() => {
                        RegisterAnim(false);
                    });
                } else if (Tile.IsConstructed) {
                    if (autoRegister) {
                        S.ActQ.Enqueue(() => {
                            RegisterAnim(true);
                        });
                    }
                }
            }
        }

        public void BeforeRotate(Facing.Type curRot, Facing.Type newRot) {
            if (skipRotations == null || curRot == newRot) { return; }
            if (Arrays.Contains(skipRotations, curRot) || Arrays.Contains(skipRotations, newRot)) {
                Tile.Graphics.ResetGraphic();
            }
        }

        #if !PRODUCTION_BUILD
        public TileAnimation DebugAnim => anim;
        #endif
    }
}
