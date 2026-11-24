using Game.Components;
using UnityEngine;

namespace Game.Data {
    public abstract class InfoItem {
        // this is cached, and may be invalid! need to heck if Items.Entity == entity before trying to use it
        public EntityInfoItems ParentCache;
        public abstract InfoItemType Type { get; }
        public abstract string Renderer { get; }
        public abstract int RendererHash { get; }
        public abstract int TypeInt { get; }
        public abstract int RendererShardSize { get; }
        public abstract bool RendererSorted { get; }
        public abstract int RendererSortOrder { get; }
        public bool IsShowing { get; protected set; }
        protected string InfoKey { get; set; }
        public bool HasText => InfoKey != null;
        public abstract string Info { get; }
        public GameState S;
        public Entity Entity;
        public Being Being;
        public Tile Tile;
        public Obj Obj;
        public int Timeout;
        public IComponent Component;
        public bool WasUpdated;
        public bool CanHaveCustomTooltip;

        public void Touch() {
            WasUpdated = true;
        }
        public abstract bool IsBlank { get; }
        public abstract string DebugInfo { get; }
        public abstract void Hide();
        public abstract void Show();
        public abstract void Move(Vector2 pos);
        public abstract void MoveBy(float dx, float dy);
        public abstract void Clear();
    }
}
