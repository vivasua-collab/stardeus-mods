using Game.Data;
using Game.Input;
using Game.Research;

namespace Game.Systems.UIUX {
    public abstract class ShipComputerApp {
        public abstract string Id { get; }
        public abstract string Icon { get; }
        public abstract string ColorId { get; }
        public abstract string Name { get; }
        // A research node has to be researched itself, while a buildable def unlocks as soon
        // as its prerequisites are - IsDefUnlocked on a node would open the app one node early
        public virtual bool IsAvailable => RequiredResearch == null
            || (Researchable.TreeNode(RequiredResearch)
                ? A.S.Research.IsUnlocked(RequiredResearch.IdH)
                : A.S.Research.IsDefUnlocked(RequiredResearch));

        public virtual bool IsVisible => true;

        public virtual Def RequiredResearch => null;
        public virtual ActionType Shortcut => ActionType.None;
        // Apps without an ActionType shortcut get a rebindable named binding instead
        // (ShipComputerAppHotkeys), unbound unless HotkeyDefault says otherwise
        public virtual string HotkeyDefault => null;
        public abstract void OnClick();
        public static void Register(ShipComputerApp app) {
            Registries.ShipComputerApps.Add(app.Id, app);
        }
    }
}
