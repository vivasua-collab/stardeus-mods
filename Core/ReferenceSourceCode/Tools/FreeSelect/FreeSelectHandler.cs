using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public abstract class FreeSelectHandler : IResettable {
        protected static void Register(string id, FreeSelectHandler handler) {
            handler.Id = id;
            handler.IdH = Hashes.S(id);
            Registries.FreeSelectHandlers.Add(id, handler);
        }
        public string Id;
        public int IdH;
        public abstract int Order { get; }
        // Whether this handler acts on what is drawn over the hull view's roofs (the being
        // markers). Under a roof the rest add no tooltip text and get no clicks, because the
        // roof hides what they would act on
        public virtual bool WorksOverRoofs => false;
        protected FreeSelectContext ctx;
        protected GameState S;
        public virtual void Initialize(FreeSelectContext ctx) {
            this.ctx = ctx;
            S = ctx.S;
        }
        public virtual void OnDoubleClick(Vector2 pos) {}
        public virtual void OnClickDown(Vector2 pos) {}
        public virtual void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {}
        public virtual void OnHover(Vector2 pos) {}
        public virtual void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {}
        public virtual void Activate() { }
        public virtual void OnResetHandler() { }
        public virtual bool AddRightClickContext(QuickChoice choice) {
            return false;
        }
        public virtual bool AddSelectedBeingRightClickContext(
                int playerFaction, int canDirectControl, int underDirectControl,
                QuickChoice choice) {
            return false;
        }

        public void OnReset() {
            OnResetHandler();
            S = null;
            ctx = null;
        }
    }
}
