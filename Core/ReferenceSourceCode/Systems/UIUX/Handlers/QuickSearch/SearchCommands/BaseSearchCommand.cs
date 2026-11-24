using System;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public abstract class BaseSearchCommand {
        public abstract string Id { get; }
        public int IdH { get; private set; }
        public abstract string Icon { get; }
        protected abstract string NameT { get; }
        public virtual string Tooltip => TitleT;
        private string titleT;
        public string TitleT {
            get {
                if (titleT != null) { return titleT; }
                if (Shortcut == ActionType.None) {
                    titleT = NameT;
                    return titleT;
                }
                titleT = Texts.TitleWithShortcutGlyph(NameT, Shortcut);
                return titleT;
            }
        }
        public virtual ActionType Shortcut => ActionType.None;
        private string searchNameT;
        public string SearchNameT => searchNameT ??= Strings.ToSearchString(NameT);
        public abstract string DescT { get; }
        public abstract string AdapterId { get; }
        protected static void Register(string id, BaseSearchCommand instance) {
            instance.IdH = Hashes.S(id);
            Registries.SearchCommands.Add(id, instance);
        }

        /// <returns>true if command succeeded and quick search should be closed</returns>
        public abstract bool Execute();
        public abstract bool IsEnabled { get; }
    }
}
