using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Commands {
    public class CommandRunner : MonoBehaviour {
        private const int LastCommandsCount = 20;

        [SerializeField] private string[] lastCommands;
        [SerializeField] private int lastCommandIndex = 0;
        [SerializeField] private int minCommandsPerFrame = 0;

        private int maxCommandsAllTime;
        private int maxCommandsPerFrame = 0;
        private int commandsThisFrame = 0;
        private void Start() {
            lastCommands = new string[LastCommandsCount];
        }

        private void Update() {
            if (!Ready.Save) { return; }
            if (The.R == null) { return; }
            commandsThisFrame = 0;
            var q = The.R.CmdQ;
            var cmdQsize = q.Size;
            Command currentCmd = null;
            try {
                while (cmdQsize > 0) {
                    currentCmd = The.R.CmdQ.Execute();
                    lastCommands[lastCommandIndex] = currentCmd.Id;
                    lastCommandIndex = (lastCommandIndex + 1) % LastCommandsCount;
                    if (commandsThisFrame++ > maxCommandsPerFrame) {
                        maxCommandsPerFrame =
                            Mathf.Max(minCommandsPerFrame, commandsThisFrame);
                    }
                    maxCommandsAllTime = Mathf.Max(maxCommandsAllTime,
                        commandsThisFrame);
                    cmdQsize = The.R.CmdQ.Size;
                    if (FrameTimer.HasToYield()) { break; }
                }
            } catch (System.Exception ex) {
                // Execute threw before returning, so currentCmd still holds the previous one
                currentCmd = q.LastStarted;
                cmdQsize = q.Size;
                if (ex.InnerException != null) {
                    D.LogEx(ex.InnerException, "Inner exception");
                }
                if (currentCmd != null) {
                    D.LogEx(ex, "Failed a command: {0}. {1}", currentCmd, currentCmd.Trace);
                } else {
                    D.LogEx(ex, "Failed a command: {0}", currentCmd);
                }
            }
            #if !PRODUCTION_BUILD
            Ready.SetCmdQ(cmdQsize == 0, Units.Num(cmdQsize));
            #else
            // Prevent garbage
            Ready.SetCmdQ(cmdQsize == 0, null);
            #endif
        }

        private void OnDestroy() {
            The.R?.CmdQ?.Clear();
        }
    }
}
