using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // Shared base for warnings that surface a detached body part Obj's BodyPartState
    // (wounds, Crippled/Infected/Destroyed flags, decay) in the entity panel.
    public abstract class BaseBodyPartWarning : BaseEntityWarning {
        protected ObjData obj;
        protected BodyPartState State => obj.BodyPartState;
        protected bool IsBio => obj.BodyPart.IsBiological;

        protected abstract bool Matches(ObjData obj);

        public override bool ShowFor(GameState s, ObjData obj, out bool blockActions) {
            blockActions = false;
            if (obj.BodyPart == null || obj.BodyPartState == null) { return false; }
            if (!Matches(obj)) { return false; }
            this.obj = obj;
            return true;
        }

        protected WIconText2 SpawnRow(Lifetime life, WGroup group, ref int idx,
                string icon, string title, string text2 = null) {
            var warning = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContent2Text2,
                group.Container, idx++);
            warning.Load(icon, title, text2);
            return warning;
        }

        public override void Unload() {
            obj = null;
        }
    }
}
