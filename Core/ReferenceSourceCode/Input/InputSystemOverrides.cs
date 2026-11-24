using System;
using UnityEngine.InputSystem;
using KL.Utils;

namespace Game.Input {
    public static class InputSystemOverrides {
        public static string GetBindingDisplayStringOverride(this InputAction action, 
                InputBinding bindingMask, 
                InputBinding.DisplayStringOptions options = default) {
            if (action == null) {
                throw new ArgumentNullException(nameof(action));
            }

            var result = string.Empty;
            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; ++i) {
                //if (bindings[i].isPartOfComposite) {
                    //continue;
                //} 
                if (!bindingMask.Matches(bindings[i])) {
                    continue;
                }

                ////REVIEW: should this filter out bindings that are not resolving to any controls?

                var text = action.GetBindingDisplayString(i, options);
                if (result != "") {
                    result = $"{result} | {text}";
                } else {
                    result = text;
                }
            }

            return result;
        }
    }
}
