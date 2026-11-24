using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using KL.Grid;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public abstract class Tool : ITool {
        public virtual ActionType Shortcut => ActionType.None;
        public virtual string ParentId => null;
        public virtual string ParentParam => null;
        public virtual bool ShortcutCyclesParameters => false;
        public virtual string ShortcutNextParam => null;
        public virtual bool IsDoubleClickable => false;
        public virtual bool SkipParameterSorting => false;
        protected bool isDragging;
        public bool IsDragging => isDragging;
        protected Grid<Tile> ToolGrid => S.Map.Grids[WorldLayer.Tools];
        public abstract string Id { get; }
        public abstract string MenuIcon { get; }
        abstract public bool IsConfigurable { get; }
        public virtual bool IsBuild => false;
        public virtual bool IsPassive => false;
        public virtual bool IsAvailable => true;
        /// <summary>
        /// If this doesn't work in tests, override it in the tool
        /// </summary>
        public virtual bool IsClickedDown => The.Bindings.IsPressed(ActionType.Click);
        /// <summary>
        /// If this doesn't work in tests, override it in the tool
        /// </summary>
        public virtual int HoverPos => Pos.FromVector(The.Bindings.Mouse.WorldPosition);
        public virtual bool IsDevTool => false;
        public virtual bool SkipMenu => false;
        public virtual bool IsResearchable => false;
        public virtual string CurrentParam => null;
        //public virtual Sprite MenuIcon => Images.Sprite(IconId);
        public virtual string[] ShortcutKeys => null;
        public virtual List<string> UnlockedParams => AvailableParams;
        public virtual List<string> AvailableParams => null;
        public virtual List<string> AvailableParamsDev => AvailableParams;
        public virtual string NameKey => string.Format("tool.{0}", Id);
        public string NameT { get; set; }
        // TODO cache!
        public string DescriptionKey => string.Format("tool.{0}.desc", Id);
        public virtual int Cursor => CursorTypeH.Cursor;

        public virtual bool IsToggle(string param) { return false; }
        public virtual bool Configure(string param) { return true; }
        public virtual void Activate() { }
        public virtual void Deactivate() { }
        public virtual bool Cancel() { return false; }
        protected GameState S;
        public void Initialize(GameState s) {
            S = s;
            Initialize();
        }

        protected virtual void Initialize() { }

        public virtual void CycleMode() { }
        public virtual bool HandlesRotate => false;
        public virtual void OnRotate(int dir) { }
        public virtual void OnHover(Vector2 pos) { }
        public virtual void OnHoverUI() { }
        public virtual void OnClickDown(Vector2 pos) { }
        public virtual int LoadDragRectParticles(ParticleSystem.Particle[] particles) {
            return 0;
        }

        public virtual void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) { }

        public virtual void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) { }

        protected bool IsWithinBounds(int pos) {
            return S.Grid.IsWithinBounds(pos);
        }

        public virtual Def DefFor(string param) {
            return null;
        }

        public virtual string MenuNameFor(string param) {
            D.Err("Requesting menu name from {0} for {1}", Id, param);
            return "unlocalized";
        }
        public virtual string MenuIconFor(string param) {
            // D.Warn("Requesting icon from {0} for {1}", Id, param);
            return null;
        }

        public virtual Color MenuIconTintFor(string param) {
            return Palette.White;
        }

        public virtual bool CancelDrag() {
            return false;
        }

        public virtual void AddParticles(Dictionary<int, XParticles> x) { }
        public virtual void UpdateVFX(ParticlesSys ps) { }

        public static string NameWithShortcut(ITool tool, out string shortcutGlyph, string param = null) {
            var tname = tool.NameT;
            if (param != null) {
                tname = tool.MenuNameFor(param).T();
            }
            // FIXME this will have to change for consoles
            if (tool.Shortcut != ActionType.None) {
                shortcutGlyph = UserInput.GetControlName(tool.Shortcut);
            } else {
                shortcutGlyph = null;
            }
            return tname;
        }
    }
}
