using Game.Input;
using KL.Console;
using UnityEngine;

namespace Game.Console {
    public class ConsoleToggler : MonoBehaviour {
        private void Update() {
            if (The.Bindings?.IsJustPressed(ActionType.ToggleConsole) == true) {
                InGameConsole.ToggleConsole.Send();
            }
        }
    }
}
