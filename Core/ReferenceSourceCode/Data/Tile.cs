using System;
using Game.Components;
using UnityEngine;
using MessagePack;
using KL.Utils;
using Game.Constants;
using Game.Systems.Combat;
using Game.Systems.Areas;
using Game.Systems.Firex;
using Game.Utils;
using System.Runtime.Serialization;
using Game.Systems.UIUX;

namespace Game.Data {
    [MessagePackObject]
    public class Tile : Entity, IAttackable, INamedEntity {
        [IgnoreMember] public bool IsConstructed {
            get {
                if (Constructable == null) {
                    if (Transform.IsChild) {
                        D.Err("Calling IsConstructed on child tile! Call it from parent instead! {0}", this);
                        return Transform.Parent.IsConstructed;
                    }
                    D.Err("Calling IsConstructed on a non-child tile that has no Constructable! {0}", this);
                    return false;
                }
                return Constructable.IsFinished;
            }
        }
        [IgnoreMember] public override Vector2 Size => Transform.Size;
        [IgnoreMember] public TileTransformComp Transform;
        [IgnoreMember] public DamageableComp Health;
        [IgnoreMember] public TileGraphicsComp Graphics;
        [IgnoreMember] public ElectricNodeComp ENode;
        [IgnoreMember] public ConstructableComp Constructable;
        [IgnoreMember] public HullIntegrityComp Integrity;
        [IgnoreMember] public PassabilityComp Passability;
        [IgnoreMember] public MatStorageComp MaterialStorage;
        [IgnoreMember] public TeleporterComp Teleporter;
        [IgnoreMember] public ReinstallableComp Reinstallable;
        [IgnoreMember] public TileLabelComp Label;
        [IgnoreMember] public DoorComp Door;
        [IgnoreMember] public VFXComp VFX;
        [IgnoreMember] public OutdoorsCheckComp Outdoors;
        [IgnoreMember] public PendingReplacementComp PendingReplacement;
        [IgnoreMember] public UpgradesComp Upgrades;
        [IgnoreMember] public bool IsPendingMove => Reinstallable?.IsPending == true || Reinstallable?.IsMidRelocation == true;
        [IgnoreMember] public bool? PlacementCheckResult;
        [IgnoreMember] public string EntityName => NameLink;

        public override bool SetComponent(IComponent comp, int i, int totalComps) {
            if (!base.SetComponent(comp, i, totalComps)) {
                return false;
            }
            if (comp is TileTransformComp tr) {
                Transform = tr;
            }
            else if (comp is TileGraphicsComp gr) { Graphics = gr; }
            else if (comp is ConstructableComp c) { Constructable = c; }
            else if (comp is DamageableComp dm) { Health = dm; }
            else if (comp is ElectricNodeComp eln) { ENode = eln; }
            else if (comp is ReinstallableComp re) { Reinstallable = re; }
            else if (comp is TeleporterComp tel) { Teleporter = tel; }
            else if (comp is VFXComp vf) { VFX = vf; }
            else if (comp is HullIntegrityComp h) { Integrity = h; }
            else if (comp is TileLabelComp l) { Label = l; }
            else if (comp is UpgradesComp up) { Upgrades = up; }
            else if (comp is PassabilityComp pa) { Passability = pa; }
            else if (comp is PendingReplacementComp pr) { PendingReplacement = pr; }
            else if (comp is DoorComp d) { Door = d; }
            else if (comp is OutdoorsCheckComp o) { Outdoors = o; }
            if (comp is MatStorageComp rms) { MaterialStorage = rms; }
            return true;
        }
        public override void AfterRemove() {
            if (Id == 0) {
                if (Transform?.IsChild == true) {
                    return;
                }
            }
            base.AfterRemove();
        }

        [IgnoreMember] public override string CategoryTitle {
            get {
                if (Transform.IsChild) {
                    return Transform.Parent.CategoryTitle;
                }
                switch (Definition.LayerId) {
                    case WorldLayer.Floor:
                    case WorldLayer.Walls: {
                        return T.NounStructure;
                    }
                    case WorldLayer.Objects: {
                        return T.NounDevice;
                    }
                }
                D.Err("Unhandled category title for tile layer: {0}: {1}",
                    Definition.Layer, this);
                return T.Unknown;
            }
        }

