using UnityEngine;
using UnityEngine.InputSystem;
using Game.Input;
using KL.Utils;
using Game.Utils;
#if UNITY_EDITOR
using UnityEditor;
[InitializeOnLoad]
#endif
public class ScrollFixProcessor : InputProcessor<float> {
    #if UNITY_EDITOR
    static ScrollFixProcessor() {
        Initialize();
    }
    #endif

    [SelfInit(SelfInit.Stage.BeforeCoreDefs)]
    static void Initialize() {
        #if UNITY_STANDALONE_OSX
        D.Err("Registering ScrollFixProcessor on a Mac");
        #endif
        InputSystem.RegisterProcessor<ScrollFixProcessor>();
    }

    public override float Process(float value, InputControl control) {
        if (value == 0f) { return 0f; }
        /*
        #if UNITY_STANDALONE_LINUX
        if (!VirtualMouseInput.IsVScrollInUse) {
            value *= -1;
        }
        #endif
        */
        if (value > 1f) { return 1f; }
        if (value < -1f) { return -1f; }
        return value;
    }
}
