using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Utils;
using Game.CodeGen;
using Game.VFXR;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Rendering;

namespace Game.Components {
    public sealed class VFXComp : BaseComponent<VFXComp>, IRotationDependent {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new VFXComp());
        }
        private string VFXId;
        private int VFXIdHash;
        private bool isLinkedToEnergy;
        // Shown only while the ship engine on the same tile pushes (ShipEngineComp). An
        // engine's flame tied to energy also burned while a hyperdrive charged with the ship
        // parked (STA-1890).
        private bool isLinkedToThrust;
        private Vector2[] offsets;
        public Vector2 Offset;
        private int maxCount;
        private bool isStatic;
        private bool isCosmetic;
        private bool skipOnOSX;
        private float minEnergy;

        private VFXShardedParticleRenderer rend;

        private ParticleSlot particleSlot;
        public ParticleSlot Particle => particleSlot;
        private bool needsRotation;

        protected override void OnConfig() {
            maxCount = Config.GetInt(PropertyIdH.MaxCount, Consts.DefaultParticleShardSize);
            isLinkedToThrust = Config.GetBool(PropertyIdH.IsLinkedToThrust, false);
            isLinkedToEnergy = !isLinkedToThrust
                && Config.GetBool(PropertyIdH.IsLinkedToEnergy, true);
            if (isLinkedToEnergy) {
                minEnergy = Config.GetFloat(PropertyIdH.MinEnergy, 0f);
            }
            VFXId = Config.GetString(PropertyIdH.Id, null);
            skipOnOSX = Config.GetBool(PropertyIdH.SkipOnOSX, false);
            isCosmetic = Config.GetBool(PropertyIdH.IsCosmetic, true);
            if (string.IsNullOrEmpty(VFXId)) { VFXId = null; }
            if (VFXId == null) {
                needsRotation = true;
                Config.MarkUsed(PropertyIdH.Id_D);
                Config.MarkUsed(PropertyIdH.Id_L);
                Config.MarkUsed(PropertyIdH.Id_R);
                Config.MarkUsed(PropertyIdH.Id_U);
            } else {
                if (Config.HasKey(PropertyIdH.Id_D)) {
                    D.Err("Config has both VDX Id and Id_D! {0} | {1}. Set VFX Id to empty string when overriding!",
                        VFXId, Tile);
                }
            }
            offsets = Config.GetVector2Set(PropertyIdH.Offsets, null);
            if (Arrays.IsEmpty(offsets)) { offsets = null; }
            if (offsets == null) {
                Offset = Config.GetVector2(PropertyIdH.Offset, Consts.V2Zero);
            }
        }

        public override string ToString() {
            return $" * VFX [{VFXId}]";
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (particleSlot != null) {
                particleSlot.Pos += new float2(dx, dy);
            }
        }

        public void AfterRotationUpdated() {
            if (particleSlot != null) {
                RemoveParticle();
                if (needsRotation) {
                    VFXId = null;
                }
                rend = null;
                AddParticle();
            } else {
                rend = null;
                if (needsRotation) {
                    VFXId = null;
                }
            }
        }

        private bool InitializeParticles() {
            if (rend != null) { return true; }
            if (RenderUtils.DisableAllVFX) { return false; }
            if (isCosmetic && RenderUtils.DisableCosmeticVFX) { return false; }
            #if UNITY_STANDALONE_OSX
            // if (skipOnOSX) { return false; }
            #endif
            isStatic = Tile != null;
            // we are in a tile and have a rotation
            if (VFXId == null && isStatic) {
                switch (Tile.Transform.Rotation) {
                    case Facing.Type.Down: {
                        VFXId = Config.GetString(PropertyIdH.Id_D, null); break;
                    }
                    case Facing.Type.Left: {
                        VFXId = Config.GetString(PropertyIdH.Id_L, null); break;
                    }
                    case Facing.Type.Up: {
                        VFXId = Config.GetString(PropertyIdH.Id_U, null); break;
                    }
                    case Facing.Type.Right: {
                        VFXId = Config.GetString(PropertyIdH.Id_R, null); break;
                    }
                }
            }
            if (offsets != null) {
                Offset = offsets[(int) Tile.Transform.Rotation];
            }
            if (VFXId == null) {
                D.Err("VFXId was not resolved for {0}", Entity.DefinitionId);
            } else {
                VFXIdHash = Hashes.S(VFXId);
                rend = S.Sys.Rend.GetParticleRenderer(VFXId, VFXIdHash, maxCount);
            }
            if (isLinkedToEnergy && rend != null) {
                if (!isStatic) {
                    D.Err("VFX is linked to energy but not static! {0}", entity.DefinitionId);
                    return false;
                }
            }
            return rend != null;
        }

        public override void OnLateReady(bool wasLoaded) {
            Ready.WhenGame(this, "AddParticle", () => {
                if (particleSlot == null) {
                    AddParticle();
                }
            });
        }

        public override void Receive(IComponent sender, int message) {
            if (!isLinkedToEnergy && Ready.Game && message == MsgIdH.ConstructionFinished) {
                if (particleSlot == null) {
                    AddParticle();
                } else if (!isLinkedToThrust) {
                    particleSlot.Visibility = 1f;
                }
            }
        }

        public void SetThrustVisible(bool on) {
            if (!isLinkedToThrust || particleSlot == null) { return; }
            particleSlot.Visibility = on ? 1f : 0f;
        }

        public void AddParticle() {
            if (particleSlot == null) {
                if (!isLinkedToEnergy && Tile?.Constructable.IsConstructed == false) {
                    return;
                }
                if (InitializeParticles() && rend.TryReserveSlot(out particleSlot)) {
                    particleSlot.Pos = Entity.Position;
                    particleSlot.Offset = Offset;
                    if (isLinkedToEnergy) {
                        particleSlot.Visibility = 0f;
                        if (isLinkedToEnergy) {
                            if (Tile.ENode == null) {
                                D.Err("Failure when initializing VFX for {0}. FX linked to energy but ENode is null!",
                                    Tile);
                                ModRegistry.WarnAboutBrokenMod(Tile, "Misconfigured VFX component");
                            } else {
                                Tile.ENode.SetVFX(particleSlot, minEnergy);
                            }
                        }
                    } else if (isLinkedToThrust) {
                        particleSlot.Visibility = 0f;
                    } else {
                        if (Tile?.IsConstructed == false) {
                            particleSlot.Visibility = 0f;
                        } else {
                            particleSlot.Visibility = 1f;
                        }
                    }
                }
            } else {
                D.Err("Particle already added! {0}", particleSlot);
            }
        }

        public void RemoveParticle() {
            if (particleSlot != null) {
                if (isLinkedToEnergy) {
                    Tile.ENode.SetVFX(null, 0f);
                }
                rend.ReleaseSlot(particleSlot);
                particleSlot = null;
            }
        }

        public override void OnRemove() {
            RemoveParticle();
        }
        #if !PRODUCTION_BUILD
        public string DebugVFXId => VFXId;
        public Vector2[] DebugOffsets => offsets;
        #endif
    }
}