        public bool GenReplaceWith(GameState s, Tile other) {
            if (!IsActive) {
                D.Err("Trying to replace an inactive tile with another: {0}->{1}", this, other);
                return false;
            }
            if (base.GenReplaceWith(s, other)) {
                if (!Constructable.HasSameMaterialsAs(other.Constructable)) {
                    Constructable.ReplaceMaterialsWith(other.Constructable);
                }
                Health.ChangeMaxHealth(other.Health.MaxHealth);
                Graphics.HasShader = other.Graphics.HasShader;
                Graphics.Shader = other.Graphics.Shader;
                Graphics.Type = other.Graphics.Type;
                Graphics.SetGraphicId(other.Graphics.GraphicId);
                S.Grids[Definition.LayerId].TriggerChanged(PosIdx);
                return true;
            }
            return false;
        }

        [IgnoreMember] public string NameOrLabel {
            get {
                if (Label != null) {
                    return Label.DecoratedName(Definition.NameT);
                }
                return Definition.NameT;
            }
        }
        [IgnoreMember] public string NameLink {
            get {
                return Texts.LinkToTile(this);
            }
        }
        [IgnoreMember] public string NameOrLabelCodex {
            get {
                if (Label != null) {
                    return Label.DecoratedName(Definition.CodexLink);
                }
                return Definition.CodexLink;
            }
        }

        [IgnoreMember] public Vector2 AttackablePosition => Position;
        [IgnoreMember] public int AttackablePosIdx => PosIdx;
        private static readonly string[] tileDamageSounds = new[] {
            "FX/PunchMetal_01",
            "FX/PunchMetal_02",
            "FX/PunchMetal_03",
            "FX/PunchMetal_04",
            "FX/PunchMetal_05"
        };
        [IgnoreMember] string[] IAttackable.TakeDamageSounds => tileDamageSounds;

        public void SetImmutableWithoutFloor(IComponent c, bool isImmutable) {
            base.SetImmutable(c, isImmutable);
        }
        public override void SetImmutable(IComponent c, bool isImmutable) {
            // D.Log("Setting immutable: {0}({1}) => {2}", isImmutable, c, this);
            base.SetImmutable(c, isImmutable);
            if (Definition.LayerId == WorldLayer.Objects) {
                var cnt = Transform.GetSubtilePos(Caches.PosIdx);
                for (int i = 0; i < cnt; i++) {
                    var p = Caches.PosIdx[i];
                    var floor = EntityUtils.FloorAt(p);
                    floor?.SetImmutable(c, isImmutable);
                }
            }
        }
        // For tests
        public override void ClearImmutable() {
            if (IsImmutable) {
                immutable.Clear();
                if (Definition.LayerId == WorldLayer.Objects) {
                    var cnt = Transform.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < cnt; i++) {
                        var p = Caches.PosIdx[i];
                        var floor = EntityUtils.FloorAt(p);
                        floor?.ClearImmutable();
                    }
                }
            }
        }

        // public override bool Equals(object obj) {
        //     if (obj == null || !(obj is Tile tile)) { return false; }
        //     if (tile.Id != Id) { return false; }
        //     if (Graphics != null && tile.Graphics != null
        //             && Graphics.GraphicId == tile.Graphics.GraphicId) {
        //         return true;
        //     }
        //     if (Graphics == null && tile.Graphics == null) { return true; }
        //     return false;
        // }

        /// <summary>
        /// Required for registering tile changes in the map renderer
        /// correctly
        /// </summary>
        /// <returns></returns>
        public int GetVisualHashCode() {
            unchecked {
                var graphicsHash =
                    (Graphics == null || Graphics.GraphicId == null)
                        ? 1 : Graphics.GraphicId.GetHashCode();
                return (Primes.Prime005 + Id) * (Primes.Prime015 * graphicsHash);
            }
        }

        public override bool Equals(object obj) {
            if (Transform?.IsChild == true) {
                return ReferenceEquals(this, obj);
            }
            return base.Equals(obj);
        }

        public override int GetHashCode() {
            return base.GetHashCode();
        }

        public override string ToString() {
            return string.Format("[{0}] {1} (Active: {2}). {3}",
                Id, DefinitionId, IsActive, Transform);
        }
    }
}
