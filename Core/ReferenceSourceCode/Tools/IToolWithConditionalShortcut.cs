using System;
using Game.Input;

namespace Game.Tools {
    // No longer read by Toolbox: a shared key is now expressed by putting the
    // other binding into a higher InputLayer. Kept so that mods implementing
    // it still compile.
    [Obsolete("Bind the competing action in a higher InputLayer instead")]
    public interface IToolWithConditionalShortcut {
        ActionType[] AllowShortcutDuplicateActions { get; }
        Func<bool> ShortcutCondition { get; }
    }
}
