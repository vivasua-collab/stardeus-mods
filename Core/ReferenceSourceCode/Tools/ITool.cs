using System.Collections.Generic;
using Game.Data;
using Game.Input;
using Game.Systems;
using UnityEngine;
namespace Game.Tools {
    public interface ITool {
        string Id { get; }
        string ParentId { get; }
        string ParentParam { get; }
        string NameKey { get; }
        string NameT { get; set; }
        string DescriptionKey { get; }
        bool IsConfigurable { get; }
        bool IsAvailable { get; }
        bool SkipMenu { get; }
        bool IsPassive { get; }
        bool IsDevTool { get; }
        bool IsResearchable { get; }
        bool IsClickedDown { get; }
        int HoverPos { get; }
        bool SkipParameterSorting { get; }
        List<string> UnlockedParams { get; }
        List<string> AvailableParams { get; }
        List<string> AvailableParamsDev { get; }
        ActionType Shortcut { get; }
        string[] ShortcutKeys { get; }
        int Cursor { get; }
        bool ShortcutCyclesParameters { get; }
        string ShortcutNextParam { get; }
        string CurrentParam { get; }
        void CycleMode();
        // While true, the Rotate binding claims its keys over lower input layers
        bool HandlesRotate { get; }
        void Initialize(GameState s);
        void AddParticles(Dictionary<int, XParticles> x);
        void UpdateVFX(ParticlesSys ps);
        void Activate();
        void Deactivate();
        bool Configure(string param);
        ///<returns>true if tool cancelled it's state, false if tool should be removed<returns>
        bool Cancel();
        void OnHover(Vector2 pos);
        // Called instead of OnHover while the cursor is over UI and nothing is being dragged
        void OnHoverUI();
        void OnClickDown(Vector2 pos);
        void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance);
        void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance);
        void OnRotate(int direction);
        //string IconId { get; }
        string MenuIcon { get; }
        string MenuIconFor(string param);
        Color MenuIconTintFor(string param);
        string MenuNameFor(string param);
        // If this is a tool menu that lets us build something, we can use Def
        // as index key and to get more stuff, like translated name, name key, etc
        Def DefFor(string param);
        bool CancelDrag();
        // Tells if param switches into tool mode or just toggles a setting
        bool IsToggle(string param);
    }
}
