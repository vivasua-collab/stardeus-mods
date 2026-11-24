using Game.Data;
using Game.Rendering;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Proxy {
    public sealed class EntityProxy {
        private AnimatedSpriteSet fauxObj;
        public int PosIdx;
        public Vector2 Position;
        public Entity Entity;
        public string Tooltip;

        public static EntityProxy CreateFor(GameState s, Entity e, Vector2 at, string tooltip) {
            var proxy = new EntityProxy();
            proxy.Entity = e;
            proxy.PosIdx = Pos.FromVector(at);
            proxy.Position = at;
            proxy.CreateFauxObj(s, e, at);
            proxy.Tooltip = tooltip;
            s.Query.AddEntityProxy.Ask(proxy);
            return proxy;

        }
        public void Destroy(GameState s) {
            s.Query.RemoveEntityProxy.Ask(this);
            DestroyFauxObj(s);
        }

        public void UpdatePosition(GameState s, Vector2 newPos) {
            DestroyFauxObj(s);
            PosIdx = Pos.FromVector(newPos);
            Position = newPos;
            CreateFauxObj(s, Entity, newPos);
        }
        private void DestroyFauxObj(GameState s) {
            if (fauxObj != null) {
                fauxObj.RemoveFromRenderer(s);
                fauxObj = null;
            }
        }

        private void CreateFauxObj(GameState s, Entity entity, Vector2 at) {
            AnimatedSpriteSet source;
            if (entity is Obj obj) {
                source = obj.Graphics.SpriteSet;
            } else if (entity is Being being) {
                source = being.Graphics.SpriteSet;
            } else {
                D.Err("Can't create faux object for movable that is neither Obj nor Being! {0}", entity);
                return;
            }
            fauxObj = source.Copy(this, at, copyZOffsets: false,
                copyPosFilters: false, newRenderer: "GPUR/SpriteGhost");
            fauxObj.AddToRenderer(s);
        }
        public override string ToString() {
            return $"EntityProxy[{Pos.String(PosIdx)}|{Position}|{Tooltip}->{Entity}]";
        }
    }
}
