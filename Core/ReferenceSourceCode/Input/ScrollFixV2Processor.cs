using UnityEngine;
using UnityEngine.InputSystem;
using Game.Input;
using KL.Utils;
using Game.Utils;
#if UNITY_EDITOR
using UnityEditor;
[InitializeOnLoad]
#endif
public class ScrollFixV2Processor : InputProcessor<Vector2> {
    #if UNITY_EDITOR
    static ScrollFixV2Processor() {
        Initialize();
    }
    #endif

    [SelfInit(SelfInit.Stage.BeforeCoreDefs)]
    static void Initialize() {
        #if UNITY_STANDALONE_OSX
        D.Err("Registering ScrollFixV2Processor on a Mac");
        #endif
        InputSystem.RegisterProcessor<ScrollFixV2Processor>();
    }

    private readonly static Vector2 minusOne = new Vector2(0, -1f);
    private readonly static Vector2 plusOne = new Vector2(0, 1f);

    public override Vector2 Process(Vector2 value, InputControl control) {
        var y = value.y;
        if (y == 0f) { return value; }
        /*
        #if UNITY_STANDALONE_LINUX
        if (!VirtualMouseInput.IsVScrollInUse) {
            value = new Vector2(0, y * -1);
        }
        #endif
        */
        if (value.y > 1f) { return plusOne; }
        if (value.y < -1f) { return minusOne; }
        return value;
    }
}
