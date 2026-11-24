using System;
using Game.Data;
using Game.Mods;
using Game.Utils;
using KL.Utils;

namespace Game.Systems {

    public abstract class GameSystem {
        protected GameRuntime R { get; private set; }
        public GameState S;
        public void RemoveState() {
            S = null;
            R = null;
        }
        // Invoked if game system is disabled for any reason (unsupported save file?)
        public virtual void OnDeactivate() {

        }
        public void Initialize(GameRuntime r) {
            R = r;
            S = r.State;
            try {
                OnInitialize();
            } catch (Exception ex) {
                var gameBreaking = Texts.Red("This can cause game breaking bugs!");
                if (ModRegistry.IsModType(GetType(), out var modInfo)) {
                    ModRegistry.MarkRuntimeBroken(modInfo, $"Failed initializing system: {Id}");
                    ModRegistry.WarnAboutBrokenMod(this,
                        $"Failed initializing system: {Texts.Accented(Id)}<br>{gameBreaking} Please disable this mod.<br><br>Error: {Texts.Red(ex.Message)}<br>{Texts.GreySmaller(ex.StackTrace)}");
                }
                D.LogEx(ex, "Failed initializing system: {0}", Id);
            }
        }
        public virtual bool SkipInSandbox => false;
        public abstract string Id { get; }
        public virtual string MinRequiredVersion => null;
        public bool IsDeactivated;
        protected abstract void OnInitialize();
        /// <summary>
        /// When it comes to signals / queries unbinding, only signals from The.SysSig should have their
        /// listeners removed here. GameSig and GameQuery will be wiped out all together,
        /// so unbinding from them here is useless.
        /// </summary>
        public abstract void Unload();
    }
}
