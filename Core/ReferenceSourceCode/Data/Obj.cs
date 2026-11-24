using System;
using Game.Components;
using UnityEngine;
using MessagePack;
using Game.Constants;
using Game.Systems.Items;
using Game.Systems.Stats;
using Game.Systems.Firex;
using KL.Utils;
using Game.Utils;
using KL.QuadTree;

namespace Game.Data {
    [MessagePackObject]
    public class Obj : Entity, INamedEntity, IQuadable {
        [IgnoreMember] public ObjData Data;
        [IgnoreMember] public TransformComp Transform;
        [IgnoreMember] public ObjHealthComp Health;
        [IgnoreMember] public ObjGraphicsComp Graphics;
        [IgnoreMember] public UnstoredMatComp Pile;
        [IgnoreMember] public StorableObjComp StorableObj;
        [IgnoreMember] public MovableComp Movable;
        [IgnoreMember] public override Vector2 Size => Graphics.SelectableSize;
        [IgnoreMember] public string EntityName => Texts.LinkToObj(this);
        [IgnoreMember] public override string CategoryTitle {
            get {
                if (Pile != null) {
                    return T.NounMaterial;
                } else {
                    return T.NounObject;
                }
            }
        }
        public Vector2 GetPosition() {
            return SelectPosition;
        }
        public override bool SetComponent(IComponent comp, int i, int totalComps) {
            if (!base.SetComponent(comp, i, totalComps)) {
                return false;
            }
            if (comp is TransformComp tr) { Transform = tr; }
            else if (comp is ObjGraphicsComp gr) { Graphics = gr; }
            else if (comp is ObjHealthComp he) { Health = he; }
            else if (comp is UnstoredMatComp pi) { Pile = pi; }
            else if (comp is MovableComp mo) { Movable = mo; }
            else if (comp is StorableObjComp so) { StorableObj = so; }
            return true;
        }
        public override string ToString() {
            return string.Format("Obj [{0}] {1} (Active: {2}). {3}",
                Id, DefinitionId, IsActive, Transform);
        }
    }
}
